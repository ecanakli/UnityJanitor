using System;
using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Zenject;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.DependencyInjection.Tests.PlayMode
{
    // LifetimeInstaller.Install detects its context through the local Context binding and captures the
    // context lifetime at install time: ProjectContext gives App, SceneContext its scene lifetime, GameObjectContext its component lifetime.
    [TestFixture]
    public sealed class LifetimeContextDetectionTests
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

        // Guard: the ProjectContext is found through the ancestors of a context-less sub-container and means App.
        [UnityTest]
        public IEnumerator Install_BelowTheProjectContext_ServicesGetChildrenOfApp()
        {
            var parent = _s.NewInstalledParent();

            var injected = parent.Instantiate<AlphaService>().Injected;

            Assert.That(injected.Parent, Is.SameAs(Lifetime.App));
            Assert.That(injected.Name, Is.EqualTo(nameof(AlphaService)));
            yield break;
        }

        // Guard: a SceneContext captures SceneLifetimes.Get(its scene), not App.
        [UnityTest]
        public IEnumerator Install_InASceneContext_ServicesGetChildrenOfTheSceneLifetime()
        {
            var scene = _s.NewScene("Detect");
            var context = _s.NewSceneContext(scene, c =>
            {
                LifetimeInstaller.Install(c);
                c.Bind<AlphaService>().AsSingle();
            });
            yield return _s.Frames(2);

            var injected = context.Container.Resolve<AlphaService>().Injected;

            var sceneLifetime = SceneLifetimes.Get(scene);
            Assert.That(sceneLifetime.Kind, Is.EqualTo(LifetimeKind.Scene));
            Assert.That(sceneLifetime, Is.Not.SameAs(Lifetime.App));
            Assert.That(injected.Parent, Is.SameAs(sceneLifetime));
            Assert.That(injected.Name, Is.EqualTo(nameof(AlphaService)));
        }

        // Guard: a GameObjectContext captures its own component lifetime, which is a child of the scene lifetime.
        [UnityTest]
        public IEnumerator Install_InAGameObjectContext_ServicesGetChildrenOfTheContextComponentLifetime()
        {
            var scene = _s.NewScene("GoContext");
            var sceneContext = _s.NewSceneContext(scene, c => LifetimeInstaller.Install(c));
            yield return _s.Frames(2);
            var host = _s.NewObjectIn(scene, "GameObjectContextHost");
            var goContext = host.AddComponent<GameObjectContext>();
            goContext.AddNormalInstaller(new ActionInstaller(c =>
            {
                LifetimeInstaller.Install(c);
                c.Bind<AlphaService>().AsSingle();
            }));

            goContext.Install(sceneContext.Container);
            var injected = goContext.Container.Resolve<AlphaService>().Injected;

            var contextLifetime = goContext.GetLifetime();
            Assert.That(contextLifetime.Kind, Is.EqualTo(LifetimeKind.Component));
            Assert.That(contextLifetime.Parent, Is.SameAs(SceneLifetimes.Get(scene)), "first access: the default parent is the scene lifetime");
            Assert.That(injected.Parent, Is.SameAs(contextLifetime));
            Assert.That(injected.Parent, Is.Not.SameAs(SceneLifetimes.Get(scene)), "not the scene context's lifetime");
        }

        // Guard: the disposer is bound for the SceneContext only, and a sub-container below it uses the scene lifetime without adding another.
        [UnityTest]
        public IEnumerator Install_InASubContainerOfASceneContext_UsesTheSceneLifetimeAndAddsNoDisposer()
        {
            var scene = _s.NewScene("Sub");
            var context = _s.NewSceneContext(scene, c => LifetimeInstaller.Install(c));
            yield return _s.Frames(2);
            var sub = context.Container.CreateSubContainer();

            LifetimeInstaller.Install(sub);
            var injected = sub.Instantiate<AlphaService>().Injected;

            Assert.That(injected.Parent, Is.SameAs(SceneLifetimes.Get(scene)), "the enclosing SceneContext decides");
            Assert.That(CountDisposers(context.Container), Is.EqualTo(1), "the scene container has the disposer");
            Assert.That(sub.HasBindingId(typeof(IDisposable), null, InjectSources.Local), Is.False, "the sub-container binds no disposable at all");
        }

        // Guard: each SceneContext captures its own scene, so two additive scenes never share a lifetime.
        [UnityTest]
        public IEnumerator Install_InTwoScenes_EachSceneContextGetsItsOwnSceneLifetime()
        {
            var first = _s.NewScene("First");
            var second = _s.NewScene("Second");
            var firstContext = _s.NewSceneContext(first, c =>
            {
                LifetimeInstaller.Install(c);
                c.Bind<AlphaService>().AsSingle();
            });
            var secondContext = _s.NewSceneContext(second, c =>
            {
                LifetimeInstaller.Install(c);
                c.Bind<AlphaService>().AsSingle();
            });
            yield return _s.Frames(2);

            var inFirst = firstContext.Container.Resolve<AlphaService>().Injected;
            var inSecond = secondContext.Container.Resolve<AlphaService>().Injected;

            Assert.That(inFirst.Parent, Is.SameAs(SceneLifetimes.Get(first)));
            Assert.That(inSecond.Parent, Is.SameAs(SceneLifetimes.Get(second)));
            Assert.That(SceneLifetimes.Get(first), Is.Not.SameAs(SceneLifetimes.Get(second)));
        }

        // Guard: a second Install in a SceneContext is a no-op: one Lifetime binding, one disposer, and the destroy path still works.
        [UnityTest]
        public IEnumerator Install_TwiceInASceneContext_IsHarmless()
        {
            var scene = _s.NewScene("Twice");
            var context = _s.NewSceneContext(scene, c =>
            {
                LifetimeInstaller.Install(c);
                LifetimeInstaller.Install(c);
            });
            yield return _s.Frames(2);
            var sceneLifetime = SceneLifetimes.Get(scene);

            Assert.That(context.Container.ResolveAll<Lifetime>().Count, Is.EqualTo(1), "exactly one Lifetime binding");
            Assert.That(CountDisposers(context.Container), Is.EqualTo(1), "exactly one disposer");

            LogAssert.Expect(LogType.Warning, new Regex("JANITOR104"));
            Object.Destroy(context.gameObject);
            yield return _s.WaitUntil(() => sceneLifetime.IsDisposed, "the disposer to end the scene lifetime");
        }

        private static int CountDisposers(DiContainer container)
        {
            var count = 0;
            foreach (var disposable in container.ResolveAll<IDisposable>())
            {
                if (disposable is SceneContextLifetimeDisposer)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
