using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // The three settings the window sets: TrackingEnabled (default on), CaptureStackTraces (default off) and OverrunFrames
    // (default 3; its boundaries are in TaskOverrunTests). Off must mean off: nothing recorded, no frame read, nothing allocated.
    [TestFixture]
    public sealed class DiagnosticsToggleTests
    {
        private DiagnosticsRecordingKit _d;
        private TestScope _t;
        private DiagnosticsSnapshot _snapshot;

        [SetUp]
        public void SetUp()
        {
            _d = new DiagnosticsRecordingKit();
            _t = new TestScope();
            _t.Tree.MakeDefault();
            _snapshot = new DiagnosticsSnapshot();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                _t.Complete();
            }
            finally
            {
                _d.Dispose();
            }
        }

        [Test]
        public void Defaults_AreTrackingOnStackTracesOffAndThreeFrames()
        {
            Assert.That(LifetimeDiagnostics.TrackingEnabled, Is.True);
            Assert.That(LifetimeDiagnostics.CaptureStackTraces, Is.False);
            Assert.That(LifetimeDiagnostics.OverrunFrames, Is.EqualTo(3));
        }

        [Test]
        public void ResetAll_RestoresTheDefaultsAndDropsEverythingRecorded()
        {
            LifetimeDiagnostics.TrackingEnabled = false;
            LifetimeDiagnostics.CaptureStackTraces = true;
            LifetimeDiagnostics.OverrunFrames = 9;
            LifetimeDiagnostics.TrackingEnabled = true;
            LifetimeDiagnostics.Report(null, "TEST100", "recorded");
            _t.App.CreateChild("gone").Dispose();

            LifetimeDiagnostics.ResetAll();

            Assert.That(LifetimeDiagnostics.TrackingEnabled, Is.True);
            Assert.That(LifetimeDiagnostics.CaptureStackTraces, Is.False);
            Assert.That(LifetimeDiagnostics.OverrunFrames, Is.EqualTo(3));
            Assert.That(LifetimeDiagnostics.WarningCount, Is.Zero);
            Assert.That(LifetimeDiagnostics.RecentCount, Is.Zero);
            Assert.That(LifetimeDiagnostics.Session, Is.Zero);
            Assert.That(LifetimeDiagnostics.FrameProvider, Is.Null);
            Assert.That(LifetimeDiagnostics.Scheduler, Is.Null);
        }

        // TrackingEnabled

        [Test]
        public void TrackingDisabled_RegistrationsCancelsAndDisposesRecordNothingAndReadNoFrame()
        {
            var area = _t.App.CreateChild("area");
            var counter = new Counter();
            LifetimeDiagnostics.TrackingEnabled = false;
            var framesRead = _d.FrameProviderCalls;

            for (var i = 0; i < 5; i++)
            {
                area.OnCancel(counter, static c => c.Value++);
            }

            area.Cancel();
            area.OnCancel(() => { });
            area.Dispose();

            Assert.That(_d.FrameProviderCalls, Is.EqualTo(framesRead), "with tracking off the core never asks for the frame");
            Assert.That(area.DiagTotalRegistered, Is.Zero);
            Assert.That(area.DiagCancelCount, Is.Zero);
            Assert.That(area.DiagRefusedCount, Is.Zero);
            Assert.That(area.DiagDisposedFrame, Is.EqualTo(-1));
            Assert.That(LifetimeDiagnostics.RecentCount, Is.Zero, "a disposal is not recorded");
            Assert.That(counter.Value, Is.EqualTo(5), "the work itself is unaffected");
        }

        [Test]
        public void TrackingEnabled_TheSameOperations_ReadTheFrameAndRecord()
        {
            var area = _t.App.CreateChild("area");
            var framesRead = _d.FrameProviderCalls;

            area.OnCancel(() => { });
            area.Cancel();
            area.Dispose();

            Assert.That(_d.FrameProviderCalls, Is.GreaterThan(framesRead), "the control for the test above");
            Assert.That(area.DiagTotalRegistered, Is.EqualTo(1));
            Assert.That(area.DiagCancelCount, Is.EqualTo(1));
            Assert.That(LifetimeDiagnostics.RecentCount, Is.EqualTo(1));
        }

        [Test]
        public void TrackingDisabled_ConsoleWarningsAreStillLoggedButNotRecorded()
        {
            LifetimeDiagnostics.TrackingEnabled = false;
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR107"));

            _t.App.Dispose();

            Assert.That(_d.Total, Is.Zero);
        }

        [Test]
        public void TrackingDisabled_GrowthIsNotCountedOrRecorded()
        {
            LifetimeDiagnostics.TrackingEnabled = false;
            var area = _t.App.CreateChild("crowded");

            for (var i = 0; i < 300; i++)
            {
                area.OnCancel(() => { });
            }

            Assert.That(_d.Total, Is.Zero);
            Assert.That(area.DiagTotalRegistered, Is.Zero);
        }

        [Test]
        public void TrackingDisabled_EntriesGetNoRegistrationFrame_AndOnesRegisteredWhileOnKeepTheirs()
        {
            var area = _t.App.CreateChild("area");
            _d.Frame = 100;
            area.OnCancel(() => { }, "WhileOn", 1);
            LifetimeDiagnostics.TrackingEnabled = false;
            _d.Frame = 110;
            area.OnCancel(() => { }, "WhileOff", 2);
            var entries = new List<EntryView>();

            LifetimeDiagnostics.CaptureEntries(area, entries);

            Assert.That(entries.Count, Is.EqualTo(2));
            Assert.That(entries[0].Member, Is.EqualTo("WhileOff"));
            Assert.That(entries[0].RegisteredFrame, Is.EqualTo(-1), "never stamped");
            Assert.That(entries[0].AgeFrames, Is.EqualTo(-1), "the age is unknown rather than wrong");
            Assert.That(entries[1].RegisteredFrame, Is.EqualTo(100));
        }

        [Test]
        public void TrackingDisabled_TheSnapshotStillShowsTheTree()
        {
            var area = _t.App.CreateChild("area");
            area.OnCancel(() => { });
            LifetimeDiagnostics.TrackingEnabled = false;

            LifetimeDiagnostics.CaptureDefault(_snapshot);

            var node = SnapshotQueries.NodeOf(_snapshot, area);
            Assert.That(node.EntryCount, Is.EqualTo(1), "structure is read from the tree, not from the counters");
            Assert.That(node.Label, Is.EqualTo("area"));
        }

        [Test]
        public void TrackingEnabledAgain_RecordingResumes()
        {
            var area = _t.App.CreateChild("area");
            LifetimeDiagnostics.TrackingEnabled = false;
            area.OnCancel(() => { });
            LifetimeDiagnostics.TrackingEnabled = true;

            area.OnCancel(() => { });
            LifetimeDiagnostics.Report(area, "TEST100", "back");

            Assert.That(area.DiagTotalRegistered, Is.EqualTo(1), "only the registration made while on is counted");
            Assert.That(_d.Single("TEST100").Lifetime, Is.SameAs(area));
        }

        // CaptureStackTraces

        [Test]
        public void CaptureStackTraces_On_StoresATracePerRegistrationOfEveryKind()
        {
            var area = _t.App.CreateChild("area");
            var counter = new Counter();
            var source = Cysharp.Threading.Tasks.AutoResetUniTaskCompletionSource.Create();
            Action handler = () => { };
            var coins = new OwnedEvent<int>("Coins");
            LifetimeDiagnostics.CaptureStackTraces = true;

            area.OnCancel(() => { });
            area.OnCancel(counter, static c => c.Value++);
            new DisposeProbe().AddTo(area);
            area.Subscribe(h => { }, h => { }, handler);
            coins.Subscribe(_ => { }, area);
            area.Run(source, static (s, ct) => s.Task);
            var entries = new List<EntryView>();
            LifetimeDiagnostics.CaptureEntries(area, entries);

            Assert.That(entries.Count, Is.EqualTo(6));
            for (var i = 0; i < entries.Count; i++)
            {
                Assert.That(entries[i].HasTrace, Is.True, "entry " + i + " (" + entries[i].FormatLabel() + ")");
                var trace = entries[i].FormatTrace();
                Assert.That(trace, Is.Not.Null.And.Not.Empty);
                Assert.That(trace, Does.Contain(nameof(DiagnosticsToggleTests)), "the trace reaches the calling test");
            }

            source.TrySetResult();
        }

        [Test]
        public void CaptureStackTraces_Off_StoresNoTrace()
        {
            var area = _t.App.CreateChild("area");

            area.OnCancel(() => { });
            new DisposeProbe().AddTo(area);
            var entries = new List<EntryView>();
            LifetimeDiagnostics.CaptureEntries(area, entries);

            Assert.That(entries.Count, Is.EqualTo(2));
            Assert.That(entries[0].HasTrace, Is.False);
            Assert.That(entries[1].HasTrace, Is.False);
            Assert.That(entries[0].FormatTrace(), Is.Null);
        }

        [Test]
        public void CaptureStackTraces_ToggledOff_OnlyLaterRegistrationsLoseTheTrace()
        {
            var area = _t.App.CreateChild("area");
            LifetimeDiagnostics.CaptureStackTraces = true;
            area.OnCancel(() => { }, "TracedSite", 1);
            LifetimeDiagnostics.CaptureStackTraces = false;
            area.OnCancel(() => { }, "PlainSite", 2);
            var entries = new List<EntryView>();

            LifetimeDiagnostics.CaptureEntries(area, entries);

            Assert.That(entries[0].Member, Is.EqualTo("PlainSite"));
            Assert.That(entries[0].HasTrace, Is.False);
            Assert.That(entries[1].Member, Is.EqualTo("TracedSite"));
            Assert.That(entries[1].HasTrace, Is.True, "a trace already stored stays with its entry");
        }

        [Test]
        public void CaptureStackTraces_OnWhileTrackingIsOff_StoresNothing()
        {
            var area = _t.App.CreateChild("area");
            LifetimeDiagnostics.TrackingEnabled = false;
            LifetimeDiagnostics.CaptureStackTraces = true;

            area.OnCancel(() => { });
            var entries = new List<EntryView>();
            LifetimeDiagnostics.CaptureEntries(area, entries);

            Assert.That(entries[0].HasTrace, Is.False, "the master switch wins");
        }

        [Test]
        public void CaptureStackTraces_OnARefusedRegistration_IsHarmless()
        {
            var area = _t.App.CreateChild("area");
            LifetimeDiagnostics.CaptureStackTraces = true;
            area.Dispose();

            Assert.DoesNotThrow(() => area.OnCancel(() => { }));

            Assert.That(area.DiagRefusedCount, Is.EqualTo(1));
        }
    }
}
