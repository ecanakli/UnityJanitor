using System;
using NUnit.Framework;

namespace Ecanakli.Janitor.EditorTools.Tests
{
    // The view-model reading a real lifetime tree through the core snapshot API (Refresh, not Apply).
    [TestFixture]
    public sealed class JanitorViewModelLiveTests : DiagnosticsTestBase
    {
        private JanitorViewModel _vm;

        [SetUp]
        public void SetUp()
        {
            _vm = new JanitorViewModel();
            Scope.Tree.MakeDefault();
        }

        [Test]
        public void Refresh_RealTree_ShowsAppAndItsDescendantsWithKindsAndDepths()
        {
            var level = Scope.App.CreateChild("Level");
            var area = level.CreateChild("Named");

            _vm.Refresh(true, true);

            var app = _vm.Tree.Find(Scope.App.DiagId);
            Assert.That(app.NameText, Is.EqualTo("App"));
            Assert.That(app.KindText, Is.EqualTo("App"));
            Assert.That(app.Depth, Is.Zero);
            Assert.That(_vm.Tree.Find(level.DiagId).Depth, Is.EqualTo(1));
            Assert.That(_vm.Tree.Find(area.DiagId).Depth, Is.EqualTo(2));
            Assert.That(_vm.Tree.Find(area.DiagId).KindText, Is.EqualTo("Area"));
            Assert.That(_vm.Tree.Find(area.DiagId).StateText, Is.EqualTo("Active"));
            Assert.That(_vm.Tree.Find(level.DiagId).ChildrenText, Is.EqualTo("1"));
            Assert.That(_vm.TreeEmptyText, Is.Null);
        }

        [Test]
        public void Refresh_AreaWithTaskSubscriptionsAndOtherEntries_CountsThemPerKind()
        {
            Scope.Tree.UseManualClock();
            var area = Scope.App.CreateChild("Named");
            RegisterEveryKind(area);

            _vm.Refresh(true, true);
            var row = _vm.Tree.Find(area.DiagId);

            Assert.That(row.TasksText, Is.EqualTo("1"), "After is a task");
            Assert.That(row.TweensText, Is.EqualTo("0"));
            Assert.That(row.CoroutinesText, Is.EqualTo("0"));
            Assert.That(row.SubscriptionsText, Is.EqualTo("2"), "an OwnedEvent subscription and a paired Subscribe");
            Assert.That(row.OthersText, Is.EqualTo("2"), "OnCancel and an IDisposable");
        }

        [Test]
        public void Refresh_UnnamedArea_ShowsTheCallSiteAsItsName()
        {
            var area = Scope.App.CreateChild();

            _vm.Refresh(true, true);

            Assert.That(_vm.Tree.Find(area.DiagId).NameText, Does.Match("^" + nameof(Refresh_UnnamedArea_ShowsTheCallSiteAsItsName) + @":\d+$"));
        }

        [Test]
        public void Refresh_AfterCancel_ShowsTheNextGeneration()
        {
            var area = Scope.App.CreateChild("Named");
            _vm.Refresh(true, true);
            var before = _vm.Tree.Find(area.DiagId).GenerationText;

            area.Cancel();
            _vm.Refresh(true, true);

            var after = _vm.Tree.Find(area.DiagId).GenerationText;
            Assert.That(after, Is.Not.EqualTo(before));
            Assert.That(after, Is.EqualTo(area.Generation.ToString()));
        }

        [Test]
        public void Refresh_FramesPass_AgesTheRows()
        {
            var area = Scope.App.CreateChild("Named");
            _vm.Refresh(true, true);
            Assert.That(_vm.Tree.Find(area.DiagId).AgeText, Is.EqualTo("0"));

            Frame = StartFrame + 30;
            _vm.Refresh(true, true);

            Assert.That(_vm.Tree.Find(area.DiagId).AgeText, Is.EqualTo("30"));
        }

        [Test]
        public void Refresh_UnchangedTree_KeepsTheStructureVersion()
        {
            Scope.App.CreateChild("Named");
            _vm.Refresh(true, true);
            var version = _vm.Tree.StructureVersion;

            Frame++;
            _vm.Refresh(true, true);

            Assert.That(_vm.Tree.StructureVersion, Is.EqualTo(version));
        }

        [Test]
        public void Refresh_LifetimeDisposed_LeavesTheTreeAndEntersTheRecentlyDisposedRows()
        {
            var area = Scope.App.CreateChild("Doomed");
            _vm.SetTab(JanitorTab.Recent);
            _vm.Refresh(true, true);
            Assert.That(_vm.Tree.Find(area.DiagId), Is.Not.Null);

            Frame = StartFrame + 5;
            area.Dispose();
            _vm.Refresh(true, true);

            Assert.That(_vm.Tree.Find(area.DiagId), Is.Null);
            Assert.That(_vm.Recent.Rows.Count, Is.EqualTo(1));
            Assert.That(_vm.Recent.Rows[0].NameText, Is.EqualTo("Doomed"));
            Assert.That(_vm.RecentTabText, Is.EqualTo("Recently disposed (1)"));
        }

        [Test]
        public void Refresh_NotPlaying_ShowsOnlyTheHistoryEvenWhenAStaleTreeIsStillTheDefault()
        {
            Scope.App.CreateChild("Stale");
            LifetimeDiagnostics.Report(null, DiagnosticIds.DuplicateSubscription, "dup");
            _vm.SetTab(JanitorTab.Warnings);

            _vm.Refresh(false, true);

            Assert.That(_vm.Tree.TotalCount, Is.Zero, "the window never walks a tree outside Play Mode");
            Assert.That(_vm.TreeEmptyText, Is.EqualTo(EmptyStates.NotPlaying));
            Assert.That(_vm.Warnings.Rows.Count, Is.EqualTo(1));
            Assert.That(_vm.WarningsTabText, Is.EqualTo("Warnings (1)"));
        }

        [Test]
        public void Refresh_NotPlaying_ShowsTheRecentlyDisposedHistory()
        {
            Scope.App.CreateChild("Gone").Dispose();
            _vm.SetTab(JanitorTab.Recent);

            _vm.Refresh(false, true);

            Assert.That(_vm.Recent.Rows.Count, Is.EqualTo(1));
            Assert.That(_vm.Recent.Rows[0].NameText, Is.EqualTo("Gone"));
            Assert.That(_vm.RecentEmptyText, Is.Null);
        }

        [Test]
        public void Refresh_SelectedLifetime_ShowsItsEntriesInTheDetails()
        {
            Scope.Tree.UseManualClock();
            var area = Scope.App.CreateChild("Named");
            RegisterEveryKind(area);
            _vm.Refresh(true, true);

            _vm.SelectLifetime(area.DiagId);

            Assert.That(_vm.Details.HintText, Is.Null);
            Assert.That(_vm.Details.Row.Lifetime, Is.SameAs(area));
            Assert.That(_vm.Details.CanCancel, Is.True);
            Assert.That(_vm.Details.Entries.Rows.Count, Is.EqualTo(5));
        }

        [Test]
        public void Refresh_SelectedLifetime_LabelsOwnedEventAndPairedSubscriptionEntries()
        {
            Scope.Tree.UseManualClock();
            var area = Scope.App.CreateChild("Named");
            RegisterEveryKind(area);
            _vm.Refresh(true, true);

            _vm.SelectLifetime(area.DiagId);
            var labels = new System.Collections.Generic.List<string>();
            foreach (var row in _vm.Details.Entries.Rows)
            {
                labels.Add(row.ItemText);
            }

            Assert.That(labels, Does.Contain("OwnedEvent<Int32> \"Coins\""));
            Assert.That(labels, Does.Contain("Paired<Action>"));
            Assert.That(labels, Does.Contain("Task.After"));
            Assert.That(labels, Does.Contain("OnCancel"));
        }

        private static void RegisterEveryKind(Lifetime area)
        {
            var events = new OwnedEvent<int>("Coins");
            var source = new PairedSource();
            area.OnCancel(() => { });
            area.After(10f, () => { });
            events.Subscribe(_ => { }, area);
            area.Subscribe(h => source.Raised += h, h => source.Raised -= h, () => { });
            new DisposeProbeForTests().AddTo(area);
        }

        private sealed class PairedSource
        {
#pragma warning disable CS0067 // The tests only subscribe and unsubscribe; nothing needs to raise it.
            public event Action Raised;
#pragma warning restore CS0067
        }

        private sealed class DisposeProbeForTests : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
