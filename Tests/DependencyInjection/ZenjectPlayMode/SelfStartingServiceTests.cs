using System.Collections;
using Ecanakli.Janitor.Tests;
using NUnit.Framework;
using UnityEngine.TestTools;
using Zenject;

namespace Ecanakli.Janitor.DependencyInjection.Tests.PlayMode
{
    // A plain service that asks for a Lifetime and starts its own work in Initialize.
    internal sealed class SelfStartingService : IInitializable
    {
        public SelfStartingService(Lifetime lifetime)
        {
            Injected = lifetime;
        }

        public Lifetime Injected { get; }

        public bool Initialized { get; private set; }

        public int Ticks { get; private set; }

        public void Initialize()
        {
            Initialized = true;
            Injected.Every(1f, Tick);
        }

        private void Tick()
        {
            Ticks++;
        }
    }

    // The self-starting service shape: no MonoBehaviour owns the work, the injected lifetime does.
    [TestFixture]
    public sealed class SelfStartingServiceTests
    {
        private ZenjectPlaySession _s;

        [SetUp]
        public void SetUp()
        {
            _s = ZenjectPlaySession.Begin();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return _s.CompleteAsync();
        }

        [UnityTest]
        public IEnumerator Initialize_AServiceThatStartsAnEveryOnItsInjectedLifetime_TicksUntilTheSceneEnds()
        {
            var clock = new ManualClock();
            _s.Tree.Delay = clock.Delay;
            var scene = _s.NewScene("ServiceScene");
            var context = _s.NewSceneContext(scene, c =>
            {
                LifetimeInstaller.Install(c);
                c.BindInterfacesAndSelfTo<SelfStartingService>().AsSingle();
            });
            yield return _s.Frames(2);
            var service = context.Container.Resolve<SelfStartingService>();

            Assert.That(service.Initialized, Is.True, "the kernel called Initialize");
            Assert.That(service.Injected.Kind, Is.EqualTo(LifetimeKind.Area));
            Assert.That(service.Injected.Name, Is.EqualTo(nameof(SelfStartingService)));
            Assert.That(service.Injected.Parent, Is.SameAs(SceneLifetimes.Get(scene)));
            Assert.That(clock.PendingCount, Is.EqualTo(1), "one timer waits");
            Assert.That(service.Ticks, Is.Zero, "the first tick comes after one interval");

            clock.Advance(1f);
            clock.Advance(1f);

            Assert.That(service.Ticks, Is.EqualTo(2));
            Assert.That(service.Injected.IsDisposed, Is.False);

            SceneLifetimes.Dispose(scene);
            clock.Advance(1f);

            Assert.That(service.Injected.IsDisposed, Is.True, "the lifetime ended with the scene");
            Assert.That(service.Ticks, Is.EqualTo(2), "and the timer stopped with it");
            Assert.That(clock.PendingCount, Is.Zero, "no timer is left waiting");
            Assert.That(_s.Warnings.Count("JANITOR112"), Is.Zero);
            Assert.That(_s.Errors.Count, Is.Zero);
        }
    }
}
