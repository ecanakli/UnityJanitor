using System;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // A fault inside the diagnostics is contained: Cancel, Dispose and registrations never see it, nothing is routed to
    // LifetimeErrors, and it is logged once per play session. The seam is LifetimeDiagnostics.FrameProvider, which every
    // recording hook calls, and LifetimeDiagnostics.Scheduler for the overrun scan.
    [TestFixture]
    public sealed class DiagnosticsFailureTests
    {
        // Unity logs the innermost exception of a wrapped failure, so the log text is the seam's own message ("frame boom").
        private static readonly Regex ContainedFailure = new Regex("frame boom");

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

        private static int ThrowingFrame()
        {
            throw new InvalidOperationException("frame boom");
        }

        private static void FailFrames()
        {
            LifetimeDiagnostics.FrameProvider = ThrowingFrame;
        }

        [Test]
        public void Register_WithAThrowingFrameProvider_StillRegistersAndLogsTheFailureOnce()
        {
            var area = _t.App.CreateChild("area");
            var ran = 0;
            FailFrames();
            LogAssert.Expect(LogType.Exception, ContainedFailure);

            var first = area.OnCancel(() => ran++);
            var second = area.OnCancel(() => ran++);

            Assert.That(first.IsActive, Is.True);
            Assert.That(second.IsActive, Is.True);
            area.Cancel();
            Assert.That(ran, Is.EqualTo(2), "every item still terminates");
            Assert.That(_t.Errors.Count, Is.Zero, "a diagnostics fault is not a lifetime error");
        }

        [Test]
        public void CreateChild_WithAThrowingFrameProvider_StillCreatesAUsableLifetime()
        {
            FailFrames();
            LogAssert.Expect(LogType.Exception, ContainedFailure);

            var area = _t.App.CreateChild("area");

            Assert.That(area.IsDisposed, Is.False);
            Assert.That(area.Parent, Is.SameAs(_t.App));
            var ran = 0;
            area.OnCancel(() => ran++);
            area.Cancel();
            Assert.That(ran, Is.EqualTo(1));
        }

        [Test]
        public void Teardown_WithAThrowingFrameProvider_RunsEveryTerminateActionAndStillDisposes()
        {
            var area = _t.App.CreateChild("area");
            var child = area.CreateChild("child");
            var counter = new Counter();
            var source = AutoResetUniTaskCompletionSource.Create();
            for (var i = 0; i < 10; i++)
            {
                area.OnCancel(counter, static c => c.Value++);
                child.OnCancel(counter, static c => c.Value++);
            }

            area.Run(source, static (s, ct) => s.Task);
            FailFrames();
            LogAssert.Expect(LogType.Exception, ContainedFailure);

            area.Cancel();
            area.Dispose();

            Assert.That(counter.Value, Is.EqualTo(20));
            Assert.That(area.IsDisposed, Is.True);
            Assert.That(child.IsDisposed, Is.True);
            Assert.That(_t.Errors.Count, Is.Zero);
            Assert.That(TaskOverrunTracker.Count, Is.Zero, "the overrun record could not be made, and nothing broke");
            source.TrySetResult();
        }

        [Test]
        public void Failure_IsLoggedOncePerSession_AndAgainAfterTheNextSessionStarts()
        {
            var area = _t.App.CreateChild("area");
            FailFrames();
            LogAssert.Expect(LogType.Exception, ContainedFailure);

            area.OnCancel(() => { });
            area.OnCancel(() => { });
            area.OnCancel(() => { });
            LifetimeDiagnostics.SessionStarted();
            LogAssert.Expect(LogType.Exception, ContainedFailure);
            area.OnCancel(() => { });
            area.OnCancel(() => { });

            Assert.That(area.EntryCount, Is.EqualTo(5), "every registration worked");
        }

        [Test]
        public void Capture_WithAThrowingFrameProvider_IsContained()
        {
            _t.App.CreateChild("area");
            FailFrames();
            LogAssert.Expect(LogType.Exception, ContainedFailure);

            Assert.DoesNotThrow(() => LifetimeDiagnostics.CaptureDefault(_snapshot));
        }

        [Test]
        public void CaptureEntries_WithAThrowingFrameProvider_IsContained()
        {
            var area = _t.App.CreateChild("area");
            area.OnCancel(() => { });
            FailFrames();
            LogAssert.Expect(LogType.Exception, ContainedFailure);
            var entries = new System.Collections.Generic.List<EntryView>();

            Assert.DoesNotThrow(() => LifetimeDiagnostics.CaptureEntries(area, entries));
        }

        [Test]
        public void Report_WithAThrowingFrameProvider_NeverThrowsAndRecordsNothing()
        {
            FailFrames();
            LogAssert.Expect(LogType.Exception, ContainedFailure);

            Assert.DoesNotThrow(() => LifetimeDiagnostics.Report(_t.App, "TEST100", "message"));

            LifetimeDiagnostics.FrameProvider = null;
            Assert.That(_d.Total, Is.Zero);
        }

        [Test]
        public void Dispose_WithAThrowingFrameProvider_IsContainedAndNothingIsRecorded()
        {
            var area = _t.App.CreateChild("area");
            FailFrames();
            LogAssert.Expect(LogType.Exception, ContainedFailure);

            Assert.DoesNotThrow(() => area.Dispose());

            Assert.That(area.IsDisposed, Is.True);
            Assert.That(LifetimeDiagnostics.RecentCount, Is.Zero, "the record could not be completed");
        }

        [Test]
        public void Cancel_WithAThrowingScheduler_IsContainedAndTheNextCaptureStillFindsTheOverrun()
        {
            var area = _t.App.CreateChild("area");
            var source = AutoResetUniTaskCompletionSource.Create();
            area.Run(source, static (s, ct) => s.Task);
            LifetimeDiagnostics.Scheduler = static scan => throw new InvalidOperationException("scheduler boom");

            Assert.DoesNotThrow(() => area.Cancel());

            Assert.That(TaskOverrunTracker.Count, Is.EqualTo(1), "the task is tracked even though no scan could be scheduled");
            _d.AdvanceFrames(4);
            LifetimeDiagnostics.CaptureDefault(_snapshot);
            Assert.That(_d.Single(DiagnosticIds.TaskOverrun).Lifetime, Is.SameAs(area));
            source.TrySetResult();
        }

        [Test]
        public void Cancel_AfterAFailedSchedule_AsksTheSchedulerAgainNextTime()
        {
            var area = _t.App.CreateChild("area");
            var calls = 0;
            LifetimeDiagnostics.Scheduler = scan =>
            {
                calls++;
                throw new InvalidOperationException("scheduler boom");
            };
            var first = AutoResetUniTaskCompletionSource.Create();
            var second = AutoResetUniTaskCompletionSource.Create();
            area.Run(first, static (s, ct) => s.Task);
            area.Cancel();
            area.Run(second, static (s, ct) => s.Task);
            area.Cancel();

            Assert.That(calls, Is.EqualTo(2), "a failed schedule must not leave the scan marked as scheduled for good");
            first.TrySetResult();
            second.TrySetResult();
        }

        [Test]
        public void Registration_AfterAContainedFailure_RecordsNormallyOnceTheFaultIsGone()
        {
            var area = _t.App.CreateChild("area");
            FailFrames();
            LogAssert.Expect(LogType.Exception, ContainedFailure);
            area.OnCancel(() => { });
            LifetimeDiagnostics.FrameProvider = null;

            area.OnCancel(() => { });
            LifetimeDiagnostics.Report(area, "TEST100", "recovered");

            Assert.That(_d.Single("TEST100").Lifetime, Is.SameAs(area));
            Assert.That(area.DiagTotalRegistered, Is.EqualTo(2), "the first registration was counted before the fault, the second after");
        }
    }
}
