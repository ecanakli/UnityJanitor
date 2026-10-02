using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using Zenject;

namespace Ecanakli.Janitor.DependencyInjection.Tests.PlayMode
{
    // The project level the way a game starts it: a ProjectContext whose installers include LifetimeInstaller.Install. Scenes below it
    // answer plain services from their own install, or fall back to the App lifetime and say so with JANITOR112.
    [TestFixture]
    public sealed class RealProjectContextTests
    {
        private ZenjectPlaySession _s;
        private RealProjectContext _project;

        [SetUp]
        public void SetUp()
        {
            _s = ZenjectPlaySession.Begin();
            _project = RealProjectContext.Begin(c => c.Bind<BetaService>().AsSingle());
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
                _project.Dispose();
            }
        }

        [Test]
        public void Start_WithTheInstallerInTheProjectInstallers_BindsLifetimeAndGivesProjectServicesAreasUnderApp()
        {
            var service = _project.Container.Resolve<BetaService>();

            Assert.That(_project.Container.HasBindingId(typeof(Lifetime), null, InjectSources.Local), Is.True);
            Assert.That(service.Injected.Parent, Is.SameAs(Lifetime.App));
            Assert.That(service.Injected.Kind, Is.EqualTo(LifetimeKind.Area));
            Assert.That(service.Injected.Name, Is.EqualTo(nameof(BetaService)));
            Assert.That(_s.Warnings.Count("JANITOR112"), Is.Zero, "the project container answers its own services");
        }

        [UnityTest]
        public IEnumerator NewSceneContext_WithItsOwnInstall_BelowTheStartedProjectContext_GetsSceneLifetimesAndNoWarning()
        {
            var scene = _s.NewScene("ProjectWithInstall");
            var context = _s.NewSceneContext(scene, c =>
            {
                LifetimeInstaller.Install(c);
                c.Bind<AlphaService>().AsSingle();
            });
            yield return _s.Frames(2);

            var service = context.Container.Resolve<AlphaService>();

            Assert.That(context.Container.ParentContainers[0], Is.SameAs(_project.Container), "the scene sits below the started ProjectContext");
            Assert.That(service.Injected.Parent, Is.SameAs(SceneLifetimes.Get(scene)));
            Assert.That(service.Injected.Kind, Is.EqualTo(LifetimeKind.Area));
            Assert.That(_s.Warnings.Count("JANITOR112"), Is.Zero);
        }

        [UnityTest]
        public IEnumerator NewSceneContext_WithoutInstall_BelowTheStartedProjectContext_FallsBackToAppAndWarnsOnce()
        {
            var scene = _s.NewScene("ProjectNoInstall");
            var context = _s.NewSceneContext(scene, c =>
            {
                c.Bind<AlphaService>().AsSingle();
                c.Bind<BetaService>().AsSingle();
            });
            yield return _s.Frames(2);

            var alpha = context.Container.Resolve<AlphaService>();
            context.Container.Resolve<BetaService>();

            var text = _s.Warnings.TextOf("JANITOR112");
            Assert.That(_s.Warnings.Count("JANITOR112"), Is.EqualTo(1), "once per scene, not once per service");
            Assert.That(text, Does.Contain(nameof(AlphaService)).And.Contain("'" + scene.name + "'"));
            Assert.That(alpha.Injected.Parent, Is.SameAs(Lifetime.App), "the warning is about exactly this: the project's lifetime inside a scene");
        }

        [UnityTest]
        public IEnumerator SceneLifetimesDispose_EndsTheSceneServicesAndLeavesTheProjectOnes()
        {
            var scene = _s.NewScene("ProjectDispose");
            var context = _s.NewSceneContext(scene, c =>
            {
                LifetimeInstaller.Install(c);
                c.Bind<AlphaService>().AsSingle();
            });
            yield return _s.Frames(2);
            var sceneService = context.Container.Resolve<AlphaService>();
            var projectService = _project.Container.Resolve<BetaService>();
            var sceneEnded = 0;
            var projectEnded = 0;
            sceneService.Injected.OnCancel(() => sceneEnded++);
            projectService.Injected.OnCancel(() => projectEnded++);

            SceneLifetimes.Dispose(scene);

            Assert.That(sceneService.Injected.IsDisposed, Is.True);
            Assert.That(sceneEnded, Is.EqualTo(1));
            Assert.That(projectService.Injected.IsDisposed, Is.False, "a scene's end does not reach the project level");
            Assert.That(projectEnded, Is.Zero);
        }
    }
}
