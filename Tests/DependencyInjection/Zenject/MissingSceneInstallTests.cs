using System.Text.RegularExpressions;
using Ecanakli.Janitor.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Zenject;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.DependencyInjection.Tests
{
    // JANITOR112: a plain service of a SceneContext that never called LifetimeInstaller.Install received a lifetime
    // of an outer context. The scene container here is a plain DiContainer that carries a fake SceneContext component as its
    // Context binding (exactly what SceneContext.InstallBindings adds); the real SceneContext is in the PlayMode tests.
    [TestFixture]
    public sealed class MissingSceneInstallTests : ZenjectUnitTestFixture
    {
        private TestScope _t;
        private WarningSpy _warnings;
        private GameObject _host;
        private SceneContext _sceneContext;

        [SetUp]
        public void SetUpMissingInstall()
        {
            _t = new TestScope();
            _t.Tree.MakeDefault();
            _warnings = new WarningSpy();
            _host = new GameObject("fake SceneContext");
            _host.SetActive(false);
            _sceneContext = _host.AddComponent<SceneContext>();
        }

        [TearDown]
        public void TearDownMissingInstall()
        {
            _warnings.Dispose();
            Object.DestroyImmediate(_host);
            _t.Complete();
        }

        // Guard: ContextLifetimeResolver.CheckContextInstall finds a SceneContext container below the resolver's own and warns once, naming the service.
        [Test]
        public void Resolve_FromASceneContainerWithoutInstall_WarnsOnceNamingTheService()
        {
            var parent = NewInstalledParent();
            var scene = NewSceneContainer(parent);
            scene.Bind<AlphaService>().AsSingle();
            scene.Bind<BetaService>().AsSingle();
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR112"));

            var alpha = scene.Resolve<AlphaService>();
            scene.Resolve<BetaService>();

            var text = _warnings.TextOf("JANITOR112");
            Assert.That(_warnings.Count("JANITOR112"), Is.EqualTo(1), "once per scene, not once per service");
            Assert.That(text, Does.Contain(nameof(AlphaService)), "the first offender is named");
            Assert.That(text, Does.Contain("LifetimeInstaller.Install(Container)"));
            Assert.That(text, Does.Contain("Troubleshooting#janitor112"));
            Assert.That(alpha.Injected.Parent, Is.SameAs(_t.App), "the service still works, with an App-level lifetime");
        }

        // Guard: the walk goes through ancestors, so a sub-container below the install-less scene is reported as well.
        [Test]
        public void Resolve_FromASubContainerBelowASceneContainerWithoutInstall_Warns()
        {
            var parent = NewInstalledParent();
            var scene = NewSceneContainer(parent);
            var sub = new DiContainer(scene);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR112"));

            sub.Instantiate<AlphaService>();

            Assert.That(_warnings.Count("JANITOR112"), Is.EqualTo(1));
        }

        // Guard: a service in the container that owns the resolver is fine, whatever that container is.
        [Test]
        public void Resolve_InTheContainerThatOwnsTheResolver_NeverWarns()
        {
            var parent = NewInstalledParent();

            parent.Instantiate<AlphaService>();

            Assert.That(_warnings.Count("JANITOR112"), Is.Zero);
        }

        // Guard: the walk stops at the resolver's own container, so a context-less sub-container below the installed one is fine.
        [Test]
        public void Resolve_FromASubContainerBelowTheInstalledContainer_NeverWarns()
        {
            var parent = NewInstalledParent();
            var sub = new DiContainer(parent);

            sub.Instantiate<AlphaService>();

            Assert.That(_warnings.Count("JANITOR112"), Is.Zero);
        }

        // Guard: the installed scene container answers with its own resolver, so nothing is reported.
        [Test]
        public void Resolve_FromASceneContainerWithItsOwnInstall_NeverWarns()
        {
            var parent = NewInstalledParent();
            var scene = NewSceneContainer(parent);
            var own = new ContextLifetimeResolver(scene, _t.App.CreateChild("scene"));
            scene.Bind<Lifetime>().FromMethod(own.Resolve).AsTransient();

            scene.Instantiate<AlphaService>();

            Assert.That(_warnings.Count("JANITOR112"), Is.Zero);
        }

        // A container that is not a scene at all is never a misuse.
        [Test]
        public void Resolve_FromAContextLessContainerBelowAnotherContextLessOne_NeverWarns()
        {
            var parent = NewInstalledParent();
            var middle = new DiContainer(parent);
            var leaf = new DiContainer(middle);

            leaf.Instantiate<AlphaService>();

            Assert.That(_warnings.Count("JANITOR112"), Is.Zero);
        }

        // The stand-in for the project level: a context-less container, so its context lifetime is App.
        private static DiContainer NewInstalledParent()
        {
            var parent = new DiContainer(StaticContext.Container);
            LifetimeInstaller.Install(parent);
            return parent;
        }

        private DiContainer NewSceneContainer(DiContainer parent)
        {
            var scene = new DiContainer(parent);
            scene.Bind(typeof(Context), typeof(SceneContext)).To<SceneContext>().FromInstance(_sceneContext);
            return scene;
        }
    }
}
