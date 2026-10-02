using System;
using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Zenject;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.DependencyInjection.Tests.PlayMode
{
    // bus.Subscribe<TSignal>(handler, Component owner). A MonoBehaviour owner uses its component lifetime, any other
    // component the lifetime of its GameObject, and the subscription dies with that lifetime. The bus lives in a real SceneContext.
    [TestFixture]
    public sealed class SignalBusComponentOwnerTests
    {
        private ZenjectPlaySession _s;
        private int _hits;
        private Action<TestSignal> _handler;

        [SetUp]
        public void SetUp()
        {
            _s = ZenjectPlaySession.Begin();
            _hits = 0;
            _handler = _ => _hits++;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return _s.CompleteAsync();
        }

        // Guard: the subscription is tied to the component lifetime, so destroying the component unsubscribes without any user code.
        [UnityTest]
        public IEnumerator Subscribe_MonoBehaviourOwner_DeliversAndDiesWithTheComponent()
        {
            var scene = _s.NewScene("CompOwner");
            var bus = NewBus(scene, false);
            yield return _s.Frames(2);
            var owner = _s.NewBehaviourIn<OwnerBehaviour>(scene);
            var lifetime = owner.GetLifetime();
            var registration = bus.Subscribe<TestSignal>(_handler, owner);
            yield return _s.Frames(1);

            bus.Fire(new TestSignal());
            Assert.That(_hits, Is.EqualTo(1));
            Assert.That(registration.IsActive, Is.True);
            Assert.That(SignalSubscriptions.LiveCount, Is.EqualTo(1));

            Object.Destroy(owner);
            yield return _s.WaitUntil(() => lifetime.IsDisposed, "the component lifetime to be disposed");
            bus.Fire(new TestSignal());

            Assert.That(_hits, Is.EqualTo(1), "nothing is delivered after the component died");
            Assert.That(registration.IsActive, Is.False);
            Assert.That(bus.NumSubscribers, Is.Zero);
            Assert.That(SignalSubscriptions.LiveCount, Is.Zero, "the table entry is gone");
        }

        // Guard: a component that is not a MonoBehaviour resolves to the GameObject lifetime, which ends with the GameObject.
        [UnityTest]
        public IEnumerator Subscribe_TransformOwner_DiesWithTheGameObject()
        {
            var scene = _s.NewScene("TransformOwner");
            var bus = NewBus(scene, false);
            yield return _s.Frames(2);
            var host = _s.NewObjectIn(scene, "TransformHost");
            var lifetime = host.GetLifetime();
            var registration = bus.Subscribe<TestSignal>(_handler, host.transform);

            bus.Fire(new TestSignal());
            Assert.That(_hits, Is.EqualTo(1));
            Assert.That(registration.IsActive, Is.True);

            Object.Destroy(host);
            yield return _s.WaitUntil(() => lifetime.IsDisposed, "the GameObject lifetime to be disposed");
            bus.Fire(new TestSignal());

            Assert.That(_hits, Is.EqualTo(1));
            Assert.That(bus.NumSubscribers, Is.Zero);
            Assert.That(SignalSubscriptions.LiveCount, Is.Zero);
        }

        // Guard: a destroyed owner is not an argument error; it subscribes nothing.
        [UnityTest]
        public IEnumerator Subscribe_DestroyedMonoBehaviourOwner_SubscribesNothing()
        {
            var scene = _s.NewScene("DeadBehaviour");
            var bus = NewBus(scene, false);
            yield return _s.Frames(2);
            var owner = _s.NewBehaviourIn<OwnerBehaviour>(scene);
            Object.DestroyImmediate(owner);

            var registration = bus.Subscribe<TestSignal>(_handler, owner);
            bus.Fire(new TestSignal());

            Assert.That(registration.IsActive, Is.False);
            Assert.That(_hits, Is.Zero);
            Assert.That(bus.NumSubscribers, Is.Zero);
            Assert.That(SignalSubscriptions.LiveCount, Is.Zero);
            yield break;
        }

        // Guard: a destroyed component that is not a MonoBehaviour has no GameObject to ask, and still subscribes nothing.
        [UnityTest]
        public IEnumerator Subscribe_DestroyedTransformOwner_SubscribesNothing()
        {
            var scene = _s.NewScene("DeadTransform");
            var bus = NewBus(scene, false);
            yield return _s.Frames(2);
            var host = _s.NewObjectIn(scene, "DeadTransformHost");
            var transform = host.transform;
            Object.DestroyImmediate(host);

            var registration = bus.Subscribe<TestSignal>(_handler, transform);

            Assert.That(registration.IsActive, Is.False);
            Assert.That(bus.NumSubscribers, Is.Zero);
            Assert.That(SignalSubscriptions.LiveCount, Is.Zero);
            yield break;
        }

        // Guard: the OnEnable double subscribe. A component subscribing the same handler again gets the existing registration and a warning.
        [UnityTest]
        public IEnumerator Subscribe_SameHandlerTwiceForAComponent_ReturnsTheExistingRegistrationAndWarns()
        {
            var scene = _s.NewScene("CompTwice");
            var bus = NewBus(scene, false);
            yield return _s.Frames(2);
            var owner = _s.NewBehaviourIn<OwnerBehaviour>(scene);
            var first = bus.Subscribe<TestSignal>(_handler, owner);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));

            var second = bus.Subscribe<TestSignal>(_handler, owner);
            bus.Fire(new TestSignal());

            Assert.That(second.EntryId, Is.EqualTo(first.EntryId));
            Assert.That(_hits, Is.EqualTo(1), "one delivery, not two");
            Assert.That(bus.NumSubscribers, Is.EqualTo(1));
            Assert.That(_s.Warnings.Count("JANITOR105"), Is.EqualTo(1));
            yield break;
        }

        // Guard: Subscribe(handler, component) is an implicit first access; a later GetLifetime(category) warns and keeps the parent.
        [UnityTest]
        public IEnumerator Subscribe_ComponentOwner_IsTheFirstAccessThatFixesTheDefaultParent()
        {
            var scene = _s.NewScene("CompFirstAccess");
            var bus = NewBus(scene, false);
            yield return _s.Frames(2);
            var owner = _s.NewBehaviourIn<OwnerBehaviour>(scene);
            bus.Subscribe<TestSignal>(_handler, owner);
            var category = SceneLifetimes.Get(scene).CreateChild("Category");
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR113"));

            var lifetime = owner.GetLifetime(category);

            Assert.That(lifetime.Parent, Is.SameAs(SceneLifetimes.Get(scene)));
            Assert.That(lifetime.Placed, Is.False);
            Assert.That(_s.Warnings.Count("JANITOR113"), Is.EqualTo(1));
            yield break;
        }

        // Strict unsubscribe

        // Guard: with RequireStrictUnsubscribe on, SignalBus.LateDispose throws for a leftover subscription. Owner-bound subscriptions
        // are gone by then, because the disposer ended the scene lifetime first. An unhandled exception log fails the test on its own.
        [UnityTest]
        public IEnumerator Destroy_OfTheSceneContext_WithStrictUnsubscribe_PassesForOwnerBoundSubscriptions()
        {
            var scene = _s.NewScene("StrictPass");
            var context = NewBusContext(scene, true);
            var bus = context.Container.Resolve<SignalBus>();
            var owner = _s.NewBehaviourIn<OwnerBehaviour>(scene);
            var area = SceneLifetimes.Get(scene).CreateChild("area");
            bus.Subscribe<TestSignal>(_handler, owner);
            bus.Subscribe<TestSignal>(_ => { }, area);
            yield return _s.Frames(2);
            Assert.That(bus.NumSubscribers, Is.EqualTo(2));

            Object.Destroy(context.gameObject);
            yield return _s.WaitUntil(() => context == null, "the SceneContext to be destroyed");

            Assert.That(bus.NumSubscribers, Is.Zero, "both subscriptions ended before LateDispose");
            Assert.That(SignalSubscriptions.LiveCount, Is.Zero);
        }

        // Guard: the control. The same setup with a raw subscription fails in LateDispose, which proves the strict setting is live.
        [UnityTest]
        public IEnumerator Destroy_OfTheSceneContext_WithStrictUnsubscribe_ThrowsForARawSubscription()
        {
            var scene = _s.NewScene("StrictControl");
            var context = NewBusContext(scene, true);
            var bus = context.Container.Resolve<SignalBus>();
            bus.Subscribe<TestSignal>(_handler);
            yield return _s.Frames(2);
            // Unity logs the innermost exception (Zenject's wrapper only appears as "Rethrow as ZenjectException" in the stack).
            LogAssert.Expect(LogType.Exception, new Regex("Found subscriptions for signals"));

            Object.Destroy(context.gameObject);
            yield return _s.WaitUntil(() => context == null, "the SceneContext to be destroyed");
        }

        private SignalBus NewBus(Scene scene, bool strict)
        {
            var context = NewBusContext(scene, strict);
            return context.Container.Resolve<SignalBus>();
        }

        private SceneContext NewBusContext(Scene scene, bool strict)
        {
            return _s.NewSceneContext(scene, c =>
            {
                LifetimeInstaller.Install(c);
                if (strict)
                {
                    var signals = new ZenjectSettings.SignalSettings(SignalDefaultSyncModes.Synchronous, SignalMissingHandlerResponses.Ignore, requireStrictUnsubscribe: true);
                    c.Settings = new ZenjectSettings(ValidationErrorResponses.Log, signalSettings: signals);
                }

                SignalBusInstaller.Install(c);
                c.DeclareSignal<TestSignal>().OptionalSubscriber();
            });
        }
    }
}
