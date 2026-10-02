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
    // JANITOR112 reaches the window through the public LifetimeDiagnostics.Report hook, next to its console warning. The scene
    // container is a plain DiContainer that carries a fake SceneContext component as its Context binding, exactly like
    // MissingSceneInstallTests; the real SceneContext is in the PlayMode diagnostics tests.
    [TestFixture]
    public sealed class MissingSceneInstallDiagnosticsTests : ZenjectUnitTestFixture
    {
        private DiagnosticsRecordingKit _d;
        private TestScope _t;
        private WarningSpy _warnings;
        private GameObject _host;
        private SceneContext _sceneContext;

        [SetUp]
        public void SetUpDiagnostics()
        {
            _d = new DiagnosticsRecordingKit();
            _t = new TestScope();
            _t.Tree.MakeDefault();
            _warnings = new WarningSpy();
            _host = new GameObject("fake SceneContext");
            _host.SetActive(false);
            _sceneContext = _host.AddComponent<SceneContext>();
        }

        [TearDown]
        public void TearDownDiagnostics()
        {
            _warnings.Dispose();
            Object.DestroyImmediate(_host);
            try
            {
                _t.Complete();
            }
            finally
            {
                _d.Dispose();
            }
        }

        [Test]
        public void Resolve_FromASceneContainerWithoutInstall_RecordsJanitor112OnceThroughThePublicHook()
        {
            var parent = NewInstalledParent();
            var scene = NewSceneContainer(parent);
            scene.Bind<AlphaService>().AsSingle();
            scene.Bind<BetaService>().AsSingle();
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR112"));

            scene.Resolve<AlphaService>();
            scene.Resolve<BetaService>();

            var warning = _d.Single(DiagnosticIds.MissingSceneInstall);
            Assert.That(warning.Count, Is.EqualTo(1), "once per scene, not once per service");
            Assert.That(warning.Lifetime, Is.SameAs(_t.App), "the lifetime the service received: an App-level one");
            Assert.That(warning.LifetimeLabel, Is.EqualTo("App"));
            Assert.That(warning.Context, Is.SameAs(_sceneContext), "the window can ping the scene context");
            Assert.That(warning.Message, Does.Contain(nameof(AlphaService)).And.Contain("LifetimeInstaller.Install(Container)"));
            Assert.That(warning.IsInfo, Is.False);
            Assert.That(_warnings.Count("JANITOR112"), Is.EqualTo(1), "the console warning is kept");
            Assert.That(_warnings.TextOf("JANITOR112"), Is.EqualTo(ZenjectDiagnostics.Format(DiagnosticIds.MissingSceneInstall, warning.Message)), "console and record agree");
        }

        [Test]
        public void Resolve_FromASceneContainerWithItsOwnInstall_RecordsNothing()
        {
            var parent = NewInstalledParent();
            var scene = NewSceneContainer(parent);
            var own = new ContextLifetimeResolver(scene, _t.App.CreateChild("scene"));
            scene.Bind<Lifetime>().FromMethod(own.Resolve).AsTransient();

            scene.Instantiate<AlphaService>();

            Assert.That(_d.Count(DiagnosticIds.MissingSceneInstall), Is.Zero);
        }

        [Test]
        public void Resolve_InTheContainerThatOwnsTheResolver_RecordsNothing()
        {
            var parent = NewInstalledParent();

            parent.Instantiate<AlphaService>();

            Assert.That(_d.Total, Is.Zero);
        }

        [Test]
        public void Resolve_FromASceneContainerWithoutInstallWhileTrackingIsOff_StillWarnsOnTheConsoleButRecordsNothing()
        {
            var parent = NewInstalledParent();
            var scene = NewSceneContainer(parent);
            LifetimeDiagnostics.TrackingEnabled = false;
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR112"));

            scene.Instantiate<AlphaService>();

            Assert.That(_d.Total, Is.Zero);
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
#endif
