using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // What the editor records per lifetime: kind, label, owner, generation, cancel count, frames, live counts by kind,
    // total registered, refused registrations with the first call site. Every test reads the data through the snapshot the window uses.
    [TestFixture]
    public sealed class LifetimeRecordingTests
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

        private LifetimeNode Capture(Lifetime lifetime)
        {
            LifetimeDiagnostics.CaptureDefault(_snapshot);
            return SnapshotQueries.NodeOf(_snapshot, lifetime);
        }

        // Kind, label, owner

        [Test]
        public void Capture_TheApp_IsTheRootNodeOfKindApp()
        {
            LifetimeDiagnostics.CaptureDefault(_snapshot);

            Assert.That(_snapshot.HasTree, Is.True);
            Assert.That(_snapshot.Nodes.Count, Is.GreaterThanOrEqualTo(1));
            var root = _snapshot.Nodes[0];
            Assert.That(root.Lifetime, Is.SameAs(_t.App));
            Assert.That(root.Kind, Is.EqualTo(LifetimeKind.App));
            Assert.That(root.Label, Is.EqualTo("App"));
            Assert.That(root.Depth, Is.Zero);
            Assert.That(root.ParentId, Is.Zero);
            Assert.That(root.State, Is.EqualTo(LifetimeState.Active));
            Assert.That(root.Id, Is.Positive);
        }

        [Test]
        public void Capture_AnUnnamedArea_IsLabelledByItsCallSite()
        {
            var area = _t.App.CreateChild();

            var node = Capture(area);

            Assert.That(area.CreatorLine, Is.GreaterThan(0));
            Assert.That(node.Kind, Is.EqualTo(LifetimeKind.Area));
            Assert.That(node.Label, Is.EqualTo(nameof(Capture_AnUnnamedArea_IsLabelledByItsCallSite) + ":" + area.CreatorLine));
        }

        [Test]
        public void Capture_ANamedArea_IsLabelledByItsName()
        {
            var area = _t.App.CreateChild("Popups");

            var node = Capture(area);

            Assert.That(node.Label, Is.EqualTo("Popups"));
            Assert.That(node.Owner, Is.Null, "an area has no owning object");
            Assert.That(node.OwnerDestroyed, Is.False);
            Assert.That(node.Placed, Is.False);
        }

        [Test]
        public void Capture_TheLabelIsCachedOnTheLifetime_SoARefreshBuildsNoNewString()
        {
            var area = _t.App.CreateChild();

            var first = Capture(area).Label;
            var second = Capture(area).Label;

            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void Capture_AnObjectLifetime_ReportsItsKindAndOwner()
        {
            var owner = new GameObject("DiagnosticsOwner");
            try
            {
                var lifetime = _t.App.CreateChildCore("owned", LifetimeKind.GameObject, true, owner, null, 0);

                var node = Capture(lifetime);

                Assert.That(node.Kind, Is.EqualTo(LifetimeKind.GameObject));
                Assert.That(node.Owner, Is.SameAs(owner));
                Assert.That(node.OwnerDestroyed, Is.False);
                Assert.That(node.ParentId, Is.EqualTo(_t.App.DiagId));
                Assert.That(node.Depth, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        // Generation, cancel count, frames

        [Test]
        public void Capture_GenerationAndCancelCount_FollowEveryCancel()
        {
            var area = _t.App.CreateChild("area");
            var child = area.CreateChild("child");

            area.Cancel();
            area.Cancel();
            var areaNode = Capture(area);
            var childNode = SnapshotQueries.NodeOf(_snapshot, child);

            Assert.That(areaNode.Generation, Is.EqualTo(2));
            Assert.That(areaNode.CancelCount, Is.EqualTo(2));
            Assert.That(childNode.Generation, Is.EqualTo(2), "a Cancel reaches the descendants");
            Assert.That(childNode.CancelCount, Is.EqualTo(2));
        }

        [Test]
        public void Capture_CancelCount_IsNotRaisedByTheRegistrationOfWork()
        {
            var area = _t.App.CreateChild("area");
            area.OnCancel(() => { });

            var node = Capture(area);

            Assert.That(node.CancelCount, Is.Zero);
            Assert.That(node.Generation, Is.Zero);
        }

        [Test]
        public void Capture_CreatedFrameAndAge_UseTheFrameOfCreation()
        {
            _d.Frame = 120;
            var area = _t.App.CreateChild("area");
            _d.Frame = 135;

            var node = Capture(area);

            Assert.That(node.CreatedFrame, Is.EqualTo(120));
            Assert.That(node.AgeFrames, Is.EqualTo(15));
        }

        [Test]
        public void Dispose_RecordsTheDisposedFrame_AndTheLifetimeLeavesTheTree()
        {
            var area = _t.App.CreateChild("area");
            Assert.That(area.DiagDisposedFrame, Is.EqualTo(-1), "alive");
            _d.Frame = 140;

            area.Dispose();
            LifetimeDiagnostics.CaptureDefault(_snapshot);

            Assert.That(area.DiagDisposedFrame, Is.EqualTo(140));
            Assert.That(SnapshotQueries.Contains(_snapshot, area), Is.False);
        }

        [Test]
        public void Capture_EveryLifetime_HasItsOwnStableId()
        {
            var first = _t.App.CreateChild("first");
            var second = _t.App.CreateChild("second");

            var firstId = Capture(first).Id;
            var secondId = SnapshotQueries.NodeOf(_snapshot, second).Id;
            var firstIdAgain = Capture(first).Id;

            Assert.That(firstId, Is.Not.EqualTo(secondId));
            Assert.That(firstIdAgain, Is.EqualTo(firstId), "a tree view keys its rows on the id, so it must not change");
        }

        // Totals and refusals

        [Test]
        public void Capture_TotalRegistered_CountsEveryRegistrationAndIgnoresCancel()
        {
            var area = _t.App.CreateChild("area");
            var counter = new Counter();
            for (var i = 0; i < 5; i++)
            {
                area.OnCancel(counter, static c => c.Value++);
            }

            area.Cancel();
            area.OnCancel(counter, static c => c.Value++);
            area.OnCancel(counter, static c => c.Value++);

            var node = Capture(area);

            Assert.That(node.TotalRegistered, Is.EqualTo(7));
            Assert.That(node.EntryCount, Is.EqualTo(2), "only the second generation's items are live");
        }

        [Test]
        public void Capture_RefusedRegistrations_CountWithTheFirstCallSiteOnly()
        {
            var area = _t.App.CreateChild("area");
            area.OnCancel(() =>
            {
                area.OnCancel(() => { }, "FirstSite", 11);
                area.OnCancel(() => { }, "SecondSite", 22);
            });

            area.Cancel();
            var node = Capture(area);

            Assert.That(node.RefusedCount, Is.EqualTo(2));
            Assert.That(node.FirstRefusedMember, Is.EqualTo("FirstSite"));
            Assert.That(node.FirstRefusedLine, Is.EqualTo(11));
            Assert.That(node.TotalRegistered, Is.EqualTo(1), "a refused registration is not a registered one");
        }

        [Test]
        public void Register_OnADisposedLifetime_IsCountedAsRefusedWithItsCallSite()
        {
            var area = _t.App.CreateChild("area");
            area.Dispose();

            area.OnCancel(() => { }, "LateSite", 33);

            Assert.That(area.DiagRefusedCount, Is.EqualTo(1));
            Assert.That(area.DiagFirstRefusedMember, Is.EqualTo("LateSite"));
            Assert.That(area.DiagFirstRefusedLine, Is.EqualTo(33));
            Assert.That(area.DiagTotalRegistered, Is.Zero);
        }

        // Live counts by kind

        [Test]
        public void Capture_LiveCountsByKind_AddUpToTheEntryCount()
        {
            var area = _t.App.CreateChild("area");
            var counter = new Counter();
            var first = AutoResetUniTaskCompletionSource.Create();
            var second = AutoResetUniTaskCompletionSource.Create();
            area.Run(first, static (s, ct) => s.Task);
            area.Run(second, static (s, ct) => s.Task);
            var coins = new OwnedEvent<int>("Coins");
            coins.Subscribe(_ => { }, area);
            Action handler = () => { };
            area.Subscribe(h => { }, h => { }, handler);
            area.OnCancel(() => { });
            area.OnCancel(counter, static c => c.Value++);
            new DisposeProbe().AddTo(area);

            var node = Capture(area);

            Assert.That(node.Tasks, Is.EqualTo(2));
            Assert.That(node.Subscriptions, Is.EqualTo(2), "an OwnedEvent subscription and a paired one");
            Assert.That(node.Others, Is.EqualTo(3), "two OnCancel and one IDisposable");
            Assert.That(node.Tweens, Is.Zero);
            Assert.That(node.Coroutines, Is.Zero);
            Assert.That(node.EntryCount, Is.EqualTo(7));
            Assert.That(node.Tasks + node.Tweens + node.Coroutines + node.Subscriptions + node.Others, Is.EqualTo(node.EntryCount));

            first.TrySetResult();
            second.TrySetResult();
        }

        [Test]
        public void Capture_ChildCount_CountsLiveChildrenOnly()
        {
            var area = _t.App.CreateChild("area");
            var keep = area.CreateChild("keep");
            var drop = area.CreateChild("drop");
            drop.Dispose();

            var node = Capture(area);

            Assert.That(node.ChildCount, Is.EqualTo(1));
            Assert.That(SnapshotQueries.Contains(_snapshot, keep), Is.True);
            Assert.That(SnapshotQueries.Contains(_snapshot, drop), Is.False);
        }

        // Per entry

        [Test]
        public void CaptureEntries_TellsEveryKindOfItemApartAndLabelsIt()
        {
            var area = _t.App.CreateChild("area");
            var counter = new Counter();
            _t.Tree.UseManualClock();
            var source = AutoResetUniTaskCompletionSource.Create();
            area.Run(source, static (s, ct) => s.Task);
            area.After(10f, counter, static c => c.Value++);
            area.Every(10f, counter, static c => c.Value++);
            var coins = new OwnedEvent<int>("Coins");
            coins.Subscribe(_ => { }, area);
            var plain = new OwnedEvent("Plain");
            plain.Subscribe(() => { }, area);
            var pair = new OwnedEvent<int, string>();
            pair.Subscribe((a, b) => { }, area);
            Action handler = () => { };
            area.Subscribe(h => { }, h => { }, handler);
            area.Subscribe<int>(h => { }, h => { }, (int v) => { });
            area.OnCancel(() => { });
            area.OnCancel(counter, static c => c.Value++);
            area.OnCancel(counter, new CallLog(), static (a, b) => { });
            new DisposeProbe().AddTo(area);
            var entries = new List<EntryView>();

            LifetimeDiagnostics.CaptureEntries(area, entries);

            Assert.That(
                SnapshotQueries.EntryLabels(entries),
                Is.EqualTo(new[]
                {
                    "IDisposable DisposeProbe",
                    "OnCancel<Counter, CallLog>",
                    "OnCancel<Counter>",
                    "OnCancel",
                    "Paired<Action<Int32>>",
                    "Paired<Action>",
                    "OwnedEvent<Int32, String>",
                    "OwnedEvent \"Plain\"",
                    "OwnedEvent<Int32> \"Coins\"",
                    "Task.Every",
                    "Task.After",
                    "Task.Run",
                }),
                "newest first");
            var kinds = new List<EntryKind>();
            for (var i = 0; i < entries.Count; i++)
            {
                kinds.Add(entries[i].Kind);
            }

            Assert.That(
                kinds,
                Is.EqualTo(new[]
                {
                    EntryKind.Other, EntryKind.Other, EntryKind.Other, EntryKind.Other,
                    EntryKind.Subscription, EntryKind.Subscription, EntryKind.Subscription, EntryKind.Subscription, EntryKind.Subscription,
                    EntryKind.Task, EntryKind.Task, EntryKind.Task,
                }));

            source.TrySetResult();
        }

        [Test]
        public void CaptureEntries_AnOwnedEventWithoutAName_IsLabelledByItsTypeAlone()
        {
            var area = _t.App.CreateChild("area");
            var unnamed = new OwnedEvent<int>();
            unnamed.Subscribe(_ => { }, area);
            var entries = new List<EntryView>();

            LifetimeDiagnostics.CaptureEntries(area, entries);

            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].FormatLabel(), Is.EqualTo("OwnedEvent<Int32>"));
        }

        [Test]
        public void CaptureEntries_MemberLineGenerationAndStatus_AreTheRegistrationsOwn()
        {
            var area = _t.App.CreateChild("area");
            area.Cancel();
            area.OnCancel(() => { }, "RegisteredHere", 77);
            var entries = new List<EntryView>();

            LifetimeDiagnostics.CaptureEntries(area, entries);

            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].Member, Is.EqualTo("RegisteredHere"));
            Assert.That(entries[0].Line, Is.EqualTo(77));
            Assert.That(entries[0].Generation, Is.EqualTo(1), "the generation the lifetime is in now");
            Assert.That(entries[0].Status, Is.EqualTo(EntryStatus.Live));
        }

        [Test]
        public void CaptureEntries_AgeIsCountedInFramesFromTheRegistrationFrame()
        {
            var area = _t.App.CreateChild("area");
            _d.Frame = 100;
            area.OnCancel(() => { });
            _d.Frame = 107;
            var entries = new List<EntryView>();

            LifetimeDiagnostics.CaptureEntries(area, entries);

            Assert.That(entries[0].RegisteredFrame, Is.EqualTo(100));
            Assert.That(entries[0].AgeFrames, Is.EqualTo(7));
        }

        [Test]
        public void CaptureEntries_TheRegistrationOfAView_CancelsExactlyThatItem()
        {
            var area = _t.App.CreateChild("area");
            var counter = new Counter();
            var oldest = area.OnCancel(counter, static c => c.Value += 1);
            area.OnCancel(counter, static c => c.Value += 10);
            var entries = new List<EntryView>();
            LifetimeDiagnostics.CaptureEntries(area, entries);

            entries[0].Registration.Cancel();

            Assert.That(counter.Value, Is.EqualTo(10), "the newest entry is the first view");
            Assert.That(area.EntryCount, Is.EqualTo(1));
            Assert.That(oldest.IsActive, Is.True);
        }

        [Test]
        public void CaptureEntries_ForANullLifetime_ReturnsAnEmptyListWithoutThrowing()
        {
            var entries = new List<EntryView> { default };

            Assert.DoesNotThrow(() => LifetimeDiagnostics.CaptureEntries(null, entries));

            Assert.That(entries, Is.Empty);
        }
    }
}
