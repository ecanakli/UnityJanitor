using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Triggers;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests
{
    // The state a finished Play Mode session leaves in Edit Mode, reached through the session's own end path.
    [TestFixture]
    public sealed class SessionEndEditModeTests
    {
        private LifetimeTree _previousDefault;
        private CancellationTokenSource _exit;
        private GameObject _go;
        private ActiveLifetimeTrigger _behaviour;

        [SetUp]
        public void SetUp()
        {
            _previousDefault = LifetimeTree.Default;
            _exit = new CancellationTokenSource();
            PlayModeBootstrap.BeginSession(_exit.Token);
            _go = new GameObject("SessionEnd");
            _behaviour = _go.AddComponent<ActiveLifetimeTrigger>();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (_go != null)
                {
                    Object.DestroyImmediate(_go);
                }
            }
            finally
            {
                PlayModeBootstrap.EndSession();
                LifetimeTree.Default = _previousDefault;
                _exit.Dispose();
            }
        }

        [Test]
        public void ReturningToEditMode_AfterTheExitToken_LeavesNoDefaultTree()
        {
            var tree = LifetimeTree.Default;
            Assert.That(tree, Is.Not.Null);

            EndTheSession();

            Assert.That(LifetimeTree.Default, Is.Null);
            Assert.That(tree.App.IsDisposed, Is.True, "the session's tree ended with it");
            Assert.That(PlayModeBootstrap.IsExiting, Is.False, "no session is left to be ending");
            AssertHandlerCounts(0);
        }

        [Test]
        public void Sessions_SubscribeEachHandlerOnce_AndTheEndRemovesThem()
        {
            using (var next = new CancellationTokenSource())
            {
                PlayModeBootstrap.BeginSession(next.Token);
                PlayModeBootstrap.BeginSession(next.Token);

                AssertHandlerCounts(1);

                EndTheSession();

                AssertHandlerCounts(0);
            }
        }

        [Test]
        public void ReturningToEditMode_WithoutAnExitBinding_DisposesTheTreeAndLeavesNoDefault()
        {
            using (var stale = new CancellationTokenSource())
            {
                stale.Cancel();
                PlayModeBootstrap.BeginSession(stale.Token);
            }

            var tree = LifetimeTree.Default;
            Assert.That(tree.App.IsDisposed, Is.False, "no signal reached the session yet");

            PlayModeBootstrap.HandlePlayModeStateChanged(PlayModeStateChange.EnteredEditMode);

            Assert.That(tree.App.IsDisposed, Is.True);
            Assert.That(LifetimeTree.Default, Is.Null);
        }

        [Test]
        public void App_AfterTheSessionEnded_ThrowsThePlayModeOnlyException()
        {
            var sessions = PlayModeBootstrap.SessionCount;
            EndTheSession();

            AssertPlayModeOnly(() => { _ = Lifetime.App; });

            Assert.That(PlayModeBootstrap.SessionCount, Is.EqualTo(sessions), "Edit Mode never starts a session");
            Assert.That(LifetimeTree.Default, Is.Null);
        }

        [Test]
        public void TheEntryPoints_AfterTheSessionEnded_ThrowAndTouchNothing()
        {
            var category = LifetimeTree.Default.App.CreateChild("category");
            EndTheSession();
            var plain = new GameObject("Plain");
            try
            {
                AssertPlayModeOnly(() => _behaviour.GetLifetime());
                AssertPlayModeOnly(() => _behaviour.GetLifetime(category));
                AssertPlayModeOnly(() => plain.GetLifetime());
                AssertPlayModeOnly(() => plain.transform.GetActiveLifetime());
                AssertPlayModeOnly(() => SceneLifetimes.Get(default(Scene)));
                AssertPlayModeOnly(() => SceneLifetimes.Dispose(default(Scene)));
                AssertPlayModeOnly(() => SceneLifetimes.DisposeAll());
                AssertPlayModeOnly(() => _behaviour.Run(ct => UniTask.CompletedTask));
                AssertPlayModeOnly(() => _behaviour.OnCancel(() => { }));

                Assert.That(plain.TryGetComponent<AsyncDestroyTrigger>(out _), Is.False, "a rejected call must not touch the object");
                Assert.That(plain.TryGetComponent<ActiveLifetimeTrigger>(out _), Is.False, "a rejected call must not add the hidden trigger");
            }
            finally
            {
                Object.DestroyImmediate(plain);
            }
        }

        // Teardown code that runs after the exit token fired must keep getting disposed lifetimes, not exceptions.
        [Test]
        public void AfterTheExitToken_UntilEditModeIsEntered_TheApiHandsOutDisposedLifetimes()
        {
            var tree = LifetimeTree.Default;

            _exit.Cancel();

            Assert.That(LifetimeTree.Default, Is.SameAs(tree));
            Assert.That(Lifetime.App.IsDisposed, Is.True);
        }

        [Test]
        public void PlayModeStatesOtherThanEnteredEditMode_DoNotEndTheSession()
        {
            var tree = LifetimeTree.Default;

            PlayModeBootstrap.HandlePlayModeStateChanged(PlayModeStateChange.ExitingEditMode);
            PlayModeBootstrap.HandlePlayModeStateChanged(PlayModeStateChange.EnteredPlayMode);
            PlayModeBootstrap.HandlePlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);

            Assert.That(LifetimeTree.Default, Is.SameAs(tree));
            Assert.That(tree.App.IsDisposed, Is.False);
        }

        [Test]
        public void ASessionAfterTheEnd_StartsFresh()
        {
            var first = LifetimeTree.Default;
            EndTheSession();
            AssertHandlerCounts(0);
            using (var next = new CancellationTokenSource())
            {
                PlayModeBootstrap.BeginSession(next.Token);
                AssertHandlerCounts(1);

                var second = LifetimeTree.Default;
                Assert.That(second, Is.Not.SameAs(first));
                Assert.That(Lifetime.App, Is.SameAs(second.App));
                Assert.That(second.App.IsDisposed, Is.False);
                Assert.That(second.App.ChildCount, Is.Zero);
                Assert.That(PlayModeBootstrap.IsExiting, Is.False);

                PlayModeBootstrap.EndSession();
            }
        }

        private void EndTheSession()
        {
            _exit.Cancel();
            PlayModeBootstrap.HandlePlayModeStateChanged(PlayModeStateChange.EnteredEditMode);
        }

        private static void AssertHandlerCounts(int expected)
        {
            Assert.That(PlayModeBootstrap.SceneUnloadedHandlerCount, Is.EqualTo(expected), "sceneUnloaded handlers");
            Assert.That(PlayModeBootstrap.QuittingHandlerCount, Is.EqualTo(expected), "Application.quitting handlers");
            Assert.That(PlayModeBootstrap.PlayModeStateHandlerCount, Is.EqualTo(expected), "playModeStateChanged handlers");
        }

        private static void AssertPlayModeOnly(TestDelegate call)
        {
            var exception = Assert.Throws<InvalidOperationException>(call);

            Assert.That(exception.Message, Does.Contain("Play Mode"));
        }
    }
}
