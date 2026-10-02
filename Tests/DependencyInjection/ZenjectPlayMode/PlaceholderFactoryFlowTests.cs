using System.Collections;
using Cysharp.Threading.Tasks.Triggers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Zenject;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.DependencyInjection.Tests.PlayMode
{
    // Objects a game creates at run time through the container: a PlaceholderFactory of plain objects, a factory and
    // Container.InstantiatePrefab of a MonoBehaviour from a prefab built in code. Each scene installs the lifetime binding itself.
    [TestFixture]
    public sealed class PlaceholderFactoryFlowTests
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
        public IEnumerator Create_APlainObjectThroughAFactory_GetsAnAreaUnderTheSceneLifetimeAndEndsWithTheScene()
        {
            var scene = _s.NewScene("PlainFactory");
            var context = _s.NewSceneContext(scene, c =>
            {
                LifetimeInstaller.Install(c);
                c.BindFactory<AlphaService, AlphaFactory>();
            });
            yield return _s.Frames(2);
            var sceneLifetime = SceneLifetimes.Get(scene);
            var factory = context.Container.Resolve<AlphaFactory>();
            var childrenBefore = sceneLifetime.ChildCount;

            var first = factory.Create();
            var second = factory.Create();

            Assert.That(first.Injected, Is.Not.SameAs(second.Injected), "every object asks for its own lifetime");
            Assert.That(first.Injected.Parent, Is.SameAs(sceneLifetime));
            Assert.That(second.Injected.Parent, Is.SameAs(sceneLifetime));
            Assert.That(first.Injected.Kind, Is.EqualTo(LifetimeKind.Area));
            Assert.That(first.Injected.Name, Is.EqualTo(nameof(AlphaService)));
            Assert.That(sceneLifetime.ChildCount, Is.EqualTo(childrenBefore + 2));
            Assert.That(_s.Warnings.Count("JANITOR112"), Is.Zero);
            var ended = 0;
            first.Injected.OnCancel(() => ended++);
            second.Injected.OnCancel(() => ended++);

            SceneLifetimes.Dispose(scene);

            Assert.That(first.Injected.IsDisposed, Is.True);
            Assert.That(second.Injected.IsDisposed, Is.True);
            Assert.That(ended, Is.EqualTo(2), "the scene's end stops the work of every object the factory made");
        }

        [UnityTest]
        public IEnumerator Create_AComponentThroughAFactoryFromAPrefabBuiltInCode_InjectsItsComponentLifetimeUnderTheScene()
        {
            var prefab = _prefabs.New<InjectedBehaviour>("FactoryCoin");
            var scene = _s.NewScene("ComponentFactory");
            var context = _s.NewSceneContext(scene, c =>
            {
                LifetimeInstaller.Install(c);
                c.BindFactory<InjectedBehaviour, InjectedBehaviourFactory>().FromComponentInNewPrefab(prefab);
            });
            yield return _s.Frames(2);
            var factory = context.Container.Resolve<InjectedBehaviourFactory>();

            var behaviour = factory.Create();

            var injected = behaviour.Injected;
            Assert.That(behaviour.gameObject.scene, Is.EqualTo(scene), "Zenject puts the clone in the scene of its context");
            Assert.That(behaviour.gameObject.activeInHierarchy, Is.True, "Zenject activates the clone after injecting it");
            Assert.That(injected, Is.SameAs(behaviour.GetLifetime()), "the injected lifetime is the component lifetime");
            Assert.That(injected.Kind, Is.EqualTo(LifetimeKind.Component));
            Assert.That(injected.Parent, Is.SameAs(SceneLifetimes.Get(scene)));
            Assert.That(_s.Warnings.Count("JANITOR112"), Is.Zero);

            SceneLifetimes.Dispose(scene);

            Assert.That(injected.IsDisposed, Is.True, "the scene's end reaches the object the factory made");
        }

        [UnityTest]
        public IEnumerator Create_AComponentThroughAFactory_EndsItsLifetimeWhenTheComponentAloneIsDestroyed()
        {
            var prefab = _prefabs.New<InjectedBehaviour>("FactoryCoin");
            var scene = _s.NewScene("ComponentFactoryDestroy");
            var context = _s.NewSceneContext(scene, c =>
            {
                LifetimeInstaller.Install(c);
                c.BindFactory<InjectedBehaviour, InjectedBehaviourFactory>().FromComponentInNewPrefab(prefab);
            });
            yield return _s.Frames(2);
            var behaviour = context.Container.Resolve<InjectedBehaviourFactory>().Create();
            var injected = behaviour.Injected;
            var ended = 0;
            injected.OnCancel(() => ended++);

            Object.Destroy(behaviour);
            yield return _s.Frames(2);

            Assert.That(injected.IsDisposed, Is.True, "the component is gone, so its lifetime is, although it was injected while inactive");
            Assert.That(ended, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator InstantiatePrefab_OfAPrefabBuiltInCode_InjectsOnTheInactivePathAndEndsWithTheGameObject()
        {
            var prefab = _prefabs.New<InjectedBehaviour>("InstantiatedCoin");
            var scene = _s.NewScene("InstantiatePrefab");
            var context = _s.NewSceneContext(scene, c => LifetimeInstaller.Install(c));
            yield return _s.Frames(2);

            var clone = context.Container.InstantiatePrefab(prefab);
            var other = context.Container.InstantiatePrefab(prefab);

            var behaviour = clone.GetComponent<InjectedBehaviour>();
            var injected = behaviour.Injected;
            Assert.That(clone.TryGetComponent<AsyncDestroyTrigger>(out _), Is.True, "premise: the clone was injected while it was inactive");
            Assert.That(clone.scene, Is.EqualTo(scene));
            Assert.That(clone.activeInHierarchy, Is.True);
            Assert.That(injected, Is.SameAs(behaviour.GetLifetime()));
            Assert.That(injected.Kind, Is.EqualTo(LifetimeKind.Component));
            Assert.That(injected.Parent, Is.SameAs(SceneLifetimes.Get(scene)));
            Assert.That(other.GetComponent<InjectedBehaviour>().Injected, Is.Not.SameAs(injected), "each clone has its own lifetime");
            Assert.That(_s.Warnings.Count("JANITOR112"), Is.Zero);
            var ended = 0;
            injected.OnCancel(() => ended++);

            Object.Destroy(clone);
            yield return _s.Frames(3);

            Assert.That(injected.IsDisposed, Is.True);
            Assert.That(ended, Is.EqualTo(1));
            Assert.That(other.GetComponent<InjectedBehaviour>().Injected.IsDisposed, Is.False, "the other clone is untouched");
        }
    }
}
