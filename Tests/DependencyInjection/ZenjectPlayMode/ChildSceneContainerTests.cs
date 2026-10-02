using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using Zenject;

namespace Ecanakli.Janitor.DependencyInjection.Tests.PlayMode
{
    // The container hierarchy that ZenjectSceneLoader sets up for an additive load with LoadSceneRelationship.Child: the new scene's
    // context has the loading scene's container as its parent. The loader call itself needs a scene in the build settings and is not
    // made here; the same hand-over is made through SceneContext.ParentContainers, which is all the loader does with it.
    [TestFixture]
    public sealed class ChildSceneContainerTests
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
        public IEnumerator Load_ASceneWithItsOwnInstallBelowAnInstalledScene_GivesItsServicesItsOwnSceneLifetime()
        {
            var parentScene = _s.NewScene("ParentScene");
            var parent = _s.NewSceneContext(parentScene, c => LifetimeInstaller.Install(c));
            var childScene = _s.NewScene("ChildScene");
            var child = _s.NewSceneContext(childScene, c =>
            {
                LifetimeInstaller.Install(c);
                c.Bind<AlphaService>().AsSingle();
            }, parent.Container);
            yield return _s.Frames(2);

            var service = child.Container.Resolve<AlphaService>();

            Assert.That(child.Container.ParentContainers[0], Is.SameAs(parent.Container));
            Assert.That(service.Injected.Parent, Is.SameAs(SceneLifetimes.Get(childScene)));
            Assert.That(_s.Warnings.Count("JANITOR112"), Is.Zero);

            SceneLifetimes.Dispose(parentScene);

            Assert.That(service.Injected.IsDisposed, Is.False, "lifetimes follow scenes, not the container hierarchy");

            SceneLifetimes.Dispose(childScene);

            Assert.That(service.Injected.IsDisposed, Is.True);
        }

        [UnityTest]
        public IEnumerator Load_ASceneWithoutInstallBelowAnInstalledScene_WarnsAndTiesItsServicesToTheLoadingScene()
        {
            var parentScene = _s.NewScene("ParentScene");
            var parent = _s.NewSceneContext(parentScene, c => LifetimeInstaller.Install(c));
            var childScene = _s.NewScene("ChildScene");
            var child = _s.NewSceneContext(childScene, c => c.Bind<AlphaService>().AsSingle(), parent.Container);
            yield return _s.Frames(2);

            var service = child.Container.Resolve<AlphaService>();

            var text = _s.Warnings.TextOf("JANITOR112");
            Assert.That(_s.Warnings.Count("JANITOR112"), Is.EqualTo(1));
            Assert.That(text, Does.Contain("'" + childScene.name + "'").And.Contain("'" + parentScene.name + "'").And.Contain(nameof(AlphaService)));
            Assert.That(service.Injected.Parent, Is.SameAs(SceneLifetimes.Get(parentScene)), "the loading scene's binding answers");

            SceneLifetimes.Dispose(parentScene);

            Assert.That(service.Injected.IsDisposed, Is.True, "the service of the loaded scene ends with the loading scene, while the loaded scene lives on");
            Assert.That(SceneLifetimes.Get(childScene).IsDisposed, Is.False);
        }
    }
}
