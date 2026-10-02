using System.Collections.Generic;
using Ecanakli.Janitor.Tests;
using NUnit.Framework;
using UnityEngine;
using Zenject;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.DependencyInjection.Tests
{
    // The Context lookup behind LifetimeInstaller: the context is detected through the local Context binding. The fake contexts are
    // components on inactive GameObjects, so no Awake runs; only the detection (type and binding) is exercised here.
    [TestFixture]
    public sealed class ContextLookupTests : ZenjectUnitTestFixture
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private TestScope _t;

        [SetUp]
        public void SetUpLookup()
        {
            _t = new TestScope();
            _t.Tree.MakeDefault();
        }

        [TearDown]
        public void TearDownLookup()
        {
            for (var i = _objects.Count - 1; i >= 0; i--)
            {
                if (_objects[i] != null)
                {
                    Object.DestroyImmediate(_objects[i]);
                }
            }

            _objects.Clear();
            _t.Complete();
        }

        [Test]
        public void FindLocal_ContainerWithoutAContextBinding_ReturnsNull()
        {
            Assert.That(ContextLookup.FindLocal(Container), Is.Null);
        }

        [Test]
        public void FindLocal_ReturnsTheContextBoundInThatContainer()
        {
            var context = NewContext<ProjectContext>();
            Container.Bind<Context>().FromInstance(context);

            Assert.That(ContextLookup.FindLocal(Container), Is.SameAs(context));
        }

        // Guard: InjectSources.Local, so a parent's Context is not mistaken for the container's own (a scene container and its project parent).
        [Test]
        public void FindLocal_DoesNotSeeAParentsContext()
        {
            var context = NewContext<ProjectContext>();
            Container.Bind<Context>().FromInstance(context);
            var child = new DiContainer(Container);

            Assert.That(ContextLookup.FindLocal(child), Is.Null);
            Assert.That(ContextLookup.FindLocal(Container), Is.SameAs(context));
        }

        [Test]
        public void FindInAncestors_ReturnsTheNearestEnclosingContext()
        {
            var project = NewContext<ProjectContext>();
            var scene = NewContext<SceneContext>();
            Container.Bind<Context>().FromInstance(project);
            var sceneContainer = new DiContainer(Container);
            sceneContainer.Bind(typeof(Context), typeof(SceneContext)).To<SceneContext>().FromInstance(scene);
            var inner = new DiContainer(sceneContainer);

            Assert.That(ContextLookup.FindInAncestors(inner), Is.SameAs(scene), "the nearest context wins");
            Assert.That(ContextLookup.FindInAncestors(sceneContainer), Is.SameAs(project));
        }

        [Test]
        public void FindInAncestors_WithNoContextAnywhere_ReturnsNull()
        {
            var child = new DiContainer(Container);

            Assert.That(ContextLookup.FindInAncestors(child), Is.Null);
        }

        // Guard: IsInstalling is cleared only while the lookup resolves, and restored either way (Zenject reads it for one warning).
        [TestCase(true)]
        [TestCase(false)]
        public void FindLocal_RestoresTheInstallingFlag(bool installing)
        {
            var context = NewContext<ProjectContext>();
            Container.Bind<Context>().FromInstance(context);
            Container.IsInstalling = installing;

            ContextLookup.FindLocal(Container);

            Assert.That(Container.IsInstalling, Is.EqualTo(installing));
        }

        // Guard: a ProjectContext binding means App, found locally.
        [Test]
        public void Install_ContainerWithAProjectContext_CapturesApp()
        {
            var project = NewContext<ProjectContext>();
            Container.Bind<Context>().FromInstance(project);
            LifetimeInstaller.Install(Container);

            var injected = Container.Instantiate<AlphaService>().Injected;

            Assert.That(injected.Parent, Is.SameAs(_t.App));
        }

        // Guard: a sub-container without its own Context uses the enclosing one (here a ProjectContext, so App).
        [Test]
        public void Install_SubContainerBelowAProjectContext_CapturesApp()
        {
            var project = NewContext<ProjectContext>();
            Container.Bind<Context>().FromInstance(project);
            var sub = new DiContainer(Container);
            LifetimeInstaller.Install(sub);

            var injected = sub.Instantiate<AlphaService>().Injected;

            Assert.That(ContextLookup.FindLocal(sub), Is.Null);
            Assert.That(ContextLookup.FindInAncestors(sub), Is.SameAs(project));
            Assert.That(injected.Parent, Is.SameAs(_t.App));
        }

        private T NewContext<T>()
            where T : Context
        {
            var go = new GameObject("fake " + typeof(T).Name);
            go.SetActive(false);
            _objects.Add(go);
            return go.AddComponent<T>();
        }
    }
}
