using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.Binding
{
    // Category parents, re-home and scene membership.
    // "[Inject] Lifetime counts as the first access" is covered by the Zenject PlayMode tests, not here.
    [TestFixture]
    public sealed class CategoryLifetimeTests
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

        // First access decides

        // Guard: ComponentLifetimes.CheckParent warns and returns the existing lifetime instead of reparenting.
        [UnityTest]
        public IEnumerator GetLifetime_FirstAccessDecidesTheParent_ALaterMismatchRaisesJanitor113AndKeepsIt()
        {
            var probe = _s.NewProbe();
            var first = Lifetime.App.CreateChild("First");
            var second = Lifetime.App.CreateChild("Second");

            var placed = probe.GetLifetime(first);
            Assert.That(placed.Parent, Is.SameAs(first));
            Assert.That(placed.Placed, Is.True);

            LogAssert.Expect(LogType.Warning, new Regex("JANITOR113"));
            var again = probe.GetLifetime(second);

            Assert.That(again, Is.SameAs(placed));
            Assert.That(placed.Parent, Is.SameAs(first), "the parent is never silently changed");
            Assert.That(first.ChildCount, Is.EqualTo(1));
            Assert.That(second.ChildCount, Is.Zero);
            Assert.That(_s.Logs.Count("JANITOR113"), Is.EqualTo(1));

            Assert.That(probe.GetLifetime(first), Is.SameAs(placed));
            Assert.That(probe.GetLifetime(), Is.SameAs(placed));
            Assert.That(_s.Logs.Count("JANITOR113"), Is.EqualTo(1), "the same parent and the parent-less form do not warn");
            yield break;
        }

        // Guard: an implicit first access (AddTo(this), this.Run, Subscribe(h, this)) is a parent-less GetLifetime.
        [UnityTest]
        public IEnumerator GetLifetime_AfterAnImplicitFirstAccess_TheRequestedCategoryIsIgnoredWithJanitor113()
        {
            var probe = _s.NewProbe();
            var category = Lifetime.App.CreateChild("Late");
            new DisposeProbe().AddTo(probe);
            var scene = SceneLifetimes.Get(probe.gameObject.scene);
            Assert.That(probe.GetLifetime().Parent, Is.SameAs(scene));

            LogAssert.Expect(LogType.Warning, new Regex("JANITOR113"));
            var lifetime = probe.GetLifetime(category);

            Assert.That(lifetime.Parent, Is.SameAs(scene));
            Assert.That(lifetime.Placed, Is.False);
            Assert.That(category.ChildCount, Is.Zero);
            yield break;
        }

        [UnityTest]
        public IEnumerator GetActiveLifetime_ALaterMismatch_RaisesJanitor113AndKeepsTheParent()
        {
            var probe = _s.NewProbe();
            var first = Lifetime.App.CreateChild("First");
            var second = Lifetime.App.CreateChild("Second");

            var active = probe.GetActiveLifetime(first);
            Assert.That(active.Kind, Is.EqualTo(LifetimeKind.Active));
            Assert.That(active.Parent, Is.SameAs(first));

            LogAssert.Expect(LogType.Warning, new Regex("JANITOR113"));
            var again = probe.GetActiveLifetime(second);

            Assert.That(again, Is.SameAs(active));
            Assert.That(active.Parent, Is.SameAs(first));
            Assert.That(probe.GetActiveLifetime(first), Is.SameAs(active));
            Assert.That(_s.Logs.Count("JANITOR113"), Is.EqualTo(1));
            yield break;
        }

        [UnityTest]
        public IEnumerator GetLifetime_WithADisposedCategory_FallsBackToTheSceneLifetimeWithJanitor113()
        {
            var probe = _s.NewProbe();
            var gone = Lifetime.App.CreateChild("Gone");
            gone.Dispose();
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR113"));

            var lifetime = probe.GetLifetime(gone);

            Assert.That(lifetime.Parent, Is.SameAs(SceneLifetimes.Get(probe.gameObject.scene)));
            Assert.That(lifetime.Placed, Is.False);
            Assert.That(lifetime.IsDisposed, Is.False);
            yield break;
        }

        [UnityTest]
        public IEnumerator GetLifetime_WithTheSceneLifetimeAsTheCategory_IsNotPlaced()
        {
            var probe = _s.NewProbe();
            var scene = SceneLifetimes.Get(probe.gameObject.scene);

            var lifetime = probe.GetLifetime(scene);

            Assert.That(lifetime.Parent, Is.SameAs(scene));
            Assert.That(lifetime.Placed, Is.False, "the scene lifetime is where the lifetime goes anyway");
            Assert.That(MemberCount(scene), Is.Zero);
            Assert.That(_s.Logs.Count("JANITOR113"), Is.Zero);
            yield break;
        }

        // Category Cancel and Dispose

        [UnityTest]
        public IEnumerator Cancel_OnTheCategory_ReachesPlacedComponentAndActiveLifetimes()
        {
            var probe = _s.NewProbe();
            var category = Lifetime.App.CreateChild("Popups");
            var component = probe.GetLifetime(category);
            var active = probe.GetActiveLifetime(category);
            component.Record(_log, "component");
            active.Record(_log, "active");
            var scene = SceneLifetimes.Get(probe.gameObject.scene);
            Assert.That(MemberCount(scene), Is.EqualTo(2), "a category outside the scene subtree gives dispose-only memberships");
            var componentGeneration = component.Generation;
            var activeGeneration = active.Generation;

            category.Cancel();

            Assert.That(_log.ToArray(), Is.EquivalentTo(new[] { "component", "active" }));
            Assert.That(component.IsDisposed, Is.False);
            Assert.That(active.IsDisposed, Is.False);
            Assert.That(component.Parent, Is.SameAs(category), "a Cancel keeps everything attached");
            Assert.That(active.Parent, Is.SameAs(category));
            Assert.That(component.Generation, Is.EqualTo(componentGeneration + 1));
            Assert.That(active.Generation, Is.EqualTo(activeGeneration + 1));
            Assert.That(component.Record(_log, "again").IsActive, Is.True);
            yield break;
        }

        // Guard: SceneBinding.ShouldRehome marks a placed node under a disposed category for cancel-and-re-home.
        [UnityTest]
        public IEnumerator Dispose_OnTheCategory_CancelsAndRehomesPlacedLifetimes_AndTheObjectKeepsWorking()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var scene = SceneLifetimes.Get(go.scene);
            var category = Lifetime.App.CreateChild("Popups");
            var component = probe.GetLifetime(category);
            var active = probe.GetActiveLifetime(category);
            component.Record(_log, "component");
            active.Record(_log, "active");

            category.Dispose();

            Assert.That(category.IsDisposed, Is.True);
            Assert.That(_log.ToArray(), Is.EquivalentTo(new[] { "component", "active" }), "the placed lifetimes are cancelled");
            Assert.That(component.IsDisposed, Is.False, "a category groups cancellation; it does not own the object");
            Assert.That(active.IsDisposed, Is.False);
            Assert.That(component.Parent, Is.SameAs(scene));
            Assert.That(active.Parent, Is.SameAs(scene));
            Assert.That(component.Placed, Is.False);
            Assert.That(active.Placed, Is.False);
            Assert.That(component.Membership, Is.Null);
            Assert.That(active.Membership, Is.Null);
            Assert.That(MemberCount(scene), Is.Zero);
            Assert.That(category.ChildCount, Is.Zero);
            Assert.That(_s.Tree.RehomeCount, Is.EqualTo(2), "JANITOR114 is recorded per re-homed lifetime");

            Assert.That(component.Record(_log, "after").IsActive, Is.True, "the object keeps working");
            Assert.That(active.Record(_log, "afterActive").IsActive, Is.True);

            Object.Destroy(go);
            yield return null;

            Assert.That(component.IsDisposed, Is.True, "a re-homed lifetime is still disposed by its owner's destroy");
            Assert.That(active.IsDisposed, Is.True);
            Assert.That(_log.ToArray(), Is.EquivalentTo(new[] { "component", "active", "after", "afterActive" }));
            Assert.That(_s.Tree.Owners.Count, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Dispose_OnTheCategory_ChildAreaOfAPlacedLifetime_IsCancelledNotDisposed()
        {
            var probe = _s.NewProbe();
            var category = Lifetime.App.CreateChild("Popups");
            var component = probe.GetLifetime(category);
            var area = component.CreateChild("area");
            area.Record(_log, "area");
            component.Record(_log, "component");

            category.Dispose();

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "area", "component" }));
            Assert.That(area.IsDisposed, Is.False, "the subtree of a re-homed lifetime gets Cancel semantics");
            Assert.That(area.Parent, Is.SameAs(component));
            Assert.That(area.Record(_log, "again").IsActive, Is.True);
            yield break;
        }

        // A category Dispose while the object's own scene is going away

        [UnityTest]
        public IEnumerator SceneDispose_WithTheCategoryInsideTheScene_DisposesThePlacedLifetimeInsteadOfRehoming()
        {
            var scene = _s.NewScene("Own");
            var probe = _s.NewProbeIn(scene);
            var sceneLifetime = SceneLifetimes.Get(scene);
            var category = sceneLifetime.CreateChild("Inside");
            var lifetime = probe.GetLifetime(category);
            lifetime.Record(_log, "item");
            Assert.That(lifetime.Placed, Is.True);
            Assert.That(MemberCount(sceneLifetime), Is.Zero, "a category inside the scene subtree needs no membership");

            SceneLifetimes.Dispose(scene);

            Assert.That(lifetime.IsDisposed, Is.True);
            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "item" }));
            Assert.That(_s.Tree.RehomeCount, Is.Zero);
            yield break;
        }

        // Guard: SceneBinding.ShouldRehome does not re-home into a scene that is Disposing, and TryMark skips the node the scene already owns.
        [UnityTest]
        public IEnumerator CategoryDispose_DuringTheScenesOwnDisposal_DisposesThePlacedLifetimeInsteadOfRehoming()
        {
            var scene = _s.NewScene("Own");
            var probe = _s.NewProbeIn(scene);
            var sceneLifetime = SceneLifetimes.Get(scene);
            var category = Lifetime.App.CreateChild("Outside");
            var lifetime = probe.GetLifetime(category);
            lifetime.Record(_log, "item");
            sceneLifetime.OnCancel(category, static c => c.Dispose());
            Assert.That(MemberCount(sceneLifetime), Is.EqualTo(1));

            SceneLifetimes.Dispose(scene);

            Assert.That(category.IsDisposed, Is.True, "the scene's own item disposed the category mid-operation");
            Assert.That(lifetime.IsDisposed, Is.True);
            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "item" }), "disposed once, by the scene's operation");
            Assert.That(_s.Tree.RehomeCount, Is.Zero);
            yield break;
        }

        // Guard: ShouldRehome compares the node's current scene with the operation's scene, not the category's.
        [UnityTest]
        public IEnumerator SceneDispose_RehomesAPlacedObjectThatMovedToAnotherScene()
        {
            var origin = _s.NewScene("Origin");
            var target = _s.NewScene("Target");
            var go = _s.NewObjectIn(origin);
            var probe = go.AddComponent<BindingProbe>();
            var originLifetime = SceneLifetimes.Get(origin);
            var targetLifetime = SceneLifetimes.Get(target);
            var category = originLifetime.CreateChild("InsideOrigin");
            var lifetime = probe.GetLifetime(category);
            lifetime.Record(_log, "item");
            SceneManager.MoveGameObjectToScene(go, target);

            SceneLifetimes.Dispose(origin);

            Assert.That(originLifetime.IsDisposed, Is.True);
            Assert.That(lifetime.IsDisposed, Is.False, "the object lives in another scene, so it is re-homed");
            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "item" }));
            Assert.That(lifetime.Parent, Is.SameAs(targetLifetime));
            Assert.That(lifetime.Placed, Is.False);
            Assert.That(_s.Tree.RehomeCount, Is.EqualTo(1));
            Assert.That(targetLifetime.IsDisposed, Is.False);
            yield break;
        }

        // Destroy detaches

        [UnityTest]
        public IEnumerator Destroy_OfAPlacedObject_DetachesItFromTheCategoryAndTheMembership()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var scene = SceneLifetimes.Get(go.scene);
            var category = Lifetime.App.CreateChild("Outside");
            var component = probe.GetLifetime(category);
            var active = probe.GetActiveLifetime(category);
            Assert.That(category.ChildCount, Is.EqualTo(2));
            Assert.That(MemberCount(scene), Is.EqualTo(2));

            Object.Destroy(go);
            yield return null;

            Assert.That(component.IsDisposed, Is.True);
            Assert.That(active.IsDisposed, Is.True);
            Assert.That(category.ChildCount, Is.Zero, "a destroyed object leaves its category");
            Assert.That(MemberCount(scene), Is.Zero, "and the scene membership");
            Assert.That(category.IsDisposed, Is.False);
        }

        [UnityTest]
        public IEnumerator Destroy_OfAPlacedObject_InACategoryInsideTheScene_DetachesItFromTheCategory()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var scene = SceneLifetimes.Get(go.scene);
            var category = scene.CreateChild("Inside");
            var component = probe.GetLifetime(category);
            Assert.That(category.ChildCount, Is.EqualTo(1));

            Object.Destroy(go);
            yield return null;

            Assert.That(component.IsDisposed, Is.True);
            Assert.That(category.ChildCount, Is.Zero);
        }

        // Scene membership

        // Guard: LifetimeTeardown.Mark walks Scene.Members after the normal traversal when the operation is a Dispose.
        [UnityTest]
        public IEnumerator DisposeAll_DisposesPlacedObjectsOfTheScene_EvenWhenTheCategoryIsUnderApp()
        {
            var scene = _s.NewScene("Unloaded");
            var placedProbe = _s.NewProbeIn(scene);
            var plainProbe = _s.NewProbeIn(scene);
            var category = Lifetime.App.CreateChild("Outside");
            var placed = placedProbe.GetLifetime(category);
            var active = placedProbe.GetActiveLifetime(category);
            var plain = plainProbe.GetLifetime();
            placed.Record(_log, "placed");
            active.Record(_log, "active");
            plain.Record(_log, "plain");

            SceneLifetimes.DisposeAll();

            Assert.That(_log.ToArray(), Is.EquivalentTo(new[] { "placed", "active", "plain" }));
            Assert.That(placed.IsDisposed, Is.True);
            Assert.That(active.IsDisposed, Is.True);
            Assert.That(plain.IsDisposed, Is.True);
            Assert.That(category.IsDisposed, Is.False, "DisposeAll never disposes a category");
            Assert.That(category.ChildCount, Is.Zero);
            Assert.That(_s.Tree.RehomeCount, Is.Zero);
            yield break;
        }

        // Guard: a scene Cancel does not read Scene.Members.
        [UnityTest]
        public IEnumerator SceneCancel_DoesNotReachPlacedObjectsOutsideTheSceneSubtree()
        {
            var scene = _s.NewScene("Cancelled");
            var outsideProbe = _s.NewProbeIn(scene);
            var insideProbe = _s.NewProbeIn(scene);
            var plainProbe = _s.NewProbeIn(scene);
            var sceneLifetime = SceneLifetimes.Get(scene);
            var outsideCategory = Lifetime.App.CreateChild("Outside");
            var insideCategory = sceneLifetime.CreateChild("Inside");
            var outside = outsideProbe.GetLifetime(outsideCategory);
            var inside = insideProbe.GetLifetime(insideCategory);
            var plain = plainProbe.GetLifetime();
            outside.Record(_log, "outside");
            inside.Record(_log, "inside");
            plain.Record(_log, "plain");
            var outsideGeneration = outside.Generation;

            sceneLifetime.Cancel();

            Assert.That(_log.ToArray(), Is.EquivalentTo(new[] { "inside", "plain" }), "for cancellation the outside object belongs to its category");
            Assert.That(outside.Generation, Is.EqualTo(outsideGeneration));
            Assert.That(outside.IsDisposed, Is.False);

            SceneLifetimes.Dispose(scene);

            Assert.That(_log.ToArray(), Is.EquivalentTo(new[] { "inside", "plain", "outside" }), "a scene Dispose does reach it");
            Assert.That(outside.IsDisposed, Is.True);
            yield break;
        }

        // Guard: SceneBinding.PrePass.MoveMembershipIfMoved moves the membership entry, never the category.
        [UnityTest]
        public IEnumerator MoveGameObjectToScene_MovesTheMembership_AndKeepsTheCategory()
        {
            var origin = _s.NewScene("Origin");
            var target = _s.NewScene("Target");
            var go = _s.NewObjectIn(origin);
            var probe = go.AddComponent<BindingProbe>();
            var originLifetime = SceneLifetimes.Get(origin);
            var targetLifetime = SceneLifetimes.Get(target);
            var category = Lifetime.App.CreateChild("Outside");
            var lifetime = probe.GetLifetime(category);
            lifetime.Record(_log, "placed");
            Assert.That(lifetime.Membership, Is.SameAs(originLifetime));
            SceneManager.MoveGameObjectToScene(go, target);

            originLifetime.Cancel();

            Assert.That(lifetime.Membership, Is.SameAs(targetLifetime), "the membership follows the object");
            Assert.That(lifetime.Parent, Is.SameAs(category), "the category stays");
            Assert.That(MemberCount(originLifetime), Is.Zero);
            Assert.That(MemberCount(targetLifetime), Is.EqualTo(1));
            Assert.That(_log.Count, Is.Zero, "a scene Cancel never reaches it");

            SceneLifetimes.Dispose(origin);
            Assert.That(lifetime.IsDisposed, Is.False, "the old scene no longer owns it");
            Assert.That(_log.Count, Is.Zero);

            SceneLifetimes.Dispose(target);
            Assert.That(lifetime.IsDisposed, Is.True, "the new scene does");
            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "placed" }));
            Assert.That(category.IsDisposed, Is.False);
            Assert.That(category.ChildCount, Is.Zero);
            yield break;
        }

        [UnityTest]
        public IEnumerator DontDestroyOnLoad_DropsTheMembership_AndKeepsTheCategory()
        {
            var scene = _s.NewScene("Origin");
            var go = _s.NewObjectIn(scene);
            var probe = go.AddComponent<BindingProbe>();
            var sceneLifetime = SceneLifetimes.Get(scene);
            var category = Lifetime.App.CreateChild("Outside");
            var lifetime = probe.GetLifetime(category);
            lifetime.Record(_log, "placed");
            Assert.That(lifetime.Membership, Is.SameAs(sceneLifetime));
            Object.DontDestroyOnLoad(go);

            sceneLifetime.Cancel();

            Assert.That(lifetime.Membership, Is.Null, "DontDestroyOnLoad objects have no scene to belong to");
            Assert.That(lifetime.Parent, Is.SameAs(category));
            Assert.That(MemberCount(sceneLifetime), Is.Zero);

            SceneLifetimes.Dispose(scene);
            SceneLifetimes.DisposeAll();

            Assert.That(lifetime.IsDisposed, Is.False, "it survives every scene disposal");
            Assert.That(_log.Count, Is.Zero);

            Object.Destroy(go);
            yield return null;

            Assert.That(lifetime.IsDisposed, Is.True, "its own destroy still disposes it");
            Assert.That(category.ChildCount, Is.Zero);
        }

        private static int MemberCount(Lifetime scene)
        {
            return scene.Members == null ? 0 : scene.Members.Count;
        }
    }
}
