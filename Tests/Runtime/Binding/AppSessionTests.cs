using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.Binding
{
    // App, the exit token and the per-session reset.
    // Running outside Play Mode is covered by BindingEditModeTests.
    [TestFixture]
    public sealed class AppSessionTests
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
        public IEnumerator AppCancel_CascadesToEveryLifetimeWithoutDisposingAnything()
        {
            var probe = _s.NewProbe();
            var component = probe.GetLifetime();
            var gameObjectLifetime = probe.gameObject.GetLifetime();
            var active = probe.GetActiveLifetime();
            var scene = SceneLifetimes.Get(probe.gameObject.scene);
            var area = Lifetime.App.CreateChild("area");
            var category = Lifetime.App.CreateChild("category");
            var placed = _s.NewProbe().GetLifetime(category);
            var all = new[] { component, gameObjectLifetime, active, scene, area, category, placed, Lifetime.App };
            component.Record(_log, "component");
            gameObjectLifetime.Record(_log, "gameObject");
            active.Record(_log, "active");
            scene.Record(_log, "scene");
            area.Record(_log, "area");
            placed.Record(_log, "placed");
            Lifetime.App.Record(_log, "app");
            var generations = new int[all.Length];
            for (var i = 0; i < all.Length; i++)
            {
                generations[i] = all[i].Generation;
            }

            var owners = _s.Tree.Owners.Count;
            var scenes = _s.Tree.Scenes.Count;

            Lifetime.App.Cancel();

            Assert.That(_log.ToArray(), Is.EquivalentTo(new[] { "component", "gameObject", "active", "scene", "area", "placed", "app" }));
            for (var i = 0; i < all.Length; i++)
            {
                Assert.That(all[i].IsDisposed, Is.False, "lifetime " + i + " must survive");
                Assert.That(all[i].Generation, Is.EqualTo(generations[i] + 1), "lifetime " + i + " opens a new generation");
            }

            Assert.That(_s.Tree.Owners.Count, Is.EqualTo(owners), "nothing left the owner index");
            Assert.That(_s.Tree.Scenes.Count, Is.EqualTo(scenes), "nothing left the scene index");
            Assert.That(component.Record(_log, "again").IsActive, Is.True, "the objects keep working");
            yield break;
        }

        // Guard: PlayModeBootstrap.ExitCore shuts the session tree down when the exit token is cancelled; ShouldRehome does not re-home on an App dispose.
        [UnityTest]
        public IEnumerator ExitToken_Cancelled_DisposesEverything()
        {
            var probe = _s.NewProbe();
            var component = probe.GetLifetime();
            var gameObjectLifetime = probe.gameObject.GetLifetime();
            var active = probe.GetActiveLifetime();
            var scene = SceneLifetimes.Get(probe.gameObject.scene);
            var area = Lifetime.App.CreateChild("area");
            var category = Lifetime.App.CreateChild("category");
            var placed = _s.NewProbe().GetLifetime(category);
            var all = new[] { component, gameObjectLifetime, active, scene, area, category, placed, Lifetime.App };
            component.Record(_log, "component");
            gameObjectLifetime.Record(_log, "gameObject");
            active.Record(_log, "active");
            scene.Record(_log, "scene");
            area.Record(_log, "area");
            placed.Record(_log, "placed");
            Lifetime.App.Record(_log, "app");
            var tree = _s.Tree;

            _s.Exit.Cancel();

            Assert.That(PlayModeBootstrap.IsExiting, Is.True);
            Assert.That(_log.ToArray(), Is.EquivalentTo(new[] { "component", "gameObject", "active", "scene", "area", "placed", "app" }));
            for (var i = 0; i < all.Length; i++)
            {
                Assert.That(all[i].IsDisposed, Is.True, "lifetime " + i + " must be disposed");
            }

            Assert.That(tree.Owners.Count, Is.Zero);
            Assert.That(tree.RehomeCount, Is.Zero, "a placed object is disposed with App, not re-homed");

            _s.Exit.Cancel();
            Assert.That(_log.Count, Is.EqualTo(7), "a second cancel of the exit token changes nothing");
            yield break;
        }

        // Guard: BeginSession step 2 creates a new tree and step 1 shuts the old one down.
        [UnityTest]
        public IEnumerator BeginSession_GivesAFreshTree_AndDisposesTheOldOne()
        {
            var probe = _s.NewProbe();
            var old = probe.GetLifetime();
            old.Record(_log, "old");
            var oldTree = _s.Tree;
            var sessions = PlayModeBootstrap.SessionCount;

            _s.Restart();

            Assert.That(_s.Tree, Is.Not.SameAs(oldTree));
            Assert.That(oldTree.App.IsDisposed, Is.True);
            Assert.That(old.IsDisposed, Is.True);
            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "old" }));
            Assert.That(Lifetime.App.IsDisposed, Is.False);
            Assert.That(Lifetime.App, Is.SameAs(_s.Tree.App));
            Assert.That(Lifetime.App.ChildCount, Is.Zero, "the new tree is empty");
            Assert.That(_s.Tree.Owners.Count, Is.Zero);
            Assert.That(_s.Tree.Scenes.Count, Is.Zero);
            Assert.That(PlayModeBootstrap.SessionCount, Is.EqualTo(sessions + 1));

            var fresh = probe.GetLifetime();
            Assert.That(fresh, Is.Not.SameAs(old), "a live object gets a new lifetime in the new tree");
            Assert.That(fresh.IsDisposed, Is.False);
            Assert.That(fresh.Tree, Is.SameAs(_s.Tree));
            yield break;
        }

        // Guard: BeginSession releases the old exit registration before it binds the new token.
        [UnityTest]
        public IEnumerator BeginSession_TheOldExitToken_NoLongerDisposesTheNewApp()
        {
            var oldExit = _s.Exit;
            _s.Restart();
            var probe = _s.NewProbe();
            var lifetime = probe.GetLifetime();

            oldExit.Cancel();

            Assert.That(Lifetime.App.IsDisposed, Is.False);
            Assert.That(lifetime.IsDisposed, Is.False);
            Assert.That(PlayModeBootstrap.IsExiting, Is.False, "the new session has its own exit token");
            _s.Exit.Cancel();
            Assert.That(Lifetime.App.IsDisposed, Is.True, "only the current token ends the current session");
            yield break;
        }

        // Guard: PlayModeBootstrap.BeginSession does not bind a token that is already cancelled.
        [UnityTest]
        public IEnumerator BeginSession_AnAlreadyCancelledToken_IsNotBound()
        {
            using (var stale = new CancellationTokenSource())
            {
                stale.Cancel();

                PlayModeBootstrap.BeginSession(stale.Token);
                _s.CaptureErrors();

                Assert.That(Lifetime.App.IsDisposed, Is.False, "a stale token must not dispose the new session's App");
                Assert.That(PlayModeBootstrap.IsExiting, Is.False);
            }

            yield break;
        }

        // Guard: BeginSession step 4 resets the error handler.
        [UnityTest]
        public IEnumerator BeginSession_ResetsTheErrorHandler()
        {
            LifetimeErrors.Handler = null;
            var defaultHandler = LifetimeErrors.Handler;
            LifetimeErrors.Handler = IgnoreErrors;
            Assert.That(LifetimeErrors.Handler, Is.Not.EqualTo(defaultHandler));

            PlayModeBootstrap.BeginSession(_s.Exit.Token);
            var afterReset = LifetimeErrors.Handler;
            _s.CaptureErrors();

            Assert.That(afterReset, Is.EqualTo(defaultHandler), "the previous session's handler must not receive this session's errors");
            yield break;
        }

        // Guard: PlayModeBootstrap.BeginSession unsubscribes before it subscribes, so two sessions never double the event.
        [UnityTest]
        public IEnumerator BeginSession_CalledTwice_SceneUnloadedIsHandledOnce()
        {
            _s.Restart();
            _s.Restart();
            var scene = _s.NewScene("Unloaded");
            var before = PlayModeBootstrap.SceneUnloadedCount;

            yield return BindingScenes.Unload(scene);

            Assert.That(PlayModeBootstrap.SceneUnloadedCount, Is.EqualTo(before + 1), "one unload must be handled exactly once");
        }

        [UnityTest]
        public IEnumerator Bootstrap_ResetsTheSessionAtSubsystemRegistration()
        {
            var method = typeof(PlayModeBootstrap).GetMethod("OnSubsystemRegistration", BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            var attribute = (RuntimeInitializeOnLoadMethodAttribute)Attribute.GetCustomAttribute(method, typeof(RuntimeInitializeOnLoadMethodAttribute));
            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute.loadType, Is.EqualTo(RuntimeInitializeLoadType.SubsystemRegistration));
            yield break;
        }

        private static void IgnoreErrors(Exception exception, in LifetimeErrorContext context)
        {
        }
    }
}
