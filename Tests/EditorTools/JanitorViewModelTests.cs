using NUnit.Framework;
using static Ecanakli.Janitor.EditorTools.Tests.SnapshotBuilder;

namespace Ecanakli.Janitor.EditorTools.Tests
{
    // The window's view-model fed with hand-built snapshots: rows, texts, empty states, tab labels, selection.
    [TestFixture]
    public sealed class JanitorViewModelTests
    {
        private JanitorViewModel _vm;

        [SetUp]
        public void SetUp()
        {
            _vm = new JanitorViewModel();
        }

        // Rows

        [Test]
        public void Apply_Nodes_FillsTheRowsInTreeOrder()
        {
            _vm.Apply(SampleTree(), true, true);

            Assert.That(_vm.Tree.Shown.Count, Is.EqualTo(6));
            Assert.That(_vm.Tree.Shown[0].NameText, Is.EqualTo("App"));
            Assert.That(_vm.Tree.Shown[3].NameText, Is.EqualTo("PlayerController"));
            Assert.That(_vm.Tree.Shown[3].Depth, Is.EqualTo(3));
        }

        [Test]
        public void Apply_Node_ShowsKindStateGenerationAndEveryCount()
        {
            var node = Node(2, 1, "Boot", LifetimeKind.Scene);
            node.State = LifetimeState.Cancelling;
            node.Generation = 4;
            node.Tasks = 2;
            node.Tweens = 1;
            node.Coroutines = 3;
            node.Subscriptions = 5;
            node.Others = 7;
            node.ChildCount = 2;
            node.AgeFrames = 15;

            _vm.Apply(Snapshot(Node(1, 0, "App", LifetimeKind.App), node), true, true);
            var row = _vm.Tree.Find(2);

            Assert.That(row.NameText, Is.EqualTo("Boot"));
            Assert.That(row.KindText, Is.EqualTo("Scene"));
            Assert.That(row.StateText, Is.EqualTo("Cancelling"));
            Assert.That(row.GenerationText, Is.EqualTo("4"));
            Assert.That(row.TasksText, Is.EqualTo("2"));
            Assert.That(row.TweensText, Is.EqualTo("1"));
            Assert.That(row.CoroutinesText, Is.EqualTo("3"));
            Assert.That(row.SubscriptionsText, Is.EqualTo("5"));
            Assert.That(row.OthersText, Is.EqualTo("7"));
            Assert.That(row.ChildrenText, Is.EqualTo("2"));
            Assert.That(row.AgeText, Is.EqualTo("15"));
        }

        [Test]
        public void Apply_NodeWithACallSiteLabel_ShowsTheCallSite()
        {
            _vm.Apply(Snapshot(Node(1, 0, "Awake:12")), true, true);

            Assert.That(_vm.Tree.Find(1).NameText, Is.EqualTo("Awake:12"));
        }

        [Test]
        public void Apply_NodeWithoutALabel_ShowsTheUnnamedText()
        {
            _vm.Apply(Snapshot(Node(1, 0, null)), true, true);

            Assert.That(_vm.Tree.Find(1).NameText, Is.EqualTo(LifetimeRow.UnnamedText));
        }

        [Test]
        public void Apply_LifetimeWhoseOwnerWasDestroyed_ShowsTheOrphanState()
        {
            var orphan = Node(1, 0, "Gone");
            orphan.OwnerDestroyed = true;

            _vm.Apply(Snapshot(orphan), true, true);

            Assert.That(_vm.Tree.Find(1).StateText, Is.EqualTo("Active (orphan)"));
        }

        [Test]
        public void Apply_DisposedLifetimeWithADestroyedOwner_ShowsDisposedNotOrphan()
        {
            var node = Node(1, 0, "Gone");
            node.OwnerDestroyed = true;
            node.State = LifetimeState.Disposed;

            _vm.Apply(Snapshot(node), true, true);

            Assert.That(_vm.Tree.Find(1).StateText, Is.EqualTo("Disposed"));
        }

        [TestCase(5, "5")]
        [TestCase(9999, "9999")]
        [TestCase(12500, "12k")]
        [TestCase(-1, "-")]
        public void Apply_Age_ShowsFramesInACompactCachedForm(int ageFrames, string expected)
        {
            var node = Node(1, 0, "x");
            node.AgeFrames = ageFrames;

            _vm.Apply(Snapshot(node), true, true);

            Assert.That(_vm.Tree.Find(1).AgeText, Is.EqualTo(expected));
        }

        // Structure version

        [Test]
        public void Apply_SameSnapshotTwice_KeepsTheStructureVersion()
        {
            var snapshot = SampleTree();
            _vm.Apply(snapshot, true, true);
            var version = _vm.Tree.StructureVersion;

            _vm.Apply(snapshot, true, true);

            Assert.That(_vm.Tree.StructureVersion, Is.EqualTo(version));
        }

        [Test]
        public void Apply_OnlyAnEntryCountChanges_KeepsTheStructureVersion()
        {
            var snapshot = SampleTree();
            _vm.Apply(snapshot, true, true);
            var version = _vm.Tree.StructureVersion;

            var node = snapshot.Nodes[3];
            node.Subscriptions = 6;
            snapshot.Nodes[3] = node;
            _vm.Apply(snapshot, true, true);

            Assert.That(_vm.Tree.StructureVersion, Is.EqualTo(version), "no rebuild for a count-only change");
            Assert.That(_vm.Tree.Find(4).SubscriptionsText, Is.EqualTo("6"));
        }

        [Test]
        public void Apply_ALifetimeAppears_BumpsTheStructureVersion()
        {
            var snapshot = SampleTree();
            _vm.Apply(snapshot, true, true);
            var version = _vm.Tree.StructureVersion;

            snapshot.Nodes.Add(Node(7, 1, "Late"));
            _vm.Apply(snapshot, true, true);

            Assert.That(_vm.Tree.StructureVersion, Is.Not.EqualTo(version));
        }

        [Test]
        public void Apply_ALifetimeDisappears_BumpsTheStructureVersionAndDropsTheRow()
        {
            var snapshot = SampleTree();
            _vm.Apply(snapshot, true, true);
            var version = _vm.Tree.StructureVersion;

            snapshot.Nodes.RemoveAt(5);
            _vm.Apply(snapshot, true, true);

            Assert.That(_vm.Tree.StructureVersion, Is.Not.EqualTo(version));
            Assert.That(_vm.Tree.Find(6), Is.Null);
        }

        // Empty states

        [Test]
        public void Apply_NotPlaying_ShowsNoRowsAndTheNotPlayingMessage()
        {
            _vm.Apply(SampleTree(), false, true);

            Assert.That(_vm.Tree.TotalCount, Is.Zero);
            Assert.That(_vm.TreeEmptyText, Is.EqualTo(EmptyStates.NotPlaying));
        }

        [Test]
        public void Apply_TrackingOff_ShowsNoRowsAndTheTrackingOffMessage()
        {
            _vm.Apply(SampleTree(), true, false);

            Assert.That(_vm.Tree.TotalCount, Is.Zero);
            Assert.That(_vm.TreeEmptyText, Is.EqualTo(EmptyStates.TrackingOff));
        }

        [Test]
        public void Apply_NotPlayingAndTrackingOff_ShowsTheNotPlayingMessage()
        {
            _vm.Apply(SampleTree(), false, false);

            Assert.That(_vm.TreeEmptyText, Is.EqualTo(EmptyStates.NotPlaying));
        }

        [Test]
        public void Apply_PlayingWithoutNodes_ShowsTheNoLifetimesMessage()
        {
            _vm.Apply(Snapshot(), true, true);

            Assert.That(_vm.TreeEmptyText, Is.EqualTo(EmptyStates.NoLifetimes));
        }

        [Test]
        public void Apply_NullSnapshot_IsTreatedAsEmpty()
        {
            _vm.Apply(null, true, true);

            Assert.That(_vm.TreeEmptyText, Is.EqualTo(EmptyStates.NoLifetimes));
        }

        [Test]
        public void Apply_FilterMatchesNothing_ShowsTheNoMatchMessage()
        {
            _vm.Tree.Filter = "zzz";

            _vm.Apply(SampleTree(), true, true);

            Assert.That(_vm.Tree.Shown.Count, Is.Zero);
            Assert.That(_vm.TreeEmptyText, Is.EqualTo(EmptyStates.NoFilterMatch));
        }

        [Test]
        public void Apply_RowsPresent_HasNoEmptyMessage()
        {
            _vm.Apply(SampleTree(), true, true);

            Assert.That(_vm.TreeEmptyText, Is.Null);
        }

        [Test]
        public void Reapply_AfterTheFilterChanged_AppliesItToTheLastSnapshot()
        {
            _vm.Apply(SampleTree(), true, true);

            _vm.Tree.Filter = "awake";
            _vm.Reapply(true, true);

            Assert.That(_vm.Tree.Shown.Count, Is.EqualTo(3));
        }

        [Test]
        public void Apply_NoWarnings_ShowsTheNoWarningsMessage()
        {
            _vm.Apply(Snapshot(), false, true);

            Assert.That(_vm.WarningsEmptyText, Is.EqualTo(EmptyStates.NoWarnings));
        }

        [Test]
        public void Apply_NoWarningsAndTrackingOff_ShowsTheTrackingOffWarningsMessage()
        {
            _vm.Apply(Snapshot(), true, false);

            Assert.That(_vm.WarningsEmptyText, Is.EqualTo(EmptyStates.NoWarningsTrackingOff));
        }

        [Test]
        public void Apply_WarningsRecorded_HasNoWarningsEmptyMessage()
        {
            var snapshot = Snapshot();
            snapshot.Warnings.Add(Warning(1, DiagnosticIds.DuplicateSubscription, "dup"));

            _vm.Apply(snapshot, false, true);

            Assert.That(_vm.WarningsEmptyText, Is.Null);
        }

        [Test]
        public void Apply_NothingDisposed_ShowsTheNoRecentMessage()
        {
            _vm.Apply(Snapshot(), false, true);

            Assert.That(_vm.RecentEmptyText, Is.EqualTo(EmptyStates.NoRecent));
        }

        [Test]
        public void Apply_NothingDisposedAndTrackingOff_ShowsTheTrackingOffRecentMessage()
        {
            _vm.Apply(Snapshot(), true, false);

            Assert.That(_vm.RecentEmptyText, Is.EqualTo(EmptyStates.NoRecentTrackingOff));
        }

        // Tabs

        [Test]
        public void Apply_NothingRecorded_LabelsTheTabsWithoutACount()
        {
            _vm.Apply(Snapshot(), true, true);

            Assert.That(_vm.WarningsTabText, Is.EqualTo("Warnings"));
            Assert.That(_vm.RecentTabText, Is.EqualTo("Recently disposed"));
        }

        [Test]
        public void Apply_WarningsAndRecentRecorded_PutTheCountsInTheTabLabels()
        {
            var snapshot = Snapshot();
            snapshot.Warnings.Add(Warning(1, DiagnosticIds.DuplicateSubscription, "a"));
            snapshot.Warnings.Add(Warning(2, DiagnosticIds.Marshalled, "b"));
            snapshot.Recent.Add(Recent(1, "x", 1, 2));
            snapshot.Recent.Add(Recent(2, "y", 1, 2));
            snapshot.Recent.Add(Recent(3, "z", 1, 2));

            _vm.Apply(snapshot, false, true);

            Assert.That(_vm.WarningsTabText, Is.EqualTo("Warnings (2)"));
            Assert.That(_vm.RecentTabText, Is.EqualTo("Recently disposed (3)"));
        }

        [Test]
        public void Apply_DetailsTabOpen_BuildsNoWarningOrRecentRows()
        {
            var snapshot = Snapshot();
            snapshot.Warnings.Add(Warning(1, DiagnosticIds.DuplicateSubscription, "a"));
            snapshot.Recent.Add(Recent(1, "x", 1, 2));

            _vm.Apply(snapshot, false, true);

            Assert.That(_vm.Warnings.Rows.Count, Is.Zero, "the lists of a hidden tab are not kept up to date");
            Assert.That(_vm.Recent.Rows.Count, Is.Zero);
        }

        [Test]
        public void Apply_WarningsTabOpen_BuildsTheWarningRows()
        {
            var snapshot = Snapshot();
            snapshot.Warnings.Add(Warning(1, DiagnosticIds.DuplicateSubscription, "a"));
            _vm.SetTab(JanitorTab.Warnings);

            _vm.Apply(snapshot, false, true);

            Assert.That(_vm.Warnings.Rows.Count, Is.EqualTo(1));
            Assert.That(_vm.Recent.Rows.Count, Is.Zero);
        }

        [Test]
        public void Apply_RecentTabOpen_BuildsTheRecentRows()
        {
            var snapshot = Snapshot();
            snapshot.Recent.Add(Recent(1, "x", 1, 2));
            _vm.SetTab(JanitorTab.Recent);

            _vm.Apply(snapshot, false, true);

            Assert.That(_vm.Recent.Rows.Count, Is.EqualTo(1));
            Assert.That(_vm.Warnings.Rows.Count, Is.Zero);
        }

        [Test]
        public void SetTab_OpeningATabAfterAnApply_BuildsItsRowsOnTheNextApply()
        {
            var snapshot = Snapshot();
            snapshot.Warnings.Add(Warning(1, DiagnosticIds.DuplicateSubscription, "a"));
            _vm.Apply(snapshot, false, true);

            _vm.SetTab(JanitorTab.Warnings);
            _vm.Reapply(false, true);

            Assert.That(_vm.Warnings.Rows.Count, Is.EqualTo(1));
        }

        // Details

        [Test]
        public void SelectLifetime_KnownRow_ShowsHeaderAndSummary()
        {
            var node = Node(2, 1, "Boot", LifetimeKind.Scene);
            node.State = LifetimeState.Cancelling;
            node.Generation = 4;
            node.TotalRegistered = 9;
            node.RefusedCount = 2;
            node.FirstRefusedMember = "Awake";
            node.FirstRefusedLine = 12;
            node.CancelCount = 3;
            node.CreatedFrame = 40;
            _vm.Apply(Snapshot(Node(1, 0, "App", LifetimeKind.App), node), true, true);

            _vm.SelectLifetime(2);

            Assert.That(_vm.Details.HintText, Is.Null);
            Assert.That(_vm.Details.HeaderText, Is.EqualTo("Boot (Scene, Cancelling), generation 4"));
            Assert.That(_vm.Details.SummaryText, Is.EqualTo("Registered 9, refused 2 (first at Awake:12), cancelled 3 times, created at frame 40."));
        }

        [Test]
        public void SelectLifetime_NoRefusedRegistrations_LeavesOutTheFirstCallSite()
        {
            var node = Node(2, 1, "Boot");
            node.TotalRegistered = 1;
            _vm.Apply(Snapshot(Node(1, 0, "App", LifetimeKind.App), node), true, true);

            _vm.SelectLifetime(2);

            Assert.That(_vm.Details.SummaryText, Does.StartWith("Registered 1, refused 0, cancelled 0 times"));
            Assert.That(_vm.Details.SummaryText, Does.Not.Contain("first at"));
        }

        [Test]
        public void SelectLifetime_RowWithoutALifetimeObject_CannotCancelOrPing()
        {
            _vm.Apply(SampleTree(), true, true);

            _vm.SelectLifetime(3);

            Assert.That(_vm.Details.CanCancel, Is.False);
            Assert.That(_vm.Details.CanPing, Is.False);
        }

        [Test]
        public void Apply_NothingSelected_ShowsTheNoSelectionHint()
        {
            _vm.Apply(SampleTree(), true, true);

            Assert.That(_vm.Details.HintText, Is.EqualTo(EmptyStates.NoSelection));
        }

        [Test]
        public void Apply_SelectedLifetimeDisappears_ShowsTheGoneHint()
        {
            var snapshot = SampleTree();
            _vm.Apply(snapshot, true, true);
            _vm.SelectLifetime(5);

            snapshot.Nodes.RemoveAt(4);
            _vm.Apply(snapshot, true, true);

            Assert.That(_vm.Details.HintText, Is.EqualTo(EmptyStates.SelectionGone));
            Assert.That(_vm.Details.Row, Is.Null);
        }

        [Test]
        public void Apply_NotPlayingWithASelection_ClearsTheSelectionAndSaysDetailsNeedPlayMode()
        {
            var snapshot = SampleTree();
            _vm.Apply(snapshot, true, true);
            _vm.SelectLifetime(5);

            _vm.Apply(snapshot, false, true);

            Assert.That(_vm.Details.SelectedId, Is.Zero);
            Assert.That(_vm.Details.HintText, Is.EqualTo(EmptyStates.DetailsNotPlaying));
        }
    }
}
