using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // JANITOR101: a Run, After or Every task still incomplete more than N frames (default 3) after its generation
    // ended is reported once. Frames and the scan are driven by the kit, so every boundary is exact.
    [TestFixture]
    public sealed class TaskOverrunTests
    {
        private DiagnosticsRecordingKit _d;
        private TestScope _t;
        private ManualClock _clock;
        private Lifetime _area;
        private DiagnosticsSnapshot _snapshot;

        [SetUp]
        public void SetUp()
        {
            _d = new DiagnosticsRecordingKit();
            _t = new TestScope();
            _t.Tree.MakeDefault();
            _clock = _t.Tree.UseManualClock();
            _area = _t.App.CreateChild("area");
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

        // A task that ignores the lifetime token: it ends only when the test completes the source.
        private AutoResetUniTaskCompletionSource StartIgnoringTask(Lifetime lifetime, int line = 41)
        {
            var source = AutoResetUniTaskCompletionSource.Create();
            lifetime.Run(source, static (s, ct) => s.Task, "TaskSite", line);
            return source;
        }

        // The scan the core scheduled, run at the current kit frame.
        private void ScanAt(int framesAfterNow)
        {
            _d.AdvanceFrames(framesAfterNow);
            _d.RunScheduledScan();
        }

        [Test]
        public void Cancel_ATaskThatIgnoresTheToken_IsReportedOnlyAfterMoreThanOverrunFrames()
        {
            var source = StartIgnoringTask(_area);

            _area.Cancel();

            Assert.That(TaskOverrunTracker.Count, Is.EqualTo(1));
            Assert.That(_d.ScanScheduled, Is.True, "the core asks for a scan only when a task is tracked");
            ScanAt(3);
            Assert.That(_d.Count(DiagnosticIds.TaskOverrun), Is.Zero, "3 frames after the end is not more than 3");
            Assert.That(_d.ScanScheduled, Is.True, "an unreported task keeps the scan alive");
            ScanAt(1);

            var warning = _d.Single(DiagnosticIds.TaskOverrun);
            Assert.That(warning.Lifetime, Is.SameAs(_area));
            Assert.That(warning.LifetimeLabel, Is.EqualTo("area"));
            Assert.That(warning.Member, Is.EqualTo("TaskSite"), "the call site of the Run that started it");
            Assert.That(warning.Line, Is.EqualTo(41));
            Assert.That(warning.Message, Does.Contain("Run task").And.Contain("TaskSite:41").And.Contain("more than 3 frames"));
            Assert.That(warning.Count, Is.EqualTo(1));
            Assert.That(warning.IsInfo, Is.False);
            Assert.That(_d.ScanScheduled, Is.False, "once everything tracked is reported the scan stops asking");

            source.TrySetResult();
        }

        [Test]
        public void Scan_AReportedTask_IsNotReportedAgainOnLaterScans()
        {
            var source = StartIgnoringTask(_area);
            _area.Cancel();
            ScanAt(10);

            _d.AdvanceFrames(50);
            LifetimeDiagnostics.CaptureDefault(_snapshot);
            TaskOverrunTracker.Scan(_d.Frame);
            TaskOverrunTracker.Scan(_d.Frame + 100);

            Assert.That(_d.Single(DiagnosticIds.TaskOverrun).Count, Is.EqualTo(1), "once per task, however often it is scanned");
            Assert.That(TaskOverrunTracker.Count, Is.EqualTo(1), "the record stays until the task finishes, for the details pane");

            source.TrySetResult();
            Assert.That(TaskOverrunTracker.Count, Is.Zero);
        }

        [Test]
        public void OverrunFrames_IsConfigurable()
        {
            LifetimeDiagnostics.OverrunFrames = 10;
            var source = StartIgnoringTask(_area);
            _area.Cancel();

            ScanAt(10);
            Assert.That(_d.Count(DiagnosticIds.TaskOverrun), Is.Zero, "10 frames is not more than 10");
            ScanAt(1);

            Assert.That(_d.Single(DiagnosticIds.TaskOverrun).Message, Does.Contain("more than 10 frames"));
            source.TrySetResult();
        }

        [Test]
        public void OverrunFrames_Zero_IsRaisedToTheMinimumOfOne()
        {
            LifetimeDiagnostics.OverrunFrames = 0;
            var source = StartIgnoringTask(_area);
            _area.Cancel();

            ScanAt(1);
            Assert.That(_d.Count(DiagnosticIds.TaskOverrun), Is.Zero, "the next frame is the first chance a cancelled await gets to observe its token");
            ScanAt(1);

            var warning = _d.Single(DiagnosticIds.TaskOverrun);
            Assert.That(warning.Count, Is.EqualTo(1));
            Assert.That(warning.Message, Does.Contain("more than 1 frame after"));
            source.TrySetResult();
        }

        [Test]
        public void OverrunFrames_Negative_IsRaisedToTheMinimumToo()
        {
            LifetimeDiagnostics.OverrunFrames = -5;
            var source = StartIgnoringTask(_area);
            _area.Cancel();

            ScanAt(1);

            Assert.That(_d.Count(DiagnosticIds.TaskOverrun), Is.Zero);
            source.TrySetResult();
        }

        [Test]
        public void Scan_TheMessage_DoesNotClaimTheTokenWasMissingForATaskThatObservesItLate()
        {
            var source = StartIgnoringTask(_area);
            _area.Cancel();

            ScanAt(4);

            var message = _d.Single(DiagnosticIds.TaskOverrun).Message;
            Assert.That(message, Does.Contain("Pass the lifetime's token to every await"));
            Assert.That(message, Does.Contain("observes the token late").And.Contain("FixedUpdate").And.Contain("thread-pool"));
            source.TrySetResult();
        }

        [Test]
        public void Shutdown_OfTheTree_DoesNotTrackTheTasksItEnds()
        {
            var source = StartIgnoringTask(_area);

            _t.Tree.Shutdown();

            Assert.That(TaskOverrunTracker.Count, Is.Zero, "a session ending takes every task down at once; those records would only linger");
            Assert.That(_d.ScanScheduled, Is.False);
            source.TrySetResult();
        }

        [Test]
        public void Cancel_ATaskThatFinishesWithinTheThreshold_IsNeverReported()
        {
            var source = StartIgnoringTask(_area);
            _area.Cancel();
            _d.AdvanceFrames(2);

            source.TrySetResult();
            ScanAt(20);

            Assert.That(TaskOverrunTracker.Count, Is.Zero, "finishing drops the record");
            Assert.That(_d.Count(DiagnosticIds.TaskOverrun), Is.Zero);
        }

        [Test]
        public void Cancel_ATaskThatHonoursTheToken_IsNeverTracked()
        {
            var source = AutoResetUniTaskCompletionSource.Create();
            _area.Run(source, static (s, ct) =>
            {
                ct.Register(static state => ((AutoResetUniTaskCompletionSource)state).TrySetCanceled(), s);
                return s.Task;
            });

            _area.Cancel();
            _d.AdvanceFrames(20);
            _d.RunScheduledScan();

            Assert.That(TaskOverrunTracker.Count, Is.Zero, "the token ended it before its generation's teardown reached the entry");
            Assert.That(_d.ScanScheduled, Is.False);
            Assert.That(_d.Count(DiagnosticIds.TaskOverrun), Is.Zero);
        }

        [Test]
        public void Cancel_ATimerThatHonoursTheCancelledDelay_IsNeverReported()
        {
            _area.Every(1f, new Counter(), static c => c.Value++);
            _area.Cancel();

            _clock.Advance(1f);
            ScanAt(20);

            Assert.That(TaskOverrunTracker.Count, Is.Zero, "the cancelled delay completed, so the timer stopped");
            Assert.That(_d.Count(DiagnosticIds.TaskOverrun), Is.Zero);
        }

        [Test]
        public void Cancel_AnEveryTimerWhoseDelayNeverWakes_IsReportedWithItsKind()
        {
            _area.Every(1f, new Counter(), static c => c.Value++, member: "TimerSite", line: 52);
            _area.Cancel();

            ScanAt(4);

            var warning = _d.Single(DiagnosticIds.TaskOverrun);
            Assert.That(warning.Message, Does.Contain("Every task").And.Contain("TimerSite:52"));
            Assert.That(warning.Lifetime, Is.SameAs(_area));

            _clock.Advance(1f);
            Assert.That(TaskOverrunTracker.Count, Is.Zero, "when the delay finally completes the timer stops and the record goes");
        }

        [Test]
        public void Cancel_AnAfterTimerWhoseDelayNeverWakes_IsReportedWithItsKind()
        {
            _area.After(1f, new Counter(), static c => c.Value++, member: "AfterSite", line: 61);
            _area.Cancel();

            ScanAt(4);

            Assert.That(_d.Single(DiagnosticIds.TaskOverrun).Message, Does.Contain("After task").And.Contain("AfterSite:61"));
        }

        [Test]
        public void Cancel_SeveralTasksOfSeveralLifetimes_AreEachReportedOnceWithTheirOwnLifetime()
        {
            var other = _t.App.CreateChild("other");
            var first = StartIgnoringTask(_area, 41);
            var second = StartIgnoringTask(other, 42);
            var third = StartIgnoringTask(_area, 43);
            _area.Cancel();
            other.Cancel();

            ScanAt(4);

            var rows = _d.Warnings(DiagnosticIds.TaskOverrun);
            Assert.That(rows.Count, Is.EqualTo(3), "tasks from different call sites are different entries");
            Assert.That(_d.Occurrences(DiagnosticIds.TaskOverrun), Is.EqualTo(3), "one report per task");
            var areaRows = 0;
            var otherRows = 0;
            for (var i = 0; i < rows.Count; i++)
            {
                areaRows += ReferenceEquals(rows[i].Lifetime, _area) ? 1 : 0;
                otherRows += ReferenceEquals(rows[i].Lifetime, other) ? 1 : 0;
            }

            Assert.That(areaRows, Is.EqualTo(2));
            Assert.That(otherRows, Is.EqualTo(1));
            first.TrySetResult();
            second.TrySetResult();
            third.TrySetResult();
            Assert.That(TaskOverrunTracker.Count, Is.Zero);
        }

        // The same lifetime, call site and age make the same cause: the entries merge and the counter keeps the number of tasks.
        [Test]
        public void Cancel_TwoTasksFromOneCallSiteOfOneLifetime_AreOneEntryWithACounterOfTwo()
        {
            var first = StartIgnoringTask(_area);
            var second = StartIgnoringTask(_area);
            _area.Cancel();

            ScanAt(4);

            Assert.That(TaskOverrunTracker.Count, Is.EqualTo(2), "both tasks were tracked and scanned");
            var warning = _d.Single(DiagnosticIds.TaskOverrun);
            Assert.That(warning.Count, Is.EqualTo(2), "each task was reported once, the repeat only raises the counter");
            first.TrySetResult();
            second.TrySetResult();
        }

        // The text names no frame count that depends on when the scan ran, so tasks of one call site that ended in different frames
        // and are found by one scan still make one entry.
        [Test]
        public void Scan_TwoTasksOfOneCallSiteThatEndedInDifferentFrames_AreOneEntryWithACounterOfTwo()
        {
            var first = StartIgnoringTask(_area, 52);
            _area.Cancel();
            _d.AdvanceFrames(1);
            var second = StartIgnoringTask(_area, 52);
            _area.Cancel();

            ScanAt(9);

            Assert.That(TaskOverrunTracker.Count, Is.EqualTo(2), "premise: the tasks ended 10 and 9 frames before the scan");
            var warning = _d.Single(DiagnosticIds.TaskOverrun);
            Assert.That(warning.Count, Is.EqualTo(2), "one row for the call site, with a counter of two");
            Assert.That(warning.Line, Is.EqualTo(52));
            Assert.That(warning.Message, Does.Contain("more than 3 frames"));
            first.TrySetResult();
            second.TrySetResult();
        }

        [Test]
        public void Scan_TwoTasksOfOneCallSiteReportedByDifferentScans_AreOneEntryWithACounterOfTwo()
        {
            var first = StartIgnoringTask(_area, 52);
            _area.Cancel();
            _d.AdvanceFrames(2);
            var second = StartIgnoringTask(_area, 52);
            _area.Cancel();

            ScanAt(2);
            Assert.That(_d.Single(DiagnosticIds.TaskOverrun).Count, Is.EqualTo(1), "only the first task is past the threshold");
            ScanAt(2);

            var warning = _d.Single(DiagnosticIds.TaskOverrun);
            Assert.That(warning.Count, Is.EqualTo(2), "the second task joins the same row");
            first.TrySetResult();
            second.TrySetResult();
        }

        [Test]
        public void Cancel_SeveralTasks_AskTheSchedulerOncePerQuietPeriod()
        {
            var first = StartIgnoringTask(_area);
            var second = StartIgnoringTask(_area);
            var third = StartIgnoringTask(_area);

            _area.Cancel();

            Assert.That(_d.SchedulerCalls, Is.EqualTo(1), "three ended tasks, one scheduled scan");
            ScanAt(1);
            Assert.That(_d.SchedulerCalls, Is.EqualTo(2), "an unreported task requeues the scan");
            ScanAt(5);
            Assert.That(_d.SchedulerCalls, Is.EqualTo(2), "nothing left to report, so the scan is not requeued");
            first.TrySetResult();
            second.TrySetResult();
            third.TrySetResult();
        }

        [Test]
        public void Dispose_ATaskThatIgnoresTheToken_IsReportedLikeACancelledOne()
        {
            var source = StartIgnoringTask(_area);
            _area.Dispose();

            ScanAt(4);

            Assert.That(_d.Single(DiagnosticIds.TaskOverrun).Lifetime, Is.SameAs(_area), "the report keeps the disposed lifetime");
            source.TrySetResult();
        }

        [Test]
        public void Capture_RunsTheScanItself_SoANeverScheduledScanStillReports()
        {
            var source = StartIgnoringTask(_area);
            _area.Cancel();
            _d.AdvanceFrames(4);

            LifetimeDiagnostics.CaptureDefault(_snapshot);

            Assert.That(_d.Single(DiagnosticIds.TaskOverrun).Lifetime, Is.SameAs(_area));
            Assert.That(_snapshot.Warnings.Count, Is.EqualTo(1), "the copied warning list already contains what the scan found");
            source.TrySetResult();
        }

        [Test]
        public void CaptureEntries_ATaskPastItsGeneration_ShowsAsOutlivedWithItsAge()
        {
            var source = StartIgnoringTask(_area);
            _area.Cancel();
            _d.AdvanceFrames(2);
            var entries = new List<EntryView>();

            LifetimeDiagnostics.CaptureEntries(_area, entries);

            Assert.That(entries.Count, Is.EqualTo(1), "the entry list is empty after the cancel; only the outlived task remains");
            Assert.That(entries[0].Status, Is.EqualTo(EntryStatus.Outlived));
            Assert.That(entries[0].Kind, Is.EqualTo(EntryKind.Task));
            Assert.That(entries[0].FormatLabel(), Is.EqualTo("Task.Run"));
            Assert.That(entries[0].AgeFrames, Is.EqualTo(2), "frames since the generation ended");
            Assert.That(entries[0].Member, Is.EqualTo("TaskSite"));
            Assert.That(entries[0].Line, Is.EqualTo(41));
            source.TrySetResult();
        }

        [Test]
        public void Cancel_WhileTrackingIsOff_TracksNothingAndAsksForNoScan()
        {
            LifetimeDiagnostics.TrackingEnabled = false;
            var source = StartIgnoringTask(_area);

            _area.Cancel();
            ScanAt(20);

            Assert.That(TaskOverrunTracker.Count, Is.Zero);
            Assert.That(_d.SchedulerCalls, Is.Zero);
            Assert.That(_d.Total, Is.Zero);
            source.TrySetResult();
        }

        [Test]
        public void Cancel_AFinishedTasksPooledEntry_DoesNotLeakIntoTheNextTask()
        {
            var first = StartIgnoringTask(_area);
            _area.Cancel();
            first.TrySetResult();
            Assert.That(TaskOverrunTracker.Count, Is.Zero);

            var second = StartIgnoringTask(_area);
            _area.Cancel();
            ScanAt(4);

            var warning = _d.Single(DiagnosticIds.TaskOverrun);
            Assert.That(warning.Count, Is.EqualTo(1), "the pooled entry object was reused, but only the second task is reported");
            second.TrySetResult();
        }

        [Test]
        public void Cancel_MoreThan512OutlivedTasks_KeepsTheNewest512()
        {
            var sources = new List<AutoResetUniTaskCompletionSource>();
            var areas = new[] { _t.App.CreateChild("a"), _t.App.CreateChild("b"), _t.App.CreateChild("c") };
            for (var i = 0; i < 513; i++)
            {
                sources.Add(StartIgnoringTask(areas[i % 3]));
            }

            areas[0].Cancel();
            areas[1].Cancel();
            areas[2].Cancel();

            Assert.That(TaskOverrunTracker.Count, Is.EqualTo(512), "the list is capped, so a leak cannot grow it without bound");
            for (var i = 0; i < sources.Count; i++)
            {
                sources[i].TrySetResult();
            }

            Assert.That(TaskOverrunTracker.Count, Is.Zero, "a task whose record was dropped finishes without trouble");
        }
    }
}
