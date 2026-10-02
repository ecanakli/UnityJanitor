using System;
using NUnit.Framework;
using UnityEngine.Events;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.Events
{
    // The Component owner resolves as AddTo(Component) does: a MonoBehaviour to its component lifetime, any other component to
    // its GameObject's lifetime, a destroyed owner to a disposed lifetime (nothing is added), and the active lifetime follows activation.
    [TestFixture]
    public sealed class UnityEventComponentOwnerTests
    {
        private EventFixture _f;
        private EventSink _sink;

        [SetUp]
        public void SetUp()
        {
            _f = new EventFixture();
            _sink = new EventSink(_f.Log, "s");
        }

        [TearDown]
        public void TearDown()
        {
            _f.Complete();
        }

        // Resolution

        // Guard: ResolveOwnerOrNull gives a MonoBehaviour its component lifetime, the same one GetLifetime returns.
        [Test]
        public void Subscribe_MonoBehaviourOwner_ResolvesToTheComponentLifetime()
        {
            var evt = new UnityEvent();
            var listener = _f.NewListener();

            var registration = evt.Subscribe(_sink.Zero, listener);

            Assert.That(registration.IsActive, Is.True);
            Assert.That(listener.GetLifetime().EntryCount, Is.EqualTo(1), "the entry lives in the component lifetime");
            Assert.That(listener.gameObject.GetLifetime().EntryCount, Is.Zero, "and not in the GameObject lifetime");
            evt.Invoke();
            Assert.That(_sink.Calls, Is.EqualTo(1));
        }

        // Guard: any other component resolves to the lifetime of its GameObject, exactly as AddTo(Component) does.
        [Test]
        public void Subscribe_NonMonoBehaviourComponentOwner_ResolvesToTheGameObjectLifetime()
        {
            var evt = new UnityEvent();
            var listener = _f.NewListener();

            evt.Subscribe(_sink.Zero, listener.transform);

            Assert.That(listener.gameObject.GetLifetime().EntryCount, Is.EqualTo(1), "the entry lives in the GameObject lifetime");
            Assert.That(listener.GetLifetime().EntryCount, Is.Zero, "and not in the component lifetime");
        }

        // Guard: the resolution is the one AddTo(Component) uses (same lifetime object), not a parallel one.
        [Test]
        public void Subscribe_ComponentOwner_SharesItsLifetimeWithAddToAndGetLifetime()
        {
            var evt = new UnityEvent();
            var listener = _f.NewListener();
            var disposable = new DisposeProbe();
            disposable.AddTo(listener);

            evt.Subscribe(_sink.Zero, listener);

            Assert.That(listener.GetLifetime().EntryCount, Is.EqualTo(2), "AddTo and Subscribe use the same component lifetime");
        }

        // Ending the owner

        // Guard: destroying the component ends its lifetime, which removes the guard.
        [Test]
        public void Subscribe_MonoBehaviourOwner_DestroyingTheComponentRemovesTheGuard()
        {
            var evt = new UnityEvent();
            var listener = _f.NewListener();
            evt.Subscribe(_sink.Zero, listener);
            UnityEventInspector.AssertCount(evt, 1);

            Object.DestroyImmediate(listener);
            evt.Invoke();

            Assert.That(_sink.Calls, Is.Zero);
            UnityEventInspector.AssertCount(evt, 0);
            Assert.That(_f.Errors.Count, Is.Zero);
        }

        // Guard: destroying the GameObject ends the lifetime of a non-MonoBehaviour owner's GameObject.
        [Test]
        public void Subscribe_NonMonoBehaviourComponentOwner_DestroyingTheGameObjectRemovesTheGuard()
        {
            var evt = new UnityEvent();
            var listener = _f.NewListener();
            evt.Subscribe(_sink.Zero, listener.transform);
            UnityEventInspector.AssertCount(evt, 1);

            Object.DestroyImmediate(listener.gameObject);
            evt.Invoke();

            Assert.That(_sink.Calls, Is.Zero);
            UnityEventInspector.AssertCount(evt, 0);
        }

        // Guard: destroying another component of the GameObject does not end the GameObject lifetime.
        [Test]
        public void Subscribe_NonMonoBehaviourComponentOwner_DestroyingASiblingComponentKeepsTheGuard()
        {
            var evt = new UnityEvent();
            var listener = _f.NewListener();
            var sibling = listener.gameObject.AddComponent<EventTestSubscriber>();
            evt.Subscribe(_sink.Zero, listener.transform);

            Object.DestroyImmediate(sibling);
            evt.Invoke();

            Assert.That(_sink.Calls, Is.EqualTo(1));
            UnityEventInspector.AssertCount(evt, 1);
        }

        // A destroyed or missing owner

        // Guard: ResolveOwner gives a destroyed owner the disposed sentinel, so TryBegin adds nothing and nothing is logged.
        [Test]
        public void Subscribe_DestroyedComponentOwner_AddsNothing()
        {
            var evt = new UnityEvent();
            var listener = _f.NewListener();
            Object.DestroyImmediate(listener);

            var registration = evt.Subscribe(_sink.Zero, listener);
            evt.Invoke();

            Assert.That(registration.IsActive, Is.False);
            Assert.That(_sink.Calls, Is.Zero);
            UnityEventInspector.AssertCount(evt, 0);
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: a C# null component reaches the Lifetime form as null and throws its own argument exception.
        [Test]
        public void Subscribe_NullComponentOwner_ThrowsAndAddsNothing()
        {
            var evt = new UnityEvent();
            EventTestListener none = null;

            var failure = Assert.Throws<ArgumentNullException>(() => evt.Subscribe(_sink.Zero, none));

            Assert.That(failure.ParamName, Is.EqualTo("owner"));
            UnityEventInspector.AssertCount(evt, 0);
        }

        // All five arities

        // Guard: each Component form forwards to its Lifetime form with the resolved lifetime.
        [Test]
        public void Subscribe_ComponentOwner_AllFiveAritiesDeliverAndRemoveWhenTheOwnerIsDestroyed()
        {
            var listener = _f.NewListener();
            var zero = new UnityEvent();
            var one = new UnityEvent<int>();
            var two = new UnityEvent<int, string>();
            var three = new UnityEvent<int, string, long>();
            var four = new UnityEvent<int, string, long, bool>();
            zero.Subscribe(_sink.Zero, listener);
            one.Subscribe(_sink.One, listener);
            two.Subscribe(_sink.Two, listener);
            three.Subscribe(_sink.Three, listener);
            four.Subscribe(_sink.Four, listener);

            zero.Invoke();
            one.Invoke(1);
            two.Invoke(1, "x");
            three.Invoke(1, "x", 3L);
            four.Invoke(1, "x", 3L, true);

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "s", "s:1", "s:1,x", "s:1,x,3", "s:1,x,3,True" }));
            Assert.That(listener.GetLifetime().EntryCount, Is.EqualTo(5));

            _f.Log.Clear();
            Object.DestroyImmediate(listener.gameObject);
            zero.Invoke();
            one.Invoke(2);
            two.Invoke(2, "y");
            three.Invoke(2, "y", 4L);
            four.Invoke(2, "y", 4L, false);

            Assert.That(_f.Log.Count, Is.Zero, "every guard was removed with the owner");
            UnityEventInspector.AssertCount(zero, 0);
            UnityEventInspector.AssertCount(one, 0);
            UnityEventInspector.AssertCount(two, 0);
            UnityEventInspector.AssertCount(three, 0);
            UnityEventInspector.AssertCount(four, 0);
        }

        // The active lifetime

        // Guard: the active lifetime's OnDisable cancel removes an OnEnable subscription, and the duplicate rule is not what stops the double.
        [Test]
        public void Subscribe_InOnEnableWithTheActiveLifetime_IsRemovedOnDisableAndReEnableDoesNotDoubleIt()
        {
            var evt = new UnityEvent();
            var subscriberObject = _f.NewObject("Subscriber", active: false);
            var subscriber = subscriberObject.AddComponent<EventTestSubscriber>();
            subscriber.Source = evt;

            subscriberObject.SetActive(true);
            evt.Invoke();
            Assert.That(subscriber.Calls, Is.EqualTo(1));
            UnityEventInspector.AssertCount(evt, 1);

            for (var i = 0; i < 3; i++)
            {
                subscriberObject.SetActive(false);
                UnityEventInspector.AssertCount(evt, 0);
                evt.Invoke();
                subscriberObject.SetActive(true);
                UnityEventInspector.AssertCount(evt, 1);
                evt.Invoke();
            }

            Assert.That(subscriber.Calls, Is.EqualTo(4), "one call per enabled period, never two");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
