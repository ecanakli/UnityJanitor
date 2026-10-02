using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.Binding
{
    // A placed lifetime whose home disappears while its category is being disposed ends with the areas below it.
    [TestFixture]
    public sealed class CategoryRehomeFallbackTests
    {
        private BindingSession _s;
        private CallLog _log;

        [SetUp]
        public void SetUp()
        {
            _s = BindingSession.Begin();
            _log = new CallLog();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return _s.CompleteAsync();
        }

        [UnityTest]
        public IEnumerator CategoryDispose_WhenACancelActionDisposesTheScene_DisposesTheAreasOfThePlacedLifetime()
        {
            var scene = _s.NewScene("Own");
            var probe = _s.NewProbeIn(scene);
            var sceneLifetime = SceneLifetimes.Get(scene);
            var category = sceneLifetime.CreateChild("Inside");
            var placed = probe.GetLifetime(category);
            var area = placed.CreateChild("area");
            var inner = area.CreateChild("inner");
            area.Record(_log, "area item");
            category.OnCancel(() => SceneLifetimes.Dispose(scene));
            Assert.That(placed.Placed, Is.True, "premise: the placed lifetime would normally be re-homed");

            category.Dispose();

            Assert.That(sceneLifetime.IsDisposed, Is.True, "the cancel action disposed the scene during the drain");
            Assert.That(category.IsDisposed, Is.True);
            Assert.That(placed.IsDisposed, Is.True, "its home is gone, so it cannot be re-homed");
            Assert.That(area.IsDisposed, Is.True, "the area below it ends with it");
            Assert.That(inner.IsDisposed, Is.True);
            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "area item" }), "the area's item ran once, during the category's drain");
            Assert.That(_s.Tree.RehomeCount, Is.Zero);
            Assert.That(_s.Tree.Owners.Count, Is.Zero, "nothing of the object stays indexed");

            var late = area.Record(_log, "late");

            Assert.That(late.IsActive, Is.False, "a disposed area refuses work");
            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "area item", "late" }), "and terminates it on the spot");
            yield break;
        }

        [UnityTest]
        public IEnumerator CategoryDispose_WithTheSceneStillAlive_KeepsTheAreasOfThePlacedLifetimeUsable()
        {
            var scene = _s.NewScene("Own");
            var probe = _s.NewProbeIn(scene);
            var sceneLifetime = SceneLifetimes.Get(scene);
            var category = sceneLifetime.CreateChild("Inside");
            var placed = probe.GetLifetime(category);
            var area = placed.CreateChild("area");

            category.Dispose();

            Assert.That(placed.IsDisposed, Is.False);
            Assert.That(placed.Parent, Is.SameAs(sceneLifetime), "re-homed under its scene");
            Assert.That(area.IsDisposed, Is.False, "the area stays with the re-homed lifetime");
            Assert.That(area.Record(_log, "again").IsActive, Is.True);
            Assert.That(_s.Tree.RehomeCount, Is.EqualTo(1));
            yield break;
        }
    }
}
