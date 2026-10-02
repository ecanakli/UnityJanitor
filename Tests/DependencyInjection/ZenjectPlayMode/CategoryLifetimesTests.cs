using System.Collections;
using System.Text.RegularExpressions;
using Ecanakli.Janitor.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Zenject;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.DependencyInjection.Tests.PlayMode
{
    // A categories service bound per scene. It takes the injected Lifetime and creates one area per category;
    // the areas end with the scene, and cancelling one category stops only that category.
    [TestFixture]
    public sealed class CategoryLifetimesTests
    {
        private ZenjectPlaySession _s;
        private CallLog _log;

        [SetUp]
        public void SetUp()
        {
            _s = ZenjectPlaySession.Begin();
            _log = new CallLog();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return _s.CompleteAsync();
        }

        [UnityTest]
        public IEnumerator Resolve_TheCategoriesService_HasItsAreasUnderTheInjectedLifetimeOfTheScene()
        {
            var scene = _s.NewScene("Categories");
            var context = NewCategoriesContext(scene);
            yield return _s.Frames(2);

            var categories = context.Container.Resolve<CategoryLifetimes>();

            Assert.That(categories.Root.Parent, Is.SameAs(SceneLifetimes.Get(scene)));
            Assert.That(categories.Root.Name, Is.EqualTo(nameof(CategoryLifetimes)));
            Assert.That(categories.Popups.Parent, Is.SameAs(categories.Root));
            Assert.That(categories.Combat.Parent, Is.SameAs(categories.Root));
            Assert.That(categories.Popups.Name, Is.EqualTo("Popups"));
        }

        // Guard: Cancel on one category ends that category's generation only; the other category, the root and the scene are untouched.
        [UnityTest]
        public IEnumerator Cancel_OnCombat_StopsOnlyCombat()
        {
            var scene = _s.NewScene("CancelOne");
            var context = NewCategoriesContext(scene);
            yield return _s.Frames(2);
            var categories = context.Container.Resolve<CategoryLifetimes>();
            categories.Popups.Record(_log, "popups");
            categories.Combat.Record(_log, "combat");
            var popupsGeneration = categories.Popups.Generation;
            var rootGeneration = categories.Root.Generation;
            var sceneLifetime = SceneLifetimes.Get(scene);
            var sceneGeneration = sceneLifetime.Generation;

            categories.Combat.Cancel();

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "combat" }));
            Assert.That(categories.Popups.Generation, Is.EqualTo(popupsGeneration));
            Assert.That(categories.Root.Generation, Is.EqualTo(rootGeneration));
            Assert.That(sceneLifetime.Generation, Is.EqualTo(sceneGeneration));
            Assert.That(categories.Combat.IsDisposed, Is.False);
            Assert.That(categories.Combat.Record(_log, "again").IsActive, Is.True, "the category stays usable");
        }

        // Guard: the areas are descendants of the scene lifetime, so the one-line call ends them with the scene.
        [UnityTest]
        public IEnumerator SceneLifetimesDispose_EndsEveryCategoryWithTheScene()
        {
            var scene = _s.NewScene("DisposeScene");
            var context = NewCategoriesContext(scene);
            yield return _s.Frames(2);
            var categories = context.Container.Resolve<CategoryLifetimes>();
            categories.Popups.Record(_log, "popups");
            categories.Combat.Record(_log, "combat");

            SceneLifetimes.Dispose(scene);

            Assert.That(_log.ToArray(), Is.EquivalentTo(new[] { "popups", "combat" }));
            Assert.That(categories.Popups.IsDisposed, Is.True);
            Assert.That(categories.Combat.IsDisposed, Is.True);
            Assert.That(categories.Root.IsDisposed, Is.True);
        }

        // Guard: with the one-line call skipped, the disposer still ends the areas when the SceneContext is destroyed.
        [UnityTest]
        public IEnumerator Destroy_OfTheSceneContext_EndsEveryCategoryWithTheScene()
        {
            var scene = _s.NewScene("DestroyContext");
            var context = NewCategoriesContext(scene);
            yield return _s.Frames(2);
            var categories = context.Container.Resolve<CategoryLifetimes>();
            categories.Popups.Record(_log, "popups");
            categories.Combat.Record(_log, "combat");
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR104"));

            Object.Destroy(context.gameObject);
            yield return _s.WaitUntil(() => categories.Root.IsDisposed, "the disposer to end the scene lifetime");

            Assert.That(_log.ToArray(), Is.EquivalentTo(new[] { "popups", "combat" }));
            Assert.That(categories.Popups.IsDisposed, Is.True);
            Assert.That(categories.Combat.IsDisposed, Is.True);
        }

        // Guard: every scene gets its own categories, so a cancel in one scene never reaches the other.
        [UnityTest]
        public IEnumerator Cancel_OnCombatOfOneScene_DoesNotReachTheOtherScene()
        {
            var first = _s.NewScene("One");
            var second = _s.NewScene("Two");
            var firstContext = NewCategoriesContext(first);
            var secondContext = NewCategoriesContext(second);
            yield return _s.Frames(2);
            var inFirst = firstContext.Container.Resolve<CategoryLifetimes>();
            var inSecond = secondContext.Container.Resolve<CategoryLifetimes>();
            inFirst.Combat.Record(_log, "first");
            inSecond.Combat.Record(_log, "second");

            inFirst.Combat.Cancel();

            Assert.That(inFirst, Is.Not.SameAs(inSecond));
            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "first" }));
        }

        private SceneContext NewCategoriesContext(Scene scene)
        {
            return _s.NewSceneContext(scene, c =>
            {
                LifetimeInstaller.Install(c);
                c.Bind<CategoryLifetimes>().AsSingle();
            });
        }
    }
}
