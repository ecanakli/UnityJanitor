using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ecanakli.Janitor.EditorTools
{
    /// <summary>
    /// The Janitor window (Window > Analysis > Janitor): the live lifetime tree with its entry counts, a details pane for the
    /// selected lifetime, the recorded warnings with links to their Troubleshooting entries, and the recently disposed lifetimes.
    /// Built with UI Toolkit. It refreshes at 4 Hz while playing; outside Play Mode it shows the last session's history.
    /// </summary>
    public sealed class JanitorWindow : EditorWindow
    {
        private const string MenuPath = "Window/Analysis/Janitor";
        private const string StyleSheetPath = "Packages/com.ecanakli.janitor/Editor/JanitorWindow.uss";
        private const long RefreshIntervalMs = 250;

        private readonly ToolbarButton[] _tabButtons = new ToolbarButton[3];

        private JanitorViewModel _model;
        private LifetimeTreeView _treeView;
        private LifetimeDetailsView _detailsView;
        private WarningsView _warningsView;
        private RecentView _recentView;
        private Label _errorLabel;
        private ToolbarToggle _pauseToggle;
        private IVisualElementScheduledItem _tick;
        private bool _playing;
        private bool _paused;
        private bool _tracking = true;
        private bool _errorShown;
        private string _lastError;

        [MenuItem(MenuPath)]
        private static void Open()
        {
            var window = GetWindow<JanitorWindow>();
            window.titleContent = new GUIContent("Janitor");
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Janitor");
            minSize = new Vector2(560f, 340f);
            _playing = EditorApplication.isPlaying;
            _tracking = DiagnosticsSettings.TrackingEnabled;
            DiagnosticsSettings.Apply();

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            DiagnosticsSettings.StateRestored -= OnStateRestored;
            DiagnosticsSettings.StateRestored += OnStateRestored;
            UpdateSchedule();
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            DiagnosticsSettings.StateRestored -= OnStateRestored;
            if (_tick != null)
            {
                _tick.Pause();
            }
        }

        private void CreateGUI()
        {
            try
            {
                BuildUi();
            }
            catch (Exception exception)
            {
                rootVisualElement.Clear();
                var label = new Label("The Janitor window could not be built: " + exception.Message);
                label.AddToClassList(ViewHelpers.EmptyClass);
                rootVisualElement.Add(label);
                Debug.LogException(exception);
            }
        }

        private void BuildUi()
        {
            var root = rootVisualElement;
            if (_tick != null)
            {
                _tick.Pause();
                _tick = null;
            }

            root.Clear();
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (sheet != null && !root.styleSheets.Contains(sheet))
            {
                root.styleSheets.Add(sheet);
            }

            root.AddToClassList("janitor-root");
            _model = new JanitorViewModel();
            _errorShown = false;
            _lastError = null;

            _treeView = new LifetimeTreeView(_model.Tree);
            _treeView.LifetimeSelected += OnTreeSelected;
            _treeView.SortChanged += OnTreeSortChanged;

            _detailsView = new LifetimeDetailsView(_model.Details);
            _detailsView.PingClicked += OnPingClicked;
            _detailsView.CancelClicked += OnCancelClicked;

            _warningsView = new WarningsView(_model.Warnings);
            _warningsView.WarningChosen += OnWarningChosen;

            _recentView = new RecentView(_model.Recent);

            _errorLabel = new Label();
            _errorLabel.AddToClassList("janitor-error");
            ViewHelpers.SetDisplay(_errorLabel, false);

            root.Add(BuildToolbar());
            root.Add(_errorLabel);
            root.Add(BuildBody());

            ShowTabVisuals();
            _tick = root.schedule.Execute(OnTick).Every(RefreshIntervalMs);
            UpdateSchedule();
            RefreshNow();
        }

        private Toolbar BuildToolbar()
        {
            var toolbar = new Toolbar();

            _tracking = DiagnosticsSettings.TrackingEnabled;
            var tracking = new ToolbarToggle
            {
                text = "Tracking",
                tooltip = "Record lifetimes, counters, warnings and the disposed history. Off records nothing.",
                value = _tracking,
            };
            tracking.RegisterValueChangedCallback(OnTrackingChanged);

            var traces = new ToolbarToggle
            {
                text = "Stack traces",
                tooltip = "Capture a stack trace for every registration made while this is on. It costs an allocation per registration.",
                value = DiagnosticsSettings.CaptureStackTraces,
            };
            traces.RegisterValueChangedCallback(OnStackTracesChanged);

            _pauseToggle = new ToolbarToggle
            {
                text = "Pause",
                tooltip = "Freeze the window. Recording continues; it is cleared when Play Mode starts.",
                value = _paused,
            };
            _pauseToggle.RegisterValueChangedCallback(OnPauseChanged);

            var overrun = new IntegerField("Overrun frames")
            {
                tooltip = "JANITOR101 fires when a task is still running more than this many frames after its generation ended. The minimum is 1: a task that honours its token needs a frame to notice the cancel.",
                value = DiagnosticsSettings.OverrunFrames,
            };
            overrun.AddToClassList("janitor-overrun");
            overrun.RegisterValueChangedCallback(OnOverrunChanged);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;

            var filter = new ToolbarSearchField();
            filter.AddToClassList("janitor-filter");
            filter.tooltip = "Filter the tree by name, kind or state. Matching rows stay visible together with their ancestors.";
            filter.RegisterValueChangedCallback(OnFilterChanged);

            toolbar.Add(tracking);
            toolbar.Add(traces);
            toolbar.Add(_pauseToggle);
            toolbar.Add(overrun);
            toolbar.Add(spacer);
            toolbar.Add(filter);
            return toolbar;
        }

        private VisualElement BuildBody()
        {
            var split = new TwoPaneSplitView(1, 270f, TwoPaneSplitViewOrientation.Vertical);
            split.style.flexGrow = 1;

            var top = new VisualElement();
            top.style.flexGrow = 1;
            top.style.minHeight = 100f;
            top.Add(_treeView);

            var tabs = new Toolbar();
            _tabButtons[(int)JanitorTab.Details] = new ToolbarButton(ShowDetailsTab) { text = JanitorViewModel.DetailsTabText };
            _tabButtons[(int)JanitorTab.Warnings] = new ToolbarButton(ShowWarningsTab) { text = JanitorViewModel.WarningsTabBase };
            _tabButtons[(int)JanitorTab.Recent] = new ToolbarButton(ShowRecentTab) { text = JanitorViewModel.RecentTabBase };
            for (var i = 0; i < _tabButtons.Length; i++)
            {
                _tabButtons[i].AddToClassList("janitor-tab");
                tabs.Add(_tabButtons[i]);
            }

            var bottom = new VisualElement();
            bottom.style.flexGrow = 1;
            bottom.style.minHeight = 120f;
            bottom.Add(tabs);
            bottom.Add(_detailsView);
            bottom.Add(_warningsView);
            bottom.Add(_recentView);

            split.Add(top);
            split.Add(bottom);
            return split;
        }

        private void ShowDetailsTab()
        {
            SelectTab(JanitorTab.Details);
        }

        private void ShowWarningsTab()
        {
            SelectTab(JanitorTab.Warnings);
        }

        private void ShowRecentTab()
        {
            SelectTab(JanitorTab.Recent);
        }

        private void SelectTab(JanitorTab tab)
        {
            if (_model == null)
            {
                return;
            }

            _model.SetTab(tab);
            ShowTabVisuals();
            ReapplyAndBind();
        }

        private void ShowTabVisuals()
        {
            var tab = _model.Tab;
            for (var i = 0; i < _tabButtons.Length; i++)
            {
                _tabButtons[i].EnableInClassList("janitor-tab--active", i == (int)tab);
            }

            ViewHelpers.SetDisplay(_detailsView, tab == JanitorTab.Details);
            ViewHelpers.SetDisplay(_warningsView, tab == JanitorTab.Warnings);
            ViewHelpers.SetDisplay(_recentView, tab == JanitorTab.Recent);
        }

        // The scheduler runs this every 250 ms, but it is paused unless the window is playing, not paused and tracking.
        private void OnTick()
        {
            if (ShouldTick())
            {
                RefreshNow();
            }
        }

        private bool ShouldTick()
        {
            return _playing && !_paused && _tracking;
        }

        private void UpdateSchedule()
        {
            if (_tick == null)
            {
                return;
            }

            if (ShouldTick())
            {
                _tick.Resume();
            }
            else
            {
                _tick.Pause();
            }
        }

        // Captures the core diagnostics and rebinds the views.
        private void RefreshNow()
        {
            Run(true);
        }

        // Applies the last capture again, for a change that does not need new data.
        private void ReapplyAndBind()
        {
            Run(false);
        }

        private void Run(bool capture)
        {
            if (_model == null)
            {
                return;
            }

            try
            {
                if (capture)
                {
                    _model.Refresh(_playing, _tracking);
                }
                else
                {
                    _model.Reapply(_playing, _tracking);
                }

                BindViews();
                HideError();
            }
            catch (Exception exception)
            {
                ShowError(exception);
            }
        }

        private void BindViews()
        {
            _treeView.ShowModel(_model.TreeEmptyText, _model.Details.SelectedId);
            switch (_model.Tab)
            {
                case JanitorTab.Details:
                    _detailsView.ShowModel();
                    break;
                case JanitorTab.Warnings:
                    _warningsView.ShowModel(_model.WarningsEmptyText);
                    break;
                default:
                    _recentView.ShowModel(_model.RecentEmptyText);
                    break;
            }

            SetTabText(_tabButtons[(int)JanitorTab.Warnings], _model.WarningsTabText);
            SetTabText(_tabButtons[(int)JanitorTab.Recent], _model.RecentTabText);
        }

        private static void SetTabText(ToolbarButton button, string text)
        {
            if (!string.Equals(button.text, text))
            {
                button.text = text;
            }
        }

        private void ShowError(Exception exception)
        {
            var message = exception == null ? "unknown error" : exception.Message;
            if (_errorLabel != null)
            {
                _errorLabel.text = "Janitor window error: " + message + " The window keeps running.";
                ViewHelpers.SetDisplay(_errorLabel, true);
                _errorShown = true;
            }

            // One console entry per distinct message, so a persistent fault does not flood the console at 4 Hz.
            if (exception != null && !string.Equals(_lastError, message))
            {
                _lastError = message;
                Debug.LogException(exception);
            }
        }

        private void HideError()
        {
            if (!_errorShown)
            {
                return;
            }

            _errorShown = false;
            _lastError = null;
            ViewHelpers.SetDisplay(_errorLabel, false);
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    _playing = true;
                    SetPaused(false);
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                case PlayModeStateChange.EnteredEditMode:
                case PlayModeStateChange.ExitingEditMode:
                    _playing = false;
                    break;
            }

            UpdateSchedule();
            RefreshNow();
        }

        // A saved history was imported after a domain reload: show it.
        private void OnStateRestored()
        {
            RefreshNow();
        }

        private void SetPaused(bool paused)
        {
            _paused = paused;
            if (_pauseToggle != null)
            {
                _pauseToggle.SetValueWithoutNotify(paused);
            }
        }

        private void OnTrackingChanged(ChangeEvent<bool> evt)
        {
            _tracking = evt.newValue;
            DiagnosticsSettings.TrackingEnabled = evt.newValue;
            UpdateSchedule();
            RefreshNow();
        }

        private void OnStackTracesChanged(ChangeEvent<bool> evt)
        {
            DiagnosticsSettings.CaptureStackTraces = evt.newValue;
        }

        private void OnPauseChanged(ChangeEvent<bool> evt)
        {
            _paused = evt.newValue;
            UpdateSchedule();
            if (!_paused)
            {
                RefreshNow();
            }
        }

        private void OnOverrunChanged(ChangeEvent<int> evt)
        {
            var clamped = DiagnosticsSettings.ClampOverrunFrames(evt.newValue);
            DiagnosticsSettings.OverrunFrames = clamped;
            if (clamped != evt.newValue && evt.target is IntegerField field)
            {
                field.SetValueWithoutNotify(clamped);
            }
        }

        private void OnFilterChanged(ChangeEvent<string> evt)
        {
            if (_model == null)
            {
                return;
            }

            _model.Tree.Filter = evt.newValue;
            ReapplyAndBind();
        }

        private void OnTreeSortChanged()
        {
            ReapplyAndBind();
        }

        // The tree selection feeds the details pane; the tree itself is already showing it.
        private void OnTreeSelected(int id)
        {
            try
            {
                _model.SelectLifetime(id);
                if (_model.Tab == JanitorTab.Details)
                {
                    _detailsView.ShowModel();
                }
            }
            catch (Exception exception)
            {
                ShowError(exception);
            }
        }

        // Pings the warning's context object, and selects its lifetime in the tree when that is still alive.
        private void OnWarningChosen(WarningRow warning)
        {
            try
            {
                if (warning == null)
                {
                    return;
                }

                LifetimeActions.PingContext(warning.Context);
                var row = warning.FindAliveLifetime(_model.Tree);
                if (row == null)
                {
                    return;
                }

                _model.SelectLifetime(row.Id);
                _treeView.SelectLifetimeRow(row.Id);
            }
            catch (Exception exception)
            {
                ShowError(exception);
            }
        }

        private void OnPingClicked()
        {
            LifetimeActions.Ping(_model.Details.Row);
        }

        private void OnCancelClicked()
        {
            var outcome = LifetimeActions.Cancel(_model.Details.Row, LifetimeActions.ConfirmCancelApp);
            if (outcome == CancelOutcome.Cancelled)
            {
                RefreshNow();
            }
        }
    }
}
