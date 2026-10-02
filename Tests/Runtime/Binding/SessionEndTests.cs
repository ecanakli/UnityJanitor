using System.Collections;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.Binding
{
    // How a play session ends: the exit token, Application.quitting and the editor's return to Edit Mode run the same shutdown.
    [TestFixture]
    public sealed class SessionEndTests
    {
        private static readonly string[] AllLifetimes = { "component", "scene", "area", "app" };

        private static readonly string[][] Orders =
        {
            new[] { "exit", "quit" },
            new[] { "quit", "exit" },
#if UNITY_EDITOR
            new[] { "exit", "quit", "edit" },
            new[] { "quit", "edit", "exit" },
            new[] { "edit", "exit", "quit" },
#endif
        };

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
        public IEnumerator ExitToken_Alone_EndsTheSessionOnce()
        {
            var tree = Populate();

            _s.Exit.Cancel();

            AssertEndedOnce(tree);
            Assert.That(PlayModeBootstrap.IsExiting, Is.True);
            yield break;
        }

        [UnityTest]
        public IEnumerator Quitting_WithoutAnExitBinding_EndsTheSessionOnce()
        {
            BeginSessionWithAStaleToken();
            var tree = Populate();
            Assert.That(PlayModeBootstrap.IsExiting, Is.False);

            PlayModeBootstrap.HandleQuitting();

            AssertEndedOnce(tree);
            Assert.That(PlayModeBootstrap.IsExiting, Is.True, "the quit signal marks the session as ending");
            PlayModeBootstrap.HandleQuitting();
            Assert.That(_log.Count, Is.EqualTo(AllLifetimes.Length), "a second quit signal changes nothing");
            yield break;
        }

        [UnityTest]
        public IEnumerator EveryOrderOfTheSignals_EndsTheSessionOnce()
        {
            for (var i = 0; i < Orders.Length; i++)
            {
                _s.Restart();
                _log.Clear();
                var tree = Populate();

                for (var j = 0; j < Orders[i].Length; j++)
                {
                    Fire(Orders[i][j]);
                }

                AssertEndedOnce(tree, string.Join(" then ", Orders[i]));
            }

            yield break;
        }

        // Guard: the previous session's tree is tracked, so a start after the default was cleared still disposes it.
        [UnityTest]
        public IEnumerator BeginSession_AfterTheDefaultWasCleared_StillDisposesThePreviousSessionsTree()
        {
            var old = Populate();
            LifetimeTree.Default = null;

            _s.Restart();

            AssertEndedOnce(old);
            Assert.That(_s.Tree, Is.Not.SameAs(old));
            Assert.That(_s.Tree.App.IsDisposed, Is.False);
            yield break;
        }

        [UnityTest]
        public IEnumerator Bootstrap_AtSubsystemRegistration_BeginsASessionWithTheLiveExitToken()
        {
            var method = typeof(PlayModeBootstrap).GetMethod("OnSubsystemRegistration", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            var before = Populate();
            var sessions = PlayModeBootstrap.SessionCount;

            method.Invoke(null, null);
            _s.CaptureErrors();

            AssertEndedOnce(before);
            Assert.That(_s.Tree, Is.Not.SameAs(before));
            Assert.That(_s.Tree.App.IsDisposed, Is.False);
            Assert.That(PlayModeBootstrap.IsExiting, Is.False, "the engine's exit token is not cancelled while Play Mode runs");
            Assert.That(PlayModeBootstrap.SessionCount, Is.EqualTo(sessions + 1));
            yield break;
        }

#if UNITY_EDITOR
        // The counts come from the bootstrap's own adds and removes, not from the engine's private event fields.
        [UnityTest]
        public IEnumerator Sessions_SubscribeEachHandlerOnce()
        {
            _s.Restart();
            _s.Restart();
            _s.Restart();

            AssertHandlerCounts(1);
            yield break;
        }

        [UnityTest]
        public IEnumerator ReturnToEditMode_WithoutAnExitBinding_EndsTheSessionOnceAndLeavesNoDefaultTree()
        {
            BeginSessionWithAStaleToken();
            var tree = Populate();

            PlayModeBootstrap.HandlePlayModeStateChanged(UnityEditor.PlayModeStateChange.EnteredEditMode);

            AssertEndedOnce(tree);
            Assert.That(LifetimeTree.Default, Is.Null);
            AssertHandlerCounts(0);
            PlayModeBootstrap.HandlePlayModeStateChanged(UnityEditor.PlayModeStateChange.EnteredEditMode);
            Assert.That(_log.Count, Is.EqualTo(AllLifetimes.Length), "a second return to Edit Mode changes nothing");
            AssertHandlerCounts(0);
            yield break;
        }

        [UnityTest]
        public IEnumerator TwoSessionsInARow_WithDomainReloadDisabled_TheSecondStartsFresh()
        {
            var first = Populate();
            var sessions = PlayModeBootstrap.SessionCount;

            // The end of session one, in the order the editor reaches it.
            _s.Exit.Cancel();
            PlayModeBootstrap.HandleQuitting();
            PlayModeBootstrap.HandlePlayModeStateChanged(UnityEditor.PlayModeStateChange.EnteredEditMode);

            AssertEndedOnce(first);
            Assert.That(LifetimeTree.Default, Is.Null, "Edit Mode has no default tree");
            AssertHandlerCounts(0);

            _s.Restart();

            var second = _s.Tree;
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(Lifetime.App, Is.SameAs(second.App));
            Assert.That(second.App.IsDisposed, Is.False);
            Assert.That(second.App.ChildCount, Is.Zero, "nothing of session one is reachable");
            Assert.That(second.Owners.Count, Is.Zero);
            Assert.That(second.Scenes.Count, Is.Zero);
            Assert.That(PlayModeBootstrap.SessionCount, Is.EqualTo(sessions + 1));
            Assert.That(PlayModeBootstrap.IsExiting, Is.False, "the new session is not ending");
            Assert.That(_log.Count, Is.EqualTo(AllLifetimes.Length), "nothing of session one ran again");
            AssertHandlerCounts(1);

            var scene = _s.NewScene("SecondSession");
            var unloaded = PlayModeBootstrap.SceneUnloadedCount;
            yield return BindingScenes.Unload(scene);
            Assert.That(PlayModeBootstrap.SceneUnloadedCount, Is.EqualTo(unloaded + 1), "one unload is handled once");

            PlayModeBootstrap.HandleQuitting();
            Assert.That(second.App.IsDisposed, Is.True, "session two ends through its own signals");
        }

        private static void AssertHandlerCounts(int expected)
        {
            Assert.That(PlayModeBootstrap.SceneUnloadedHandlerCount, Is.EqualTo(expected), "sceneUnloaded handlers");
            Assert.That(PlayModeBootstrap.QuittingHandlerCount, Is.EqualTo(expected), "Application.quitting handlers");
            Assert.That(PlayModeBootstrap.PlayModeStateHandlerCount, Is.EqualTo(expected), "playModeStateChanged handlers");
        }
#endif

        // An object lifetime, the scene lifetime, an App area and App itself; each logs once when it ends.
        private LifetimeTree Populate()
        {
            var probe = _s.NewProbe();
            var tree = _s.Tree;
            probe.GetLifetime().Record(_log, "component");
            SceneLifetimes.Get(probe.gameObject.scene).Record(_log, "scene");
            tree.App.CreateChild("area").Record(_log, "area");
            tree.App.Record(_log, "app");
            return tree;
        }

        private void AssertEndedOnce(LifetimeTree tree, string order = "")
        {
            Assert.That(tree.App.IsDisposed, Is.True, order);
            Assert.That(_log.ToArray(), Is.EquivalentTo(AllLifetimes), "every lifetime ends, each exactly once: " + order);
            Assert.That(tree.Owners.Count, Is.Zero, order);
        }

        // A session whose exit token was already cancelled when it began binds nothing; only the other signals can end it.
        private void BeginSessionWithAStaleToken()
        {
            using (var stale = new CancellationTokenSource())
            {
                stale.Cancel();
                PlayModeBootstrap.BeginSession(stale.Token);
            }

            _s.CaptureErrors();
        }

        private void Fire(string signal)
        {
            switch (signal)
            {
                case "exit":
                    _s.Exit.Cancel();
                    break;
                case "quit":
                    PlayModeBootstrap.HandleQuitting();
                    break;
#if UNITY_EDITOR
                case "edit":
                    PlayModeBootstrap.HandlePlayModeStateChanged(UnityEditor.PlayModeStateChange.EnteredEditMode);
                    break;
#endif
                default:
                    Assert.Fail("Unknown signal " + signal);
                    break;
            }
        }
    }
}
