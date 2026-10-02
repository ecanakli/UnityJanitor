#if UNITY_EDITOR
using System;
using System.Collections;
using System.Text.RegularExpressions;
using Ecanakli.Janitor.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Zenject;

namespace Ecanakli.Janitor.DependencyInjection.Tests.PlayMode
{
    // Plain injectees under a real SceneContext: the growth check counts their areas under the scene lifetime,
    // and a GameObjectContext that never installed the binding is reported with the real contexts.
    [TestFixture]
    public sealed class InjectedGrowthPlayTests
    {
        private ZenjectPlaySession _s;
        private DiagnosticsRecordingKit _d;

        [SetUp]
        public void SetUp()
        {
            _s = ZenjectPlaySession.Begin();
            _d = new DiagnosticsRecordingKit();
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
                _d.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator Resolve_100PlainInjecteesUnderOneSceneContext_RecordsJanitor106OnceOnTheSceneLifetime()
        {
            var scene = _s.NewScene("Grow");
            var context = _s.NewSceneContext(scene, c =>
            {
                LifetimeInstaller.Install(c);
                c.Bind<AlphaService>().AsTransient();
            });
            yield return _s.Frames(2);
            var sceneLifetime = SceneLifetimes.Get(scene);

            for (var i = 0; i < 100; i++)
            {
                context.Container.Resolve<AlphaService>();
            }

            var warning = _d.Single(DiagnosticIds.Growth);
            Assert.That(warning.Lifetime, Is.SameAs(sceneLifetime));
            Assert.That(warning.Count, Is.EqualTo(1), "once per lifetime, not once per injectee");
            Assert.That(warning.Message, Does.Contain("65 live children that are areas"));
        }

        [UnityTest]
        public IEnumerator Resolve_ComponentLifetimesAndPlainInjecteesUnderOneScene_CountOnlyThePlainOnes()
        {
            var scene = _s.NewScene("Mixed");
            for (var i = 0; i < 100; i++)
            {
                _s.NewBehaviourIn<InjectedBehaviour>(scene);
            }

            var context = _s.NewSceneContext(scene, c =>
            {
                LifetimeInstaller.Install(c);
                c.Bind<AlphaService>().AsTransient();
            });
            yield return _s.Frames(2);
            var sceneLifetime = SceneLifetimes.Get(scene);
            Assert.That(sceneLifetime.ChildCount, Is.GreaterThanOrEqualTo(100), "premise: 100 component lifetimes are children of the scene lifetime");
            Assert.That(_d.Count(DiagnosticIds.Growth), Is.Zero, "component lifetimes are not counted");

            for (var i = 0; i < LifetimeDiagnostics.GrowthChildLimit; i++)
            {
                context.Container.Resolve<AlphaService>();
            }

            Assert.That(_d.Count(DiagnosticIds.Growth), Is.Zero, "64 plain areas are below the limit, however many components the scene holds");

            context.Container.Resolve<AlphaService>();

            Assert.That(_d.Single(DiagnosticIds.Growth).Lifetime, Is.SameAs(sceneLifetime));
        }

        // JANITOR112 for a real GameObjectContext

        [UnityTest]
        public IEnumerator Resolve_InAGameObjectContextWithoutInstall_RecordsJanitor112Once()
        {
            var scene = _s.NewScene("FacadeNoInstall");
            var sceneContext = _s.NewSceneContext(scene, c => LifetimeInstaller.Install(c));
            yield return _s.Frames(2);
            var facade = NewObjectContext(scene, sceneContext, "EnemyFacade", c =>
            {
                c.Bind<AlphaService>().AsSingle();
                c.Bind<BetaService>().AsSingle();
            });
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR112"));

            var alpha = facade.Container.Resolve<AlphaService>();
            facade.Container.Resolve<BetaService>();

            var warning = _d.Single(DiagnosticIds.MissingSceneInstall);
            Assert.That(warning.Count, Is.EqualTo(1), "once per context name, not once per service");
            Assert.That(warning.Context, Is.SameAs(facade), "the window can ping the GameObjectContext");
            Assert.That(warning.Lifetime, Is.SameAs(SceneLifetimes.Get(scene)), "the lifetime the plain services received");
            Assert.That(warning.Message, Does.Contain("GameObjectContext 'EnemyFacade'").And.Contain(nameof(AlphaService)));
            Assert.That(_s.Warnings.Count("JANITOR112"), Is.EqualTo(1), "the console warning is kept");
            Assert.That(alpha.Injected.Parent, Is.SameAs(SceneLifetimes.Get(scene)), "the warning is about exactly this: the service lives as long as the scene");
        }

        [UnityTest]
        public IEnumerator Resolve_InTwoGameObjectContextsWithTheSameNameWithoutInstall_RecordsOnce()
        {
            var scene = _s.NewScene("SameFacades");
            var sceneContext = _s.NewSceneContext(scene, c => LifetimeInstaller.Install(c));
            yield return _s.Frames(2);
            var first = NewObjectContext(scene, sceneContext, "Enemy(Clone)", c => c.Bind<AlphaService>().AsSingle());
            var second = NewObjectContext(scene, sceneContext, "Enemy(Clone)", c => c.Bind<AlphaService>().AsSingle());
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR112"));

            first.Container.Resolve<AlphaService>();
            second.Container.Resolve<AlphaService>();

            var warning = _d.Single(DiagnosticIds.MissingSceneInstall);
            Assert.That(warning.Count, Is.EqualTo(1), "a facade prefab spawned twice is one facade kind");
            Assert.That(warning.Context, Is.SameAs(first));
            Assert.That(_s.Warnings.Count("JANITOR112"), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Resolve_InTwoGameObjectContextsWithDifferentNamesWithoutInstall_RecordsOneEntryPerName()
        {
            var scene = _s.NewScene("TwoFacades");
            var sceneContext = _s.NewSceneContext(scene, c => LifetimeInstaller.Install(c));
            yield return _s.Frames(2);
            var first = NewObjectContext(scene, sceneContext, "EnemyA", c => c.Bind<AlphaService>().AsSingle());
            var second = NewObjectContext(scene, sceneContext, "EnemyB", c => c.Bind<AlphaService>().AsSingle());
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR112"));
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR112"));

            first.Container.Resolve<AlphaService>();
            second.Container.Resolve<AlphaService>();

            var rows = _d.Warnings(DiagnosticIds.MissingSceneInstall);
            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(rows[0].Context, Is.SameAs(first));
            Assert.That(rows[1].Context, Is.SameAs(second));
        }

        [UnityTest]
        public IEnumerator Resolve_InAGameObjectContextWithItsOwnInstall_RecordsNothing()
        {
            var scene = _s.NewScene("FacadeInstall");
            var sceneContext = _s.NewSceneContext(scene, c => LifetimeInstaller.Install(c));
            yield return _s.Frames(2);
            var facade = NewObjectContext(scene, sceneContext, "EnemyFacade", c =>
            {
                LifetimeInstaller.Install(c);
                c.Bind<AlphaService>().AsSingle();
            });

            var alpha = facade.Container.Resolve<AlphaService>();

            Assert.That(_d.Count(DiagnosticIds.MissingSceneInstall), Is.Zero);
            Assert.That(_s.Warnings.Count("JANITOR112"), Is.Zero);
            Assert.That(alpha.Injected.Parent, Is.SameAs(facade.GetLifetime()), "the plain service lives with the facade");
        }

        private GameObjectContext NewObjectContext(Scene scene, SceneContext parent, string name, Action<DiContainer> install)
        {
            var host = _s.NewObjectIn(scene, name);
            var context = host.AddComponent<GameObjectContext>();
            context.AddNormalInstaller(new ActionInstaller(install));
            context.Install(parent.Container);
            return context;
        }
    }
}
#endif
