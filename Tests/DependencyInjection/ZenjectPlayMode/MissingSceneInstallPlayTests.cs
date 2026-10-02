using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Zenject;

namespace Ecanakli.Janitor.DependencyInjection.Tests.PlayMode
{
    // JANITOR112 with real SceneContexts: a plain service of a scene that never called LifetimeInstaller.Install receives a lifetime of the
    // installed context above it (here the project level, which is App). The installed parent is a sub-container of the ProjectContext.
    [TestFixture]
    public sealed class MissingSceneInstallPlayTests
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

        // Guard: the install-less scene logs JANITOR112 once, naming the service and the scene; the service gets an App-level lifetime.
        [UnityTest]
        public IEnumerator Resolve_InASceneWithoutInstall_WarnsOnceNamingTheServiceAndTheScene()
        {
            var parent = _s.NewInstalledParent();
            var scene = _s.NewScene("NoInstall");
            var context = _s.NewSceneContext(scene, c =>
            {
                c.Bind<AlphaService>().AsSingle();
                c.Bind<BetaService>().AsSingle();
            }, parent);
            yield return _s.Frames(2);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR112"));

            var alpha = context.Container.Resolve<AlphaService>();
            context.Container.Resolve<BetaService>();

            var text = _s.Warnings.TextOf("JANITOR112");
            Assert.That(_s.Warnings.Count("JANITOR112"), Is.EqualTo(1), "once per scene, not once per service");
            Assert.That(text, Does.Contain(nameof(AlphaService)));
            Assert.That(text, Does.Contain("'" + scene.name + "'"));
            Assert.That(text, Does.Contain("LifetimeInstaller.Install(Container)"));
            Assert.That(alpha.Injected.Parent, Is.SameAs(Lifetime.App), "the warning is about exactly this: an App-level lifetime in a scene");
        }

        // Guard: the control. The same scene with the install gets a scene-level child and no warning.
        [UnityTest]
        public IEnumerator Resolve_InASceneWithInstall_GetsASceneLevelLifetimeAndNoWarning()
        {
            var parent = _s.NewInstalledParent();
            var scene = _s.NewScene("WithInstall");
            var context = _s.NewSceneContext(scene, c =>
            {
                LifetimeInstaller.Install(c);
                c.Bind<AlphaService>().AsSingle();
            }, parent);
            yield return _s.Frames(2);

            var alpha = context.Container.Resolve<AlphaService>();

            Assert.That(alpha.Injected.Parent, Is.SameAs(SceneLifetimes.Get(scene)));
            Assert.That(_s.Warnings.Count("JANITOR112"), Is.Zero);
        }

        // Guard: each scene is reported on its own, so a second install-less scene logs its own warning.
        [UnityTest]
        public IEnumerator Resolve_InTwoScenesWithoutInstall_WarnsOncePerScene()
        {
            var parent = _s.NewInstalledParent();
            var first = _s.NewScene("NoInstallA");
            var second = _s.NewScene("NoInstallB");
            var firstContext = _s.NewSceneContext(first, c => c.Bind<AlphaService>().AsSingle(), parent);
            var secondContext = _s.NewSceneContext(second, c => c.Bind<AlphaService>().AsSingle(), parent);
            yield return _s.Frames(2);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR112"));
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR112"));

            firstContext.Container.Resolve<AlphaService>();
            secondContext.Container.Resolve<AlphaService>();

            Assert.That(_s.Warnings.Count("JANITOR112"), Is.EqualTo(2));
        }

        // Guard: only plain services are affected. A MonoBehaviour injectee gets its component lifetime whatever the install state,
        // so it is not reported; here the install-less scene gets its lifetime from the parent's binding.
        [UnityTest]
        public IEnumerator Inject_IntoAMonoBehaviourInASceneWithoutInstall_DoesNotWarn()
        {
            var parent = _s.NewInstalledParent();
            var scene = _s.NewScene("NoInstallMb");
            var behaviour = _s.NewBehaviourIn<InjectedBehaviour>(scene);
            _s.NewSceneContext(scene, c => { }, parent);
            yield return _s.Frames(2);

            Assert.That(behaviour.Injected, Is.SameAs(behaviour.GetLifetime()));
            Assert.That(behaviour.Injected.Parent, Is.SameAs(SceneLifetimes.Get(scene)), "the component lifetime belongs to the scene either way");
            Assert.That(_s.Warnings.Count("JANITOR112"), Is.Zero);
        }
    }
}
