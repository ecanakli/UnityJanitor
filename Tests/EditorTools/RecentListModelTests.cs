using NUnit.Framework;
using static Ecanakli.Janitor.EditorTools.Tests.SnapshotBuilder;

namespace Ecanakli.Janitor.EditorTools.Tests
{
    // The "Recently disposed" rows: contents, newest-first order, change detection and the 200 entry cap of the core ring.
    [TestFixture]
    public sealed class RecentListModelTests : DiagnosticsTestBase
    {
        private RecentListModel _model;

        [SetUp]
        public void SetUp()
        {
            _model = new RecentListModel();
        }

        // Contents

        [Test]
        public void Update_Entry_ShowsEveryColumn()
        {
            var item = Recent(7, "Doomed", 10, 55);
            item.Kind = LifetimeKind.Scene;
            item.Generation = 3;
            item.CancelCount = 2;
            item.TotalRegistered = 9;
            item.RefusedCount = 1;
            item.Session = 4;

            _model.Update(SnapshotWith(item));
            var row = _model.Rows[0];

            Assert.That(row.NameText, Is.EqualTo("Doomed"));
            Assert.That(row.KindText, Is.EqualTo("Scene"));
            Assert.That(row.GenerationText, Is.EqualTo("3"));
            Assert.That(row.CancelsText, Is.EqualTo("2"));
            Assert.That(row.RegisteredText, Is.EqualTo("9"));
            Assert.That(row.RefusedText, Is.EqualTo("1"));
            Assert.That(row.LivedText, Is.EqualTo("45"), "disposed frame minus created frame");
            Assert.That(row.DisposedText, Is.EqualTo("55"));
            Assert.That(row.SessionText, Is.EqualTo("4"));
        }

        [Test]
        public void Update_EntryWithoutANameButACallSite_ShowsTheCallSite()
        {
            var item = Recent(1, null, 0, 1);
            item.Member = "Awake";
            item.Line = 12;

            _model.Update(SnapshotWith(item));

            Assert.That(_model.Rows[0].NameText, Is.EqualTo("Awake:12"));
        }

        [Test]
        public void Update_EntryWithNeitherANameNorACallSite_ShowsUnnamed()
        {
            _model.Update(SnapshotWith(Recent(1, null, 0, 1)));

            Assert.That(_model.Rows[0].NameText, Is.EqualTo("unnamed"));
        }

        [Test]
        public void Update_RestoredEntry_ShowsItsSavedLabel()
        {
            var item = Recent(1, "ignored", 0, 1);
            item.LabelText = "Restored:3";

            _model.Update(SnapshotWith(item));

            Assert.That(_model.Rows[0].NameText, Is.EqualTo("Restored:3"));
        }

        [Test]
        public void Update_UnknownLifetimeFrames_ShowsADash()
        {
            _model.Update(SnapshotWith(Recent(1, "x", 50, 10)));

            Assert.That(_model.Rows[0].LivedText, Is.EqualTo(CountText.Unknown));
        }

        // Order

        [Test]
        public void Update_SnapshotOrder_IsKeptNewestFirst()
        {
            _model.Update(SnapshotWith(Recent(3, "newest", 0, 30), Recent(2, "middle", 0, 20), Recent(1, "oldest", 0, 10)));

            Assert.That(_model.Rows.Count, Is.EqualTo(3));
            Assert.That(_model.Rows[0].NameText, Is.EqualTo("newest"));
            Assert.That(_model.Rows[1].NameText, Is.EqualTo("middle"));
            Assert.That(_model.Rows[2].NameText, Is.EqualTo("oldest"));
        }

        // Change detection

        [Test]
        public void Update_SameVersionAndCount_ReturnsFalse()
        {
            var snapshot = SnapshotWith(Recent(1, "x", 0, 1));
            Assert.That(_model.Update(snapshot), Is.True);
            var revision = _model.Revision;

            Assert.That(_model.Update(snapshot), Is.False);
            Assert.That(_model.Revision, Is.EqualTo(revision));
        }

        [Test]
        public void Update_VersionBumpedWithTheSameEntries_KeepsTheRowsAndTheirTexts()
        {
            var item = Recent(1, null, 0, 1);
            item.Member = "Awake";
            item.Line = 12;
            var snapshot = SnapshotWith(item);
            _model.Update(snapshot);
            var row = _model.Rows[0];
            var name = row.NameText;

            snapshot.RecentVersion++;
            Assert.That(_model.Update(snapshot), Is.True);

            Assert.That(_model.Rows[0], Is.SameAs(row));
            Assert.That(row.NameText, Is.SameAs(name), "an entry already shown is not formatted again");
        }

        [Test]
        public void Update_PositionHoldsAnotherEntry_RewritesThatRow()
        {
            var snapshot = SnapshotWith(Recent(1, "first", 0, 1));
            _model.Update(snapshot);

            snapshot.Recent.Insert(0, Recent(2, "second", 0, 2));
            snapshot.RecentVersion++;
            _model.Update(snapshot);

            Assert.That(_model.Rows[0].NameText, Is.EqualTo("second"));
            Assert.That(_model.Rows[1].NameText, Is.EqualTo("first"));
        }

        [Test]
        public void Invalidate_UnchangedSnapshot_RebuildsOnTheNextUpdate()
        {
            var snapshot = SnapshotWith(Recent(1, "x", 0, 1));
            _model.Update(snapshot);

            _model.Invalidate();

            Assert.That(_model.Update(snapshot), Is.True);
        }

        [Test]
        public void Update_FewerEntries_ShrinksTheRows()
        {
            var snapshot = SnapshotWith(Recent(2, "b", 0, 2), Recent(1, "a", 0, 1));
            _model.Update(snapshot);

            snapshot.Recent.RemoveAt(0);
            snapshot.RecentVersion++;
            _model.Update(snapshot);

            Assert.That(_model.Rows.Count, Is.EqualTo(1));
            Assert.That(_model.Rows[0].NameText, Is.EqualTo("a"));
        }

        [Test]
        public void Update_NullSnapshot_EmptiesTheRows()
        {
            _model.Update(SnapshotWith(Recent(1, "x", 0, 1)));

            _model.Update(null);

            Assert.That(_model.Rows, Is.Empty);
        }

        // The core ring behind the rows

        [Test]
        public void Update_TwoHundredAndFiveDisposals_KeepsOnlyTheLast200NewestFirst()
        {
            for (var i = 0; i < 205; i++)
            {
                var area = Scope.App.CreateChild("area" + i);
                Frame = StartFrame + i;
                area.Dispose();
            }

            _model.Update(CaptureHistory());

            Assert.That(LifetimeDiagnostics.RecentCount, Is.EqualTo(LifetimeDiagnostics.RecentCapacity));
            Assert.That(_model.Rows.Count, Is.EqualTo(200));
            Assert.That(_model.Rows[0].NameText, Is.EqualTo("area204"));
            Assert.That(_model.Rows[199].NameText, Is.EqualTo("area5"), "the five oldest were dropped");
        }

        [Test]
        public void Update_Disposal_ShowsTheNameKindAndFrames()
        {
            var area = Scope.App.CreateChild("Doomed");
            Frame = StartFrame + 12;
            area.Dispose();

            _model.Update(CaptureHistory());
            var row = _model.Rows[0];

            Assert.That(row.NameText, Is.EqualTo("Doomed"));
            Assert.That(row.KindText, Is.EqualTo("Area"));
            Assert.That(row.LivedText, Is.EqualTo("12"));
            Assert.That(row.DisposedText, Is.EqualTo((StartFrame + 12).ToString()));
        }

        [Test]
        public void Update_UnnamedAreaDisposal_ShowsTheCallSiteLabel()
        {
            var area = Scope.App.CreateChild();
            area.Dispose();

            _model.Update(CaptureHistory());

            Assert.That(_model.Rows[0].NameText, Does.Match("^" + nameof(Update_UnnamedAreaDisposal_ShowsTheCallSiteLabel) + @":\d+$"));
        }

        private static DiagnosticsSnapshot SnapshotWith(params RecentLifetime[] items)
        {
            var snapshot = Snapshot();
            for (var i = 0; i < items.Length; i++)
            {
                snapshot.Recent.Add(items[i]);
            }

            return snapshot;
        }

        private static DiagnosticsSnapshot CaptureHistory()
        {
            var snapshot = new DiagnosticsSnapshot();
            snapshot.RecentVersion = LifetimeDiagnostics.RecentVersion;
            LifetimeDiagnostics.CopyRecent(snapshot.Recent);
            return snapshot;
        }
    }
}
