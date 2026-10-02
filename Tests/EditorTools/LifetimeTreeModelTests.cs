using System.Collections.Generic;
using NUnit.Framework;
using static Ecanakli.Janitor.EditorTools.Tests.SnapshotBuilder;

namespace Ecanakli.Janitor.EditorTools.Tests
{
    // The tree rows behind the window: row reuse, the structure version, the text filter and the per-column sort.
    [TestFixture]
    public sealed class LifetimeTreeModelTests
    {
        private LifetimeTreeModel _model;

        [SetUp]
        public void SetUp()
        {
            _model = new LifetimeTreeModel();
        }

        // Rows

        [Test]
        public void Update_SampleTree_ShowsEveryRowInPreOrderWithDepths()
        {
            _model.Update(SampleTree());

            Assert.That(Ids(), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6 }));
            Assert.That(Depths(), Is.EqualTo(new[] { 0, 1, 2, 3, 2, 1 }));
            Assert.That(_model.TotalCount, Is.EqualTo(6));
        }

        [Test]
        public void Update_NodeDepthDisagreesWithTheParents_UsesTheParentIds()
        {
            var child = Node(2, 1, "child");
            child.Depth = 99;
            _model.Update(Snapshot(Node(1, 0, "root"), child));

            Assert.That(Depths(), Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void Update_ParentMissingFromTheSnapshot_ShowsTheRowAsARoot()
        {
            _model.Update(Snapshot(Node(7, 99, "stray")));

            Assert.That(Ids(), Is.EqualTo(new[] { 7 }));
            Assert.That(Depths(), Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void Update_SameIdTwice_KeepsTheFirstNode()
        {
            _model.Update(Snapshot(Node(1, 0, "first"), Node(1, 0, "second")));

            Assert.That(_model.TotalCount, Is.EqualTo(1));
            Assert.That(_model.Find(1).NameText, Is.EqualTo("first"));
        }

        [Test]
        public void Update_RepeatedWithTheSameId_ReusesTheRowObject()
        {
            _model.Update(SampleTree());
            var row = _model.Find(3);

            var changed = SampleTree();
            changed.Nodes[2] = Renamed(changed.Nodes[2], "Hero");
            _model.Update(changed);

            Assert.That(_model.Find(3), Is.SameAs(row));
            Assert.That(row.NameText, Is.EqualTo("Hero"));
        }

        [Test]
        public void Update_LifetimeNoLongerInTheSnapshot_DropsItsRow()
        {
            _model.Update(SampleTree());
            var smaller = SampleTree();
            smaller.Nodes.RemoveAt(5);

            _model.Update(smaller);

            Assert.That(_model.Find(6), Is.Null);
            Assert.That(_model.TotalCount, Is.EqualTo(5));
        }

        [Test]
        public void Update_NullSnapshot_ClearsEveryRow()
        {
            _model.Update(SampleTree());

            _model.Update(null);

            Assert.That(_model.TotalCount, Is.Zero);
            Assert.That(_model.Shown.Count, Is.Zero);
            Assert.That(_model.Find(1), Is.Null);
        }

        // Structure version

        [Test]
        public void Update_FirstSnapshot_BumpsTheStructureVersion()
        {
            var before = _model.StructureVersion;

            _model.Update(SampleTree());

            Assert.That(_model.StructureVersion, Is.Not.EqualTo(before));
        }

        [Test]
        public void Update_UnchangedSnapshot_KeepsTheStructureVersion()
        {
            _model.Update(SampleTree());
            var version = _model.StructureVersion;

            _model.Update(SampleTree());

            Assert.That(_model.StructureVersion, Is.EqualTo(version));
        }

        [Test]
        public void Update_OnlyCountsChange_KeepsTheStructureVersionAndRefreshesTheText()
        {
            _model.Update(SampleTree());
            var version = _model.StructureVersion;

            var changed = SampleTree();
            var player = changed.Nodes[2];
            player.Tasks = 5;
            player.Generation = 9;
            player.AgeFrames = 40;
            changed.Nodes[2] = player;
            _model.Update(changed);

            Assert.That(_model.StructureVersion, Is.EqualTo(version), "a count-only change must not rebuild the tree");
            Assert.That(_model.Find(3).TasksText, Is.EqualTo("5"));
            Assert.That(_model.Find(3).GenerationText, Is.EqualTo("9"));
            Assert.That(_model.Find(3).AgeText, Is.EqualTo("40"));
        }

        [Test]
        public void Update_NewLifetime_BumpsTheStructureVersion()
        {
            _model.Update(SampleTree());
            var version = _model.StructureVersion;

            var grown = SampleTree();
            grown.Nodes.Add(Node(7, 1, "New"));
            _model.Update(grown);

            Assert.That(_model.StructureVersion, Is.Not.EqualTo(version));
        }

        [Test]
        public void Update_LifetimeRemoved_BumpsTheStructureVersion()
        {
            _model.Update(SampleTree());
            var version = _model.StructureVersion;

            var smaller = SampleTree();
            smaller.Nodes.RemoveAt(5);
            _model.Update(smaller);

            Assert.That(_model.StructureVersion, Is.Not.EqualTo(version));
        }

        [Test]
        public void Update_LifetimeMovedUnderAnotherParent_BumpsTheStructureVersion()
        {
            _model.Update(Snapshot(Node(1, 0, "root"), Node(2, 1, "a"), Node(3, 1, "b")));
            var version = _model.StructureVersion;

            _model.Update(Snapshot(Node(1, 0, "root"), Node(2, 1, "a"), Node(3, 2, "b")));

            Assert.That(_model.StructureVersion, Is.Not.EqualTo(version), "the ids and their order are the same, only a depth changed");
            Assert.That(Depths(), Is.EqualTo(new[] { 0, 1, 2 }));
        }

        // Filter

        [Test]
        public void Filter_NameMatch_KeepsTheMatchAndItsAncestors()
        {
            _model.Filter = "controller";

            _model.Update(SampleTree());

            Assert.That(Ids(), Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(_model.Find(3).Shown, Is.True, "an ancestor stays visible for context");
        }

        [Test]
        public void Filter_MatchWithSiblings_HidesTheSiblingsThatDoNotMatch()
        {
            _model.Filter = "awake";

            _model.Update(SampleTree());

            Assert.That(Ids(), Is.EqualTo(new[] { 1, 2, 5 }));
            Assert.That(_model.Find(3).Shown, Is.False);
            Assert.That(_model.Find(6).Shown, Is.False);
        }

        [Test]
        public void Filter_Matching_IsCaseInsensitive()
        {
            _model.Filter = "AWAKE";

            _model.Update(SampleTree());

            Assert.That(Ids(), Is.EqualTo(new[] { 1, 2, 5 }));
        }

        [Test]
        public void Filter_KindText_Matches()
        {
            _model.Filter = "scene";

            _model.Update(SampleTree());

            Assert.That(Ids(), Is.EqualTo(new[] { 1, 2 }), "the Level row is a Scene; its children do not match");
        }

        [Test]
        public void Filter_StateText_Matches()
        {
            _model.Filter = "cancelling";

            _model.Update(SampleTree());

            Assert.That(Ids(), Is.EqualTo(new[] { 1, 2, 3, 4 }));
        }

        [Test]
        public void Filter_NoMatch_ShowsNoRowsButKeepsTheTotal()
        {
            _model.Filter = "zzz";

            _model.Update(SampleTree());

            Assert.That(_model.Shown.Count, Is.Zero);
            Assert.That(_model.TotalCount, Is.EqualTo(6));
            Assert.That(_model.Find(4), Is.Not.Null, "a hidden row can still be found by id");
            Assert.That(_model.Find(4).Shown, Is.False);
        }

        [Test]
        public void Filter_WhitespaceOnly_IsTrimmedAndShowsEverything()
        {
            _model.Filter = "   ";

            _model.Update(SampleTree());

            Assert.That(_model.HasFilter, Is.False);
            Assert.That(_model.Shown.Count, Is.EqualTo(6));
        }

        [Test]
        public void Filter_SurroundingWhitespace_IsTrimmed()
        {
            _model.Filter = "  player  ";

            Assert.That(_model.Filter, Is.EqualTo("player"));
        }

        [Test]
        public void Filter_Cleared_RestoresEveryRow()
        {
            _model.Filter = "awake";
            _model.Update(SampleTree());

            _model.Filter = string.Empty;
            _model.Update(SampleTree());

            Assert.That(Ids(), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6 }));
        }

        [Test]
        public void Filter_Changed_BumpsTheStructureVersion()
        {
            _model.Update(SampleTree());
            var version = _model.StructureVersion;

            _model.Filter = "awake";
            _model.Update(SampleTree());

            Assert.That(_model.StructureVersion, Is.Not.EqualTo(version));
        }

        // Sort

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(10)]
        public void SetSort_AscendingByColumn_OrdersTheSiblingsAndKeepsChildrenUnderTheirParent(int column)
        {
            _model.SetSort((TreeColumn)column, false);

            _model.Update(SortSample());

            Assert.That(Ids(), Is.EqualTo(new[] { 1, 3, 6, 4, 2, 5 }), "column " + (TreeColumn)column);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(10)]
        public void SetSort_DescendingByColumn_ReversesTheSiblings(int column)
        {
            _model.SetSort((TreeColumn)column, true);

            _model.Update(SortSample());

            Assert.That(Ids(), Is.EqualTo(new[] { 1, 2, 5, 4, 3, 6 }), "column " + (TreeColumn)column);
        }

        [Test]
        public void SetSort_ByColumn_ReportsTheSortedColumn()
        {
            _model.SetSort(TreeColumn.Tasks, true);

            Assert.That(_model.SortColumn, Is.EqualTo(TreeColumn.Tasks));
            Assert.That(_model.SortDescending, Is.True);
        }

        [Test]
        public void SetSort_EqualValues_KeepCreationOrderInBothDirections()
        {
            var tied = SortSample();
            for (var i = 0; i < tied.Nodes.Count; i++)
            {
                var node = tied.Nodes[i];
                node.Tasks = 1;
                tied.Nodes[i] = node;
            }

            _model.SetSort(TreeColumn.Tasks, false);
            _model.Update(tied);
            var ascending = Ids();
            _model.SetSort(TreeColumn.Tasks, true);
            _model.Update(tied);
            var descending = Ids();

            Assert.That(ascending, Is.EqualTo(new[] { 1, 2, 5, 3, 6, 4 }));
            Assert.That(descending, Is.EqualTo(ascending), "ties must not flip with the direction or flicker between refreshes");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SetSort_ManySiblingsWithRepeatedKeys_AreGroupedAndStayInCreationOrderWithinAGroup(bool descending)
        {
            const int Children = 40;
            var nodes = new List<LifetimeNode> { Node(1, 0, "root", LifetimeKind.App) };
            for (var id = 2; id < 2 + Children; id++)
            {
                var child = Node(id, 1, "child" + id);
                child.Tasks = id % 3;
                nodes.Add(child);
            }

            _model.SetSort(TreeColumn.Tasks, descending);
            _model.Update(Snapshot(nodes.ToArray()));

            // Expected: the groups of equal Tasks in key order, and inside each group the ids in creation order.
            var expected = new List<int> { 1 };
            for (var step = 0; step < 3; step++)
            {
                var key = descending ? 2 - step : step;
                for (var id = 2; id < 2 + Children; id++)
                {
                    if (id % 3 == key)
                    {
                        expected.Add(id);
                    }
                }
            }

            Assert.That(Ids(), Is.EqualTo(expected.ToArray()));
        }

        [Test]
        public void SetSort_None_RestoresCreationOrder()
        {
            _model.SetSort(TreeColumn.Tasks, true);
            _model.Update(SortSample());

            _model.SetSort(TreeColumn.None, false);
            _model.Update(SortSample());

            Assert.That(Ids(), Is.EqualTo(new[] { 1, 2, 5, 3, 6, 4 }));
        }

        [Test]
        public void SetSort_WithAFilter_SortsOnlyTheVisibleRows()
        {
            _model.Filter = "b-child";
            _model.SetSort(TreeColumn.Tasks, true);

            _model.Update(SortSample());

            Assert.That(Ids(), Is.EqualTo(new[] { 1, 3, 6 }));
        }

        [Test]
        public void SetSort_UnchangedSnapshotUpdatedAgain_KeepsTheStructureVersion()
        {
            _model.SetSort(TreeColumn.Tasks, true);
            _model.Update(SortSample());
            var version = _model.StructureVersion;

            _model.Update(SortSample());

            Assert.That(_model.StructureVersion, Is.EqualTo(version));
        }

        [Test]
        public void SetSort_ChangedOrder_BumpsTheStructureVersion()
        {
            _model.Update(SortSample());
            var version = _model.StructureVersion;

            _model.SetSort(TreeColumn.Tasks, false);
            _model.Update(SortSample());

            Assert.That(_model.StructureVersion, Is.Not.EqualTo(version));
        }

        // Helpers

        private int[] Ids()
        {
            var ids = new List<int>();
            for (var i = 0; i < _model.Shown.Count; i++)
            {
                ids.Add(_model.Shown[i].Id);
            }

            return ids.ToArray();
        }

        private int[] Depths()
        {
            var depths = new List<int>();
            for (var i = 0; i < _model.Shown.Count; i++)
            {
                depths.Add(_model.Shown[i].Depth);
            }

            return depths.ToArray();
        }

        private static LifetimeNode Renamed(LifetimeNode node, string label)
        {
            node.Label = label;
            return node;
        }

        // Creation order: 1 root, 2 "c", 5 under 2, 3 "a", 6 under 3, 4 "b". Every column puts 3 before 4 before 2.
        private static DiagnosticsSnapshot SortSample()
        {
            return Snapshot(
                Node(1, 0, "root", LifetimeKind.App),
                Sibling(2, "c", LifetimeKind.GameObject, LifetimeState.Disposing, 3),
                Node(5, 2, "a-child"),
                Sibling(3, "a", LifetimeKind.Scene, LifetimeState.Active, 1),
                Node(6, 3, "b-child"),
                Sibling(4, "b", LifetimeKind.Component, LifetimeState.Cancelling, 2));
        }

        private static LifetimeNode Sibling(int id, string label, LifetimeKind kind, LifetimeState state, int rank)
        {
            var node = Node(id, 1, label, kind);
            node.State = state;
            node.Generation = rank;
            node.Tasks = rank;
            node.Tweens = rank;
            node.Coroutines = rank;
            node.Subscriptions = rank;
            node.Others = rank;
            node.ChildCount = rank;
            node.AgeFrames = rank * 10;
            return node;
        }
    }
}
