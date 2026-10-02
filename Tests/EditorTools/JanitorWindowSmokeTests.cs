using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.EditorTools.Tests
{
    // Opens a real JanitorWindow, builds its UI and refreshes it with nothing recorded and with a populated tree, warnings and history.
    // The window's members are private, so a few of them are reached by name; a rename fails these tests with a clear message.
    // A window cannot always be created (a headless run); then the test is ignored, not failed.
    [TestFixture]
    public sealed class JanitorWindowSmokeTests : DiagnosticsTestBase
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private JanitorWindow _window;

        [TearDown]
        public void TearDown()
        {
            CloseWindow();
        }

        [Test]
        public void CreateGUI_NothingRecorded_BuildsTheWindowAndShowsTheNotPlayingMessage()
        {
            CreateWindowOrIgnore();

            Invoke("CreateGUI");

            Assert.That(_window.rootVisualElement.childCount, Is.GreaterThan(0), "the toolbar, the split view and the error label");
            var model = Get<JanitorViewModel>("_model");
            Assert.That(model, Is.Not.Null);
            Assert.That(model.TreeEmptyText, Is.EqualTo(EmptyStates.NotPlaying));
            Assert.That(Get<bool>("_errorShown"), Is.False, "no error banner");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void CreateGUI_CalledTwice_RebuildsWithoutDuplicatingTheUiOrFailing()
        {
            CreateWindowOrIgnore();
            Invoke("CreateGUI");
            var children = _window.rootVisualElement.childCount;

            Invoke("CreateGUI");

            Assert.That(_window.rootVisualElement.childCount, Is.EqualTo(children));
            Assert.That(Get<bool>("_errorShown"), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Refresh_PopulatedTreeWarningsAndHistory_BindsEveryTabWithoutErrors()
        {
            Scope.Tree.MakeDefault();
            Scope.Tree.UseManualClock();
            var level = Scope.App.CreateChild("Level");
            var area = level.CreateChild("Area");
            area.OnCancel(() => { });
            area.After(10f, () => { });
            new OwnedEvent<int>("Coins").Subscribe(_ => { }, area);
            LifetimeDiagnostics.Report(area, DiagnosticIds.DuplicateSubscription, "a duplicate handler");
            LifetimeDiagnostics.Report(null, DiagnosticIds.Marshalled, "marshalled");
            level.CreateChild("Gone").Dispose();

            CreateWindowOrIgnore();
            Invoke("CreateGUI");
            Set("_playing", true);
            Set("_tracking", true);
            var model = Get<JanitorViewModel>("_model");

            Invoke("RefreshNow");
            Assert.That(model.Tree.TotalCount, Is.EqualTo(3), "App, Level and Area; the disposed area is gone");
            Assert.That(model.TreeEmptyText, Is.Null);

            model.SelectLifetime(area.DiagId);
            Invoke("RefreshNow");
            Assert.That(model.Details.Entries.Rows.Count, Is.EqualTo(3));

            Invoke("SelectTab", JanitorTab.Warnings);
            Assert.That(model.Warnings.Rows.Count, Is.EqualTo(2));

            Invoke("SelectTab", JanitorTab.Recent);
            Assert.That(model.Recent.Rows.Count, Is.EqualTo(1));

            Invoke("SelectTab", JanitorTab.Details);
            Assert.That(Get<bool>("_errorShown"), Is.False, "no error banner");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Refresh_NotPlayingWithHistory_BindsTheHistoryTabsWithoutErrors()
        {
            Scope.App.CreateChild("Gone").Dispose();
            LifetimeDiagnostics.Report(null, DiagnosticIds.Growth, "big");

            CreateWindowOrIgnore();
            Invoke("CreateGUI");
            Set("_tracking", true);
            var model = Get<JanitorViewModel>("_model");

            Invoke("SelectTab", JanitorTab.Warnings);
            Invoke("SelectTab", JanitorTab.Recent);

            Assert.That(model.Warnings.Rows.Count, Is.EqualTo(1));
            Assert.That(model.Recent.Rows.Count, Is.EqualTo(1));
            Assert.That(model.TreeEmptyText, Is.EqualTo(EmptyStates.NotPlaying));
            Assert.That(Get<bool>("_errorShown"), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Refresh_TrackingOff_ShowsTheTrackingOffMessageWithoutErrors()
        {
            Scope.Tree.MakeDefault();
            Scope.App.CreateChild("Area");

            CreateWindowOrIgnore();
            Invoke("CreateGUI");
            Set("_playing", true);
            Set("_tracking", false);
            var model = Get<JanitorViewModel>("_model");

            Invoke("RefreshNow");

            Assert.That(model.TreeEmptyText, Is.EqualTo(EmptyStates.TrackingOff));
            Assert.That(Get<bool>("_errorShown"), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        // Creates a window that is not shared with a window the user may have open. Ignores the test when none can be created.
        private void CreateWindowOrIgnore()
        {
            string reason = null;
            try
            {
                _window = EditorWindow.CreateWindow<JanitorWindow>();
                if (_window == null)
                {
                    reason = "EditorWindow.CreateWindow returned no window";
                }
                else if (_window.rootVisualElement == null)
                {
                    reason = "the window has no root visual element";
                }
            }
            catch (Exception exception)
            {
                reason = "creating an editor window failed here: " + exception.GetType().Name + ": " + exception.Message;
            }

            if (reason != null)
            {
                CloseWindow();
                Assert.Ignore(reason + (Application.isBatchMode ? " (batch mode)" : string.Empty));
            }
        }

        private void CloseWindow()
        {
            if (_window == null)
            {
                return;
            }

            try
            {
                _window.Close();
            }
            catch (Exception)
            {
                // The window is gone either way; a failing Close must not hide the test result.
            }

            _window = null;
        }

        private void Invoke(string name, params object[] arguments)
        {
            var method = typeof(JanitorWindow).GetMethod(name, Private);
            Assert.That(method, Is.Not.Null, "JanitorWindow no longer has a private method named " + name);
            try
            {
                method.Invoke(_window, arguments);
            }
            catch (TargetInvocationException exception)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }
        }

        private T Get<T>(string name)
        {
            var field = typeof(JanitorWindow).GetField(name, Private);
            Assert.That(field, Is.Not.Null, "JanitorWindow no longer has a private field named " + name);
            return (T)field.GetValue(_window);
        }

        private void Set(string name, object value)
        {
            var field = typeof(JanitorWindow).GetField(name, Private);
            Assert.That(field, Is.Not.Null, "JanitorWindow no longer has a private field named " + name);
            field.SetValue(_window, value);
        }
    }
}
