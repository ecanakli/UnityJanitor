using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // The public LifetimeDiagnostics.Report hook that integration assemblies call (DOTween, Zenject, an event bus). It records
    // only; attribution comes from the lifetime the caller names, or from the item whose cancel action is running.
    [TestFixture]
    public sealed class ReportHookTests
    {
        private const string Id = "TEST100";

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

        // An explicit lifetime

        [Test]
        public void Report_WithAnExplicitLifetime_RecordsItAttachedWithoutACallSite()
        {
            var area = _t.App.CreateChild("area");

            LifetimeDiagnostics.Report(area, Id, "an integration problem");

            var warning = _d.Single(Id);
            Assert.That(warning.Lifetime, Is.SameAs(area));
            Assert.That(warning.LifetimeLabel, Is.EqualTo("area"));
            Assert.That(warning.Message, Is.EqualTo("an integration problem"));
            Assert.That(warning.Member, Is.Null);
            Assert.That(warning.Line, Is.Zero);
            Assert.That(warning.Context, Is.Null);
            Assert.That(warning.Count, Is.EqualTo(1));
            Assert.That(warning.FirstFrame, Is.EqualTo(_d.Frame));
            Assert.That(warning.IsInfo, Is.False);
        }

        [Test]
        public void Report_WithAContextObject_KeepsItForThePing()
        {
            var area = _t.App.CreateChild("area");
            var context = new GameObject("ReportContext");
            try
            {
                LifetimeDiagnostics.Report(area, Id, "message", context);

                Assert.That(_d.Single(Id).Context, Is.SameAs(context));
            }
            finally
            {
                Object.DestroyImmediate(context);
            }
        }

        [Test]
        public void Report_AKnownId_UsesItsTitleAndAnchor_AnUnknownIdHasNeither()
        {
            LifetimeDiagnostics.Report(null, DiagnosticIds.UnkillableTween, "known");
            LifetimeDiagnostics.Report(null, Id, "unknown");
            var rows = new List<DiagnosticWarning>();

            LifetimeDiagnostics.CopyWarnings(rows);

            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(rows[0].Title, Is.EqualTo(DiagnosticIds.GetTitle(DiagnosticIds.UnkillableTween)));
            Assert.That(rows[0].Anchor, Is.EqualTo("Documentation~/Troubleshooting.md#janitor102"));
            Assert.That(rows[1].Title, Is.Null);
            Assert.That(rows[1].Anchor, Is.Null);
            Assert.That(rows[1].Severity, Is.EqualTo(DiagnosticSeverity.Warning), "an unknown id is a warning");
        }

        [Test]
        public void Report_TheRehomedId_IsInformation()
        {
            LifetimeDiagnostics.Report(null, DiagnosticIds.Rehomed, "info");

            Assert.That(_d.Single(DiagnosticIds.Rehomed).IsInfo, Is.True);
        }

        // Attribution from inside a cancel action

        [Test]
        public void Report_WithANullLifetimeInsideACancelAction_AttachesToTheCancellingLifetimeAndItsCallSite()
        {
            var area = _t.App.CreateChild("area");
            area.OnCancel(() => LifetimeDiagnostics.Report(null, Id, "from an action"), "RegisteredHere", 123);

            area.Cancel();

            var warning = _d.Single(Id);
            Assert.That(warning.Lifetime, Is.SameAs(area));
            Assert.That(warning.LifetimeLabel, Is.EqualTo("area"));
            Assert.That(warning.Member, Is.EqualTo("RegisteredHere"), "the call site of the item whose action reported");
            Assert.That(warning.Line, Is.EqualTo(123));
        }

        [Test]
        public void Report_WithTheCancellingLifetimeNamedExplicitly_AttachesTheCallSiteToo()
        {
            var area = _t.App.CreateChild("area");
            area.OnCancel(() => LifetimeDiagnostics.Report(area, Id, "from an action"), "RegisteredHere", 124);

            area.Cancel();

            var warning = _d.Single(Id);
            Assert.That(warning.Lifetime, Is.SameAs(area));
            Assert.That(warning.Member, Is.EqualTo("RegisteredHere"));
            Assert.That(warning.Line, Is.EqualTo(124));
        }

        [Test]
        public void Report_WithAnotherLifetimeInsideACancelAction_KeepsThatLifetimeWithoutACallSite()
        {
            var area = _t.App.CreateChild("area");
            var other = _t.App.CreateChild("other");
            area.OnCancel(() => LifetimeDiagnostics.Report(other, Id, "about another lifetime"), "RegisteredHere", 125);

            area.Cancel();

            var warning = _d.Single(Id);
            Assert.That(warning.Lifetime, Is.SameAs(other), "the caller named the lifetime, so it is not overridden");
            Assert.That(warning.Member, Is.Null);
            Assert.That(warning.Line, Is.Zero);
        }

        [Test]
        public void Report_InsideANestedCancelAction_UsesTheInnermostLifetimeAndRestoresTheOuterOne()
        {
            var outer = _t.App.CreateChild("outer");
            var inner = _t.App.CreateChild("inner");
            inner.OnCancel(() => LifetimeDiagnostics.Report(null, Id, "inner"), "InnerSite", 1);
            outer.OnCancel(() =>
            {
                inner.Cancel();
                LifetimeDiagnostics.Report(null, Id, "outer");
            }, "OuterSite", 2);

            outer.Cancel();

            var rows = _d.Warnings(Id);
            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(rows[0].Message, Is.EqualTo("inner"));
            Assert.That(rows[0].Lifetime, Is.SameAs(inner));
            Assert.That(rows[0].Member, Is.EqualTo("InnerSite"));
            Assert.That(rows[1].Message, Is.EqualTo("outer"));
            Assert.That(rows[1].Lifetime, Is.SameAs(outer), "the outer item's context is back after the nested cancel returned");
            Assert.That(rows[1].Member, Is.EqualTo("OuterSite"));
        }

        [Test]
        public void Report_InsideTheActionOfAnImmediatelyTerminatedRegistration_IsAttachedToThatLifetime()
        {
            var area = _t.App.CreateChild("area");
            area.Dispose();

            area.OnCancel(() => LifetimeDiagnostics.Report(null, Id, "late"), "LateSite", 9);

            var warning = _d.Single(Id);
            Assert.That(warning.Lifetime, Is.SameAs(area), "a registration on an ended lifetime runs its action at once");
            Assert.That(warning.Member, Is.EqualTo("LateSite"));
        }

        [Test]
        public void Report_AfterTheCancelActionReturned_IsUnattachedAgain()
        {
            var area = _t.App.CreateChild("area");
            area.OnCancel(() => { }, "RegisteredHere", 126);
            area.Cancel();

            LifetimeDiagnostics.Report(null, Id, "after");

            var warning = _d.Single(Id);
            Assert.That(warning.Lifetime, Is.Null);
            Assert.That(warning.Member, Is.Null);
        }

        [Test]
        public void Report_AfterACancelActionThatThrew_IsUnattachedAgain()
        {
            var area = _t.App.CreateChild("area");
            area.OnCancel(() => throw new System.InvalidOperationException("boom"), "ThrowingSite", 127);
            area.Cancel();
            _t.Errors.Clear();

            LifetimeDiagnostics.Report(null, Id, "after a failure");

            Assert.That(_d.Single(Id).Lifetime, Is.Null, "the context is restored even when the action failed");
        }

        // No cancel in progress, repeats, bounds

        [Test]
        public void Report_WithANullLifetimeAndNoCancelInProgress_IsStillRecordedUnattached()
        {
            LifetimeDiagnostics.Report(null, Id, "nobody's lifetime");

            var warning = _d.Single(Id);
            Assert.That(warning.Lifetime, Is.Null);
            Assert.That(warning.LifetimeLabel, Is.Null);
            Assert.That(warning.Member, Is.Null);
            Assert.That(warning.Line, Is.Zero);
            Assert.That(warning.Message, Is.EqualTo("nobody's lifetime"));
        }

        [Test]
        public void Report_TheSameIdLifetimeAndMessage_IsOneEntryWithACounter()
        {
            var area = _t.App.CreateChild("area");

            LifetimeDiagnostics.Report(area, Id, "again");
            _d.AdvanceFrames(2);
            LifetimeDiagnostics.Report(area, Id, "again");
            _d.AdvanceFrames(2);
            LifetimeDiagnostics.Report(area, Id, "again");

            var warning = _d.Single(Id);
            Assert.That(warning.Count, Is.EqualTo(3));
            Assert.That(warning.FirstFrame, Is.EqualTo(100));
            Assert.That(warning.LastFrame, Is.EqualTo(104));
        }

        [Test]
        public void Report_ADifferentLifetimeOrMessage_IsAnotherEntry()
        {
            var first = _t.App.CreateChild("first");
            var second = _t.App.CreateChild("second");

            LifetimeDiagnostics.Report(first, Id, "same text");
            LifetimeDiagnostics.Report(second, Id, "same text");
            LifetimeDiagnostics.Report(first, Id, "other text");

            Assert.That(_d.Count(Id), Is.EqualTo(3));
        }

        [Test]
        public void Report_WithoutAnIdOrAMessage_NeverThrows()
        {
            Assert.DoesNotThrow(() => LifetimeDiagnostics.Report(null, null, null));
            Assert.DoesNotThrow(() => LifetimeDiagnostics.Report(_t.App, null, "no id"));
            Assert.DoesNotThrow(() => LifetimeDiagnostics.Report(_t.App, Id, null));

            Assert.That(_d.Total, Is.EqualTo(1), "a null or empty id is ignored; a null message is recorded as an empty one");
            Assert.That(_d.Single(Id).Message, Is.Empty);
        }

        [Test]
        public void Report_WithAnEmptyId_RecordsNothing()
        {
            LifetimeDiagnostics.Report(_t.App, string.Empty, "no id");
            LifetimeDiagnostics.Report(null, "", "no id either");

            Assert.That(_d.Total, Is.Zero, "a row with a blank ID would have nothing to show or to link to");
        }

        [Test]
        public void Report_ManyDistinctMessages_AreBoundedAt256KeepingTheNewest()
        {
            for (var i = 0; i < 300; i++)
            {
                LifetimeDiagnostics.Report(null, Id, "m" + i);
            }

            var rows = _d.Warnings(Id);
            Assert.That(rows.Count, Is.EqualTo(256));
            Assert.That(rows[0].Message, Is.EqualTo("m44"), "the 44 oldest were dropped");
            Assert.That(rows[255].Message, Is.EqualTo("m299"));
        }

        [Test]
        public void Report_FromAWorkerThread_IsRecordedAndNeverThrows()
        {
            var area = _t.App.CreateChild("area");

            var failure = ThreadRunner.Run(() => LifetimeDiagnostics.Report(area, Id, "off thread"));

            Assert.That(failure, Is.Null);
            Assert.That(_d.Single(Id).Lifetime, Is.SameAs(area));
        }

        [Test]
        public void Report_FromAWorkerThreadWithNoLifetime_IsNotAttachedToWhateverTheMainThreadIsTerminating()
        {
            var area = _t.App.CreateChild("area");
            area.OnCancel(
                () =>
                {
                    var failure = ThreadRunner.Run(() => LifetimeDiagnostics.Report(null, Id, "worker"));
                    Assert.That(failure, Is.Null);
                },
                "MainThreadSite",
                5);

            area.Cancel();

            var warning = _d.Single(Id);
            Assert.That(warning.Lifetime, Is.Null, "the context of a cancel action belongs to the main thread only");
            Assert.That(warning.Member, Is.Null);
        }

        [Test]
        public void Report_WhileTrackingIsOff_RecordsNothing()
        {
            LifetimeDiagnostics.TrackingEnabled = false;

            LifetimeDiagnostics.Report(_t.App, Id, "ignored");

            Assert.That(_d.Total, Is.Zero);
        }
    }
}
