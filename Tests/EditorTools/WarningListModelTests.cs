using System.Collections.Generic;
using NUnit.Framework;
using static Ecanakli.Janitor.EditorTools.Tests.SnapshotBuilder;

namespace Ecanakli.Janitor.EditorTools.Tests
{
    // The Warnings tab rows: text, newest-first order, change detection, docs links and the lifetime lookup.
    [TestFixture]
    public sealed class WarningListModelTests : DiagnosticsTestBase
    {
        private WarningListModel _model;

        public static IEnumerable<string> AllIds()
        {
            return DiagnosticIds.All;
        }

        [SetUp]
        public void SetUp()
        {
            _model = new WarningListModel();
        }

        [TearDown]
        public void TearDown()
        {
            DocLinks.ResetSeams();
        }

        // Rows

        [Test]
        public void Update_Warning_ShowsIdTitleLifetimeMessageCountAndFirstFrame()
        {
            var warning = Warning(1, DiagnosticIds.DuplicateSubscription, "duplicate handler");
            warning.LifetimeLabel = "Level";
            warning.Member = "Subscribe";
            warning.Line = 7;
            warning.Count = 3;
            warning.FirstFrame = 12;

            _model.Update(SnapshotWith(warning));
            var row = _model.Rows[0];

            Assert.That(row.IdText, Is.EqualTo("JANITOR105"));
            Assert.That(row.TitleText, Is.EqualTo(DiagnosticIds.GetTitle(DiagnosticIds.DuplicateSubscription)));
            Assert.That(row.LifetimeText, Is.EqualTo("Level @ Subscribe:7"));
            Assert.That(row.MessageText, Is.EqualTo("duplicate handler"));
            Assert.That(row.RepeatText, Is.EqualTo("x3"));
            Assert.That(row.Count, Is.EqualTo(3));
            Assert.That(row.FirstSeenText, Is.EqualTo("frame 12"));
        }

        [Test]
        public void Update_WarningWithoutALifetimeOrACount_ShowsADashAndNoRepeatText()
        {
            _model.Update(SnapshotWith(Warning(1, DiagnosticIds.TaskOverrun, "late")));
            var row = _model.Rows[0];

            Assert.That(row.LifetimeText, Is.EqualTo(CountText.Unknown));
            Assert.That(row.RepeatText, Is.Empty);
        }

        [Test]
        public void Update_LifetimeLabelWithoutACallSite_ShowsOnlyTheLabel()
        {
            var warning = Warning(1, DiagnosticIds.Growth, "big");
            warning.LifetimeLabel = "Level";

            _model.Update(SnapshotWith(warning));

            Assert.That(_model.Rows[0].LifetimeText, Is.EqualTo("Level"));
        }

        [Test]
        public void Update_MultiLineMessage_ShowsOneLineAndKeepsTheFullText()
        {
            _model.Update(SnapshotWith(Warning(1, DiagnosticIds.Growth, "first\nsecond\r\nthird")));
            var row = _model.Rows[0];

            Assert.That(row.MessageText, Does.Not.Contain("\n"));
            Assert.That(row.MessageText, Does.Not.Contain("\r"));
            Assert.That(row.Message, Is.EqualTo("first\nsecond\r\nthird"));
        }

        [Test]
        public void Update_SeveralWarnings_ListsTheNewestFirst()
        {
            _model.Update(SnapshotWith(
                Warning(1, DiagnosticIds.TaskOverrun, "oldest"),
                Warning(2, DiagnosticIds.Growth, "middle"),
                Warning(3, DiagnosticIds.Marshalled, "newest")));

            Assert.That(_model.Rows.Count, Is.EqualTo(3));
            Assert.That(_model.Rows[0].Message, Is.EqualTo("newest"));
            Assert.That(_model.Rows[1].Message, Is.EqualTo("middle"));
            Assert.That(_model.Rows[2].Message, Is.EqualTo("oldest"));
        }

        [Test]
        public void Update_InformationWarning_IsMarkedAsInformation()
        {
            var info = Warning(1, DiagnosticIds.Rehomed, "moved");
            info.Severity = DiagnosticSeverity.Info;

            _model.Update(SnapshotWith(info, Warning(2, DiagnosticIds.Growth, "big")));

            Assert.That(_model.Rows[1].IsInfo, Is.True);
            Assert.That(_model.Rows[0].IsInfo, Is.False);
        }

        [Test]
        public void Update_IdThatIsNotThePackages_HasACustomTitleAndNoDocs()
        {
            _model.Update(SnapshotWith(Warning(1, "CUSTOM001", "from an integration")));
            var row = _model.Rows[0];

            Assert.That(row.IdText, Is.EqualTo("CUSTOM001"));
            Assert.That(row.TitleText, Is.EqualTo(WarningRow.UnknownTitle));
            Assert.That(row.HasDocs, Is.False);
            Assert.That(row.DocsUrl, Is.Null);
        }

        [Test]
        public void Update_NullId_ShowsADashAndNoDocs()
        {
            _model.Update(SnapshotWith(Warning(1, null, "no id")));
            var row = _model.Rows[0];

            Assert.That(row.IdText, Is.EqualTo(CountText.Unknown));
            Assert.That(row.HasDocs, Is.False);
        }

        // Docs URL

        [TestCaseSource(nameof(AllIds))]
        public void Update_PackageId_HasADocsUrlWithItsAnchor(string id)
        {
            DocLinks.VersionProvider = () => "1.2.3";

            _model.Update(SnapshotWith(Warning(1, id, "m")));
            var row = _model.Rows[0];

            Assert.That(row.HasDocs, Is.True);
            Assert.That(row.DocsUrl, Is.EqualTo(DocLinks.BlobBaseUrl + "v1.2.3/" + DiagnosticIds.GetAnchor(id)));
            Assert.That(row.DocsUrl, Does.EndWith("#" + id.ToLowerInvariant()));
        }

        // Change detection

        [Test]
        public void Update_SameVersionAndCount_ReturnsFalseAndKeepsTheRows()
        {
            var snapshot = SnapshotWith(Warning(1, DiagnosticIds.Growth, "a"));
            Assert.That(_model.Update(snapshot), Is.True);
            var revision = _model.Revision;
            var row = _model.Rows[0];

            Assert.That(_model.Update(snapshot), Is.False);

            Assert.That(_model.Revision, Is.EqualTo(revision));
            Assert.That(_model.Rows[0], Is.SameAs(row));
        }

        [Test]
        public void Update_VersionChanges_RebuildsTheRows()
        {
            var snapshot = SnapshotWith(Warning(1, DiagnosticIds.Growth, "a"));
            _model.Update(snapshot);
            var revision = _model.Revision;

            var repeated = snapshot.Warnings[0];
            repeated.Count = 2;
            snapshot.Warnings[0] = repeated;
            snapshot.WarningsVersion++;

            Assert.That(_model.Update(snapshot), Is.True);
            Assert.That(_model.Revision, Is.Not.EqualTo(revision));
            Assert.That(_model.Rows[0].RepeatText, Is.EqualTo("x2"));
        }

        [Test]
        public void Update_CountChangesWithTheSameVersion_RebuildsTheRows()
        {
            var snapshot = SnapshotWith(Warning(1, DiagnosticIds.Growth, "a"));
            _model.Update(snapshot);

            snapshot.Warnings.Add(Warning(2, DiagnosticIds.Marshalled, "b"));

            Assert.That(_model.Update(snapshot), Is.True);
            Assert.That(_model.Rows.Count, Is.EqualTo(2));
        }

        [Test]
        public void Invalidate_UnchangedSnapshot_RebuildsOnTheNextUpdate()
        {
            var snapshot = SnapshotWith(Warning(1, DiagnosticIds.Growth, "a"));
            _model.Update(snapshot);

            _model.Invalidate();

            Assert.That(_model.Update(snapshot), Is.True);
        }

        // Rows follow the list in place

        [Test]
        public void Update_ARepeatOfOneWarning_BuildsAgainOnlyThatRow()
        {
            var snapshot = SnapshotWith(Warning(1, DiagnosticIds.Growth, "a"), Warning(2, DiagnosticIds.Marshalled, "b"), Warning(3, DiagnosticIds.TaskOverrun, "c"));
            _model.Update(snapshot);
            var newest = _model.Rows[0];
            var middle = _model.Rows[1];
            var oldest = _model.Rows[2];
            var newestText = newest.MessageText;

            var repeated = snapshot.Warnings[1];
            repeated.Count = 2;
            repeated.LastFrame = 20;
            snapshot.Warnings[1] = repeated;
            snapshot.WarningsVersion++;

            Assert.That(_model.Update(snapshot), Is.True);
            Assert.That(_model.Rows[0], Is.SameAs(newest));
            Assert.That(_model.Rows[1], Is.SameAs(middle));
            Assert.That(_model.Rows[2], Is.SameAs(oldest));
            Assert.That(middle.RepeatText, Is.EqualTo("x2"));
            Assert.That(middle.LastFrame, Is.EqualTo(20));
            Assert.That(newest.RepeatText, Is.Empty);
            Assert.That(newest.MessageText, Is.SameAs(newestText), "an untouched row is not built again");
        }

        [Test]
        public void Update_ANewWarning_AddsOneRowAtTheFrontAndKeepsTheOthers()
        {
            var snapshot = SnapshotWith(Warning(1, DiagnosticIds.Growth, "a"), Warning(2, DiagnosticIds.Marshalled, "b"));
            _model.Update(snapshot);
            var newestBefore = _model.Rows[0];
            var oldestBefore = _model.Rows[1];

            snapshot.Warnings.Add(Warning(3, DiagnosticIds.TaskOverrun, "c"));
            snapshot.WarningsVersion++;

            Assert.That(_model.Update(snapshot), Is.True);
            Assert.That(_model.Rows.Count, Is.EqualTo(3));
            Assert.That(_model.Rows[0].Sequence, Is.EqualTo(3));
            Assert.That(_model.Rows[1], Is.SameAs(newestBefore));
            Assert.That(_model.Rows[2], Is.SameAs(oldestBefore));
        }

        [Test]
        public void Update_TheOldestWarningDropped_RemovesTheLastRowAndKeepsTheOthers()
        {
            var snapshot = SnapshotWith(Warning(1, DiagnosticIds.Growth, "a"), Warning(2, DiagnosticIds.Marshalled, "b"), Warning(3, DiagnosticIds.TaskOverrun, "c"));
            _model.Update(snapshot);
            var third = _model.Rows[0];
            var second = _model.Rows[1];

            snapshot.Warnings.RemoveAt(0);
            snapshot.Warnings.Add(Warning(4, DiagnosticIds.DuplicateSubscription, "d"));
            snapshot.WarningsVersion++;

            Assert.That(_model.Update(snapshot), Is.True);
            Assert.That(_model.Rows.Count, Is.EqualTo(3));
            Assert.That(_model.Rows[0].Sequence, Is.EqualTo(4));
            Assert.That(_model.Rows[1], Is.SameAs(third));
            Assert.That(_model.Rows[2], Is.SameAs(second));
        }

        [Test]
        public void Update_AnotherHistoryEntirely_BuildsTheRowsAgain()
        {
            _model.Update(SnapshotWith(Warning(5, DiagnosticIds.Growth, "old a"), Warning(6, DiagnosticIds.Marshalled, "old b")));
            var snapshot = SnapshotWith(Warning(1, DiagnosticIds.TaskOverrun, "restored a"), Warning(2, DiagnosticIds.DuplicateSubscription, "restored b"), Warning(3, DiagnosticIds.Rehomed, "restored c"));
            snapshot.WarningsVersion = 7;

            Assert.That(_model.Update(snapshot), Is.True);

            Assert.That(_model.Rows.Count, Is.EqualTo(3));
            Assert.That(_model.Rows[0].Message, Is.EqualTo("restored c"));
            Assert.That(_model.Rows[1].Message, Is.EqualTo("restored b"));
            Assert.That(_model.Rows[2].Message, Is.EqualTo("restored a"));
        }

        [Test]
        public void Update_VersionBumpWithNothingChanged_ReturnsFalseAndKeepsTheRevision()
        {
            var snapshot = SnapshotWith(Warning(1, DiagnosticIds.Growth, "a"));
            _model.Update(snapshot);
            var revision = _model.Revision;

            snapshot.WarningsVersion++;

            Assert.That(_model.Update(snapshot), Is.False, "the view has nothing to bind again");
            Assert.That(_model.Revision, Is.EqualTo(revision));
        }

        [Test]
        public void Update_NullSnapshot_EmptiesTheRows()
        {
            _model.Update(SnapshotWith(Warning(1, DiagnosticIds.Growth, "a")));

            _model.Update(null);

            Assert.That(_model.Rows, Is.Empty);
        }

        // Lifetime lookup

        [Test]
        public void FindAliveLifetime_LifetimeInTheTree_ReturnsItsRow()
        {
            var area = Scope.App.CreateChild("Area");
            var tree = TreeOf(area);
            var row = RowOf(area, tree);

            Assert.That(row, Is.Not.Null);
            Assert.That(row.Lifetime, Is.SameAs(area));
            Assert.That(row, Is.SameAs(tree.Find(area.DiagId)));
        }

        [Test]
        public void FindAliveLifetime_LifetimeDisposedAndGoneFromTheTree_ReturnsNone()
        {
            var area = Scope.App.CreateChild("Area");
            var warning = WarningOf(area);
            area.Dispose();
            var tree = TreeOf();

            Assert.That(warning.FindAliveLifetime(tree), Is.Null);
        }

        [Test]
        public void FindAliveLifetime_RowStillInTheTreeButDisposed_ReturnsNone()
        {
            var area = Scope.App.CreateChild("Area");
            var node = Node(area.DiagId, 0, "Area");
            node.Lifetime = area;
            node.State = LifetimeState.Disposed;
            var tree = new LifetimeTreeModel();
            tree.Update(Snapshot(node));
            var warning = WarningOf(area);

            Assert.That(tree.Find(area.DiagId), Is.Not.Null);
            Assert.That(warning.FindAliveLifetime(tree), Is.Null);
        }

        [Test]
        public void FindAliveLifetime_SameIdButAnotherLifetimeObject_ReturnsNone()
        {
            var first = Scope.App.CreateChild("First");
            var second = Scope.App.CreateChild("Second");
            var node = Node(first.DiagId, 0, "First");
            node.Lifetime = first;
            var tree = new LifetimeTreeModel();
            tree.Update(Snapshot(node));
            var warning = WarningOf(second);
            warning.LifetimeId = first.DiagId;

            Assert.That(warning.FindAliveLifetime(tree), Is.Null, "an id that was reused after a domain reload must not join");
        }

        [Test]
        public void FindAliveLifetime_WarningWithoutALifetimeObject_ReturnsNone()
        {
            var area = Scope.App.CreateChild("Area");
            var tree = TreeOf(area);
            var warning = new WarningRow { LifetimeId = area.DiagId };

            Assert.That(warning.FindAliveLifetime(tree), Is.Null, "a restored warning carries only an id");
        }

        [Test]
        public void FindAliveLifetime_NullTree_ReturnsNone()
        {
            var area = Scope.App.CreateChild("Area");

            Assert.That(WarningOf(area).FindAliveLifetime(null), Is.Null);
        }

        // Helpers

        private static DiagnosticsSnapshot SnapshotWith(params DiagnosticWarning[] warnings)
        {
            var snapshot = Snapshot();
            for (var i = 0; i < warnings.Length; i++)
            {
                snapshot.Warnings.Add(warnings[i]);
            }

            return snapshot;
        }

        // A tree model built from the live default tree plus the given lifetimes' ancestors.
        private LifetimeTreeModel TreeOf(params Lifetime[] expected)
        {
            Scope.Tree.MakeDefault();
            var snapshot = new DiagnosticsSnapshot();
            LifetimeDiagnostics.CaptureDefault(snapshot);
            var tree = new LifetimeTreeModel();
            tree.Update(snapshot);
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.That(tree.Find(expected[i].DiagId), Is.Not.Null, "the lifetime must be in the captured tree");
            }

            return tree;
        }

        // A warning row built from a recorded warning attached to the lifetime.
        private WarningRow WarningOf(Lifetime lifetime)
        {
            LifetimeDiagnostics.Report(lifetime, DiagnosticIds.DuplicateSubscription, "dup");
            var snapshot = new DiagnosticsSnapshot();
            LifetimeDiagnostics.CopyWarnings(snapshot.Warnings);
            var model = new WarningListModel();
            model.Update(snapshot);
            return model.Rows[0];
        }

        private LifetimeRow RowOf(Lifetime lifetime, LifetimeTreeModel tree)
        {
            return WarningOf(lifetime).FindAliveLifetime(tree);
        }
    }
}
