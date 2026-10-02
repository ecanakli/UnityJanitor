using System;
using System.Text.RegularExpressions;
using Ecanakli.Janitor.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Zenject;

namespace Ecanakli.Janitor.DependencyInjection.Tests
{
    // The duplicate rule (JANITOR105) for SignalBus: same handler and same owner returns the existing registration;
    // the same handler under another owner cannot be held by SignalBus and throws. The table that backs it is in SignalSubscriptions.
    [TestFixture]
    public sealed class SignalBusDuplicateTests : SignalBusFixtureBase
    {
        // Guard: the repeat is caught before it reaches SignalBus (whose own guard is editor-only), so there is one subscription and one delivery.
        [Test]
        public void Subscribe_SameHandlerSameOwner_ReturnsTheExistingRegistrationAndWarns()
        {
            var first = Bus.Subscribe<TestSignal>(Typed, Area);
            var entries = Area.EntryCount;
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));

            var second = Bus.Subscribe<TestSignal>(Typed, Area);
            FireTyped(5);

            Assert.That(second.IsActive, Is.True);
            Assert.That(second.EntryId, Is.EqualTo(first.EntryId), "the very same owner entry");
            Assert.That(second.EntryVersion, Is.EqualTo(first.EntryVersion));
            Assert.That(Area.EntryCount, Is.EqualTo(entries), "no second entry");
            Assert.That(Received, Is.EqualTo(new[] { 5 }), "one delivery per Fire");
            Assert.That(Bus.NumSubscribers, Is.EqualTo(1));
            Assert.That(SignalSubscriptions.LiveCount, Is.EqualTo(1));
            Assert.That(Warnings.Count("JANITOR105"), Is.EqualTo(1));
            Assert.That(Warnings.TextOf("JANITOR105"), Does.Contain("Signal<TestSignal>").And.Contain("Troubleshooting#janitor105"));
        }

        // Guard: equality is target plus method (the OnEnable double subscribe creates a fresh delegate every time), not reference identity.
        [Test]
        public void Subscribe_EqualHandlerFromAFreshMethodGroup_IsTheSameSubscription()
        {
            var first = Bus.Subscribe<TestSignal>(OnTyped, Area);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));

            var second = Bus.Subscribe<TestSignal>(OnTyped, Area);
            FireTyped(1);

            Assert.That(second.EntryId, Is.EqualTo(first.EntryId));
            Assert.That(Received, Is.EqualTo(new[] { 1 }));
            Assert.That(Bus.NumSubscribers, Is.EqualTo(1));
        }

        [Test]
        public void Subscribe_SamePlainHandlerSameOwner_ReturnsTheExistingRegistrationAndWarns()
        {
            var first = Bus.Subscribe<TestSignal>(OnPlain, Area);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));

            var second = Bus.Subscribe<TestSignal>(OnPlain, Area);
            FireTyped(1);

            Assert.That(second.EntryId, Is.EqualTo(first.EntryId));
            Assert.That(PlainHits, Is.EqualTo(1));
            Assert.That(Bus.NumSubscribers, Is.EqualTo(1));
        }

        // Guard: the returned registration is the live one, so cancelling it removes the subscription.
        [Test]
        public void Subscribe_SameHandlerSameOwner_TheReturnedRegistrationCancelsTheSubscription()
        {
            Bus.Subscribe<TestSignal>(Typed, Area);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));
            var second = Bus.Subscribe<TestSignal>(Typed, Area);

            second.Cancel();
            FireTyped(1);

            Assert.That(Received, Is.Empty);
            Assert.That(Bus.NumSubscribers, Is.Zero);
            Assert.That(SignalSubscriptions.LiveCount, Is.Zero);
        }

        // Guard: SignalBus holds one subscription per (signal, handler), so another owner cannot get it; the first subscription survives.
        [Test]
        public void Subscribe_SameHandlerDifferentOwner_ThrowsAndTheFirstSubscriptionStaysIntact()
        {
            var other = Scope.App.CreateChild("other");
            Bus.Subscribe<TestSignal>(Typed, Area);

            var exception = Assert.Throws<InvalidOperationException>(() => Bus.Subscribe<TestSignal>(Typed, other));
            FireTyped(2);

            Assert.That(exception.Message, Does.Contain("already subscribed"));
            Assert.That(other.EntryCount, Is.Zero, "nothing was registered on the second owner");
            Assert.That(Received, Is.EqualTo(new[] { 2 }));
            Assert.That(Bus.NumSubscribers, Is.EqualTo(1));
            Assert.That(SignalSubscriptions.LiveCount, Is.EqualTo(1));
            Assert.That(Warnings.Count("JANITOR105"), Is.Zero, "a different owner is an error, not a warning");

            Area.Cancel();
            Assert.That(() => Bus.Subscribe<TestSignal>(Typed, other), Throws.Nothing, "after the first ended, the second owner may subscribe it");
        }

        // After termination the record is gone

        [Test]
        public void Subscribe_AfterTheRegistrationWasCancelled_IsNotADuplicate()
        {
            var first = Bus.Subscribe<TestSignal>(Typed, Area);
            first.Cancel();

            var second = Bus.Subscribe<TestSignal>(Typed, Area);
            FireTyped(1);

            Assert.That(second.IsActive, Is.True);
            Assert.That(Warnings.Count("JANITOR105"), Is.Zero);
            Assert.That(Received, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void Subscribe_AfterTheOwnerWasDisposed_AnotherOwnerMaySubscribeTheHandler()
        {
            var first = Scope.App.CreateChild("first");
            var second = Scope.App.CreateChild("second");
            Bus.Subscribe<TestSignal>(Typed, first);
            first.Dispose();

            Assert.That(() => Bus.Subscribe<TestSignal>(Typed, second), Throws.Nothing);

            FireTyped(1);
            Assert.That(Received, Is.EqualTo(new[] { 1 }));
            Assert.That(SignalSubscriptions.LiveCount, Is.EqualTo(1));
        }

        // The key is (bus, signal type, handler, form)

        // Guard: the typed and the plain form are different subscriptions, even for one signal.
        [Test]
        public void Subscribe_TypedAndPlainHandlersForTheSameSignal_AreIndependent()
        {
            Bus.Subscribe<TestSignal>(Typed, Area);
            Bus.Subscribe<TestSignal>(Plain, Area);

            FireTyped(4);

            Assert.That(Received, Is.EqualTo(new[] { 4 }));
            Assert.That(PlainHits, Is.EqualTo(1));
            Assert.That(Bus.NumSubscribers, Is.EqualTo(2));
            Assert.That(SignalSubscriptions.LiveCount, Is.EqualTo(2));
            Assert.That(Warnings.Count("JANITOR105"), Is.Zero);
        }

        // Guard: the signal type is part of the key, so one plain handler may serve two signals under one owner.
        [Test]
        public void Subscribe_SamePlainHandlerForTwoSignals_AreIndependent()
        {
            Bus.Subscribe<TestSignal>(Plain, Area);
            Bus.Subscribe<OtherSignal>(Plain, Area);

            Bus.Fire(new OtherSignal());
            FireTyped(1);
            Area.Cancel();
            Bus.Fire(new OtherSignal());
            FireTyped(1);

            Assert.That(PlainHits, Is.EqualTo(2), "one delivery per signal type before the cancel, none after");
            Assert.That(Warnings.Count("JANITOR105"), Is.Zero);
            Assert.That(SignalSubscriptions.LiveCount, Is.Zero);
        }

        // Guard: each bus has its own table; one handler on two buses is two subscriptions.
        [Test]
        public void Subscribe_SameHandlerOnTwoBuses_AreIndependent()
        {
            var otherBus = NewBus(new DiContainer(StaticContext.Container), false);

            Bus.Subscribe<TestSignal>(Typed, Area);
            otherBus.Subscribe<TestSignal>(Typed, Area);
            FireTyped(1);
            otherBus.Fire(new TestSignal { Value = 2 });

            Assert.That(Received, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(Warnings.Count("JANITOR105"), Is.Zero);
            Assert.That(SignalSubscriptions.LiveCount, Is.EqualTo(2));
            Area.Cancel();
            Assert.That(otherBus.NumSubscribers, Is.Zero);
            Assert.That(Bus.NumSubscribers, Is.Zero);
        }
    }
}
