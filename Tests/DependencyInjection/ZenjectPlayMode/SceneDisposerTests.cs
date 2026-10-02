using System.Collections;
using System.Text.RegularExpressions;
using Ecanakli.Janitor.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Zenject;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.DependencyInjection.Tests.PlayMode
{
    // The SceneContext disposer ends the scene lifetime FIRST among the scene container's disposables.
    // When the game skipped the one-line call it logs JANITOR104 once; when SceneLifetimes.DisposeAll ran first it stays silent.
    // The contexts are destroyed (not the scenes unloaded), which is exactly the Zenject path: MonoKernel.OnDestroy runs
    // DisposableManager.Dispose, which orders by priority and runs the highest first.
    [TestFixture]
    public sealed class SceneDisposerTests
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

        // Order

        // Guard: the disposer is bound with the highest priority, so every service disposable already sees its lifetime ended,
        // whatever its own priority (a high one, the default one and a low one here).
        [UnityTest]
        public IEnumerator Destroy_OfTheSceneContext_EndsTheSceneLifetimeBeforeAnyServiceDisposes()
        {
            var recorder = new DisposeRecorder();
            var scene = _s.NewScene("Order");
            var context = _s.NewSceneContext(scene, c =>
            {
                LifetimeInstaller.Install(c);
                c.BindInstance(recorder);
                c.BindInterfacesTo<HighPriorityDisposable>().AsSingle();
                c.BindDisposableExecutionOrder<HighPriorityDisposable>(1000);
                c.BindInterfacesTo<DefaultPriorityDisposable>().AsSingle();
                c.BindInterfacesTo<LowPriorityDisposable>().AsSingle();
                c.BindDisposableExecutionOrder<LowPriorityDisposable>(-5);
            });
            yield return _s.Frames(2);
            var sceneLifetime = SceneLifetimes.Get(scene);
            Assert.That(sceneLifetime.IsDisposed, Is.False);

            Object.Destroy(context.gameObject);
            yield return _s.WaitUntil(() => recorder.Names.Count == 3, "the three service disposables to run");

            Assert.That(recorder.Names, Is.EqualTo(new[] { "high", "default", "low" }), "Zenject runs the highest priority first");
            Assert.That(recorder.LifetimeEnded, Is.EqualTo(new[] { true, true, true }), "the scene lifetime was already gone for each of them");
            Assert.That(sceneLifetime.IsDisposed, Is.True);
        }

        // Guard: one failing service Dispose cannot stop the scene lifetime from ending, because the disposer ran before it.
        // Zenject itself skips the disposables after the thrower; that is the control that shows why the order matters.
        [UnityTest]
        public IEnumerator Destroy_WithAThrowingServiceDispose_StillEndsTheSceneLifetime()
        {
            var recorder = new DisposeRecorder();
            var scene = _s.NewScene("Throwing");
            var context = _s.NewSceneContext(scene, c =>
            {
                LifetimeInstaller.Install(c);
                c.BindInstance(recorder);
                c.BindInterfacesTo<ThrowingDisposable>().AsSingle();
                c.BindDisposableExecutionOrder<ThrowingDisposable>(100);
                c.BindInterfacesTo<AfterThrowDisposable>().AsSingle();
                c.BindDisposableExecutionOrder<AfterThrowDisposable>(50);
            });
            yield return _s.Frames(2);
            var sceneLifetime = SceneLifetimes.Get(scene);
            var child = sceneLifetime.CreateChild("service work");
            var log = new CallLog();
            child.Record(log, "work stopped");
            // Unity logs the innermost exception (Zenject's wrapper only appears as "Rethrow as ZenjectException" in the stack).
            LogAssert.Expect(LogType.Exception, new Regex("the service failed to dispose"));

            Object.Destroy(context.gameObject);
            yield return _s.WaitUntil(() => context == null, "the SceneContext to be destroyed");

            Assert.That(sceneLifetime.IsDisposed, Is.True, "the scene lifetime ended although a service disposable threw");
            Assert.That(child.IsDisposed, Is.True);
            Assert.That(log.ToArray(), Is.EqualTo(new[] { "work stopped" }));
            Assert.That(recorder.Names, Is.EqualTo(new[] { "throwing" }), "Zenject skipped the disposable after the thrower");
            Assert.That(recorder.LifetimeEnded[0], Is.True, "the thrower already saw its lifetime ended");
        }

        // JANITOR104

        // Guard: SceneContextLifetimeDisposer finds the scene lifetime alive, logs the core's hint text once, then disposes it.
        [UnityTest]
        public IEnumerator Destroy_OfTheSceneContext_WhenTheOneLineCallWasSkipped_LogsJanitor104OnceAndTheUnloadAddsNothing()
        {
            var scene = _s.NewScene("Skipped");
            var context = _s.NewSceneContext(scene, c => LifetimeInstaller.Install(c));
            yield return _s.Frames(2);
            var sceneLifetime = SceneLifetimes.Get(scene);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR104"));

            Object.Destroy(context.gameObject);
            yield return _s.WaitUntil(() => sceneLifetime.IsDisposed, "the disposer to end the scene lifetime");

            Assert.That(_s.Warnings.Count("JANITOR104"), Is.EqualTo(1));
            var text = _s.Warnings.TextOf("JANITOR104");
            Assert.That(text, Does.Contain("was unloaded without SceneLifetimes.Dispose(scene) or DisposeAll()"), "the same text as the core's fallback");
            Assert.That(text, Does.Contain("'" + scene.name + "'"), "it names the scene");
            Assert.That(text, Does.Contain("Troubleshooting#janitor104"));

            yield return _s.Unload(scene);

            Assert.That(_s.Warnings.Count("JANITOR104"), Is.EqualTo(1), "the lifetime was already disposed, so the core's sceneUnloaded fallback stays quiet");
        }

        // Guard: with the one-line call made first, the disposer finds nothing alive and says nothing.
        [UnityTest]
        public IEnumerator Destroy_OfTheSceneContext_AfterDisposeAll_LogsNoHint()
        {
            var scene = _s.NewScene("Called");
            var context = _s.NewSceneContext(scene, c => LifetimeInstaller.Install(c));
            yield return _s.Frames(2);
            var sceneLifetime = SceneLifetimes.Get(scene);

            SceneLifetimes.DisposeAll();
            Assert.That(sceneLifetime.IsDisposed, Is.True);
            Object.Destroy(context.gameObject);
            yield return _s.WaitUntil(() => context == null, "the SceneContext to be destroyed");
            yield return _s.Unload(scene);

            Assert.That(_s.Warnings.Count("JANITOR104"), Is.Zero);
        }

        // Guard: the one-line call for just this scene is enough as well.
        [UnityTest]
        public IEnumerator Destroy_OfTheSceneContext_AfterDisposeOfThatScene_LogsNoHint()
        {
            var scene = _s.NewScene("CalledForOne");
            var context = _s.NewSceneContext(scene, c => LifetimeInstaller.Install(c));
            yield return _s.Frames(2);

            SceneLifetimes.Dispose(scene);
            Object.Destroy(context.gameObject);
            yield return _s.WaitUntil(() => context == null, "the SceneContext to be destroyed");

            Assert.That(_s.Warnings.Count("JANITOR104"), Is.Zero);
        }

        // Guard: a disposer of one scene never touches the lifetime of another scene.
        [UnityTest]
        public IEnumerator Destroy_OfOneSceneContext_LeavesTheOtherScenesLifetimeAlone()
        {
            var first = _s.NewScene("A");
            var second = _s.NewScene("B");
            var firstContext = _s.NewSceneContext(first, c => LifetimeInstaller.Install(c));
            _s.NewSceneContext(second, c => LifetimeInstaller.Install(c));
            yield return _s.Frames(2);
            var firstLifetime = SceneLifetimes.Get(first);
            var secondLifetime = SceneLifetimes.Get(second);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR104"));

            Object.Destroy(firstContext.gameObject);
            yield return _s.WaitUntil(() => firstLifetime.IsDisposed, "the first scene lifetime to end");

            Assert.That(secondLifetime.IsDisposed, Is.False);
            Assert.That(Lifetime.App.IsDisposed, Is.False);
        }
    }
}
