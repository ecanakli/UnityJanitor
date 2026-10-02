using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Zenject;

namespace Ecanakli.Janitor.DependencyInjection.Tests.PlayMode
{
    // The static subscription table across play sessions. With domain reload off the statics outlive a session, so a new session must
    // start empty: the old tree is shut down (which terminates every subscription), and the reset runs at every SubsystemRegistration.
    [TestFixture]
    public sealed class SignalTableSessionTests
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

        // Guard: a session start shuts the old tree down, every owner in it ends, and each termination forgets its record.
        [UnityTest]
        public IEnumerator Restart_ShutsTheOldTreeDown_AndTheTableEmpties()
        {
            var scene = _s.NewScene("SessionTable");
            var bus = NewBus(scene);
            yield return _s.Frames(2);
            var owner = _s.NewBehaviourIn<OwnerBehaviour>(scene);
            var area = SceneLifetimes.Get(scene).CreateChild("area");
            bus.Subscribe<TestSignal>(_handler, owner);
            bus.Subscribe<TestSignal>(_ => { }, area);
            Assert.That(SignalSubscriptions.LiveCount, Is.EqualTo(2));
            Assert.That(bus.NumSubscribers, Is.EqualTo(2));

            _s.Restart();

            Assert.That(SignalSubscriptions.LiveCount, Is.Zero, "every subscription of the old session was forgotten");
            Assert.That(bus.NumSubscribers, Is.Zero, "and unsubscribed from the bus");
        }

        // Guard: after a new session the same handler subscribes again under a new owner without a duplicate warning or an error.
        [UnityTest]
        public IEnumerator Restart_ThenSubscribeTheSameHandlerAgain_IsNotADuplicate()
        {
            var scene = _s.NewScene("SessionAgain");
            var bus = NewBus(scene);
            yield return _s.Frames(2);
            var first = _s.NewBehaviourIn<OwnerBehaviour>(scene);
            bus.Subscribe<TestSignal>(_handler, first);

            _s.Restart();
            var second = _s.NewBehaviourIn<OwnerBehaviour>(scene);
            var registration = bus.Subscribe<TestSignal>(_handler, second);
            bus.Fire(new TestSignal());

            Assert.That(registration.IsActive, Is.True);
            Assert.That(_hits, Is.EqualTo(1));
            Assert.That(_s.Warnings.Count("JANITOR105"), Is.Zero);
            Assert.That(SignalSubscriptions.LiveCount, Is.EqualTo(1));
        }

        // Guard: the reset callback itself empties a table that still holds a live record (what Unity does at the next session start
        // when the old tree was not shut down through the usual path), and the orphaned owner entry cannot push the count below zero.
        [UnityTest]
        public IEnumerator ResetSession_WithALiveRecord_EmptiesTheTable()
        {
            var scene = _s.NewScene("SessionReset");
            var bus = NewBus(scene);
            yield return _s.Frames(2);
            var owner = _s.NewBehaviourIn<OwnerBehaviour>(scene);
            var registration = bus.Subscribe<TestSignal>(_handler, owner);
            Assert.That(SignalSubscriptions.LiveCount, Is.EqualTo(1));

            SignalSubscriptions.ResetSession();
            Assert.That(SignalSubscriptions.LiveCount, Is.Zero);
            registration.Cancel();

            Assert.That(SignalSubscriptions.LiveCount, Is.Zero, "a forgotten record is not forgotten twice");
            Assert.That(bus.NumSubscribers, Is.Zero, "the terminate still unsubscribed from the bus");
        }

        private SignalBus NewBus(Scene scene)
        {
            var context = _s.NewSceneContext(scene, c =>
            {
                LifetimeInstaller.Install(c);
                SignalBusInstaller.Install(c);
                c.DeclareSignal<TestSignal>().OptionalSubscriber();
            });
            return context.Container.Resolve<SignalBus>();
        }
    }
}
