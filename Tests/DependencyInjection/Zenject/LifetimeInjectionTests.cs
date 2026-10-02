using System;
using Ecanakli.Janitor.Tests;
using NUnit.Framework;
using Zenject;

namespace Ecanakli.Janitor.DependencyInjection.Tests
{
    // EditMode part of the Zenject injection tests: the per-injectee lifetime, Install rules and the context-less fallback.
    // The fixture's container has no Context, so the context lifetime is Lifetime.App of the test tree.
    [TestFixture]
    public sealed class LifetimeInjectionTests : ZenjectUnitTestFixture
    {
        private TestScope _t;

        [SetUp]
        public void SetUpLifetimes()
        {
            _t = new TestScope();
            _t.Tree.MakeDefault();
            LifetimeInstaller.Install(Container);
        }

        [TearDown]
        public void TearDownLifetimes()
        {
            _t.Complete();
        }

        // Guard: ContextLifetimeResolver creates a child of the context lifetime named after ctx.ObjectType (accepted decision: an ordinary area).
        [Test]
        public void Resolve_PlainService_GetsANewChildOfTheContextLifetimeNamedAfterTheService()
        {
            Container.Bind<AlphaService>().AsSingle();

            var injected = Container.Resolve<AlphaService>().Injected;

            Assert.That(injected.Parent, Is.SameAs(_t.App));
            Assert.That(injected.Name, Is.EqualTo(nameof(AlphaService)));
            Assert.That(injected.Kind, Is.EqualTo(LifetimeKind.Area));
            Assert.That(injected.PackageOwned, Is.False);
            Assert.That(injected.IsDisposed, Is.False);
        }

        // Guard: the binding is transient, so every injection point gets its own lifetime.
        [Test]
        public void Resolve_EveryInjectee_GetsItsOwnChild()
        {
            Container.Bind<AlphaService>().AsTransient();
            Container.Bind<BetaService>().AsTransient();

            var firstAlpha = Container.Resolve<AlphaService>().Injected;
            var secondAlpha = Container.Resolve<AlphaService>().Injected;
            var beta = Container.Resolve<BetaService>().Injected;

            Assert.That(firstAlpha, Is.Not.SameAs(secondAlpha));
            Assert.That(firstAlpha, Is.Not.SameAs(beta));
            Assert.That(secondAlpha, Is.Not.SameAs(beta));
            Assert.That(_t.App.ChildCount, Is.EqualTo(3));
            Assert.That(beta.Name, Is.EqualTo(nameof(BetaService)));
        }

        // Guard: a service's Cancel() ends only its own generation; siblings and the context are untouched.
        [Test]
        public void Cancel_OnOneServicesLifetime_AffectsOnlyThatLifetime()
        {
            Container.Bind<AlphaService>().AsSingle();
            Container.Bind<BetaService>().AsSingle();
            var alpha = Container.Resolve<AlphaService>().Injected;
            var beta = Container.Resolve<BetaService>().Injected;
            alpha.Record(_t.Log, "alpha");
            beta.Record(_t.Log, "beta");
            _t.App.Record(_t.Log, "app");
            var betaGeneration = beta.Generation;
            var appGeneration = _t.App.Generation;
            var alphaGeneration = alpha.Generation;

            alpha.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "alpha" }));
            Assert.That(alpha.Generation, Is.EqualTo(alphaGeneration + 1));
            Assert.That(beta.Generation, Is.EqualTo(betaGeneration));
            Assert.That(_t.App.Generation, Is.EqualTo(appGeneration));
            Assert.That(alpha.IsDisposed, Is.False);
            Assert.That(alpha.Record(_t.Log, "again").IsActive, Is.True, "the lifetime stays usable for the service");
        }

        // Guard: the injected child ends with the context lifetime.
        [Test]
        public void Shutdown_OfTheContextLifetime_DisposesEveryInjectedChild()
        {
            Container.Bind<AlphaService>().AsSingle();
            Container.Bind<BetaService>().AsSingle();
            var alpha = Container.Resolve<AlphaService>().Injected;
            var beta = Container.Resolve<BetaService>().Injected;
            alpha.Record(_t.Log, "alpha");
            beta.Record(_t.Log, "beta");

            _t.Tree.Shutdown();

            Assert.That(alpha.IsDisposed, Is.True);
            Assert.That(beta.IsDisposed, Is.True);
            Assert.That(_t.Log.Count, Is.EqualTo(2));
        }

        // Guard: a service built after its context lifetime ended gets a child born disposed, not an exception.
        [Test]
        public void Resolve_AfterTheContextLifetimeEnded_GivesAChildBornDisposed()
        {
            Container.Bind<AlphaService>().AsSingle();
            _t.Tree.Shutdown();

            var injected = Container.Resolve<AlphaService>().Injected;

            Assert.That(injected.IsDisposed, Is.True);
            Assert.That(injected.Record(_t.Log, "late").IsActive, Is.False);
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "late" }), "a registration on it terminates at once");
        }

        // Guard: a root Resolve has no ObjectType, so the child gets the fallback label.
        [Test]
        public void Resolve_WithoutAnObjectType_UsesTheInjectedLabel()
        {
            var lifetime = Container.Resolve<Lifetime>();

            Assert.That(lifetime.Name, Is.EqualTo("Injected"));
            Assert.That(lifetime.Parent, Is.SameAs(_t.App));
        }

        // Guard: Install returns early when the container already has a local Lifetime binding (two bindings are ambiguous to Zenject).
        [Test]
        public void Install_Twice_IsANoOp()
        {
            LifetimeInstaller.Install(Container);
            LifetimeInstaller.Install(Container);
            Container.Bind<AlphaService>().AsSingle();

            Assert.That(() => Container.Resolve<AlphaService>(), Throws.Nothing);
            Assert.That(Container.ResolveAll<Lifetime>().Count, Is.EqualTo(1), "exactly one Lifetime binding exists");
        }

        [Test]
        public void Install_NullContainer_ThrowsArgumentNullException()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => LifetimeInstaller.Install(null));

            Assert.That(exception.ParamName, Is.EqualTo("container"));
        }

        // Guard: a validating container captures nothing (Lifetime.App and SceneLifetimes throw outside Play Mode) and still gets the binding.
        [Test]
        public void Install_WhileTheContainerValidates_DoesNotNeedALifetimeTree()
        {
            var validating = new DiContainer(StaticContext.Container, true);
            LifetimeTree.Default = null;

            Assert.That(() => LifetimeInstaller.Install(validating), Throws.Nothing);

            Assert.That(validating.HasBindingId(typeof(Lifetime), null, InjectSources.Local), Is.True);
        }

        // Guard: without a Context anywhere the context lifetime is App (the documented fallback).
        [Test]
        public void Install_OnAContextLessContainer_UsesApp()
        {
            var bare = new DiContainer(StaticContext.Container);
            LifetimeInstaller.Install(bare);

            var injected = bare.Instantiate<AlphaService>().Injected;

            Assert.That(injected.Parent, Is.SameAs(_t.App));
        }

        // Guard: a sub-container is its own container, so it gets its own binding and its own children.
        [Test]
        public void Install_OnASubContainer_BindsItSeparately()
        {
            var sub = new DiContainer(Container);
            LifetimeInstaller.Install(sub);

            var fromSub = sub.Instantiate<AlphaService>().Injected;

            Assert.That(sub.HasBindingId(typeof(Lifetime), null, InjectSources.Local), Is.True, "a parent's binding does not stand in for the sub-container's own");
            Assert.That(fromSub.Parent, Is.SameAs(_t.App));
        }
    }
}
