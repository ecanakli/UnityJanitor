using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Ecanakli.Janitor.EditorTools
{
    /// <summary>
    /// The details pane of the selected lifetime: its header and summary, the Ping owner and Cancel buttons, the list of its entries
    /// and the stack trace of the selected entry. It only shows a <see cref="DetailsModel"/>; the buttons raise events.
    /// </summary>
    internal sealed class LifetimeDetailsView : VisualElement
    {
        private readonly DetailsModel _model;
        private readonly Label _hint;
        private readonly VisualElement _body;
        private readonly Label _header;
        private readonly Label _summary;
        private readonly Button _pingButton;
        private readonly Button _cancelButton;
        private readonly MultiColumnListView _entries;
        private readonly Label _traceHint;
        private readonly ScrollView _traceScroll;
        private readonly TextField _trace;
        private string _shownTrace;
        private bool _applying;

        internal LifetimeDetailsView(DetailsModel model)
        {
            _model = model;
            style.flexGrow = 1;

            _hint = new Label();
            _hint.AddToClassList(ViewHelpers.EmptyClass);

            _body = new VisualElement();
            _body.style.flexGrow = 1;

            _header = new Label();
            _header.AddToClassList("janitor-details-header");
            _summary = new Label();
            _summary.AddToClassList("janitor-details-summary");

            var texts = new VisualElement();
            texts.style.flexGrow = 1;
            texts.style.flexShrink = 1;
            texts.Add(_header);
            texts.Add(_summary);

            _pingButton = new Button(OnPingClicked) { text = "Ping owner", tooltip = "Highlights the owner object in the editor." };
            _cancelButton = new Button(OnCancelClicked) { text = "Cancel", tooltip = "Calls Cancel() on this lifetime and its descendants. A debugging aid." };

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.flexShrink = 0;
            buttons.Add(_pingButton);
            buttons.Add(_cancelButton);

            var top = new VisualElement();
            top.AddToClassList("janitor-details-top");
            top.style.flexDirection = FlexDirection.Row;
            top.Add(texts);
            top.Add(buttons);

            _entries = ViewHelpers.CreateList();
            _entries.itemsSource = model.Entries.Rows;
            ViewHelpers.AddTextColumn<EntryRow>(_entries, "item", "Item", 220f, static row => row.ItemText, stretch: true, decorate: DecorateEntry);
            ViewHelpers.AddTextColumn<EntryRow>(_entries, "site", "Registered at", 170f, static row => row.SiteText);
            ViewHelpers.AddTextColumn<EntryRow>(_entries, "generation", "Generation", 78f, static row => row.GenerationText, numeric: true);
            ViewHelpers.AddTextColumn<EntryRow>(_entries, "age", "Age (frames)", 90f, static row => row.AgeText, numeric: true);
            ViewHelpers.AddTextColumn<EntryRow>(_entries, "status", "Status", 80f, static row => row.StatusText, decorate: DecorateEntry);
            _entries.selectedIndicesChanged += OnEntrySelected;

            _traceHint = new Label();
            _traceHint.AddToClassList("janitor-trace-hint");
            _trace = new TextField { multiline = true, isReadOnly = true };
            _trace.AddToClassList("janitor-trace");
            _traceScroll = new ScrollView();
            _traceScroll.AddToClassList("janitor-trace-area");
            _traceScroll.Add(_trace);

            _body.Add(top);
            _body.Add(_entries);
            _body.Add(_traceHint);
            _body.Add(_traceScroll);

            Add(_hint);
            Add(_body);
        }

        /// <summary>Raised when the Ping owner button is clicked.</summary>
        internal event Action PingClicked;

        /// <summary>Raised when the Cancel button is clicked; the window confirms and acts.</summary>
        internal event Action CancelClicked;

        /// <summary>Shows the model: the details, or a single line when there is nothing to show.</summary>
        internal void ShowModel()
        {
            var hint = _model.HintText;
            var showHint = hint != null;
            ViewHelpers.SetDisplay(_hint, showHint);
            ViewHelpers.SetDisplay(_body, !showHint);
            if (showHint)
            {
                ViewHelpers.SetText(_hint, hint);
                return;
            }

            ViewHelpers.SetText(_header, _model.HeaderText);
            ViewHelpers.SetText(_summary, _model.SummaryText);
            _pingButton.SetEnabled(_model.CanPing);
            _cancelButton.SetEnabled(_model.CanCancel);

            _applying = true;
            try
            {
                _entries.RefreshItems();
                var index = _model.Entries.SelectedIndex;
                if (_entries.selectedIndex != index)
                {
                    if (index >= 0)
                    {
                        _entries.SetSelectionWithoutNotify(new[] { index });
                    }
                    else
                    {
                        _entries.ClearSelection();
                    }
                }
            }
            finally
            {
                _applying = false;
            }

            ShowTrace(_model.Entries.TraceText);
        }

        private static void DecorateEntry(EntryRow row, Label label)
        {
            label.EnableInClassList(EntryRow.OutlivedClass, row.IsOutlived);
        }

        // Null hides the trace area, the "no trace" text becomes a hint, and a real trace goes into the read-only field.
        private void ShowTrace(string text)
        {
            var hasText = text != null;
            var isHint = hasText && ReferenceEquals(text, EntryListModel.NoTraceText);
            ViewHelpers.SetDisplay(_traceHint, isHint);
            ViewHelpers.SetDisplay(_traceScroll, hasText && !isHint);
            if (isHint)
            {
                ViewHelpers.SetText(_traceHint, text);
            }
            else if (hasText && !ReferenceEquals(_shownTrace, text))
            {
                _shownTrace = text;
                _trace.SetValueWithoutNotify(text);
            }
        }

        private void OnEntrySelected(IEnumerable<int> indices)
        {
            if (_applying)
            {
                return;
            }

            try
            {
                var picked = -1;
                foreach (var index in indices)
                {
                    picked = index;
                    break;
                }

                _model.Entries.SelectIndex(picked);
                ShowTrace(_model.Entries.TraceText);
            }
            catch (Exception)
            {
                // A selection event must not throw into the UI.
            }
        }

        private void OnPingClicked()
        {
            PingClicked?.Invoke();
        }

        private void OnCancelClicked()
        {
            CancelClicked?.Invoke();
        }
    }
}
