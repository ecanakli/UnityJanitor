using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // The "recently disposed" ring buffer of the last 200 lifetimes, as plain structs. It is fed when a lifetime is
    // finalized as disposed, never during the application exit, and it outlives a play session (the warnings do not).
    [TestFixture]
    public sealed class RecentlyDisposedTests
    {
        private DiagnosticsRecordingKit _d;
        private TestScope _t;

        [SetUp]
        public void SetUp()
        {
            _d = new DiagnosticsRecordingKit();
            _t = new TestScope();
            _t.Tree.MakeDefault();
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

        private static List<RecentLifetime> Recent()
        {
            var recent = new List<RecentLifetime>();
            LifetimeDiagnostics.CopyRecent(recent);
            return recent;
        }

        [Test]
        public void Dispose_RecordsTheLifetimeWithItsCountersAndFrames()
        {
            _d.Frame = 110;
            var area = _t.App.CreateChild("popups");
            var child = area.CreateChild("child");
            var counter = new Counter();
            area.OnCancel(counter, static c => c.Value++);
            area.OnCancel(counter, static c => c.Value++);
            area.Cancel();
            area.Cancel();
            area.Cancel();
            area.OnCancel(counter, static c => c.Value++);
            _d.Frame = 150;

            area.Dispose();

            var recent = Recent();
            Assert.That(recent.Count, Is.EqualTo(2), "the child and the area");
            var entry = recent[0];
            Assert.That(entry.FormatLabel(), Is.EqualTo("popups"));
            Assert.That(entry.Kind, Is.EqualTo(LifetimeKind.Area));
            Assert.That(entry.Generation, Is.EqualTo(3), "the last generation the lifetime lived in");
            Assert.That(entry.CancelCount, Is.EqualTo(3));
            Assert.That(entry.TotalRegistered, Is.EqualTo(3));
            Assert.That(entry.RefusedCount, Is.Zero);
            Assert.That(entry.CreatedFrame, Is.EqualTo(110));
            Assert.That(entry.DisposedFrame, Is.EqualTo(150));
            Assert.That(entry.LifetimeFrames, Is.EqualTo(40));
            Assert.That(entry.Session, Is.Zero);
            Assert.That(entry.Id, Is.EqualTo(area.DiagId));
            Assert.That(entry.ParentId, Is.EqualTo(_t.App.DiagId));
            Assert.That(recent[1].FormatLabel(), Is.EqualTo("child"));
            Assert.That(recent[1].ParentId, Is.EqualTo(area.DiagId), "the parent is read before the lifetime is unlinked");
        }

        [Test]
        public void Dispose_AnUnnamedArea_IsLabelledByItsCallSiteLazily()
        {
            var area = _t.App.CreateChild();

            area.Dispose();

            var entry = Recent()[0];
            Assert.That(entry.Name, Is.Null, "only cheap pieces are stored");
            Assert.That(entry.Member, Is.EqualTo(nameof(Dispose_AnUnnamedArea_IsLabelledByItsCallSiteLazily)));
            Assert.That(entry.Line, Is.EqualTo(area.CreatorLine));
            Assert.That(entry.FormatLabel(), Is.EqualTo(nameof(Dispose_AnUnnamedArea_IsLabelledByItsCallSiteLazily) + ":" + area.CreatorLine));
        }

        [Test]
        public void Dispose_AnObjectLifetime_RecordsItsKindAndLabel()
        {
            var lifetime = _t.App.CreateChildCore("owned", LifetimeKind.Component, true, null, null, 0);

            lifetime.DisposeFromOwner();

            var entry = Recent()[0];
            Assert.That(entry.Kind, Is.EqualTo(LifetimeKind.Component));
            Assert.That(entry.FormatLabel(), Is.EqualTo("owned"));
        }

        [Test]
        public void Dispose_ASubtree_RecordsEveryLifetimeDeepestFirstSoTheRootIsTheNewest()
        {
            var root = _t.App.CreateChild("R");
            var first = root.CreateChild("C1");
            first.CreateChild("G");
            root.CreateChild("C2");

            root.Dispose();

            var labels = new List<string>();
            var recent = Recent();
            for (var i = 0; i < recent.Count; i++)
            {
                labels.Add(recent[i].FormatLabel());
            }

            Assert.That(labels, Is.EqualTo(new[] { "R", "C1", "G", "C2" }), "newest first; teardown disposes the deepest and the newest sibling first");
        }

        [Test]
        public void Dispose_AnAlreadyDisposedLifetime_IsRecordedOnlyOnce()
        {
            var area = _t.App.CreateChild("area");

            area.Dispose();
            area.Dispose();

            Assert.That(Recent().Count, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_TheBuffer_IsCappedAt200AndNewestFirst()
        {
            for (var i = 0; i < 250; i++)
            {
                _t.App.CreateChild("a" + i).Dispose();
            }

            var recent = Recent();

            Assert.That(LifetimeDiagnostics.RecentCapacity, Is.EqualTo(200));
            Assert.That(LifetimeDiagnostics.RecentCount, Is.EqualTo(200));
            Assert.That(recent.Count, Is.EqualTo(200));
            Assert.That(recent[0].FormatLabel(), Is.EqualTo("a249"), "the newest first");
            Assert.That(recent[199].FormatLabel(), Is.EqualTo("a50"), "the 50 oldest were dropped");
            for (var i = 1; i < recent.Count; i++)
            {
                Assert.That(recent[i].DisposedFrame, Is.LessThanOrEqualTo(recent[i - 1].DisposedFrame));
            }
        }

        [Test]
        public void Dispose_TheBuffer_DropsTheOldestExactlyAtTheTwoHundredAndFirst()
        {
            for (var i = 0; i < 200; i++)
            {
                _t.App.CreateChild("a" + i).Dispose();
            }

            Assert.That(Recent()[199].FormatLabel(), Is.EqualTo("a0"), "200 fit");

            _t.App.CreateChild("a200").Dispose();

            var recent = Recent();
            Assert.That(recent.Count, Is.EqualTo(200));
            Assert.That(recent[0].FormatLabel(), Is.EqualTo("a200"));
            Assert.That(recent[199].FormatLabel(), Is.EqualTo("a1"), "the 201st pushed the first one out");
        }

        [Test]
        public void Dispose_Versions_ChangeWithEveryRecord()
        {
            var before = LifetimeDiagnostics.RecentVersion;

            _t.App.CreateChild("a").Dispose();

            Assert.That(LifetimeDiagnostics.RecentVersion, Is.Not.EqualTo(before));
        }

        [Test]
        public void Dispose_WhileTrackingIsOff_RecordsNothing()
        {
            LifetimeDiagnostics.TrackingEnabled = false;

            _t.App.CreateChild("a").Dispose();

            Assert.That(LifetimeDiagnostics.RecentCount, Is.Zero);
        }

        [Test]
        public void Shutdown_TheWholeTreeGoingDown_IsNotRecorded()
        {
            _t.App.CreateChild("before").Dispose();
            var kept = _t.App.CreateChild("kept");
            kept.CreateChild("nested");
            for (var i = 0; i < 30; i++)
            {
                _t.App.CreateChild("bulk" + i);
            }

            _t.Tree.Shutdown();

            var recent = Recent();
            Assert.That(recent.Count, Is.EqualTo(1), "the shutdown must not flood the buffer with the whole tree");
            Assert.That(recent[0].FormatLabel(), Is.EqualTo("before"));
        }

        [Test]
        public void SessionStarted_KeepsTheBufferAndTagsLaterEntriesWithTheNewSession()
        {
            _t.App.CreateChild("old").Dispose();

            LifetimeDiagnostics.SessionStarted();
            _t.App.CreateChild("new").Dispose();

            var recent = Recent();
            Assert.That(recent.Count, Is.EqualTo(2), "the history survives a new play session");
            Assert.That(recent[0].FormatLabel(), Is.EqualTo("new"));
            Assert.That(recent[0].Session, Is.EqualTo(1));
            Assert.That(recent[1].FormatLabel(), Is.EqualTo("old"));
            Assert.That(recent[1].Session, Is.Zero);
            Assert.That(LifetimeDiagnostics.Session, Is.EqualTo(1));
        }

        [Test]
        public void SessionStarted_ClearsTheWarningsAndTheTrackedTasks()
        {
            LifetimeDiagnostics.Report(null, "TEST100", "from the old session");
            var source = Cysharp.Threading.Tasks.AutoResetUniTaskCompletionSource.Create();
            var area = _t.App.CreateChild("area");
            area.Run(source, static (s, ct) => s.Task);
            area.Cancel();
            Assert.That(_d.Total, Is.EqualTo(1));
            Assert.That(TaskOverrunTracker.Count, Is.EqualTo(1));

            LifetimeDiagnostics.SessionStarted();

            Assert.That(_d.Total, Is.Zero, "warnings belong to one session");
            Assert.That(TaskOverrunTracker.Count, Is.Zero);
            source.TrySetResult();
            Assert.That(TaskOverrunTracker.Count, Is.Zero, "a task of the old session finishing later is harmless");
        }

        [Test]
        public void SessionStarted_EveryCallIsANewSession()
        {
            LifetimeDiagnostics.SessionStarted();
            LifetimeDiagnostics.SessionStarted();
            LifetimeDiagnostics.SessionStarted();

            Assert.That(LifetimeDiagnostics.Session, Is.EqualTo(3));
        }

        [Test]
        public void SessionStarted_AWarningRecordedAfterwards_CarriesTheNewSession()
        {
            LifetimeDiagnostics.SessionStarted();
            LifetimeDiagnostics.SessionStarted();
            LifetimeDiagnostics.Report(null, "TEST100", "now");
            var rows = new List<DiagnosticWarning>();

            LifetimeDiagnostics.CopyWarnings(rows);

            Assert.That(rows[0].Session, Is.EqualTo(2));
        }
    }
}
