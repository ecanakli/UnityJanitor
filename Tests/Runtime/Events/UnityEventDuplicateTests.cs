using NUnit.Framework;
using UnityEngine.Events;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.Events
{
    // The duplicate rule applied to UnityEvent: same event, same handler (target and method), same owner is one subscription.
    // The scan walks the owner's own entries, so these tests also mix in entries that are not UnityEvent guards.
    [TestFixture]
    public sealed class UnityEventDuplicateTests
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

        // Guard: TryFindDuplicate returns the existing registration and raises JANITOR105; nothing new is added to the event.
        [Test]
        public void Subscribe_SameHandlerSameOwner_ReturnsTheExistingRegistrationAndLogsJanitor105()
        {
            var evt = new UnityEvent();
            var first = evt.Subscribe(_sink.Zero, _owner);
            EventFixture.ExpectWarning("JANITOR105.*UnityEvent already has this handler");

            var second = evt.Subscribe(_sink.Zero, _owner);
            evt.Invoke();

            Assert.That(second.IsActive, Is.True);
            Assert.That(second.EntryId, Is.EqualTo(first.EntryId));
            Assert.That(second.EntryVersion, Is.EqualTo(first.EntryVersion));
            Assert.That(_owner.EntryCount, Is.EqualTo(1));
            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "s" }), "delivered once, not twice");
            UnityEventInspector.AssertCount(evt, 1);
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: the returned handle is the live one, so cancelling it removes the subscription.
        [Test]
        public void Subscribe_Duplicate_TheReturnedRegistrationCancelsTheExistingSubscription()
        {
            var evt = new UnityEvent();
            evt.Subscribe(_sink.Zero, _owner);
            EventFixture.ExpectWarning("JANITOR105");
            var second = evt.Subscribe(_sink.Zero, _owner);

            second.Cancel();
            evt.Invoke();

            Assert.That(_sink.Calls, Is.Zero);
            Assert.That(_owner.EntryCount, Is.Zero);
            UnityEventInspector.AssertCount(evt, 0);
        }

        // Guard: Same compares target then Delegate.Equals, so two delegate instances of one method and target are one handler.
        [Test]
        public void Subscribe_TwoDelegateInstancesOfTheSameMethodAndTarget_AreDuplicates()
        {
            var evt = new UnityEvent();
            UnityAction first = _sink.Zero;
            UnityAction second = _sink.Zero;
            Assert.That(ReferenceEquals(first, second), Is.False, "precondition: two distinct delegate objects");
            evt.Subscribe(first, _owner);
            EventFixture.ExpectWarning("JANITOR105");

            evt.Subscribe(second, _owner);
            evt.Invoke();

            Assert.That(_sink.Calls, Is.EqualTo(1));
            Assert.That(_owner.EntryCount, Is.EqualTo(1));
            UnityEventInspector.AssertCount(evt, 1);
        }

        // Guard: Same accepts the identical delegate object, which covers static lambdas (no target).
        [Test]
        public void Subscribe_TheSameStaticDelegateTwice_IsADuplicate()
        {
            var evt = new UnityEvent();
            UnityAction handler = StaticHandler;
            evt.Subscribe(handler, _owner);
            EventFixture.ExpectWarning("JANITOR105");

            evt.Subscribe(handler, _owner);

            Assert.That(_owner.EntryCount, Is.EqualTo(1));
            UnityEventInspector.AssertCount(evt, 1);
        }

        // Guard: IsSame includes the owner through the owner's own entry list; another owner is a separate subscription.
        [Test]
        public void Subscribe_SameHandlerDifferentOwner_IsASeparateSubscription()
        {
            var evt = new UnityEvent();
            var otherOwner = _f.Area.CreateChild("other");

            evt.Subscribe(_sink.Zero, _owner);
            evt.Subscribe(_sink.Zero, otherOwner);
            evt.Invoke();

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "s", "s" }));
            UnityEventInspector.AssertCount(evt, 2);
            otherOwner.Cancel();
            _f.Log.Clear();
            evt.Invoke();
            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "s" }), "each owner removes only its own subscription");
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: IsSame compares the event reference, so the same handler on another event is separate.
        [Test]
        public void Subscribe_SameHandlerSameOwnerOnAnotherEvent_IsASeparateSubscription()
        {
            var first = new UnityEvent();
            var second = new UnityEvent();

            first.Subscribe(_sink.Zero, _owner);
            second.Subscribe(_sink.Zero, _owner);
            first.Invoke();
            second.Invoke();

            Assert.That(_sink.Calls, Is.EqualTo(2));
            Assert.That(_owner.EntryCount, Is.EqualTo(2));
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: Same compares the method as well as the target.
        [Test]
        public void Subscribe_DifferentMethodsOfTheSameTarget_AreNotDuplicates()
        {
            var evt = new UnityEvent();

            evt.Subscribe(_sink.Zero, _owner);
            evt.Subscribe(_sink.OtherZero, _owner);
            evt.Invoke();

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "s", "s!" }));
            Assert.That(_owner.EntryCount, Is.EqualTo(2));
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: Same compares the target, so the same method on another instance is not a duplicate.
        [Test]
        public void Subscribe_TheSameMethodOnAnotherTarget_IsNotADuplicate()
        {
            var evt = new UnityEvent();
            var other = new EventSink(_f.Log, "o");

            evt.Subscribe(_sink.Zero, _owner);
            evt.Subscribe(other.Zero, _owner);
            evt.Invoke();

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "s", "o" }));
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: two distinct lambdas are two handlers even with equal bodies.
        [Test]
        public void Subscribe_TwoDistinctLambdas_AreNotDuplicates()
        {
            var evt = new UnityEvent();

            evt.Subscribe(() => _f.Log.Add("a"), _owner);
            evt.Subscribe(() => _f.Log.Add("a"), _owner);
            evt.Invoke();

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "a", "a" }));
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: the duplicate scan is limited to the current generation's entries.
        [Test]
        public void Subscribe_AfterTheOwnerCancelled_IsNotADuplicate()
        {
            var evt = new UnityEvent();
            evt.Subscribe(_sink.Zero, _owner);
            _owner.Cancel();

            var registration = evt.Subscribe(_sink.Zero, _owner);
            evt.Invoke();

            Assert.That(registration.IsActive, Is.True);
            Assert.That(_sink.Calls, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: a cancelled registration's entry is gone, so the scan cannot find it.
        [Test]
        public void Subscribe_AfterTheRegistrationWasCancelled_IsNotADuplicate()
        {
            var evt = new UnityEvent();
            evt.Subscribe(_sink.Zero, _owner).Cancel();

            var registration = evt.Subscribe(_sink.Zero, _owner);
            evt.Invoke();

            Assert.That(registration.IsActive, Is.True);
            Assert.That(_sink.Calls, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: TryFindDuplicate filters by the guard's Terminate before it casts, so other entry kinds are skipped, not cast.
        [Test]
        public void Subscribe_Duplicate_TheScanSkipsEntriesThatAreNotUnityEventGuards()
        {
            var evt = new UnityEvent();
            var otherEvent = new UnityEvent();
            var disposable = new DisposeProbe();
            _owner.OnCancel(() => { });
            evt.Subscribe(_sink.Zero, _owner);
            disposable.AddTo(_owner);
            otherEvent.Subscribe(_sink.OtherZero, _owner);
            _owner.OnCancel(() => { });
            EventFixture.ExpectWarning("JANITOR105");

            var duplicate = evt.Subscribe(_sink.Zero, _owner);

            Assert.That(duplicate.IsActive, Is.True);
            Assert.That(_owner.EntryCount, Is.EqualTo(5), "no entry was added");
            evt.Invoke();
            Assert.That(_sink.Calls, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: the JANITOR105 text carries the event label, built from the argument types.
        [Test]
        public void Subscribe_DuplicateOnAGenericEvent_LogsTheEventLabelWithItsArgumentTypes()
        {
            var evt = new UnityEvent<int, string>();
            evt.Subscribe(_sink.Two, _owner);
            EventFixture.ExpectWarning("JANITOR105.*UnityEvent<Int32, String> already has this handler");

            evt.Subscribe(_sink.Two, _owner);

            Assert.That(_owner.EntryCount, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        private static void StaticHandler()
        {
        }
    }
}
