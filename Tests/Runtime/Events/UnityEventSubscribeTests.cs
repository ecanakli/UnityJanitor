using System;
using NUnit.Framework;
using UnityEngine.Events;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.Events
{
    // A UnityEvent subscription adds a guard listener, and its end removes exactly that guard.
    [TestFixture]
    public sealed class UnityEventSubscribeTests
    {
        private EventFixture _f;
        private EventSink _sink;
        private Lifetime _owner;

        [SetUp]
        public void SetUp()
        {
            _f = new EventFixture();
            _sink = new EventSink(_f.Log, "s");
            _owner = _f.Area.CreateChild("owner");
        }

        [TearDown]
        public void TearDown()
        {
            _f.Complete();
        }

        // The engine count the other tests rely on

        // Guard: UnityEventInspector; if this fails the listener-count assertions in this folder are not checking anything.
        [Test]
        public void ListenerCountInspector_TracksAddAndRemove()
        {
            Assert.That(UnityEventInspector.IsCalibrated(), Is.True, "UnityEventBase.m_Calls.Count could not be read; update UnityEventInspector for this engine version");
        }

        // Delivery

        // Guard: UnityEventGuard.Invoke calls the handler while the subscription is live.
        [Test]
        public void Subscribe_Handler_IsDeliveredOncePerInvoke()
        {
            var evt = new UnityEvent();

            var registration = evt.Subscribe(_sink.Zero, _owner);
            evt.Invoke();
            evt.Invoke();

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "s", "s" }));
            Assert.That(registration.IsActive, Is.True);
            Assert.That(_owner.EntryCount, Is.EqualTo(1));
            UnityEventInspector.AssertCount(evt, 1);
        }

        // Guard: each guard is its own listener, so subscription order is delivery order.
        [Test]
        public void Subscribe_TwoHandlers_AreDeliveredInSubscriptionOrder()
        {
            var evt = new UnityEvent();
            var other = new EventSink(_f.Log, "o");

            evt.Subscribe(_sink.Zero, _owner);
            evt.Subscribe(other.Zero, _owner);
            evt.Invoke();

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "s", "o" }));
            Assert.That(_owner.EntryCount, Is.EqualTo(2));
        }

        // Guard: type inference from a derived event class (the serialized-event pattern) picks the generic form.
        [Test]
        public void Subscribe_DerivedEventType_WorksLikeTheBaseEvent()
        {
            var evt = new IntEvent();

            evt.Subscribe(_sink.One, _owner);
            evt.Invoke(4);
            _owner.Cancel();
            evt.Invoke(5);

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "s:4" }));
            UnityEventInspector.AssertCount(evt, 0);
        }

        // Removal removes exactly the guard

        // Guard: Terminate calls RemoveListener(guard.Listener), which matches this guard only, never a raw listener around it.
        [Test]
        public void Cancel_RemovesTheGuard_AndExtraRawListenersBeforeAndAfterItStillFire()
        {
            var evt = new UnityEvent();
            var before = new EventSink(_f.Log, "before");
            var after = new EventSink(_f.Log, "after");
            evt.AddListener(before.Zero);
            evt.Subscribe(_sink.Zero, _owner);
            evt.AddListener(after.Zero);
            UnityEventInspector.AssertCount(evt, 3);
            evt.Invoke();
            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "before", "s", "after" }), "precondition: all three listeners fire");
            _f.Log.Clear();

            _owner.Cancel();
            evt.Invoke();

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "before", "after" }), "the raw listeners survive, ours is gone");
            UnityEventInspector.AssertCount(evt, 2);
            Assert.That(_owner.EntryCount, Is.Zero);
        }

        // Guard: same as above through Dispose.
        [Test]
        public void Dispose_RemovesTheGuard_AndAnExtraRawListenerStillFires()
        {
            var evt = new UnityEvent();
            var raw = new EventSink(_f.Log, "raw");
            evt.AddListener(raw.Zero);
            evt.Subscribe(_sink.Zero, _owner);

            _owner.Dispose();
            evt.Invoke();

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "raw" }));
            UnityEventInspector.AssertCount(evt, 1);
        }

        // Guard: same as above through registration.Cancel().
        [Test]
        public void RegistrationCancel_RemovesTheGuard_AndAnExtraRawListenerStillFires()
        {
            var evt = new UnityEvent();
            var raw = new EventSink(_f.Log, "raw");
            evt.AddListener(raw.Zero);
            var registration = evt.Subscribe(_sink.Zero, _owner);

            registration.Cancel();
            evt.Invoke();

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "raw" }));
            Assert.That(registration.IsActive, Is.False);
            Assert.That(_owner.EntryCount, Is.Zero);
            UnityEventInspector.AssertCount(evt, 1);
        }

        // Guard: the guard's target is the guard, so the removal cannot strip a raw listener of the same method and target (RemoveListener matches by target and method).
        [Test]
        public void Cancel_ARawListenerOfTheSameMethodAndTarget_SurvivesTheGuardRemoval()
        {
            var evt = new UnityEvent();
            evt.AddListener(_sink.Zero);
            evt.Subscribe(_sink.Zero, _owner);
            evt.Invoke();
            Assert.That(_sink.Calls, Is.EqualTo(2), "precondition: the raw listener and the guard both deliver");

            _owner.Cancel();
            evt.Invoke();

            Assert.That(_sink.Calls, Is.EqualTo(3), "only the raw listener delivers after the guard is gone");
            UnityEventInspector.AssertCount(evt, 1);
        }

        // Guard: the raw handler is never added to the event, so after the guard is removed nothing can call it.
        [Test]
        public void Cancel_TheRawHandlerIsNeverLeftAttached()
        {
            var evt = new UnityEvent();
            evt.Subscribe(_sink.Zero, _owner);

            _owner.Cancel();
            for (var i = 0; i < 3; i++)
            {
                evt.Invoke();
            }

            Assert.That(_sink.Calls, Is.Zero);
            UnityEventInspector.AssertCount(evt, 0);
        }

        // Guard: a registration cancel ends one subscription; the rest of the owner's and the other owners' subscriptions stay.
        [Test]
        public void RegistrationCancel_RemovesExactlyOneOfManySubscriptions()
        {
            var evt = new UnityEvent();
            var otherOwner = _f.Area.CreateChild("other");
            var first = new EventSink(_f.Log, "first");
            var second = new EventSink(_f.Log, "second");
            var third = new EventSink(_f.Log, "third");
            evt.Subscribe(first.Zero, _owner);
            var middle = evt.Subscribe(second.Zero, _owner);
            evt.Subscribe(third.Zero, otherOwner);

            middle.Cancel();
            evt.Invoke();

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "first", "third" }));
            Assert.That(_owner.EntryCount, Is.EqualTo(1));
            Assert.That(otherOwner.EntryCount, Is.EqualTo(1));
            UnityEventInspector.AssertCount(evt, 2);
        }

        // Guard: End runs once (the _ended flag), so repeated and late cancels never remove anything a second time.
        [Test]
        public void Cancel_CalledRepeatedlyAndAfterTheRegistrationCancel_RemovesTheGuardOnlyOnce()
        {
            var evt = new UnityEvent();
            var raw = new EventSink(_f.Log, "raw");
            var registration = evt.Subscribe(_sink.Zero, _owner);
            evt.AddListener(raw.Zero);

            registration.Cancel();
            registration.Cancel();
            _owner.Cancel();
            _owner.Cancel();
            evt.Invoke();

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "raw" }));
            UnityEventInspector.AssertCount(evt, 1);
            Assert.That(_f.Errors.Count, Is.Zero);
        }

        // Guard: an owner Cancel ends the generation, and a new Subscribe lands in the next one.
        [Test]
        public void Subscribe_AfterCancelReturns_SubscribesInTheNewGeneration()
        {
            var evt = new UnityEvent();
            evt.Subscribe(_sink.Zero, _owner);
            _owner.Cancel();

            var registration = evt.Subscribe(_sink.Zero, _owner);
            evt.Invoke();

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "s" }));
            Assert.That(registration.IsActive, Is.True);
            UnityEventInspector.AssertCount(evt, 1);
            _owner.Cancel();
            evt.Invoke();
            Assert.That(_sink.Calls, Is.EqualTo(1));
            UnityEventInspector.AssertCount(evt, 0);
        }

        // The add is skipped

        // Guard: TryBegin returns default when the owner is not Active, before any listener is created or added.
        [Test]
        public void Subscribe_WhileTheOwnerIsCancelling_AddsNothing()
        {
            var evt = new UnityEvent();
            LifetimeRegistration seen = default;
            _owner.OnCancel(() => seen = evt.Subscribe(_sink.Zero, _owner));

            _owner.Cancel();
            evt.Invoke();

            Assert.That(seen.IsActive, Is.False);
            Assert.That(_sink.Calls, Is.Zero);
            Assert.That(_owner.EntryCount, Is.Zero);
            UnityEventInspector.AssertCount(evt, 0);
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: same, for a disposed owner.
        [Test]
        public void Subscribe_OnADisposedOwner_AddsNothing()
        {
            var evt = new UnityEvent();
            _owner.Dispose();

            var registration = evt.Subscribe(_sink.Zero, _owner);
            evt.Invoke();

            Assert.That(registration.IsActive, Is.False);
            Assert.That(_sink.Calls, Is.Zero);
            UnityEventInspector.AssertCount(evt, 0);
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: Attach does not arm the guard when Register refused it, and the refusal's Terminate removed the listener again (JANITOR108).
        [Test]
        public void Subscribe_OnAnActiveLifetimeWhileTheObjectIsInactive_AddsNothingAndLogsJanitor108()
        {
            var evt = new UnityEvent();
            var listener = _f.NewListener();
            var active = listener.GetActiveLifetime();
            listener.gameObject.SetActive(false);
            EventFixture.ExpectWarning("JANITOR108");

            var registration = evt.Subscribe(_sink.Zero, active);
            evt.Invoke();

            Assert.That(registration.IsActive, Is.False);
            Assert.That(_sink.Calls, Is.Zero);
            UnityEventInspector.AssertCount(evt, 0);
            LogAssert.NoUnexpectedReceived();
        }

        // Arguments

        // Guard: the argument checks run before anything is added; each names its parameter.
        [Test]
        public void Subscribe_NullArguments_ThrowAndAddNothing()
        {
            var evt = new UnityEvent();
            UnityEvent none = null;
            Lifetime noOwner = null;

            var eventNull = Assert.Throws<ArgumentNullException>(() => none.Subscribe(_sink.Zero, _owner));
            var handlerNull = Assert.Throws<ArgumentNullException>(() => evt.Subscribe((UnityAction)null, _owner));
            var ownerNull = Assert.Throws<ArgumentNullException>(() => evt.Subscribe(_sink.Zero, noOwner));

            Assert.That(eventNull.ParamName, Is.EqualTo("evt"));
            Assert.That(handlerNull.ParamName, Is.EqualTo("handler"));
            Assert.That(ownerNull.ParamName, Is.EqualTo("owner"));
            Assert.That(_owner.EntryCount, Is.Zero);
            UnityEventInspector.AssertCount(evt, 0);
        }

        // Guard: TryBegin calls EnsureMainThread before adding the listener.
        [Test]
        public void Subscribe_OffTheMainThread_ThrowsAndAddsNothing()
        {
            var evt = new UnityEvent();

            var failure = ThreadRunner.Run(() => evt.Subscribe(_sink.Zero, _owner));

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(_owner.EntryCount, Is.Zero);
            UnityEventInspector.AssertCount(evt, 0);
        }

        // Guard: the guard neither catches nor routes a handler failure; Unity's own Invoke semantics stay in charge.
        [Test]
        public void Invoke_HandlerThrows_IsNotRoutedToLifetimeErrorsAndTheSubscriptionSurvives()
        {
            var evt = new UnityEvent();
            var registration = evt.Subscribe(() => throw new InvalidOperationException("handler boom"), _owner);

            // Depending on the engine version, Invoke either logs the exception per listener or lets it escape.
            var escaped = false;
            try
            {
                evt.Invoke();
            }
            catch (InvalidOperationException)
            {
                escaped = true;
            }

            if (!escaped)
            {
                LogAssert.Expect(UnityEngine.LogType.Exception, new System.Text.RegularExpressions.Regex("handler boom"));
            }

            Assert.That(_f.Errors.Count, Is.Zero, "the guard must not catch or route the handler's exception");
            Assert.That(registration.IsActive, Is.True, "a failing handler does not end its subscription");
        }

        private sealed class IntEvent : UnityEvent<int>
        {
        }
    }
}
