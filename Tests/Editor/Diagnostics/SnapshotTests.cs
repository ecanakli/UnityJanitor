using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // The read side the window uses at 4 Hz: a pre-order walk of the live tree plus the warning and recently disposed lists,
    // copied into one reusable DiagnosticsSnapshot. The allocation proof is in DiagnosticsAllocationTests.
    [TestFixture]
    public sealed class SnapshotTests
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

        // Expected order, from an independent walk over the intrusive links.
        private static void Walk(Lifetime node, int depth, List<Lifetime> order, List<int> depths)
        {
            order.Add(node);
            depths.Add(depth);
            for (var child = node.FirstChild; child != null; child = child.NextSibling)
            {
                Walk(child, depth + 1, order, depths);
            }
        }

        [Test]
        public void Capture_TheTree_IsInPreOrderWithDepthAndParentIds()
        {
            var a = _t.App.CreateChild("A");
            var a1 = a.CreateChild("A1");
            var a1a = a1.CreateChild("A1a");
            var a2 = a.CreateChild("A2");
            _t.App.CreateChild("B");

            LifetimeDiagnostics.CaptureDefault(_snapshot);

            Assert.That(SnapshotQueries.Labels(_snapshot), Is.EqualTo(new[] { "App", "A", "A1", "A1a", "A2", "B" }));
            var depths = new List<int>();
            var parents = new List<int>();
            for (var i = 0; i < _snapshot.Nodes.Count; i++)
            {
                depths.Add(_snapshot.Nodes[i].Depth);
                parents.Add(_snapshot.Nodes[i].ParentId);
            }

            Assert.That(depths, Is.EqualTo(new[] { 0, 1, 2, 3, 2, 1 }));
            Assert.That(parents, Is.EqualTo(new[] { 0, _t.App.DiagId, a.DiagId, a1.DiagId, a.DiagId, _t.App.DiagId }));
            Assert.That(a2.DiagId, Is.Not.EqualTo(a1a.DiagId));
        }

        [Test]
        public void Capture_AWideAndDeepTree_MatchesAnIndependentWalkOfTheLinks()
        {
            for (var i = 0; i < 4; i++)
            {
                var branch = _t.App.CreateChild("branch" + i);
                for (var j = 0; j < 3; j++)
                {
                    var leaf = branch.CreateChild("leaf" + i + "." + j);
                    for (var k = 0; k < j; k++)
                    {
                        leaf.CreateChild("tip" + i + "." + j + "." + k);
                    }
                }
            }

            var order = new List<Lifetime>();
            var depths = new List<int>();
            Walk(_t.App, 0, order, depths);
            LifetimeDiagnostics.CaptureDefault(_snapshot);

            Assert.That(_snapshot.Nodes.Count, Is.EqualTo(order.Count));
            for (var i = 0; i < order.Count; i++)
            {
                Assert.That(_snapshot.Nodes[i].Lifetime, Is.SameAs(order[i]), "node " + i);
                Assert.That(_snapshot.Nodes[i].Depth, Is.EqualTo(depths[i]), "depth of node " + i);
            }
        }

        [Test]
        public void Capture_ADisposedLifetime_IsGoneAndItsSubtreeWithIt()
        {
            var parent = _t.App.CreateChild("parent");
            var child = parent.CreateChild("child");
            var survivor = _t.App.CreateChild("survivor");

            parent.Dispose();
            LifetimeDiagnostics.CaptureDefault(_snapshot);

            Assert.That(SnapshotQueries.Contains(_snapshot, parent), Is.False);
            Assert.That(SnapshotQueries.Contains(_snapshot, child), Is.False);
            Assert.That(SnapshotQueries.Contains(_snapshot, survivor), Is.True);
        }

        [Test]
        public void Capture_ACancelledLifetime_StaysActiveInTheNextGeneration()
        {
            var area = _t.App.CreateChild("area");
            area.Cancel();

            LifetimeDiagnostics.CaptureDefault(_snapshot);

            var node = SnapshotQueries.NodeOf(_snapshot, area);
            Assert.That(node.State, Is.EqualTo(LifetimeState.Active));
            Assert.That(node.Generation, Is.EqualTo(1));
        }

        [Test]
        public void Capture_InsideACancelAction_ReadsTheCancellingState()
        {
            var area = _t.App.CreateChild("area");
            var child = area.CreateChild("child");
            var seen = LifetimeState.Active;
            var seenChild = LifetimeState.Active;
            area.OnCancel(() =>
            {
                LifetimeDiagnostics.CaptureDefault(_snapshot);
                seen = SnapshotQueries.NodeOf(_snapshot, area).State;
                seenChild = SnapshotQueries.NodeOf(_snapshot, child).State;
            });

            area.Cancel();

            Assert.That(seen, Is.EqualTo(LifetimeState.Cancelling));
            Assert.That(seenChild, Is.EqualTo(LifetimeState.Cancelling));
        }

        [Test]
        public void Capture_Twice_ClearsTheBuffersInsteadOfAppending()
        {
            _t.App.CreateChild("area").Dispose();
            LifetimeDiagnostics.Report(null, "TEST100", "one");
            LifetimeDiagnostics.CaptureDefault(_snapshot);
            var nodes = _snapshot.Nodes.Count;

            LifetimeDiagnostics.CaptureDefault(_snapshot);

            Assert.That(_snapshot.Nodes.Count, Is.EqualTo(nodes));
            Assert.That(_snapshot.Warnings.Count, Is.EqualTo(1));
            Assert.That(_snapshot.Recent.Count, Is.EqualTo(1));
        }

        [Test]
        public void Capture_WithNoTree_HasNoNodesButStillCopiesTheWarningsAndTheRecentlyDisposed()
        {
            _t.App.CreateChild("gone").Dispose();
            LifetimeDiagnostics.Report(null, "TEST100", "kept");
            _d.Frame = 222;

            LifetimeDiagnostics.Capture(null, _snapshot);

            Assert.That(_snapshot.HasTree, Is.False);
            Assert.That(_snapshot.Nodes, Is.Empty);
            Assert.That(_snapshot.Warnings.Count, Is.EqualTo(1));
            Assert.That(_snapshot.Recent.Count, Is.EqualTo(1));
            Assert.That(_snapshot.Frame, Is.EqualTo(222));
        }

        [Test]
        public void Capture_TheListsAreCopiedOldestWarningsFirstAndNewestDisposalsFirst()
        {
            _t.App.CreateChild("first").Dispose();
            _t.App.CreateChild("second").Dispose();
            LifetimeDiagnostics.Report(null, "TEST100", "older");
            LifetimeDiagnostics.Report(null, "TEST100", "newer");

            LifetimeDiagnostics.CaptureDefault(_snapshot);

            Assert.That(_snapshot.Warnings[0].Message, Is.EqualTo("older"));
            Assert.That(_snapshot.Warnings[1].Message, Is.EqualTo("newer"));
            Assert.That(_snapshot.Recent[0].FormatLabel(), Is.EqualTo("second"));
            Assert.That(_snapshot.Recent[1].FormatLabel(), Is.EqualTo("first"));
        }

        [Test]
        public void Capture_Versions_ChangeWhenTheWarningsOrTheRecentlyDisposedChange()
        {
            LifetimeDiagnostics.CaptureDefault(_snapshot);
            var warnings = _snapshot.WarningsVersion;
            var recent = _snapshot.RecentVersion;

            LifetimeDiagnostics.CaptureDefault(_snapshot);
            Assert.That(_snapshot.WarningsVersion, Is.EqualTo(warnings), "nothing changed");
            Assert.That(_snapshot.RecentVersion, Is.EqualTo(recent));

            LifetimeDiagnostics.Report(null, "TEST100", "new");
            LifetimeDiagnostics.CaptureDefault(_snapshot);
            Assert.That(_snapshot.WarningsVersion, Is.Not.EqualTo(warnings));
            Assert.That(_snapshot.RecentVersion, Is.EqualTo(recent));

            _t.App.CreateChild("a").Dispose();
            LifetimeDiagnostics.CaptureDefault(_snapshot);
            Assert.That(_snapshot.RecentVersion, Is.Not.EqualTo(recent));
        }

        [Test]
        public void Capture_RecordsTheFrameAndTheSession()
        {
            LifetimeDiagnostics.SessionStarted();
            _d.Frame = 321;

            LifetimeDiagnostics.CaptureDefault(_snapshot);

            Assert.That(_snapshot.Frame, Is.EqualTo(321));
            Assert.That(_snapshot.Session, Is.EqualTo(1));
        }

        // 103

        [Test]
        public void Capture_AnObjectLifetimeWhoseOwnerWasDestroyed_RecordsJanitor103OnceOnALaterSnapshot()
        {
            var owner = new GameObject("Doomed");
            var lifetime = _t.App.CreateChildCore("doomed", LifetimeKind.Component, true, owner, null, 0);
            Object.DestroyImmediate(owner);

            LifetimeDiagnostics.CaptureDefault(_snapshot);

            var node = SnapshotQueries.NodeOf(_snapshot, lifetime);
            Assert.That(node.OwnerDestroyed, Is.True);
            Assert.That(_d.Count(DiagnosticIds.OrphanLifetime), Is.Zero, "the first snapshot only notes it: the owner's destroy signal may still be on its way");

            _d.AdvanceFrames(1);
            LifetimeDiagnostics.CaptureDefault(_snapshot);

            var warning = _d.Single(DiagnosticIds.OrphanLifetime);
            Assert.That(warning.Lifetime, Is.SameAs(lifetime));
            Assert.That(warning.LifetimeLabel, Is.EqualTo("doomed"));
            Assert.That(warning.Message, Does.Contain("'doomed'").And.Contain("was destroyed"));
            Assert.That(_snapshot.Warnings.Count, Is.EqualTo(1), "the copied list already contains it");

            LifetimeDiagnostics.CaptureDefault(_snapshot);
            LifetimeDiagnostics.ScanOrphans(LifetimeTree.Default);
            _d.AdvanceFrames(30);
            LifetimeDiagnostics.CaptureDefault(_snapshot);

            var again = _d.Single(DiagnosticIds.OrphanLifetime);
            Assert.That(again.Count, Is.EqualTo(1), "recorded once, not once per refresh");
            Assert.That(_snapshot.Warnings.Count, Is.EqualTo(1));
        }

        [Test]
        public void Capture_AnOrphanSeenTwiceInTheSameFrame_RecordsNothing()
        {
            var owner = new GameObject("Doomed");
            _t.App.CreateChildCore("doomed", LifetimeKind.Component, true, owner, null, 0);
            Object.DestroyImmediate(owner);

            LifetimeDiagnostics.CaptureDefault(_snapshot);
            LifetimeDiagnostics.CaptureDefault(_snapshot);
            LifetimeDiagnostics.ScanOrphans(LifetimeTree.Default);

            Assert.That(_d.Count(DiagnosticIds.OrphanLifetime), Is.Zero, "no frame passed between the snapshots, so the engine had no chance to end it");
        }

        [Test]
        public void Capture_ADestroyedOwnerWhoseLifetimeEndsBeforeTheNextFrame_RecordsNothing()
        {
            var owner = new GameObject("Doomed");
            var lifetime = _t.App.CreateChildCore("doomed", LifetimeKind.Component, true, owner, null, 0);
            Object.DestroyImmediate(owner);
            LifetimeDiagnostics.CaptureDefault(_snapshot);

            lifetime.DisposeFromOwner();
            _d.AdvanceFrames(1);
            LifetimeDiagnostics.CaptureDefault(_snapshot);

            Assert.That(SnapshotQueries.Contains(_snapshot, lifetime), Is.False);
            Assert.That(_d.Count(DiagnosticIds.OrphanLifetime), Is.Zero, "a correct teardown that lagged one frame behind the destroy is not an orphan");
        }

        [Test]
        public void Capture_TheOrphanMessage_NamesTheCauseAndTheFix()
        {
            var owner = new GameObject("Doomed");
            _t.App.CreateChildCore("doomed", LifetimeKind.Component, true, owner, null, 0);
            Object.DestroyImmediate(owner);
            LifetimeDiagnostics.CaptureDefault(_snapshot);
            _d.AdvanceFrames(1);

            LifetimeDiagnostics.CaptureDefault(_snapshot);

            var message = _d.Single(DiagnosticIds.OrphanLifetime).Message;
            Assert.That(message, Does.Contain("first used while its GameObject was inactive"));
            Assert.That(message, Does.Contain("destroy the GameObject instead of only the component"));
            Assert.That(message, Does.Contain("OnDestroy"));
            Assert.That(message, Does.Not.Contain("Destroy alone does not end it"), "destroying an inactive object does end its lifetime");
        }

        [Test]
        public void ScanOrphans_NeedsNoWindow_AndFindsTheSameOrphan()
        {
            var owner = new GameObject("Doomed");
            var lifetime = _t.App.CreateChildCore("doomed", LifetimeKind.GameObject, true, owner, null, 0);
            Object.DestroyImmediate(owner);

            LifetimeDiagnostics.ScanOrphans(LifetimeTree.Default);
            _d.AdvanceFrames(1);
            LifetimeDiagnostics.ScanOrphans(LifetimeTree.Default);

            Assert.That(_d.Single(DiagnosticIds.OrphanLifetime).Lifetime, Is.SameAs(lifetime));
        }

        [Test]
        public void Capture_ObjectLifetimesWithALiveOrNoOwnerAndAreas_AreNeverOrphans()
        {
            var alive = new GameObject("Alive");
            try
            {
                _t.App.CreateChildCore("alive", LifetimeKind.Component, true, alive, null, 0);
                _t.App.CreateChildCore("ownerless", LifetimeKind.Component, true, null, null, 0);
                _t.App.CreateChild("area");

                LifetimeDiagnostics.CaptureDefault(_snapshot);

                Assert.That(_d.Count(DiagnosticIds.OrphanLifetime), Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(alive);
            }
        }

        [Test]
        public void Capture_AnOrphanThatIsDisposedLater_IsNotReportedAgain()
        {
            var owner = new GameObject("Doomed");
            var lifetime = _t.App.CreateChildCore("doomed", LifetimeKind.Component, true, owner, null, 0);
            Object.DestroyImmediate(owner);
            LifetimeDiagnostics.CaptureDefault(_snapshot);
            _d.AdvanceFrames(1);
            LifetimeDiagnostics.CaptureDefault(_snapshot);

            lifetime.DisposeFromOwner();
            LifetimeDiagnostics.CaptureDefault(_snapshot);

            Assert.That(SnapshotQueries.Contains(_snapshot, lifetime), Is.False);
            Assert.That(_d.Single(DiagnosticIds.OrphanLifetime).Count, Is.EqualTo(1));
        }
    }
}
