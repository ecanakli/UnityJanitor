using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine.TestTools;
using Zenject;

namespace Ecanakli.Janitor.DependencyInjection.Tests.PlayMode
{
    // A MonoBehaviour injectee gets mb.GetLifetime(), and that counts as its first access, so it fixes the default parent.
    [TestFixture]
    public sealed class MonoBehaviourInjectionTests
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

        // Guard: ContextLifetimeResolver returns the component lifetime for a MonoBehaviour injectee (not a child of the context lifetime).
        [UnityTest]
        public IEnumerator Inject_IntoAMonoBehaviour_GivesItsComponentLifetime()
        {
            var scene = _s.NewScene("MbInject");
            var behaviour = _s.NewBehaviourIn<InjectedBehaviour>(scene);
            _s.NewSceneContext(scene, c => LifetimeInstaller.Install(c));
            yield return _s.Frames(2);

            var injected = behaviour.Injected;

            var sceneLifetime = SceneLifetimes.Get(scene);
            Assert.That(injected, Is.Not.Null, "Zenject injected the field");
            Assert.That(injected, Is.SameAs(behaviour.GetLifetime()), "the very component lifetime");
            Assert.That(injected.Kind, Is.EqualTo(LifetimeKind.Component));
            Assert.That(injected.Parent, Is.SameAs(sceneLifetime));
            Assert.That(injected.Name, Is.EqualTo(nameof(InjectedBehaviour)));
        }

        // Guard: a plain service in the same scene still gets a child, so the MonoBehaviour rule does not leak to other injectees.
        [UnityTest]
        public IEnumerator Inject_IntoAMonoBehaviourAndAPlainService_GivesEachItsOwnKind()
        {
            var scene = _s.NewScene("MbAndService");
            var behaviour = _s.NewBehaviourIn<InjectedBehaviour>(scene);
            var context = _s.NewSceneContext(scene, c =>
            {
                LifetimeInstaller.Install(c);
                c.Bind<AlphaService>().AsSingle();
            });
            yield return _s.Frames(2);

            var service = context.Container.Resolve<AlphaService>().Injected;

            Assert.That(service.Kind, Is.EqualTo(LifetimeKind.Area));
            Assert.That(behaviour.Injected.Kind, Is.EqualTo(LifetimeKind.Component));
            Assert.That(service, Is.Not.SameAs(behaviour.Injected));
        }

        // Guard: [Inject] Lifetime is the first access; a later GetLifetime(category) warns JANITOR113 and keeps the original parent.
        [UnityTest]
        public IEnumerator GetLifetime_WithACategory_AfterTheInjection_WarnsJanitor113AndKeepsTheParent()
        {
            var scene = _s.NewScene("MbFirstAccess");
            var behaviour = _s.NewBehaviourIn<InjectedBehaviour>(scene);
            _s.NewSceneContext(scene, c => LifetimeInstaller.Install(c));
            yield return _s.Frames(2);
            var sceneLifetime = SceneLifetimes.Get(scene);
            var category = sceneLifetime.CreateChild("Category");
            var injected = behaviour.Injected;
            Assert.That(injected.Parent, Is.SameAs(sceneLifetime));
            LogAssert.Expect(UnityEngine.LogType.Warning, new Regex("JANITOR113"));

            var again = behaviour.GetLifetime(category);

            Assert.That(again, Is.SameAs(injected), "the existing lifetime is returned");
            Assert.That(again.Parent, Is.SameAs(sceneLifetime), "the parent is never silently changed");
            Assert.That(again.Placed, Is.False);
            Assert.That(category.ChildCount, Is.Zero);
            Assert.That(_s.Warnings.Count("JANITOR113"), Is.EqualTo(1));
        }

        // Guard: the control for the test above; when the game calls GetLifetime(category) before the injection, the category wins and nothing warns.
        [UnityTest]
        public IEnumerator Inject_AfterGetLifetimeWithACategory_ReturnsThePlacedLifetimeWithoutAWarning()
        {
            var scene = _s.NewScene("MbCategoryFirst");
            var behaviour = _s.NewBehaviourIn<InjectedBehaviour>(scene);
            var category = SceneLifetimes.Get(scene).CreateChild("Category");
            var placed = behaviour.GetLifetime(category);

            _s.NewSceneContext(scene, c => LifetimeInstaller.Install(c));
            yield return _s.Frames(2);

            Assert.That(behaviour.Injected, Is.SameAs(placed));
            Assert.That(placed.Parent, Is.SameAs(category));
            Assert.That(placed.Placed, Is.True);
            Assert.That(_s.Warnings.Count("JANITOR113"), Is.Zero, "the injection is a parent-less access, so it never mismatches");
        }

        // Guard: the MonoBehaviour lifetime ends with the component, like any GetLifetime.
        [UnityTest]
        public IEnumerator Inject_IntoAMonoBehaviour_TheLifetimeEndsWhenTheComponentIsDestroyed()
        {
            var scene = _s.NewScene("MbDestroy");
            var behaviour = _s.NewBehaviourIn<InjectedBehaviour>(scene);
            _s.NewSceneContext(scene, c => LifetimeInstaller.Install(c));
            yield return _s.Frames(2);
            var injected = behaviour.Injected;
            Assert.That(injected.IsDisposed, Is.False);

            UnityEngine.Object.Destroy(behaviour);
            yield return _s.WaitUntil(() => injected.IsDisposed, "the component lifetime to be disposed");
        }
    }
}
