using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // ExportState and ImportState carry the recently disposed buffer and the warning list over a domain reload, which the core
    // cannot do itself (it may not use SessionState). The window calls them around every reload.
    [TestFixture]
    public sealed class DiagnosticsPersistenceTests
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

        private static List<DiagnosticWarning> Warnings()
        {
            var warnings = new List<DiagnosticWarning>();
            LifetimeDiagnostics.CopyWarnings(warnings);
            return warnings;
        }

        // Three disposed lifetimes of two sessions and three warning entries: one merged from a repeat, one information entry with no
        // lifetime and no call site, and one attached to a lifetime with a call site.
        private void RecordSomething()
        {
            LifetimeDiagnostics.SessionStarted();
            LifetimeDiagnostics.SessionStarted();
            _d.Frame = 120;
            var first = _t.App.CreateChild("first");
            first.OnCancel(() => { });
            first.Cancel();
            _d.Frame = 130;
            first.Dispose();
            var unnamed = _t.App.CreateChild();
            _d.Frame = 140;
            unnamed.Dispose();
            _t.App.CreateChildCore("component", LifetimeKind.Component, true, null, null, 0).DisposeFromOwner();
            LifetimeDiagnostics.Report(first, "TEST100", "one");
            LifetimeDiagnostics.Report(null, DiagnosticIds.Rehomed, "info");
            _d.AdvanceFrames(7);
            LifetimeDiagnostics.Report(first, "TEST100", "one");
            var attached = _t.App.CreateChild("attached");
            attached.OnCancel(() => LifetimeDiagnostics.Report(null, "TEST101", "from an action"), "SiteX", 12);
            attached.Cancel();
        }

        [Test]
        public void ExportThenImport_RestoresTheBufferAndTheWarningsFieldForField()
        {
            RecordSomething();
            var json = LifetimeDiagnostics.ExportState();
            var expectedRecent = Recent();
            var expectedWarnings = Warnings();
            LifetimeDiagnostics.ResetAll();
            Assert.That(LifetimeDiagnostics.RecentCount, Is.Zero, "premise: a reload wiped the statics");

            LifetimeDiagnostics.ImportState(json);

            var recent = Recent();
            Assert.That(recent.Count, Is.EqualTo(3));
            Assert.That(recent.Count, Is.EqualTo(expectedRecent.Count));
            for (var i = 0; i < recent.Count; i++)
            {
                Assert.That(recent[i].FormatLabel(), Is.EqualTo(expectedRecent[i].FormatLabel()), "label " + i);
                Assert.That(recent[i].Kind, Is.EqualTo(expectedRecent[i].Kind), "kind " + i);
                Assert.That(recent[i].Id, Is.EqualTo(expectedRecent[i].Id), "id " + i);
                Assert.That(recent[i].ParentId, Is.EqualTo(expectedRecent[i].ParentId), "parent " + i);
                Assert.That(recent[i].Generation, Is.EqualTo(expectedRecent[i].Generation), "generation " + i);
                Assert.That(recent[i].CancelCount, Is.EqualTo(expectedRecent[i].CancelCount), "cancels " + i);
                Assert.That(recent[i].TotalRegistered, Is.EqualTo(expectedRecent[i].TotalRegistered), "registered " + i);
                Assert.That(recent[i].RefusedCount, Is.EqualTo(expectedRecent[i].RefusedCount), "refused " + i);
                Assert.That(recent[i].CreatedFrame, Is.EqualTo(expectedRecent[i].CreatedFrame), "created " + i);
                Assert.That(recent[i].DisposedFrame, Is.EqualTo(expectedRecent[i].DisposedFrame), "disposed " + i);
                Assert.That(recent[i].Session, Is.EqualTo(expectedRecent[i].Session), "session " + i);
            }

            var warnings = Warnings();
            Assert.That(warnings.Count, Is.EqualTo(3));
            Assert.That(warnings.Count, Is.EqualTo(expectedWarnings.Count));
            for (var i = 0; i < warnings.Count; i++)
            {
                Assert.That(warnings[i].DiagnosticId, Is.EqualTo(expectedWarnings[i].DiagnosticId), "id " + i);
                Assert.That(warnings[i].Message, Is.EqualTo(expectedWarnings[i].Message), "message " + i);
                Assert.That(warnings[i].Severity, Is.EqualTo(expectedWarnings[i].Severity), "severity " + i);
                Assert.That(warnings[i].Count, Is.EqualTo(expectedWarnings[i].Count), "count " + i);
                Assert.That(warnings[i].FirstFrame, Is.EqualTo(expectedWarnings[i].FirstFrame), "first frame " + i);
                Assert.That(warnings[i].LastFrame, Is.EqualTo(expectedWarnings[i].LastFrame), "last frame " + i);
                Assert.That(warnings[i].LifetimeId, Is.EqualTo(expectedWarnings[i].LifetimeId), "lifetime id " + i);
                Assert.That(warnings[i].LifetimeLabel, Is.EqualTo(expectedWarnings[i].LifetimeLabel), "lifetime label " + i);
                Assert.That(warnings[i].Member, Is.EqualTo(expectedWarnings[i].Member), "member " + i);
                Assert.That(warnings[i].Line, Is.EqualTo(expectedWarnings[i].Line), "line " + i);
                Assert.That(warnings[i].Session, Is.EqualTo(expectedWarnings[i].Session), "session " + i);
                Assert.That(warnings[i].Sequence, Is.EqualTo(expectedWarnings[i].Sequence), "sequence " + i);
                Assert.That(warnings[i].Lifetime, Is.Null, "no lifetime object survives a reload");
                Assert.That(warnings[i].Context, Is.Null);
            }

            Assert.That(warnings[0].Count, Is.EqualTo(2), "the merged repeat keeps its counter");
            Assert.That(warnings[0].LifetimeLabel, Is.EqualTo("first"));
            Assert.That(warnings[0].Member, Is.Null, "no call site stays null, not an empty string");
            Assert.That(warnings[1].LifetimeLabel, Is.Null, "no lifetime stays null, not an empty string");
            Assert.That(warnings[1].Member, Is.Null);
            Assert.That(warnings[2].LifetimeLabel, Is.EqualTo("attached"));
            Assert.That(warnings[2].Member, Is.EqualTo("SiteX"));
            Assert.That(warnings[2].Line, Is.EqualTo(12));
            Assert.That(warnings[1].Severity, Is.EqualTo(DiagnosticSeverity.Info));
            Assert.That(warnings[1].Title, Is.EqualTo(DiagnosticIds.GetTitle(DiagnosticIds.Rehomed)), "a restored row still resolves its title");
            Assert.That(LifetimeDiagnostics.Session, Is.EqualTo(2), "the session counter continues where it stopped");
        }

        [Test]
        public void ExportThenImport_NewEntriesAfterTheRestoreFollowTheRestoredOnesInOrder()
        {
            RecordSomething();
            var json = LifetimeDiagnostics.ExportState();
            LifetimeDiagnostics.ResetAll();
            LifetimeDiagnostics.ImportState(json);

            _t.App.CreateChild("after").Dispose();

            var recent = Recent();
            Assert.That(recent.Count, Is.EqualTo(4));
            Assert.That(recent[0].FormatLabel(), Is.EqualTo("after"));
            Assert.That(recent[3].FormatLabel(), Is.EqualTo("first"), "the oldest restored entry is still the oldest");
        }

        [Test]
        public void ExportThenImport_NewWarningsAfterTheRestore_ContinueTheSequence()
        {
            var area = _t.App.CreateChild("area");
            LifetimeDiagnostics.Report(area, "TEST100", "same");
            var json = LifetimeDiagnostics.ExportState();
            LifetimeDiagnostics.ResetAll();
            LifetimeDiagnostics.ImportState(json);

            LifetimeDiagnostics.Report(null, "TEST100", "different");
            var rows = Warnings();

            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(rows[1].Sequence, Is.GreaterThan(rows[0].Sequence), "new entries continue the sequence");
        }

        [Test]
        public void ExportThenImport_AFullBuffer_RestoresAll200InOrder()
        {
            for (var i = 0; i < 230; i++)
            {
                _t.App.CreateChild("a" + i).Dispose();
            }

            var json = LifetimeDiagnostics.ExportState();
            LifetimeDiagnostics.ResetAll();
            LifetimeDiagnostics.ImportState(json);

            var recent = Recent();
            Assert.That(recent.Count, Is.EqualTo(200));
            Assert.That(recent[0].FormatLabel(), Is.EqualTo("a229"));
            Assert.That(recent[199].FormatLabel(), Is.EqualTo("a30"));
        }

        [Test]
        public void ImportState_ReplacesWhatWasRecorded()
        {
            var json = LifetimeDiagnostics.ExportState();
            _t.App.CreateChild("other").Dispose();
            LifetimeDiagnostics.Report(null, "TEST100", "other");

            LifetimeDiagnostics.ImportState(json);

            Assert.That(LifetimeDiagnostics.RecentCount, Is.Zero, "the exported state was empty");
            Assert.That(LifetimeDiagnostics.WarningCount, Is.Zero);
        }

        [Test]
        public void ExportState_WithNothingRecorded_IsValidAndRoundTripsToNothing()
        {
            var json = LifetimeDiagnostics.ExportState();

            Assert.That(json, Is.Not.Null.And.Not.Empty);
            LifetimeDiagnostics.ImportState(json);
            Assert.That(LifetimeDiagnostics.RecentCount, Is.Zero);
            Assert.That(LifetimeDiagnostics.WarningCount, Is.Zero);
        }

        [Test]
        public void ImportState_ANullOrEmptyString_ChangesNothingAndLogsNothing([Values(null, "")] string json)
        {
            RecordSomething();
            var warningsBefore = LifetimeDiagnostics.WarningCount;
            var recentBefore = LifetimeDiagnostics.RecentCount;

            LifetimeDiagnostics.ImportState(json);

            Assert.That(LifetimeDiagnostics.WarningCount, Is.EqualTo(warningsBefore));
            Assert.That(LifetimeDiagnostics.RecentCount, Is.EqualTo(recentBefore));
        }

        [Test]
        public void ImportState_AnUnreadableString_ChangesNothingAndIsContainedWithOneLoggedFailure()
        {
            RecordSomething();
            var warningsBefore = LifetimeDiagnostics.WarningCount;
            var recentBefore = LifetimeDiagnostics.RecentCount;
            // Unity logs the innermost exception of the wrapped failure: JsonUtility's own parse error.
            LogAssert.Expect(LogType.Exception, new Regex("JSON parse error"));

            Assert.DoesNotThrow(() => LifetimeDiagnostics.ImportState("this is not json"));

            Assert.That(LifetimeDiagnostics.WarningCount, Is.EqualTo(warningsBefore), "the recorded warnings are untouched");
            Assert.That(LifetimeDiagnostics.RecentCount, Is.EqualTo(recentBefore), "the recorded buffer is untouched");
        }

        // Valid JSON that is not a state of this format must not wipe the history (JsonUtility reads missing fields as defaults).
        [Test]
        public void ImportState_ValidJsonOfTheWrongShape_ChangesNothingAndLogsNothing(
            [Values("{}", "{\"Foo\":1}", "{\"Recent\":[],\"Warnings\":[]}", "{\"Version\":2,\"Recent\":[],\"Warnings\":[]}")] string json)
        {
            RecordSomething();
            var warningsBefore = Warnings();
            var recentBefore = Recent();
            var sessionBefore = LifetimeDiagnostics.Session;

            LifetimeDiagnostics.ImportState(json);

            var warnings = Warnings();
            var recent = Recent();
            Assert.That(warnings.Count, Is.EqualTo(warningsBefore.Count), "the recorded warnings are untouched");
            Assert.That(recent.Count, Is.EqualTo(recentBefore.Count), "the recorded buffer is untouched");
            Assert.That(warnings[0].Message, Is.EqualTo(warningsBefore[0].Message));
            Assert.That(recent[0].FormatLabel(), Is.EqualTo(recentBefore[0].FormatLabel()));
            Assert.That(LifetimeDiagnostics.Session, Is.EqualTo(sessionBefore));
        }

        // JsonUtility writes a null string back as an empty one; the window tells "no lifetime" and "no call site" by null.
        [Test]
        public void ImportState_AnAbsentLifetimeLabelAndCallSite_ComeBackNullNotEmpty()
        {
            LifetimeDiagnostics.Report(null, "TEST100", "unattached");
            var json = LifetimeDiagnostics.ExportState();
            LifetimeDiagnostics.ResetAll();

            LifetimeDiagnostics.ImportState(json);

            var warning = Warnings()[0];
            Assert.That(warning.LifetimeLabel, Is.Null);
            Assert.That(warning.Member, Is.Null);
            Assert.That(warning.Message, Is.EqualTo("unattached"));
        }
    }
}
