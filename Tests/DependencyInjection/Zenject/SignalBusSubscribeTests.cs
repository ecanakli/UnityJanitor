using System;
using Cysharp.Threading.Tasks;
using Ecanakli.Janitor.Tests;
using NUnit.Framework;
using Zenject;

namespace Ecanakli.Janitor.DependencyInjection.Tests
{
    // bus.Subscribe<TSignal>(handler, owner) with a plain Lifetime owner.
    [TestFixture]
    public sealed class SignalBusSubscribeTests : SignalBusFixtureBase
    {
        // Delivery

        [Test]
        public void Subscribe_TypedHandler_DeliversFiredSignals()
        {
            var registration = Bus.Subscribe<TestSignal>(Typed, Area);

            FireTyped(7);
            FireTyped(8);

            Assert.That(registration.IsActive, Is.True);
            Assert.That(Received, Is.EqualTo(new[] { 7, 8 }));
            Assert.That(Bus.NumSubscribers, Is.EqualTo(1));
            Assert.That(SignalSubscriptions.LiveCount, Is.EqualTo(1));
        }

        [Test]
        public void Subscribe_PlainHandler_DeliversFiredSignals()
        {
            var registration = Bus.Subscribe<TestSignal>(Plain, Area);

            FireTyped(1);
            FireTyped(2);

            Assert.That(registration.IsActive, Is.True);
            Assert.That(PlainHits, Is.EqualTo(2));
            Assert.That(Bus.NumSubscribers, Is.EqualTo(1));
        }

        // Termination: Cancel, Dispose, registration.Cancel

        // Guard: the terminate unsubscribes through TryUnsubscribe, exactly once; a second Cancel finds nothing and does not throw.
        [Test]
        public void Cancel_OnTheOwner_UnsubscribesOnce()
        {
            var registration = Bus.Subscribe<TestSignal>(Typed, Area);

            Area.Cancel();
            Area.Cancel();
            FireTyped(1);

            Assert.That(registration.IsActive, Is.False);
            Assert.That(Received, Is.Empty, "the handler is gone");
            Assert.That(Bus.NumSubscribers, Is.Zero);
            Assert.That(SignalSubscriptions.LiveCount, Is.Zero, "the table entry is gone");
        }

        [Test]
        public void Dispose_OnTheOwner_UnsubscribesOnce()
        {
            var child = Area.CreateChild("child");
            Bus.Subscribe<TestSignal>(Typed, child);
            Bus.Subscribe<TestSignal>(Plain, child);

            child.Dispose();
            child.Dispose();
            FireTyped(1);

            Assert.That(Received, Is.Empty);
            Assert.That(PlainHits, Is.Zero);
            Assert.That(Bus.NumSubscribers, Is.Zero);
            Assert.That(SignalSubscriptions.LiveCount, Is.Zero);
        }

        [Test]
        public void RegistrationCancel_UnsubscribesOnlyThatSubscription_AndALaterOwnerCancelDoesNothing()
        {
            var first = Bus.Subscribe<TestSignal>(Typed, Area);
            Bus.Subscribe<TestSignal>(Plain, Area);

            first.Cancel();
            first.Cancel();
            FireTyped(1);

            Assert.That(first.IsActive, Is.False);
            Assert.That(Received, Is.Empty);
            Assert.That(PlainHits, Is.EqualTo(1), "the other subscription is untouched");
            Assert.That(Bus.NumSubscribers, Is.EqualTo(1));
            Assert.That(SignalSubscriptions.LiveCount, Is.EqualTo(1));

            Area.Cancel();
            Assert.That(Bus.NumSubscribers, Is.Zero);
            Assert.That(SignalSubscriptions.LiveCount, Is.Zero, "the count never goes below the live records");
        }

        [Test]
        public void Cancel_ThenSubscribeAgain_DeliversInTheNewGeneration()
        {
            Bus.Subscribe<TestSignal>(Typed, Area);
            Area.Cancel();

            var again = Bus.Subscribe<TestSignal>(Typed, Area);
            FireTyped(3);

            Assert.That(again.IsActive, Is.True);
            Assert.That(Received, Is.EqualTo(new[] { 3 }));
            Assert.That(Warnings.Count("JANITOR105"), Is.Zero, "the old record is gone, so this is not a duplicate");
        }

        // Guard: the lifetime's own removal order does not matter; two subscriptions of one owner both end.
        [Test]
        public void Cancel_WithSubscriptionsToTwoSignals_EndsBoth()
        {
            Bus.Subscribe<TestSignal>(Typed, Area);
            Bus.Subscribe<OtherSignal>(Plain, Area);

            Area.Cancel();

            Assert.That(Bus.NumSubscribers, Is.Zero);
            Assert.That(SignalSubscriptions.LiveCount, Is.Zero);
        }

        // Guard: SignalBus removal during Fire is immediate, so an unsubscribe caused by an earlier handler means the later handler is skipped.
        [Test]
        public void Fire_EarlierHandlerCancelsALaterOwner_TheLaterHandlerIsNotCalled()
        {
            var second = Scope.App.CreateChild("second");
            var laterHits = 0;
            Bus.Subscribe<TestSignal>(_ => second.Cancel(), Area);
            Bus.Subscribe<TestSignal>(_ => laterHits++, second);

            FireTyped(1);
            FireTyped(2);

            Assert.That(laterHits, Is.Zero);
            Assert.That(Bus.NumSubscribers, Is.EqualTo(1));
            Assert.That(SignalSubscriptions.LiveCount, Is.EqualTo(1));
        }

        // Owner not Active

        // Guard: the owner entry is registered first, and a Cancelling owner refuses it, so the bus is never subscribed.
        [Test]
        public void Subscribe_WhileTheOwnerIsCancelling_SubscribesNothing()
        {
            var inner = default(LifetimeRegistration);
            Area.OnCancel(() => inner = Bus.Subscribe<TestSignal>(Typed, Area));

            Area.Cancel();
            FireTyped(1);

            Assert.That(inner.IsActive, Is.False);
            Assert.That(Received, Is.Empty);
            Assert.That(Bus.NumSubscribers, Is.Zero);
            Assert.That(SignalSubscriptions.LiveCount, Is.Zero);
        }

        [Test]
        public void Subscribe_OnADisposedOwner_SubscribesNothing()
        {
            var dead = Scope.App.CreateChild("dead");
            dead.Dispose();

            var registration = Bus.Subscribe<TestSignal>(Typed, dead);
            FireTyped(1);

            Assert.That(registration.IsActive, Is.False);
            Assert.That(Received, Is.Empty);
            Assert.That(Bus.NumSubscribers, Is.Zero);
            Assert.That(SignalSubscriptions.LiveCount, Is.Zero);
        }

        // Guard: a refused owner never reaches SignalBus, so an undeclared signal is not even noticed (an active owner throws for it).
        [Test]
        public void Subscribe_OnADisposedOwner_NeverCallsTheBus()
        {
            var dead = Scope.App.CreateChild("dead");
            dead.Dispose();

            Assert.That(() => Bus.Subscribe<UndeclaredSignal>(Plain, dead), Throws.Nothing);
            Assert.That(() => Bus.Subscribe<UndeclaredSignal>(Plain, Area), Throws.Exception);
        }

        // Guard: a SignalBus error undoes the owner entry without calling TryUnsubscribe, then reaches the caller.
        [Test]
        public void Subscribe_UndeclaredSignal_ThrowsAndLeavesNothingBehind()
        {
            var entries = Area.EntryCount;

            Assert.That(() => Bus.Subscribe<UndeclaredSignal>(Plain, Area), Throws.Exception);

            Assert.That(Area.EntryCount, Is.EqualTo(entries), "the owner entry was undone");
            Assert.That(SignalSubscriptions.LiveCount, Is.Zero);
            Assert.That(Bus.NumSubscribers, Is.Zero);
        }

        // Arguments and threading

        [Test]
        public void Subscribe_NullOwner_ThrowsArgumentNullExceptionAndSubscribesNothing()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => Bus.Subscribe<TestSignal>(Typed, (Lifetime)null));

            Assert.That(exception.ParamName, Is.EqualTo("owner"));
            Assert.That(Bus.NumSubscribers, Is.Zero);
            Assert.That(SignalSubscriptions.LiveCount, Is.Zero);
        }

        [Test]
        public void Subscribe_NullHandler_ThrowsArgumentNullExceptionAndSubscribesNothing()
        {
            var typed = Assert.Throws<ArgumentNullException>(() => Bus.Subscribe<TestSignal>((Action<TestSignal>)null, Area));
            var plain = Assert.Throws<ArgumentNullException>(() => Bus.Subscribe<TestSignal>((Action)null, Area));

            Assert.That(typed.ParamName, Is.EqualTo("handler"));
            Assert.That(plain.ParamName, Is.EqualTo("handler"));
            Assert.That(Bus.NumSubscribers, Is.Zero);
            Assert.That(Area.EntryCount, Is.Zero);
        }

        [Test]
        public void Subscribe_NullBus_ThrowsArgumentNullException()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => LifetimeSignalBusExtensions.Subscribe<TestSignal>(null, Typed, Area));

            Assert.That(exception.ParamName, Is.EqualTo("bus"));
        }

        // Guard: the thread check runs before any state is touched.
        [Test]
        public void Subscribe_OffTheMainThread_ThrowsInvalidOperationExceptionAndSubscribesNothing()
        {
            Assert.That(PlayerLoopHelper.MainThreadId, Is.Not.Zero, "UniTask must know the main thread for the check to run");

            var failure = ThreadRunner.Run(() => Bus.Subscribe<TestSignal>(Typed, Area));

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(Bus.NumSubscribers, Is.Zero);
            Assert.That(Area.EntryCount, Is.Zero);
            Assert.That(SignalSubscriptions.LiveCount, Is.Zero);
        }

        // Strict unsubscribe

        // Guard: the control proves the strict setting is live; without it the passing case below would prove nothing.
        [Test]
        public void StrictLateDispose_WithARawSubscriptionLeftBehind_Throws()
        {
            var container = new DiContainer(StaticContext.Container);
            var strictBus = NewBus(container, true);
            strictBus.Subscribe<TestSignal>(Typed);

            var exception = Assert.Catch(() => strictBus.LateDispose());

            Assert.That(exception.Message, Does.Contain("Found subscriptions for signals"));
        }

        // Guard: the owner-bound subscription is gone when its lifetime ended, so a strict SignalBus.LateDispose finds nothing.
        [Test]
        public void StrictLateDispose_AfterTheOwnerEnded_Passes()
        {
            var container = new DiContainer(StaticContext.Container);
            var strictBus = NewBus(container, true);
            var owner = Scope.App.CreateChild("owner");
            strictBus.Subscribe<TestSignal>(Typed, owner);
            strictBus.Subscribe<OtherSignal>(Plain, owner);
            var cancelled = owner.CreateChild("cancelled");
            strictBus.Subscribe<TestSignal>(_ => { }, cancelled);

            owner.Cancel();

            Assert.That(strictBus.NumSubscribers, Is.Zero);
            Assert.That(() => strictBus.LateDispose(), Throws.Nothing);
        }
    }
}
