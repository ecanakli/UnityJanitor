using System.Collections;
using Cysharp.Threading.Tasks.Triggers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Zenject;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.DependencyInjection.Tests.PlayMode
{
    // Zenject injects a created object while it is inactive and activates it afterwards. The injected lifetime must still follow the component.
    [TestFixture]
    public sealed class InactiveInjectionTests
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
        public IEnumerator Inject_IntoAnInactiveObjectThatIsThenActivated_TheLifetimeEndsWhenTheComponentAloneIsDestroyed()
        {
            var scene = _s.NewScene("InactiveComponent");
            var context = _s.NewSceneContext(scene, c => LifetimeInstaller.Install(c));
            var go = NewInactiveObject(scene);
            var behaviour = context.Container.InstantiateComponent<InjectedBehaviour>(go);
            var injected = behaviour.Injected;
            var ended = 0;
            injected.OnCancel(() => ended++);
            Assert.That(injected.Kind, Is.EqualTo(LifetimeKind.Component));
            Assert.That(go.TryGetComponent<AsyncDestroyTrigger>(out _), Is.True, "premise: the injection happened while the object was inactive");
            go.SetActive(true);

            Object.Destroy(behaviour);
            yield return _s.Frames(2);

            Assert.That(injected.IsDisposed, Is.True, "the component is gone, so its lifetime is");
            Assert.That(ended, Is.EqualTo(1));

            Object.Destroy(go);
            yield return _s.Frames(3);

            Assert.That(ended, Is.EqualTo(1), "destroying the GameObject later ends nothing a second time");
        }

        [UnityTest]
        public IEnumerator Inject_IntoAnInactiveObjectThatIsThenActivated_TheLifetimeEndsOnceWhenTheGameObjectIsDestroyed()
        {
            var scene = _s.NewScene("InactiveObject");
            var context = _s.NewSceneContext(scene, c => LifetimeInstaller.Install(c));
            var go = NewInactiveObject(scene);
            var behaviour = context.Container.InstantiateComponent<InjectedBehaviour>(go);
            var injected = behaviour.Injected;
            var ended = 0;
            injected.OnCancel(() => ended++);
            go.SetActive(true);

            Object.Destroy(go);
            yield return _s.Frames(3);

            Assert.That(injected.IsDisposed, Is.True);
            Assert.That(ended, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Inject_IntoAnInactiveObjectThatIsNeverActivated_TheLifetimeEndsWithTheGameObject()
        {
            var scene = _s.NewScene("NeverActivated");
            var context = _s.NewSceneContext(scene, c => LifetimeInstaller.Install(c));
            var go = NewInactiveObject(scene);
            var behaviour = context.Container.InstantiateComponent<InjectedBehaviour>(go);
            var injected = behaviour.Injected;
            var ended = 0;
            injected.OnCancel(() => ended++);

            Object.Destroy(go);
            yield return _s.Frames(3);

            Assert.That(injected.IsDisposed, Is.True, "the GameObject's destroy trigger covers an object that never ran Awake");
            Assert.That(ended, Is.EqualTo(1));
        }

        private GameObject NewInactiveObject(UnityEngine.SceneManagement.Scene scene)
        {
            var go = _s.NewObjectIn(scene, "InactiveInjectee");
            go.SetActive(false);
            return go;
        }
    }
}
