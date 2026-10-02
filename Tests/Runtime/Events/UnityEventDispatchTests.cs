using NUnit.Framework;
using UnityEngine.Events;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.Events
{
    // The core dispatch test and its variants: UnityEvent dispatches over a snapshot, so a listener removed during
    // Invoke would still run. The guard must skip it, and nothing may be logged for that.
    [TestFixture]
    public sealed class UnityEventDispatchTests
    {
        private EventFixture _f;
        private Lifetime _ownerA;
        private Lifetime _ownerB;

        [SetUp]
        public void SetUp()
        {
            _f = new EventFixture();
            _ownerA = _f.Area.CreateChild("A");
            _ownerB = _f.Area.CreateChild("B");
        }

        [TearDown]
        public void TearDown()
        {
            _f.Complete();
        }

        // The core test

        // Guard: CanDeliver reads _live, which End clears when the drain runs B's Terminate; B stays in the snapshot but is not called.
        [Test]
        public void Invoke_ListenerACancelsTheOwnerOfListenerB_BIsNotCalledInThatInvokeAndNothingIsLogged()
        {
            var evt = new UnityEvent();
            evt.Subscribe(() =>
            {
                _f.Log.Add("A");
                _ownerB.Cancel();
            }, _ownerA);
            evt.Subscribe(() => _f.Log.Add("B"), _ownerB);

            evt.Invoke();

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "A" }), "B was removed during the dispatch and must not run in it");
            UnityEventInspector.AssertCount(evt, 1);
            evt.Invoke();
            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "A", "A" }), "and stays removed");
            Assert.That(_f.Errors.Count, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: a Dispose during the dispatch ends the guard the same way.
        [Test]
        public void Invoke_ListenerADisposesTheOwnerOfListenerB_BIsNotCalledInThatInvoke()
        {
            var evt = new UnityEvent();
            evt.Subscribe(() =>
            {
                _f.Log.Add("A");
                _ownerB.Dispose();
            }, _ownerA);
            evt.Subscribe(() => _f.Log.Add("B"), _ownerB);

            evt.Invoke();

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "A" }));
            Assert.That(_ownerB.IsDisposed, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: cancelling one registration during the dispatch ends that one guard immediately.
        [Test]
        public void Invoke_ListenerACancelsTheRegistrationOfListenerB_BIsNotCalledInThatInvoke()
        {
            var evt = new UnityEvent();
            var registrationB = default(LifetimeRegistration);
            evt.Subscribe(() =>
            {
                _f.Log.Add("A");
                registrationB.Cancel();
            }, _ownerA);
            registrationB = evt.Subscribe(() => _f.Log.Add("B"), _ownerB);

            evt.Invoke();

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "A" }));
            Assert.That(registrationB.IsActive, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: the owner of a later listener of the same owner ends too, so every listener of that owner behind the canceller is skipped.
        [Test]
        public void Invoke_ListenerCancelsItsOwnOwner_TheLaterListenersOfThatOwnerAreSkipped()
        {
            var evt = new UnityEvent();
            evt.Subscribe(() =>
            {
                _f.Log.Add("first");
                _ownerA.Cancel();
            }, _ownerA);
            evt.Subscribe(() => _f.Log.Add("second"), _ownerA);

            evt.Invoke();
            evt.Invoke();

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "first" }));
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: a handler that cancels its own registration runs once and never again.
        [Test]
        public void Invoke_HandlerCancelsItsOwnRegistration_RunsOnceAndNeverAgain()
        {
            var evt = new UnityEvent();
            var registration = default(LifetimeRegistration);
            var raw = new EventSink(_f.Log, "raw");
            evt.AddListener(raw.Zero);
            registration = evt.Subscribe(() =>
            {
                _f.Log.Add("once");
                registration.Cancel();
            }, _ownerA);

            evt.Invoke();
            evt.Invoke();

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "raw", "once", "raw" }));
            UnityEventInspector.AssertCount(evt, 1);
        }

        // Guard: CanDeliver checks the owner state, so a handler is skipped while its owner is Cancelling, before its guard was drained.
        [Test]
        public void Invoke_InsideATokenCallbackOfTheEndingGeneration_TheHandlerIsSkipped()
        {
            var evt = new UnityEvent();
            var sink = new EventSink(_f.Log, "s");
            evt.Subscribe(sink.Zero, _ownerA);
            _ownerA.Token.Register(() => evt.Invoke());

            _ownerA.Cancel();

            Assert.That(sink.Calls, Is.Zero, "the owner is Cancelling when the token callback raises the event");
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: a removed listener stays removed after a resubscription of the same handler (fresh guard, fresh generation).
        [Test]
        public void Invoke_HandlerResubscribedAfterCancel_IsDeliveredByTheNewGuardOnly()
        {
            var evt = new UnityEvent();
            var sink = new EventSink(_f.Log, "s");
            evt.Subscribe(sink.Zero, _ownerA);
            _ownerA.Cancel();
            evt.Subscribe(sink.Zero, _ownerA);

            evt.Invoke();

            Assert.That(sink.Calls, Is.EqualTo(1), "one delivery: the old guard is gone, the new one delivers");
            UnityEventInspector.AssertCount(evt, 1);
        }

        // The object variants

        // Guard: A destroys the object that owns B (component owner, plain handler target): the token cancels B's lifetime inside the dispatch.
        [Test]
        public void Invoke_ListenerADestroysTheObjectThatOwnsListenerB_BIsNotCalledInThatInvoke()
        {
            var evt = new UnityEvent();
            var objectB = _f.NewListener("B");
            var sinkB = new EventSink(_f.Log, "B");
            evt.Subscribe(() =>
            {
                _f.Log.Add("A");
                Object.DestroyImmediate(objectB.gameObject);
            }, _ownerA);
            evt.Subscribe(sinkB.Zero, objectB);

            evt.Invoke();

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "A" }), "B's owner died during the dispatch");
            Assert.That(sinkB.Calls, Is.Zero);
            UnityEventInspector.AssertCount(evt, 1);
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: A deactivates the object that owns B (active lifetime owner): the trigger's OnDisable cancels B's generation inside the dispatch.
        [Test]
        public void Invoke_ListenerADeactivatesTheObjectThatOwnsListenerB_BIsNotCalledInThatInvoke()
        {
            var evt = new UnityEvent();
            var objectB = _f.NewListener("B");
            var activeB = objectB.GetActiveLifetime();
            var sinkB = new EventSink(_f.Log, "B");
            evt.Subscribe(() =>
            {
                _f.Log.Add("A");
                objectB.gameObject.SetActive(false);
            }, _ownerA);
            evt.Subscribe(sinkB.Zero, activeB);

            evt.Invoke();

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "A" }));
            Assert.That(sinkB.Calls, Is.Zero);
            UnityEventInspector.AssertCount(evt, 1);
            objectB.gameObject.SetActive(true);
            evt.Invoke();
            Assert.That(sinkB.Calls, Is.Zero, "reactivation does not bring the old subscription back");
            LogAssert.NoUnexpectedReceived();
        }

        // Destroyed handler target

        // Guard: _unityTarget is checked in CanDeliver, because wrapping the handler hid its destroyed target from Unity's own alive check.
        [Test]
        public void Invoke_HandlerWhoseTargetWasDestroyed_IsSkippedWithoutAnError()
        {
            var evt = new UnityEvent();
            var listener = _f.NewListener("Target");
            evt.Subscribe(listener.Zero, _ownerA);
            evt.Invoke();
            Assert.That(listener.Calls, Is.EqualTo(1), "precondition: delivered while the target lives");

            Object.DestroyImmediate(listener.gameObject);

            Assert.DoesNotThrow(() => evt.Invoke());
            Assert.That(listener.Calls, Is.EqualTo(1), "the destroyed target must not be called");
            Assert.That(_ownerA.EntryCount, Is.EqualTo(1), "the owner still holds the subscription; only the delivery is skipped");
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: the same check for the generic guard classes.
        [Test]
        public void Invoke_OneArgumentHandlerWhoseTargetWasDestroyed_IsSkippedWithoutAnError()
        {
            var evt = new UnityEvent<int>();
            var listener = _f.NewListener("Target");
            evt.Subscribe(listener.One, _ownerA);

            Object.DestroyImmediate(listener.gameObject);

            Assert.DoesNotThrow(() => evt.Invoke(1));
            Assert.That(listener.Calls, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
