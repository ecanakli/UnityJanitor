#if UNITY_EDITOR
using System.Text.RegularExpressions;
using Ecanakli.Janitor.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Zenject;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.DependencyInjection.Tests
{
    // JANITOR112 for a GameObjectContext: a plain service of a facade that never called LifetimeInstaller.Install receives the
    // lifetime of an outer context and outlives the facade. The facade container is a plain DiContainer that carries a fake
    // GameObjectContext component as its Context binding (what GameObjectContext.InstallBindings adds); the real one is in the PlayMode tests.
    [TestFixture]
    public sealed class MissingContextInstallTests : ZenjectUnitTestFixture
    {
        private DiagnosticsRecordingKit _d;
        private TestScope _t;
        private WarningSpy _warnings;
        private GameObject _firstHost;
        private GameObject _secondHost;
        private GameObject _sceneHost;
        private GameObjectContext _first;
        private GameObjectContext _second;
        private SceneContext _sceneContext;

        [SetUp]
        public void SetUpMissingContextInstall()
        {
            _d = new DiagnosticsRecordingKit();
            _t = new TestScope();
            _t.Tree.MakeDefault();
            _warnings = new WarningSpy();
            _firstHost = NewInactiveHost("Enemy A");
            _first = _firstHost.AddComponent<GameObjectContext>();
            _secondHost = NewInactiveHost("Enemy B");
            _second = _secondHost.AddComponent<GameObjectContext>();
            _sceneHost = NewInactiveHost("fake SceneContext");
            _sceneContext = _sceneHost.AddComponent<SceneContext>();
        }

        [TearDown]
        public void TearDownMissingContextInstall()
        {
            _warnings.Dispose();
            Object.DestroyImmediate(_firstHost);
            Object.DestroyImmediate(_secondHost);
            Object.DestroyImmediate(_sceneHost);
            try
            {
                _t.Complete();
            }
            finally
            {
                _d.Dispose();
            }
        }

        // Guard: a service of the facade warns once, naming the context, the service and what to call; a second service of the same context adds nothing.
        [Test]
        public void Resolve_FromAGameObjectContextWithoutInstall_WarnsOnceNamingTheContext()
        {
            var parent = NewInstalledParent();
            var facade = NewFacadeContainer(parent, _first);
            facade.Bind<AlphaService>().AsSingle();
            facade.Bind<BetaService>().AsSingle();
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR112"));

            var alpha = facade.Resolve<AlphaService>();
            facade.Resolve<BetaService>();

            var text = _warnings.TextOf("JANITOR112");
            Assert.That(_warnings.Count("JANITOR112"), Is.EqualTo(1), "once per context name, not once per service");
            Assert.That(text, Does.Contain(nameof(AlphaService)), "the first offender is named");
            Assert.That(text, Does.Contain("GameObjectContext 'Enemy A'"));
            Assert.That(text, Does.Contain("as long as"), "the plain services live with the outer lifetime");
            Assert.That(text, Does.Contain("LifetimeInstaller.Install(Container)"));
            Assert.That(text, Does.Contain("Troubleshooting#janitor112"));
            Assert.That(alpha.Injected.Parent, Is.SameAs(_t.App), "the service still works, with the outer lifetime");
        }

        [Test]
        public void Resolve_FromAGameObjectContextWithoutInstall_RecordsJanitor112AttachedToTheContext()
        {
            var parent = NewInstalledParent();
            var facade = NewFacadeContainer(parent, _first);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR112"));

            facade.Instantiate<AlphaService>();

            var warning = _d.Single(DiagnosticIds.MissingSceneInstall);
            Assert.That(warning.Count, Is.EqualTo(1));
            Assert.That(warning.Lifetime, Is.SameAs(_t.App), "the lifetime the service received");
            Assert.That(warning.Context, Is.SameAs(_first), "the window can ping the GameObjectContext");
            Assert.That(warning.Message, Does.Contain("GameObjectContext 'Enemy A'").And.Contain(nameof(AlphaService)));
            Assert.That(_warnings.TextOf("JANITOR112"), Is.EqualTo(ZenjectDiagnostics.Format(DiagnosticIds.MissingSceneInstall, warning.Message)), "console and record agree");
        }

        // Guard: a facade kind is reported on its own, so a context with another name logs its own warning.
        [Test]
        public void Resolve_FromTwoGameObjectContextsWithDifferentNamesWithoutInstall_WarnsOncePerName()
        {
            var parent = NewInstalledParent();
            var firstFacade = NewFacadeContainer(parent, _first);
            var secondFacade = NewFacadeContainer(parent, _second);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR112"));
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR112"));

            firstFacade.Instantiate<AlphaService>();
            secondFacade.Instantiate<AlphaService>();
            firstFacade.Instantiate<BetaService>();

            Assert.That(_warnings.Count("JANITOR112"), Is.EqualTo(2));
            var rows = _d.Warnings(DiagnosticIds.MissingSceneInstall);
            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(rows[0].Context, Is.SameAs(_first));
            Assert.That(rows[1].Context, Is.SameAs(_second));
        }

        // Guard: a facade prefab spawned many times shares one clone name, so it logs once, not once per instance.
        [Test]
        public void Resolve_FromTwoGameObjectContextsWithTheSameNameWithoutInstall_WarnsOnce()
        {
            _secondHost.name = _firstHost.name;
            var parent = NewInstalledParent();
            var firstFacade = NewFacadeContainer(parent, _first);
            var secondFacade = NewFacadeContainer(parent, _second);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR112"));

            firstFacade.Instantiate<AlphaService>();
            secondFacade.Instantiate<AlphaService>();

            Assert.That(_warnings.Count("JANITOR112"), Is.EqualTo(1), "the second instance of the same facade kind adds nothing");
            var warning = _d.Single(DiagnosticIds.MissingSceneInstall);
            Assert.That(warning.Count, Is.EqualTo(1));
            Assert.That(warning.Context, Is.SameAs(_first), "the first instance is the one named");
        }

        // Guard: the walk goes through ancestors, so a sub-container below the facade is reported as the facade.
        [Test]
        public void Resolve_FromASubContainerBelowAGameObjectContextWithoutInstall_Warns()
        {
            var parent = NewInstalledParent();
            var facade = NewFacadeContainer(parent, _first);
            var sub = new DiContainer(facade);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR112"));

            sub.Instantiate<AlphaService>();

            Assert.That(_d.Single(DiagnosticIds.MissingSceneInstall).Context, Is.SameAs(_first));
        }

        // Guard: the nearest context is the one reported, so an install-less facade below an install-less scene names the facade.
        [Test]
        public void Resolve_FromAFacadeBelowASceneContainerWithoutInstall_NamesTheFacadeNotTheScene()
        {
            var parent = NewInstalledParent();
            var scene = new DiContainer(parent);
            scene.Bind(typeof(Context), typeof(SceneContext)).To<SceneContext>().FromInstance(_sceneContext);
            var facade = NewFacadeContainer(scene, _first);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR112"));

            facade.Instantiate<AlphaService>();

            var warning = _d.Single(DiagnosticIds.MissingSceneInstall);
            Assert.That(warning.Context, Is.SameAs(_first));
            Assert.That(warning.Message, Does.Contain("GameObjectContext 'Enemy A'"));
        }

        // Guard: a facade that installed its own binding is answered by its own resolver and never reported.
        [Test]
        public void Resolve_FromAGameObjectContextWithItsOwnInstall_NeverWarns()
        {
            var parent = NewInstalledParent();
            var facade = NewFacadeContainer(parent, _first);
            var own = new ContextLifetimeResolver(facade, _t.App.CreateChild("facade"));
            facade.Bind<Lifetime>().FromMethod(own.Resolve).AsTransient();

            var alpha = facade.Instantiate<AlphaService>();

            Assert.That(_warnings.Count("JANITOR112"), Is.Zero);
            Assert.That(_d.Count(DiagnosticIds.MissingSceneInstall), Is.Zero);
            Assert.That(alpha.Injected.Parent.Name, Is.EqualTo("facade"));
        }

        // The stand-in for the installed outer level: a context-less container, so its context lifetime is App.
        private static DiContainer NewInstalledParent()
        {
            var parent = new DiContainer(StaticContext.Container);
            LifetimeInstaller.Install(parent);
            return parent;
        }

        private static DiContainer NewFacadeContainer(DiContainer parent, GameObjectContext context)
        {
            var facade = new DiContainer(parent);
            facade.Bind<Context>().FromInstance(context);
            facade.Bind<GameObjectContext>().FromInstance(context);
            return facade;
        }

        private static GameObject NewInactiveHost(string name)
        {
            var host = new GameObject(name);
            host.SetActive(false);
            return host;
        }
    }
}
#endif
