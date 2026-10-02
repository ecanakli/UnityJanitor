using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // Allocation with the diagnostics compiled in (they are, in every EditMode run): tracking on and stack traces off keep the
    // registration hot path at 0 B, the opt-in trace is the only allocator, and a snapshot refresh of an unchanged tree allocates
    // nothing beyond its reusable buffers. Each measured delegate runs once first: Mono allocates when an un-run lambda first runs.
    [TestFixture]
    public sealed class DiagnosticsAllocationTests
    {
        private const int Entries = 64;

        private DiagnosticsRecordingKit _d;
        private TestScope _t;

        [SetUp]
        public void SetUp()
        {
            _d = new DiagnosticsRecordingKit(manualFrames: false);
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

        private static void RegisterCounted(Lifetime area, Counter counter, int count)
        {
            for (var i = 0; i < count; i++)
            {
                area.OnCancel(counter, static c => c.Value++);
            }
        }

        // Registration

        [Test]
        public void OnCancel_StateWithStaticAction_WithTrackingOnAndStackTracesOff_AllocatesNothing()
        {
            LifetimeDiagnostics.TrackingEnabled = true;
            LifetimeDiagnostics.CaptureStackTraces = false;
            var area = _t.App.CreateChild("area");
            var counter = new Counter();
            TestDelegate measured = () => RegisterCounted(area, counter, Entries);
            measured();
            area.Cancel();
            var registeredBefore = area.DiagTotalRegistered;

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(area.EntryCount, Is.EqualTo(Entries), "the measured block must really have registered");
            Assert.That(area.DiagTotalRegistered - registeredBefore, Is.EqualTo(Entries), "and the diagnostics really recorded it");
            var entries = new List<EntryView>();
            LifetimeDiagnostics.CaptureEntries(area, entries);
            Assert.That(entries[0].RegisteredFrame, Is.GreaterThanOrEqualTo(0), "every entry was stamped with its frame, without allocating");
            Assert.That(entries[0].HasTrace, Is.False);
        }

        [Test]
        public void OnCancel_StateWithStaticAction_WithAManualFrameProvider_AllocatesNothing()
        {
            _d.Dispose();
            _d = new DiagnosticsRecordingKit();
            var area = _t.App.CreateChild("area");
            var counter = new Counter();
            TestDelegate measured = () => RegisterCounted(area, counter, Entries);
            measured();
            area.Cancel();
            var framesRead = _d.FrameProviderCalls;

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(_d.FrameProviderCalls - framesRead, Is.EqualTo(Entries), "one frame read per registration");
        }

        [Test]
        public void OnCancel_StateWithStaticAction_WithTrackingOff_AllocatesNothing()
        {
            LifetimeDiagnostics.TrackingEnabled = false;
            var area = _t.App.CreateChild("area");
            var counter = new Counter();
            TestDelegate measured = () => RegisterCounted(area, counter, Entries);
            measured();
            area.Cancel();

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(area.EntryCount, Is.EqualTo(Entries));
            Assert.That(area.DiagTotalRegistered, Is.Zero, "tracking off records and costs nothing");
        }

        [Test]
        public void OnCancel_WithStackTracesOn_Allocates()
        {
            LifetimeDiagnostics.CaptureStackTraces = true;
            var area = _t.App.CreateChild("area");
            var counter = new Counter();
            TestDelegate measured = () => RegisterCounted(area, counter, 4);
            measured();
            area.Cancel();

            Assert.That(measured, Is.AllocatingGCMemory(), "the opt-in stack trace is the one thing that allocates per registration");
        }

        [Test]
        public void AddToAndOwnedEventSubscribe_WithTrackingOn_AllocateNothing()
        {
            var area = _t.App.CreateChild("area");
            var probe = new DisposeProbe();
            var coins = new OwnedEvent<int>("Coins");
            var owners = new Lifetime[Entries];
            for (var i = 0; i < owners.Length; i++)
            {
                owners[i] = area.CreateChild();
            }

            Action<int> handler = _ => { };
            TestDelegate measured = () =>
            {
                for (var i = 0; i < owners.Length; i++)
                {
                    probe.AddTo(owners[i]);
                    coins.Subscribe(handler, owners[i]);
                }
            };
            measured();
            for (var i = 0; i < owners.Length; i++)
            {
                owners[i].Cancel();
            }

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(coins.SubscriberCount, Is.EqualTo(Entries));
        }

        // Teardown

        [Test]
        public void Cancel_WithTrackingOn_AllocatesNothing()
        {
            var area = _t.App.CreateChild("area");
            var child = area.CreateChild("child");
            var counter = new Counter();
            TestDelegate measured = () => area.Cancel();
            RegisterCounted(area, counter, Entries);
            RegisterCounted(child, counter, Entries);
            measured();
            RegisterCounted(area, counter, Entries);
            RegisterCounted(child, counter, Entries);
            var cancelsBefore = area.DiagCancelCount;

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(counter.Value, Is.EqualTo(4 * Entries));
            Assert.That(area.DiagCancelCount - cancelsBefore, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_OfAWarmSubtreeOfUnnamedAreas_WithTrackingOn_AllocatesNothing()
        {
            var counter = new Counter();
            var warm = BuildSubtree(_t.App.CreateChild("warm"), counter);
            warm.Dispose();

            for (var round = 0; round < 3; round++)
            {
                var doomed = BuildSubtree(_t.App.CreateChild("doomed"), counter);
                var recentBefore = LifetimeDiagnostics.RecentVersion;

                Assert.That(() => doomed.Dispose(), Is.Not.AllocatingGCMemory());

                Assert.That(doomed.IsDisposed, Is.True);
                Assert.That(LifetimeDiagnostics.RecentVersion, Is.Not.EqualTo(recentBefore), "the ring was written, without building a label");
            }
        }

        private static Lifetime BuildSubtree(Lifetime root, Counter counter)
        {
            for (var i = 0; i < 4; i++)
            {
                var child = root.CreateChild();
                child.CreateChild();
                RegisterCounted(child, counter, 8);
            }

            return root;
        }

        [Test]
        public void Cancel_WithPendingTasksAndTrackingOn_AllocatesNothing()
        {
            var area = _t.App.CreateChild("area");
            var sources = new AutoResetUniTaskCompletionSource[8];
            TestDelegate measured = () => area.Cancel();
            for (var round = 0; round < 2; round++)
            {
                StartPending(area, sources);
                measured();
                Finish(sources);
            }

            StartPending(area, sources);
            Assert.That(area.EntryCount, Is.EqualTo(sources.Length));

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(area.EntryCount, Is.Zero);
            Finish(sources);
        }

        private static void StartPending(Lifetime area, AutoResetUniTaskCompletionSource[] sources)
        {
            for (var i = 0; i < sources.Length; i++)
            {
                sources[i] = AutoResetUniTaskCompletionSource.Create();
                area.Run(sources[i], static (s, ct) => s.Task);
            }
        }

        private static void Finish(AutoResetUniTaskCompletionSource[] sources)
        {
            for (var i = 0; i < sources.Length; i++)
            {
                sources[i].TrySetResult();
            }
        }

        // Report with tracking off

        [Test]
        public void Report_WithTrackingOff_AllocatesNothing()
        {
            LifetimeDiagnostics.TrackingEnabled = false;
            var area = _t.App.CreateChild("area");
            TestDelegate measured = () => LifetimeDiagnostics.Report(area, "TEST100", "ignored");
            measured();

            Assert.That(measured, Is.Not.AllocatingGCMemory(), "a call that records nothing must cost nothing");
        }

        // Snapshot

        private Lifetime BuildSnapshotTree(out AutoResetUniTaskCompletionSource source)
        {
            var root = _t.App.CreateChild("root");
            var counter = new Counter();
            source = AutoResetUniTaskCompletionSource.Create();
            var coins = new OwnedEvent<int>("Coins");
            for (var i = 0; i < 3; i++)
            {
                var branch = root.CreateChild();
                var leaf = branch.CreateChild("leaf" + i);
                RegisterCounted(branch, counter, 3);
                new DisposeProbe().AddTo(leaf);
                coins.Subscribe(_ => { }, leaf);
            }

            root.Run(source, static (s, ct) => s.Task);
            root.Subscribe(h => { }, h => { }, new Action(() => { }));
            _t.App.CreateChild("gone").Dispose();
            LifetimeDiagnostics.Report(root, "TEST100", "a recorded warning");
            return root;
        }

        [Test]
        public void Capture_OfAnUnchangedTree_AllocatesNothingBeyondTheReusableBuffers()
        {
            var root = BuildSnapshotTree(out var source);
            var snapshot = new DiagnosticsSnapshot();
            var tree = LifetimeTree.Default;
            TestDelegate measured = () => LifetimeDiagnostics.Capture(tree, snapshot);
            measured();
            measured();

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(snapshot.HasTree, Is.True);
            Assert.That(snapshot.Nodes.Count, Is.EqualTo(8), "App, root, and 3 branches with a leaf each; 'gone' was disposed");
            Assert.That(snapshot.Warnings.Count, Is.EqualTo(1));
            Assert.That(snapshot.Recent.Count, Is.EqualTo(1));
            Assert.That(SnapshotQueries.NodeOf(snapshot, root).EntryCount, Is.EqualTo(2));
            source.TrySetResult();
        }

        [Test]
        public void CaptureDefault_RepeatedAtTheWindowsRefreshRate_AllocatesNothing()
        {
            BuildSnapshotTree(out var source);
            var snapshot = new DiagnosticsSnapshot();
            TestDelegate measured = () =>
            {
                for (var i = 0; i < 8; i++)
                {
                    LifetimeDiagnostics.CaptureDefault(snapshot);
                }
            };
            measured();

            Assert.That(measured, Is.Not.AllocatingGCMemory());
            source.TrySetResult();
        }

        [Test]
        public void CaptureEntries_OfAnUnchangedLifetime_AllocatesNothingBeyondTheReusableList()
        {
            var root = BuildSnapshotTree(out var source);
            var entries = new List<EntryView>();
            TestDelegate measured = () => LifetimeDiagnostics.CaptureEntries(root, entries);
            measured();
            measured();

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(entries.Count, Is.EqualTo(2));
            source.TrySetResult();
        }

        [Test]
        public void Capture_AfterANewUnnamedLifetimeWasSeenOnce_AllocatesNothing()
        {
            BuildSnapshotTree(out var source);
            var snapshot = new DiagnosticsSnapshot();
            var tree = LifetimeTree.Default;
            LifetimeDiagnostics.Capture(tree, snapshot);
            _t.App.CreateChild();
            LifetimeDiagnostics.Capture(tree, snapshot);
            TestDelegate measured = () => LifetimeDiagnostics.Capture(tree, snapshot);
            measured();

            Assert.That(measured, Is.Not.AllocatingGCMemory(), "the label of an unnamed lifetime is built once, on first sight");
            source.TrySetResult();
        }
    }
}
