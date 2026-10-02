using System;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using static Ecanakli.Janitor.EditorTools.Tests.SnapshotBuilder;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Ecanakli.Janitor.EditorTools.Tests
{
    // A refresh with unchanged data allocates nothing: rows are reused, texts are cached, the tree is not rebuilt.
    // Every test runs the measured call a few times first: Mono allocates on the first run of a lambda or a string literal.
    [TestFixture]
    public sealed class JanitorViewModelAllocationTests : DiagnosticsTestBase
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void Apply_UnchangedSnapshotOnEveryTab_AllocatesNothing(int tab)
        {
            var vm = new JanitorViewModel();
            var snapshot = RichSnapshot();
            vm.SetTab((JanitorTab)tab);
            vm.Apply(snapshot, true, true);
            vm.SelectLifetime(3);
            TestDelegate measured = () => vm.Apply(snapshot, true, true);
            measured();
            measured();

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(vm.Tree.Shown.Count, Is.EqualTo(6), "the measured block must really have applied the tree");
            Assert.That(vm.Details.HintText, Is.Null, "and the details");
        }

        [Test]
        public void Apply_UnchangedSnapshotWithASortAndAFilter_AllocatesNothing()
        {
            var vm = new JanitorViewModel();
            var snapshot = RichSnapshot();
            vm.Tree.Filter = "r";
            vm.Tree.SetSort(TreeColumn.Tasks, true);
            vm.Apply(snapshot, true, true);
            TestDelegate measured = () => vm.Apply(snapshot, true, true);
            measured();
            measured();

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(vm.Tree.SortColumn, Is.EqualTo(TreeColumn.Tasks));
            Assert.That(vm.Tree.Shown.Count, Is.GreaterThan(1), "the sorted walk must really have run");
        }

        // The next three isolate the source if the combined test above ever fails: the sort, the filter, the name comparison.

        [Test]
        public void Apply_UnchangedSnapshotWithASortByCount_AllocatesNothing()
        {
            var vm = new JanitorViewModel();
            var snapshot = RichSnapshot();
            vm.Tree.SetSort(TreeColumn.Tasks, true);
            vm.Apply(snapshot, true, true);
            TestDelegate measured = () => vm.Apply(snapshot, true, true);
            measured();
            measured();

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(vm.Tree.Shown[1].Id, Is.EqualTo(6), "the sort must really have reordered the root's children");
        }

        [Test]
        public void Apply_UnchangedSnapshotWithASortByName_AllocatesNothing()
        {
            var vm = new JanitorViewModel();
            var snapshot = RichSnapshot();
            vm.Tree.SetSort(TreeColumn.Name, false);
            vm.Apply(snapshot, true, true);
            TestDelegate measured = () => vm.Apply(snapshot, true, true);
            measured();
            measured();

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(vm.Tree.Shown[2].Id, Is.EqualTo(5), "Awake:12 must really have moved before Player");
        }

        [Test]
        public void Apply_UnchangedSnapshotWithAFilter_AllocatesNothing()
        {
            var vm = new JanitorViewModel();
            var snapshot = RichSnapshot();
            vm.Tree.Filter = "awake";
            vm.Apply(snapshot, true, true);
            TestDelegate measured = () => vm.Apply(snapshot, true, true);
            measured();
            measured();

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(vm.Tree.Shown.Count, Is.EqualTo(3), "the filter must really have hidden rows");
        }

        [Test]
        public void Refresh_UnchangedLiveTreeWithASelectedLifetime_AllocatesNothing()
        {
            Scope.Tree.MakeDefault();
            Scope.Tree.UseManualClock();
            var area = Scope.App.CreateChild("Named");
            Scope.App.CreateChild();
            RegisterEntries(area);
            var vm = new JanitorViewModel();
            vm.Refresh(true, true);
            vm.SelectLifetime(area.DiagId);
            TestDelegate measured = () => vm.Refresh(true, true);
            measured();
            measured();
            measured();

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(vm.Tree.Shown.Count, Is.EqualTo(3), "App and the two areas");
            Assert.That(vm.Details.Entries.Rows.Count, Is.EqualTo(3), "the details list must really have been read");
        }

        [Test]
        public void Apply_ACountChangesEachTime_AllocatesNothingForSmallNumbers()
        {
            var vm = new JanitorViewModel();
            var snapshot = RichSnapshot();
            vm.Apply(snapshot, true, true);
            var counter = 0;
            TestDelegate measured = () =>
            {
                counter = (counter + 1) % 50;
                var node = snapshot.Nodes[3];
                node.Tasks = counter;
                node.AgeFrames = counter;
                snapshot.Nodes[3] = node;
                vm.Apply(snapshot, true, true);
            };
            for (var i = 0; i < 60; i++)
            {
                measured();
            }

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(vm.Tree.Find(4).TasksText, Is.EqualTo(counter.ToString()), "the changing count must really have been applied");
        }

        private static DiagnosticsSnapshot RichSnapshot()
        {
            var snapshot = SampleTree();
            for (var i = 0; i < snapshot.Nodes.Count; i++)
            {
                var node = snapshot.Nodes[i];
                node.Tasks = i;
                node.Tweens = i + 1;
                node.AgeFrames = 10 * i;
                snapshot.Nodes[i] = node;
            }

            snapshot.Warnings.Add(Warning(1, DiagnosticIds.DuplicateSubscription, "dup"));
            snapshot.Warnings.Add(Warning(2, DiagnosticIds.Growth, "big"));
            snapshot.Recent.Add(Recent(1, "x", 1, 5));
            snapshot.Recent.Add(Recent(2, "y", 1, 6));
            snapshot.Recent.Add(Recent(3, "z", 1, 7));
            snapshot.WarningsVersion = 1;
            snapshot.RecentVersion = 1;
            return snapshot;
        }

        private static void RegisterEntries(Lifetime area)
        {
            var events = new OwnedEvent<int>("Coins");
            area.OnCancel(() => { });
            area.After(10f, () => { });
            events.Subscribe(_ => { }, area);
        }
    }
}
