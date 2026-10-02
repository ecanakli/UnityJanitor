using System.Collections;
using NUnit.Framework;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Zenject;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.DependencyInjection.Tests.PlayMode
{
    // A facade created at run time: a factory binding with ByNewGameObjectMethod makes a GameObject with a GameObjectContext and its own
    // container, and the method is its installer. Without LifetimeInstaller.Install in that method the facade's plain services get the
    // scene lifetime and outlive the facade; with it they get the lifetime of the facade's GameObjectContext.
    [TestFixture]
    public sealed class FacadeFlowTests
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
        public IEnumerator Create_AFacadeWithoutItsOwnInstall_GivesItsServicesTheSceneLifetimeAndWarnsOncePerName()
        {
            var scene = _s.NewScene("FacadeNoInstall");
            var factory = NewFacadeFactory(scene, InstallPlainFacade);
            yield return _s.Frames(2);

            var first = factory.Create();
            var second = factory.Create();

            var sceneLifetime = SceneLifetimes.Get(scene);
            Assert.That(first.Context.gameObject.scene, Is.EqualTo(scene));
            Assert.That(first.Service.Injected.Parent, Is.SameAs(sceneLifetime), "no install of its own: the scene's binding answers");
            Assert.That(second.Service.Injected.Parent, Is.SameAs(sceneLifetime));
            Assert.That(_s.Warnings.Count("JANITOR112"), Is.EqualTo(1), "two facades of one name are one report");
            Assert.That(_s.Warnings.TextOf("JANITOR112"), Does.Contain("GameObjectContext").And.Contain(nameof(AlphaService)));
        }

        [UnityTest]
        public IEnumerator Destroy_OfAFacadeWithoutItsOwnInstall_LeavesItsServicesAliveUntilTheSceneEnds()
        {
            var scene = _s.NewScene("FacadeOutlived");
            var factory = NewFacadeFactory(scene, InstallPlainFacade);
            yield return _s.Frames(2);
            var facade = factory.Create();
            var ended = 0;
            facade.Service.Injected.OnCancel(() => ended++);

            Object.Destroy(facade.Context.gameObject);
            yield return _s.Frames(3);

            Assert.That(facade.Service.Injected.IsDisposed, Is.False, "this is what the warning is about: the service outlives its facade");
            Assert.That(ended, Is.Zero);

            SceneLifetimes.Dispose(scene);

            Assert.That(facade.Service.Injected.IsDisposed, Is.True);
            Assert.That(ended, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Create_AFacadeWithItsOwnInstall_GivesItsServicesTheLifetimeOfItsGameObjectContext()
        {
            var scene = _s.NewScene("FacadeInstall");
            var factory = NewFacadeFactory(scene, InstallFacadeWithLifetime);
            yield return _s.Frames(2);

            var facade = factory.Create();

            var contextLifetime = ((GameObjectContext)facade.Context).GetLifetime();
            Assert.That(contextLifetime.Kind, Is.EqualTo(LifetimeKind.Component));
            Assert.That(contextLifetime.Parent, Is.SameAs(SceneLifetimes.Get(scene)));
            Assert.That(facade.Service.Injected.Parent, Is.SameAs(contextLifetime));
            Assert.That(facade.Service.Injected.Kind, Is.EqualTo(LifetimeKind.Area));
            Assert.That(_s.Warnings.Count("JANITOR112"), Is.Zero);
        }

        [UnityTest]
        public IEnumerator Destroy_OfAFacadeWithItsOwnInstall_EndsItsServicesAndLeavesTheScene()
        {
            var scene = _s.NewScene("FacadeInstallDestroy");
            var factory = NewFacadeFactory(scene, InstallFacadeWithLifetime);
            yield return _s.Frames(2);
            var facade = factory.Create();
            var other = factory.Create();
            var ended = 0;
            facade.Service.Injected.OnCancel(() => ended++);

            Object.Destroy(facade.Context.gameObject);
            yield return _s.Frames(3);

            Assert.That(facade.Service.Injected.IsDisposed, Is.True, "the facade is gone, so is the work of its services");
            Assert.That(ended, Is.EqualTo(1));
            Assert.That(other.Service.Injected.IsDisposed, Is.False, "another facade is untouched");
            Assert.That(SceneLifetimes.Get(scene).IsDisposed, Is.False);
        }

        [UnityTest]
        public IEnumerator SceneLifetimesDispose_EndsTheServicesOfAFacadeWithItsOwnInstall()
        {
            var scene = _s.NewScene("FacadeInstallScene");
            var factory = NewFacadeFactory(scene, InstallFacadeWithLifetime);
            yield return _s.Frames(2);
            var facade = factory.Create();

            SceneLifetimes.Dispose(scene);

            Assert.That(((GameObjectContext)facade.Context).GetLifetime().IsDisposed, Is.True);
            Assert.That(facade.Service.Injected.IsDisposed, Is.True);
        }

        private FacadeRootFactory NewFacadeFactory(Scene scene, System.Action<DiContainer> installFacade)
        {
            var context = _s.NewSceneContext(scene, c =>
            {
                LifetimeInstaller.Install(c);
                c.BindFactory<FacadeRoot, FacadeRootFactory>().FromSubContainerResolve().ByNewGameObjectMethod(installFacade);
            });
            return context.Container.Resolve<FacadeRootFactory>();
        }

        private static void InstallPlainFacade(DiContainer facade)
        {
            facade.Bind<AlphaService>().AsSingle();
            facade.Bind<FacadeRoot>().AsSingle();
        }

        private static void InstallFacadeWithLifetime(DiContainer facade)
        {
            LifetimeInstaller.Install(facade);
            InstallPlainFacade(facade);
        }
    }
}
