using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;

namespace Ecanakli.Janitor.EditorTools.Tests
{
    // The window's settings: the clamp, the EditorPrefs round trip, the push into core, and the history that survives a reload.
    [TestFixture]
    public sealed class DiagnosticsSettingsTests : DiagnosticsTestBase
    {
        private static readonly string[] Keys =
        {
            DiagnosticsSettings.TrackingKey,
            DiagnosticsSettings.StackTracesKey,
            DiagnosticsSettings.OverrunFramesKey,
        };

        // The stored form of each key before the test: null when the key did not exist.
        private readonly Dictionary<string, string> _saved = new Dictionary<string, string>();

        [SetUp]
        public void SetUp()
        {
            _saved.Clear();
            foreach (var key in Keys)
            {
                _saved[key] = EditorPrefs.HasKey(key) ? ReadRaw(key) : null;
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var key in Keys)
            {
                if (_saved[key] == null)
                {
                    EditorPrefs.DeleteKey(key);
                }
            }

            // The keys that existed hold a bool or an int; put those back with the right type.
            RestoreTyped();
        }

        // Clamp

        [TestCase(-5, 1)]
        [TestCase(0, 1)]
        [TestCase(1, 1)]
        [TestCase(3, 3)]
        [TestCase(3600, 3600)]
        [TestCase(3601, 3600)]
        [TestCase(int.MaxValue, 3600)]
        [TestCase(int.MinValue, 1)]
        public void ClampOverrunFrames_Value_StaysInTheAllowedRange(int value, int expected)
        {
            Assert.That(DiagnosticsSettings.ClampOverrunFrames(value), Is.EqualTo(expected));
        }

        // Keys and defaults

        [Test]
        public void Keys_AreTheDocumentedStrings()
        {
            Assert.That(DiagnosticsSettings.TrackingKey, Is.EqualTo("Ecanakli.Janitor.Tracking"));
            Assert.That(DiagnosticsSettings.StackTracesKey, Is.EqualTo("Ecanakli.Janitor.StackTraces"));
            Assert.That(DiagnosticsSettings.OverrunFramesKey, Is.EqualTo("Ecanakli.Janitor.OverrunFrames"));
        }

        [Test]
        public void Settings_NothingStored_UseTheDefaults()
        {
            foreach (var key in Keys)
            {
                EditorPrefs.DeleteKey(key);
            }

            Assert.That(DiagnosticsSettings.TrackingEnabled, Is.True, "tracking is on by default");
            Assert.That(DiagnosticsSettings.CaptureStackTraces, Is.False, "stack traces are off by default");
            Assert.That(DiagnosticsSettings.OverrunFrames, Is.EqualTo(3));
        }

        // EditorPrefs round trip

        [Test]
        public void Settings_Written_AreReadBackFromEditorPrefs()
        {
            DiagnosticsSettings.TrackingEnabled = false;
            DiagnosticsSettings.CaptureStackTraces = true;
            DiagnosticsSettings.OverrunFrames = 9;

            Assert.That(EditorPrefs.GetBool(DiagnosticsSettings.TrackingKey, true), Is.False);
            Assert.That(EditorPrefs.GetBool(DiagnosticsSettings.StackTracesKey, false), Is.True);
            Assert.That(EditorPrefs.GetInt(DiagnosticsSettings.OverrunFramesKey, 0), Is.EqualTo(9));
            Assert.That(DiagnosticsSettings.TrackingEnabled, Is.False);
            Assert.That(DiagnosticsSettings.CaptureStackTraces, Is.True);
            Assert.That(DiagnosticsSettings.OverrunFrames, Is.EqualTo(9));
        }

        [Test]
        public void OverrunFrames_OutOfRangeValueWritten_IsStoredClamped()
        {
            DiagnosticsSettings.OverrunFrames = 99999;
            Assert.That(EditorPrefs.GetInt(DiagnosticsSettings.OverrunFramesKey, 0), Is.EqualTo(DiagnosticsSettings.MaxOverrunFrames));

            DiagnosticsSettings.OverrunFrames = -4;
            Assert.That(EditorPrefs.GetInt(DiagnosticsSettings.OverrunFramesKey, 99), Is.EqualTo(DiagnosticsSettings.MinOverrunFrames));
        }

        [Test]
        public void OverrunFrames_OutOfRangeValueAlreadyStored_IsClampedWhenRead()
        {
            EditorPrefs.SetInt(DiagnosticsSettings.OverrunFramesKey, 99999);
            Assert.That(DiagnosticsSettings.OverrunFrames, Is.EqualTo(DiagnosticsSettings.MaxOverrunFrames));

            EditorPrefs.SetInt(DiagnosticsSettings.OverrunFramesKey, -4);
            Assert.That(DiagnosticsSettings.OverrunFrames, Is.EqualTo(DiagnosticsSettings.MinOverrunFrames));
        }

        [Test]
        public void OverrunFrames_ZeroAlreadyStored_IsReadAndPushedAsTheMinimumOfOne()
        {
            EditorPrefs.SetInt(DiagnosticsSettings.OverrunFramesKey, 0);

            Assert.That(DiagnosticsSettings.OverrunFrames, Is.EqualTo(1), "a cancelled await needs one frame to observe its token");
            DiagnosticsSettings.Apply();
            Assert.That(LifetimeDiagnostics.OverrunFrames, Is.EqualTo(1));
        }

        // Push into core

        [Test]
        public void Setters_PushTheValueIntoTheCoreAtOnce()
        {
            DiagnosticsSettings.TrackingEnabled = false;
            Assert.That(LifetimeDiagnostics.TrackingEnabled, Is.False);

            DiagnosticsSettings.CaptureStackTraces = true;
            Assert.That(LifetimeDiagnostics.CaptureStackTraces, Is.True);

            DiagnosticsSettings.OverrunFrames = 7;
            Assert.That(LifetimeDiagnostics.OverrunFrames, Is.EqualTo(7));
        }

        [Test]
        public void Apply_CoreBackAtItsDefaults_PushesTheStoredSettingsAgain()
        {
            DiagnosticsSettings.TrackingEnabled = false;
            DiagnosticsSettings.CaptureStackTraces = true;
            DiagnosticsSettings.OverrunFrames = 11;
            LifetimeDiagnostics.ResetAll();
            Assert.That(LifetimeDiagnostics.TrackingEnabled, Is.True, "ResetAll returns the core to its defaults");

            DiagnosticsSettings.Apply();

            Assert.That(LifetimeDiagnostics.TrackingEnabled, Is.False);
            Assert.That(LifetimeDiagnostics.CaptureStackTraces, Is.True);
            Assert.That(LifetimeDiagnostics.OverrunFrames, Is.EqualTo(11));
        }

        [Test]
        public void Setters_TrackingOff_StopsTheCoreFromRecordingWarnings()
        {
            DiagnosticsSettings.TrackingEnabled = false;

            LifetimeDiagnostics.Report(null, DiagnosticIds.Growth, "ignored while tracking is off");

            Assert.That(LifetimeDiagnostics.WarningCount, Is.Zero);
        }

        // History across a domain reload (what DiagnosticsSettings saves and restores)

        [Test]
        public void ExportImport_RoundTrip_KeepsTheWarningsAndTheRing()
        {
            var first = Scope.App.CreateChild("first");
            var second = Scope.App.CreateChild("second");
            LifetimeDiagnostics.Report(null, DiagnosticIds.DuplicateSubscription, "dup message");
            LifetimeDiagnostics.Report(null, DiagnosticIds.Marshalled, "marshalled");
            LifetimeDiagnostics.Report(null, DiagnosticIds.DuplicateSubscription, "dup message");
            Frame = StartFrame + 50;
            first.Dispose();
            Frame = StartFrame + 60;
            second.Dispose();
            var warningsBefore = new List<DiagnosticWarning>();
            var recentBefore = new List<RecentLifetime>();
            LifetimeDiagnostics.CopyWarnings(warningsBefore);
            LifetimeDiagnostics.CopyRecent(recentBefore);

            var json = LifetimeDiagnostics.ExportState();
            Assert.That(json, Is.Not.Null.And.Not.Empty);
            LifetimeDiagnostics.ResetAll();
            Assert.That(LifetimeDiagnostics.WarningCount, Is.Zero);
            Assert.That(LifetimeDiagnostics.RecentCount, Is.Zero);
            LifetimeDiagnostics.ImportState(json);

            var warnings = new List<DiagnosticWarning>();
            var recent = new List<RecentLifetime>();
            LifetimeDiagnostics.CopyWarnings(warnings);
            LifetimeDiagnostics.CopyRecent(recent);
            Assert.That(warnings.Count, Is.EqualTo(2), "the repeated warning was merged into one entry with a count");
            Assert.That(warnings.Count, Is.EqualTo(warningsBefore.Count));
            for (var i = 0; i < warnings.Count; i++)
            {
                Assert.That(warnings[i].DiagnosticId, Is.EqualTo(warningsBefore[i].DiagnosticId));
                Assert.That(warnings[i].Message, Is.EqualTo(warningsBefore[i].Message));
                Assert.That(warnings[i].Count, Is.EqualTo(warningsBefore[i].Count));
                Assert.That(warnings[i].FirstFrame, Is.EqualTo(warningsBefore[i].FirstFrame));
                Assert.That(warnings[i].Lifetime, Is.Null, "lifetime references are not persisted");
            }

            Assert.That(warnings[0].Count, Is.EqualTo(2));
            Assert.That(recent.Count, Is.EqualTo(2));
            Assert.That(recent[0].FormatLabel(), Is.EqualTo("second"), "newest first");
            Assert.That(recent[1].FormatLabel(), Is.EqualTo("first"));
            Assert.That(recent[0].DisposedFrame, Is.EqualTo(recentBefore[0].DisposedFrame));
        }

        [Test]
        public void ExportImport_RestoredHistory_ShowsInTheWindowsRows()
        {
            var area = Scope.App.CreateChild("gone");
            LifetimeDiagnostics.Report(null, DiagnosticIds.Growth, "big lifetime");
            area.Dispose();
            var json = LifetimeDiagnostics.ExportState();
            LifetimeDiagnostics.ResetAll();
            LifetimeDiagnostics.ImportState(json);
            var vm = new JanitorViewModel();
            vm.SetTab(JanitorTab.Warnings);

            vm.Refresh(false, true);
            var warningRow = vm.Warnings.Rows[0];
            vm.SetTab(JanitorTab.Recent);
            vm.Reapply(false, true);

            Assert.That(vm.WarningsTabText, Is.EqualTo("Warnings (1)"));
            Assert.That(warningRow.IdText, Is.EqualTo(DiagnosticIds.Growth));
            Assert.That(vm.Recent.Rows.Count, Is.EqualTo(1));
            Assert.That(vm.Recent.Rows[0].NameText, Is.EqualTo("gone"));
        }

        [Test]
        public void ExportImport_WarningOfALiveLifetime_CannotBeJoinedToTheTreeAfterTheRestore()
        {
            Scope.Tree.MakeDefault();
            var area = Scope.App.CreateChild("alive");
            LifetimeDiagnostics.Report(area, DiagnosticIds.Growth, "attached");
            var json = LifetimeDiagnostics.ExportState();
            LifetimeDiagnostics.ResetAll();
            LifetimeDiagnostics.ImportState(json);
            var vm = new JanitorViewModel();
            vm.SetTab(JanitorTab.Warnings);

            vm.Refresh(true, true);

            var warning = vm.Warnings.Rows[0];
            Assert.That(warning.LifetimeId, Is.EqualTo(area.DiagId), "the id is restored");
            Assert.That(vm.Tree.Find(area.DiagId), Is.Not.Null, "the lifetime is alive in the tree");
            Assert.That(warning.FindAliveLifetime(vm.Tree), Is.Null, "a restored warning has no lifetime object, so it must not join by id alone");
        }

        // The EditorPrefs value of a key as text, whatever its type. EditorPrefs has no typed probe, so an int is tried first.
        private static string ReadRaw(string key)
        {
            if (key == DiagnosticsSettings.OverrunFramesKey)
            {
                return "i:" + EditorPrefs.GetInt(key, 0);
            }

            return "b:" + EditorPrefs.GetBool(key, false);
        }

        private void RestoreTyped()
        {
            foreach (var key in Keys)
            {
                var saved = _saved[key];
                if (saved == null)
                {
                    continue;
                }

                if (saved.StartsWith("i:"))
                {
                    EditorPrefs.SetInt(key, int.Parse(saved.Substring(2)));
                }
                else
                {
                    EditorPrefs.SetBool(key, saved.Substring(2) == "True");
                }
            }
        }
    }
}
