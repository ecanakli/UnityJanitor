#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Ecanakli.Janitor.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.DependencyInjection.Tests
{
    // JANITOR105 for SignalBus subscriptions. The Zenject assembly cannot reach the core's internals, so it reports through the
    // public LifetimeDiagnostics.Report hook and keeps its own console warning. Also how a signal entry is counted and labelled.
    [TestFixture]
    public sealed class SignalBusDiagnosticsTests : SignalBusFixtureBase
    {
        private DiagnosticsRecordingKit _d;

        [SetUp]
        public void SetUpDiagnostics()
        {
            _d = new DiagnosticsRecordingKit();
        }

        [TearDown]
        public void TearDownDiagnostics()
        {
            _d.Dispose();
        }

        [Test]
        public void Subscribe_SameHandlerSameOwner_RecordsJanitor105ThroughThePublicHookAttachedToTheOwner()
        {
            Bus.Subscribe<TestSignal>(Typed, Area);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));

            Bus.Subscribe<TestSignal>(Typed, Area);

            var warning = _d.Single(DiagnosticIds.DuplicateSubscription);
            Assert.That(warning.Lifetime, Is.SameAs(Area));
            Assert.That(warning.LifetimeLabel, Is.EqualTo("area"));
            Assert.That(warning.Message, Does.Contain("Signal<TestSignal>").And.Contain("'area'"));
            Assert.That(warning.Count, Is.EqualTo(1));
            Assert.That(warning.IsInfo, Is.False);
            Assert.That(Warnings.Count("JANITOR105"), Is.EqualTo(1), "the console warning is kept");
            Assert.That(Warnings.TextOf("JANITOR105"), Is.EqualTo(ZenjectDiagnostics.Format(DiagnosticIds.DuplicateSubscription, warning.Message)), "console and record agree");
        }

        [Test]
        public void Subscribe_TheSameDuplicateAgain_RaisesTheCounterOfOneEntry()
        {
            Bus.Subscribe<TestSignal>(Typed, Area);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));

            Bus.Subscribe<TestSignal>(Typed, Area);
            _d.AdvanceFrames(2);
            Bus.Subscribe<TestSignal>(Typed, Area);

            var warning = _d.Single(DiagnosticIds.DuplicateSubscription);
            Assert.That(warning.Count, Is.EqualTo(2));
            Assert.That(warning.LastFrame, Is.EqualTo(warning.FirstFrame + 2));
        }

        [Test]
        public void Subscribe_APlainHandlerDuplicate_IsRecordedToo()
        {
            Bus.Subscribe<TestSignal>(Plain, Area);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));

            Bus.Subscribe<TestSignal>(Plain, Area);

            Assert.That(_d.Single(DiagnosticIds.DuplicateSubscription).Lifetime, Is.SameAs(Area));
        }

        [Test]
        public void Subscribe_NewHandlersAndOtherSignals_RecordNothing()
        {
            Bus.Subscribe<TestSignal>(Typed, Area);
            Bus.Subscribe<TestSignal>(Plain, Area);
            Bus.Subscribe<OtherSignal>(Plain, Area);

            Assert.That(_d.Total, Is.Zero);
        }

        [Test]
        public void Subscribe_SameHandlerDifferentOwner_IsAnErrorNotAWarningAndRecordsNothing()
        {
            var other = Scope.App.CreateChild("other");
            Bus.Subscribe<TestSignal>(Typed, Area);

            Assert.Throws<InvalidOperationException>(() => Bus.Subscribe<TestSignal>(Typed, other));

            Assert.That(_d.Count(DiagnosticIds.DuplicateSubscription), Is.Zero);
        }

        [Test]
        public void Subscribe_WhileTrackingIsOff_StillWarnsOnTheConsoleButRecordsNothing()
        {
            Bus.Subscribe<TestSignal>(Typed, Area);
            LifetimeDiagnostics.TrackingEnabled = false;
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));

            Bus.Subscribe<TestSignal>(Typed, Area);

            Assert.That(_d.Total, Is.Zero);
        }

        // Entry kind and label

        [Test]
        public void CaptureEntries_ASignalSubscription_IsASubscriptionLabelledWithTheSignalType()
        {
            Bus.Subscribe<TestSignal>(Typed, Area);
            Bus.Subscribe<OtherSignal>(Plain, Area);
            var entries = new List<EntryView>();
            var snapshot = new DiagnosticsSnapshot();

            LifetimeDiagnostics.CaptureEntries(Area, entries);
            LifetimeDiagnostics.CaptureDefault(snapshot);

            Assert.That(entries.Count, Is.EqualTo(2));
            Assert.That(entries[0].Kind, Is.EqualTo(EntryKind.Subscription));
            Assert.That(entries[0].FormatLabel(), Is.EqualTo("Signal"), "a handler without an argument carries no signal type");
            Assert.That(entries[1].Kind, Is.EqualTo(EntryKind.Subscription));
            Assert.That(entries[1].FormatLabel(), Is.EqualTo("Signal<TestSignal>"));
            var subscriptions = -1;
            for (var i = 0; i < snapshot.Nodes.Count; i++)
            {
                if (ReferenceEquals(snapshot.Nodes[i].Lifetime, Area))
                {
                    subscriptions = snapshot.Nodes[i].Subscriptions;
                }
            }

            Assert.That(subscriptions, Is.EqualTo(2), "counted in the subscriptions column");
        }
    }
}
#endif
