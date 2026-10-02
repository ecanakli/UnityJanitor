#if UNITY_EDITOR
using Ecanakli.Janitor.Tests;
using NUnit.Framework;
using Zenject;

namespace Ecanakli.Janitor.DependencyInjection.Tests
{
    // The growth check counts the areas that plain injectees get under the context lifetime. The fixture's container
    // has no Context, so the context lifetime is App of the test tree; the scene lifetime is covered by the PlayMode tests.
    [TestFixture]
    public sealed class InjectedGrowthTests : ZenjectUnitTestFixture
    {
        private DiagnosticsRecordingKit _d;
        private TestScope _t;

        [SetUp]
        public void SetUpGrowth()
        {
            _d = new DiagnosticsRecordingKit();
            _t = new TestScope();
            _t.Tree.MakeDefault();
            LifetimeInstaller.Install(Container);
        }

        [TearDown]
        public void TearDownGrowth()
        {
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
        public void Resolve_100PlainInjectees_RecordsJanitor106OnceOnTheContextLifetime()
        {
            Container.Bind<AlphaService>().AsTransient();

            for (var i = 0; i < 100; i++)
            {
                Container.Resolve<AlphaService>();
            }

            var warning = _d.Single(DiagnosticIds.Growth);
            Assert.That(warning.Lifetime, Is.SameAs(_t.App), "App is the context lifetime of this container");
            Assert.That(warning.Count, Is.EqualTo(1), "once per lifetime, not once per injectee");
            Assert.That(warning.Message, Does.Contain("65 live children that are areas"));
        }

        [Test]
        public void Resolve_64PlainInjectees_RecordsNothing()
        {
            Container.Bind<AlphaService>().AsTransient();

            for (var i = 0; i < LifetimeDiagnostics.GrowthChildLimit; i++)
            {
                Container.Resolve<AlphaService>();
            }

            Assert.That(_d.Count(DiagnosticIds.Growth), Is.Zero, "64 is the limit, not past it");
        }

        [Test]
        public void Resolve_PlainInjecteesThatAreDisposedWithTheirObjects_RecordNothing()
        {
            Container.Bind<AlphaService>().AsTransient();

            for (var i = 0; i < 300; i++)
            {
                Container.Resolve<AlphaService>().Injected.Dispose();
            }

            Assert.That(_t.App.ChildCount, Is.Zero, "premise: every injected area is gone");
            Assert.That(_d.Count(DiagnosticIds.Growth), Is.Zero, "only live areas are counted");
        }
    }
}
#endif
