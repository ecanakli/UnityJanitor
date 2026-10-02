using System.Collections;
using NUnit.Framework;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Zenject;

namespace Ecanakli.Janitor.DependencyInjection.Tests.PlayMode
{
    // A MonoMemoryPool of components that inject their lifetime and register work on their active lifetime in OnEnable. The pool
    // deactivates an item on Despawn and activates it on Spawn, so the work must stop on every despawn and start once per spawn.
    [TestFixture]
    public sealed class MemoryPoolFlowTests
    {
        private ZenjectPlaySession _s;
        private CodePrefabs _prefabs;

        [SetUp]
        public void SetUp()
        {
            _s = ZenjectPlaySession.Begin();
            _prefabs = new CodePrefabs();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
            {
                yield return _s.CompleteAsync();
            }
            finally
            {
                _prefabs.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator Spawn_AnItem_HasItsComponentAndActiveLifetimesUnderTheSceneAndOneRegistration()
        {
            var rig = BuildRig("PoolSpawn");
            yield return _s.Frames(2);

            var coin = rig.Pool.Spawn();

            Assert.That(coin.gameObject.scene, Is.EqualTo(rig.Scene));
            Assert.That(coin.gameObject.activeInHierarchy, Is.True);
            Assert.That(coin.Injected, Is.SameAs(coin.GetLifetime()));
            Assert.That(coin.Injected.Kind, Is.EqualTo(LifetimeKind.Component));
            Assert.That(coin.Injected.Parent, Is.SameAs(rig.SceneLifetime));
            Assert.That(coin.Active.Kind, Is.EqualTo(LifetimeKind.Active));
            Assert.That(coin.Active.Parent, Is.SameAs(rig.SceneLifetime));
            Assert.That(coin.Active.EntryCount, Is.EqualTo(1), "one registration, however many activations the creation took");
            Assert.That(coin.Live, Is.EqualTo(1));
            Assert.That(_s.Warnings.Count("JANITOR112"), Is.Zero);
        }

        [UnityTest]
        public IEnumerator Despawn_CancelsTheItemsWork_AndTheNextSpawnRegistersItOnceMore()
        {
            var rig = BuildRig("PoolCycle");
            yield return _s.Frames(2);
            var coin = rig.Pool.Spawn();
            var active = coin.Active;
            var enabledBefore = coin.EnableCount;
            var cancelledBefore = coin.CancelCount;

            rig.Pool.Despawn(coin);

            Assert.That(coin.gameObject.activeSelf, Is.False);
            Assert.That(coin.Active.EntryCount, Is.Zero, "the despawn cancelled the active lifetime");
            Assert.That(coin.CancelCount, Is.EqualTo(cancelledBefore + 1));
            Assert.That(coin.Live, Is.Zero);
            Assert.That(coin.Injected.IsDisposed, Is.False, "the component lifetime does not follow activation");

            var again = rig.Pool.Spawn();

            Assert.That(again, Is.SameAs(coin), "the pool hands the same item out again");
            Assert.That(coin.EnableCount, Is.EqualTo(enabledBefore + 1));
            Assert.That(coin.CancelCount, Is.EqualTo(cancelledBefore + 1));
            Assert.That(coin.Active, Is.SameAs(active), "the item keeps its active lifetime");
            Assert.That(coin.Active.EntryCount, Is.EqualTo(1), "registered once, not twice");
            Assert.That(coin.Live, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SpawnAndDespawn_ManyTimes_KeepsOneRegistrationAndNoNewLifetimes()
        {
            var rig = BuildRig("PoolMany");
            yield return _s.Frames(2);
            var coin = rig.Pool.Spawn();
            var injected = coin.Injected;
            var childrenBefore = rig.SceneLifetime.ChildCount;
            var cancelledBefore = coin.CancelCount;

            for (var i = 0; i < 25; i++)
            {
                rig.Pool.Despawn(coin);
                Assert.That(coin.Active.EntryCount, Is.Zero, "cycle " + i);
                Assert.That(rig.Pool.Spawn(), Is.SameAs(coin));
                Assert.That(coin.Active.EntryCount, Is.EqualTo(1), "cycle " + i);
            }

            Assert.That(coin.CancelCount, Is.EqualTo(cancelledBefore + 25));
            Assert.That(coin.Live, Is.EqualTo(1));
            Assert.That(coin.Injected, Is.SameAs(injected));
            Assert.That(rig.SceneLifetime.ChildCount, Is.EqualTo(childrenBefore), "no lifetime piles up under the scene");
        }

        [UnityTest]
        public IEnumerator SceneLifetimesDispose_EndsTheItemsWorkAndBothItsLifetimes()
        {
            var rig = BuildRig("PoolDispose");
            yield return _s.Frames(2);
            var coin = rig.Pool.Spawn();
            var cancelledBefore = coin.CancelCount;

            SceneLifetimes.Dispose(rig.Scene);

            Assert.That(coin.Active.IsDisposed, Is.True);
            Assert.That(coin.Injected.IsDisposed, Is.True);
            Assert.That(coin.CancelCount, Is.EqualTo(cancelledBefore + 1), "the registered work was stopped by the scene's end");
            Assert.That(coin.Live, Is.Zero);
        }

        private PoolRig BuildRig(string sceneName)
        {
            var prefab = _prefabs.New<PooledCoin>("PooledCoin");
            var scene = _s.NewScene(sceneName);
            var context = _s.NewSceneContext(scene, c =>
            {
                LifetimeInstaller.Install(c);
                c.BindMemoryPool<PooledCoin, PooledCoin.Pool>().FromComponentInNewPrefab(prefab);
            });
            return new PoolRig(scene, context.Container.Resolve<PooledCoin.Pool>());
        }

        private sealed class PoolRig
        {
            internal PoolRig(Scene scene, PooledCoin.Pool pool)
            {
                Scene = scene;
                Pool = pool;
            }

            internal Scene Scene { get; }

            internal PooledCoin.Pool Pool { get; }

            internal Lifetime SceneLifetime => SceneLifetimes.Get(Scene);
        }
    }
}
