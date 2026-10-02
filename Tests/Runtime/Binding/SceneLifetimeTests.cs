using System;
using System.Collections;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using Ecanakli.Janitor.Tests.Probes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.Binding
{
    // Scene lifetimes, SceneLifetimes and the fallback.
    [TestFixture]
    public sealed class SceneLifetimeTests
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

        // The recommended path

        // Guard: SceneLifetimes.DisposeAll runs before the loader, and Mark signals every token before any item is terminated.
        [UnityTest]
        public IEnumerator DisposeAll_ThenASingleLoad_EndsTasksAndSubscriptionsBeforeAnyOnDestroyRuns()
        {
            var probe = _s.NewProbe();
            var lifetime = probe.GetLifetime();
            var evt = new OwnedEvent("scene");
            var hits = 0;
            Action handler = () => hits++;
            evt.Subscribe(handler, probe);
            var source = new BindingEventSource();
            probe.Subscribe(h => source.Zero += h, h => source.Zero -= h, handler);
            var disposable = new DisposeProbe();
            disposable.AddTo(probe);
            var taskToken = default(System.Threading.CancellationToken);
            var stage = 0;
            probe.Run(async ct =>
            {
                taskToken = ct;
                stage = 1;
                await UniTask.Delay(TimeSpan.FromSeconds(60), cancellationToken: ct);
                stage = 2;
            });
            Assert.That(stage, Is.EqualTo(1));
            Assert.That(taskToken.IsCancellationRequested, Is.False);
            Assert.That(evt.SubscriberCount, Is.EqualTo(1));
            var destroyed = false;
            var deadAtOnDestroy = false;
            BindingProbe.DestroyHook = p =>
            {
                destroyed = true;
                deadAtOnDestroy = taskToken.IsCancellationRequested && evt.SubscriberCount == 0 && source.ZeroListeners == 0 && disposable.DisposeCount == 1;
            };

            SceneLifetimes.DisposeAll();

            Assert.That(destroyed, Is.False, "nothing is destroyed yet: DisposeAll runs before the loader");
            Assert.That(lifetime.IsDisposed, Is.True);
            Assert.That(taskToken.IsCancellationRequested, Is.True, "the task's token is cancelled");
            Assert.That(evt.SubscriberCount, Is.Zero, "the OwnedEvent subscription is gone");
            Assert.That(source.ZeroListeners, Is.Zero, "the paired subscription is removed");
            Assert.That(source.Removes, Is.EqualTo(1));
            Assert.That(disposable.DisposeCount, Is.EqualTo(1));

            yield return BindingScenes.LoadEmptySingle();

            Assert.That(destroyed, Is.True, "the Single load destroyed the object");
            Assert.That(deadAtOnDestroy, Is.True, "everything was already dead when OnDestroy ran");
            Assert.That(stage, Is.EqualTo(1), "the code after the token-aware await never ran");
            Assert.That(_s.Logs.Count("JANITOR104"), Is.Zero, "the one-liner was called, so the fallback has nothing to report");
            Assert.That(hits, Is.Zero);
        }

        // Guard: PlayModeBootstrap.HandleSceneUnloaded disposes a scene lifetime that is still alive, with the hint, once.
        [UnityTest]
        public IEnumerator SceneUnloaded_WithoutDispose_DisposesTheSceneLifetimeLateWithJanitor104()
        {
            var scene = _s.NewScene("Skipped");
            var probe = _s.NewProbeIn(scene);
            var sceneLifetime = SceneLifetimes.Get(scene);
            var lifetime = probe.GetLifetime();
            lifetime.Record(_log, "component");
            sceneLifetime.Record(_log, "scene");
            var stateAtOnDestroy = LifetimeState.Disposed;
            BindingProbe.DestroyHook = p => stateAtOnDestroy = sceneLifetime.State;
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR104"));
            var unloadedBefore = PlayModeBootstrap.SceneUnloadedCount;

            yield return BindingScenes.Unload(scene);

            Assert.That(stateAtOnDestroy, Is.EqualTo(LifetimeState.Active), "without the one-liner the scene lifetime is still alive when objects are destroyed");
            Assert.That(lifetime.IsDisposed, Is.True, "the object lifetime was disposed by its own destroy");
            Assert.That(sceneLifetime.IsDisposed, Is.True, "the sceneUnloaded fallback disposed the scene lifetime late");
            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "component", "scene" }));
            Assert.That(_s.Logs.Count("JANITOR104"), Is.EqualTo(1));
            Assert.That(PlayModeBootstrap.SceneUnloadedCount, Is.EqualTo(unloadedBefore + 1));
            Assert.That(_s.Tree.Scenes.Count, Is.Zero, "the index entry is dropped at sceneUnloaded");

            PlayModeBootstrap.HandleSceneUnloaded(scene);
            Assert.That(_s.Logs.Count("JANITOR104"), Is.EqualTo(1), "the hint appears once per scene");
        }

        // Additive scenes, Cancel, Get

        [UnityTest]
        public IEnumerator Dispose_OnOneAdditiveScene_TouchesOnlyThatScene()
        {
            var first = _s.NewScene("First");
            var second = _s.NewScene("Second");
            var firstProbe = _s.NewProbeIn(first);
            var secondProbe = _s.NewProbeIn(second);
            var activeProbe = _s.NewProbe();
            var firstLifetime = firstProbe.GetLifetime();
            var secondLifetime = secondProbe.GetLifetime();
            var activeLifetime = activeProbe.GetLifetime();
            firstLifetime.Record(_log, "first");
            secondLifetime.Record(_log, "second");
            activeLifetime.Record(_log, "active");

            SceneLifetimes.Dispose(first);

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "first" }));
            Assert.That(firstLifetime.IsDisposed, Is.True);
            Assert.That(SceneLifetimes.Get(first).IsDisposed, Is.True, "the disposed record stays until the scene is unloaded");
            Assert.That(secondLifetime.IsDisposed, Is.False);
            Assert.That(SceneLifetimes.Get(second).IsDisposed, Is.False);
            Assert.That(activeLifetime.IsDisposed, Is.False);
            Assert.That(Lifetime.App.IsDisposed, Is.False);
            yield break;
        }

        [UnityTest]
        public IEnumerator Get_ThenCancel_KeepsTheSceneUsable()
        {
            var scene = _s.NewScene("Cancelled");
            var probe = _s.NewProbeIn(scene);
            var sceneLifetime = SceneLifetimes.Get(scene);
            var lifetime = probe.GetLifetime();
            lifetime.Record(_log, "component");
            sceneLifetime.Record(_log, "scene");
            var generation = sceneLifetime.Generation;

            sceneLifetime.Cancel();

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "component", "scene" }), "descendants first");
            Assert.That(sceneLifetime.IsDisposed, Is.False);
            Assert.That(sceneLifetime.Generation, Is.EqualTo(generation + 1));
            Assert.That(SceneLifetimes.Get(scene), Is.SameAs(sceneLifetime));
            Assert.That(lifetime.IsDisposed, Is.False);
            Assert.That(lifetime.Record(_log, "again").IsActive, Is.True);

            var late = _s.NewProbeIn(scene);
            var lateLifetime = late.GetLifetime();
            Assert.That(lateLifetime.IsDisposed, Is.False, "a new object in the cancelled scene still gets a live lifetime");
            Assert.That(lateLifetime.Parent, Is.SameAs(sceneLifetime));

            SceneLifetimes.Dispose(scene);
            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "component", "scene", "again" }), "a later Dispose still works");
            yield break;
        }

        [UnityTest]
        public IEnumerator Get_ForACreatedScene_IsASceneLifetimeUnderApp()
        {
            var scene = _s.NewScene("Named");

            var lifetime = SceneLifetimes.Get(scene);

            Assert.That(lifetime, Is.Not.SameAs(Lifetime.App), "a scene the manager lists is not DontDestroyOnLoad");
            Assert.That(lifetime.Kind, Is.EqualTo(LifetimeKind.Scene));
            Assert.That(lifetime.Name, Is.EqualTo(scene.name));
            Assert.That(lifetime.Parent, Is.SameAs(Lifetime.App));
            Assert.That(SceneLifetimes.Get(scene), Is.SameAs(lifetime));
            Assert.That(SceneLifetimes.Get(SceneManager.GetActiveScene()), Is.Not.SameAs(lifetime), "one lifetime per scene");
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR107"));
            lifetime.Dispose();
            Assert.That(lifetime.IsDisposed, Is.False, "a scene lifetime is disposed by SceneLifetimes, not by the game");
            yield break;
        }

        [UnityTest]
        public IEnumerator Dispose_OnASceneWithoutALifetime_CreatesItAndDisposesIt_AndRepeatedCallsAreNoOps()
        {
            var scene = _s.NewScene("Empty");
            Assert.That(_s.Tree.Scenes.Count, Is.Zero);

            SceneLifetimes.Dispose(scene);

            Assert.That(_s.Tree.Scenes.Count, Is.EqualTo(1));
            var lifetime = SceneLifetimes.Get(scene);
            Assert.That(lifetime.IsDisposed, Is.True, "disposal is terminal, so the lifetime is created disposed");
            Assert.That(() =>
            {
                SceneLifetimes.Dispose(scene);
                SceneLifetimes.DisposeAll();
                SceneLifetimes.DisposeAll();
            }, Throws.Nothing);
            Assert.That(SceneLifetimes.Get(scene), Is.SameAs(lifetime));
            yield break;
        }

        [UnityTest]
        public IEnumerator DisposeAll_DisposesLoadedScenesLastLoadedFirst()
        {
            var first = _s.NewScene("A");
            var second = _s.NewScene("B");
            SceneLifetimes.Get(first).Record(_log, "A");
            SceneLifetimes.Get(second).Record(_log, "B");
            SceneLifetimes.Get(SceneManager.GetActiveScene()).Record(_log, "Active");

            SceneLifetimes.DisposeAll();

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "B", "A", "Active" }), "reverse load order, like ZenjectSceneLoader");
            yield break;
        }

        // Guard: DisposeAll skips a scene that is !isLoaded, so a scene held for activation is not disposed early.
        [UnityTest]
        public IEnumerator DisposeAll_SkipsASceneThatIsStillLoading()
        {
            var operation = BindingScenes.BeginAwakeAdditive();
            Assert.That(operation, Is.Not.Null);
            operation.allowSceneActivation = false;
            var observable = false;
            var hadLifetimeWhileLoading = false;
            try
            {
                var deadline = Time.realtimeSinceStartup + BindingScenes.DeadlineSeconds;
                while (operation.progress < 0.9f && !operation.isDone)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "the held load never reached 90 percent");
                    yield return null;
                }

                var loading = SceneManager.GetSceneByName(BindingScenes.AwakeSceneName);
                observable = loading.IsValid() && !loading.isLoaded;
                if (observable)
                {
                    SceneLifetimes.DisposeAll();
                    hadLifetimeWhileLoading = SceneBinding.Find(_s.Tree, loading) != null;
                }
            }
            finally
            {
                operation.allowSceneActivation = true;
            }

            yield return BindingScenes.Wait(operation);
            var loaded = SceneManager.GetSceneByName(BindingScenes.AwakeSceneName);
            _s.TrackScene(loaded);
            if (!observable)
            {
                Assert.Ignore("The engine does not expose a scene that is held before activation as valid and not loaded.");
            }

            Assert.That(hadLifetimeWhileLoading, Is.False, "DisposeAll must not create or dispose a lifetime for a scene that is still loading");
            Assert.That(SceneLifetimes.Get(loaded).IsDisposed, Is.False, "the scene that was held is usable once it is loaded");
        }

        // DontDestroyOnLoad, moved objects, late reparent

        [UnityTest]
        public IEnumerator DontDestroyOnLoad_ObjectsSurviveDisposeAllAndDispose()
        {
            var go = _s.NewObject();
            Object.DontDestroyOnLoad(go);
            var probe = go.AddComponent<BindingProbe>();
            var lifetime = probe.GetLifetime();
            lifetime.Record(_log, "ddol");
            Assert.That(SceneLifetimes.Get(go.scene), Is.SameAs(Lifetime.App), "the DontDestroyOnLoad scene maps to App");
            Assert.That(lifetime.Parent, Is.SameAs(Lifetime.App));

            SceneLifetimes.DisposeAll();
            SceneLifetimes.Dispose(go.scene);

            Assert.That(lifetime.IsDisposed, Is.False);
            Assert.That(_log.Count, Is.Zero);
            Assert.That(Lifetime.App.IsDisposed, Is.False);

            Object.Destroy(go);
            yield return null;

            Assert.That(lifetime.IsDisposed, Is.True, "its own destroy still disposes it");
            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "ddol" }));
        }

        // Guard: SceneBinding.PrePass.ReparentIfMoved runs before a scene's Dispose.
        [UnityTest]
        public IEnumerator LateDontDestroyOnLoad_IsReparentedToAppBeforeTheScenesDispose()
        {
            var scene = _s.NewScene("Origin");
            var go = _s.NewObjectIn(scene);
            var probe = go.AddComponent<BindingProbe>();
            var sceneLifetime = SceneLifetimes.Get(scene);
            var lifetime = probe.GetLifetime();
            lifetime.Record(_log, "item");
            Assert.That(lifetime.Parent, Is.SameAs(sceneLifetime));
            Object.DontDestroyOnLoad(go);

            SceneLifetimes.Dispose(scene);

            Assert.That(sceneLifetime.IsDisposed, Is.True);
            Assert.That(lifetime.Parent, Is.SameAs(Lifetime.App), "a default-parented lifetime follows the object to App");
            Assert.That(lifetime.IsDisposed, Is.False);
            Assert.That(_log.Count, Is.Zero);
            yield break;
        }

        [UnityTest]
        public IEnumerator LateDontDestroyOnLoad_IsReparentedToAppBeforeTheScenesCancel()
        {
            var scene = _s.NewScene("Origin");
            var go = _s.NewObjectIn(scene);
            var probe = go.AddComponent<BindingProbe>();
            var sceneLifetime = SceneLifetimes.Get(scene);
            var lifetime = probe.GetLifetime();
            lifetime.Record(_log, "item");
            Object.DontDestroyOnLoad(go);

            sceneLifetime.Cancel();

            Assert.That(lifetime.Parent, Is.SameAs(Lifetime.App));
            Assert.That(_log.Count, Is.Zero, "the scene's Cancel no longer reaches the object");
            Assert.That(sceneLifetime.IsDisposed, Is.False);
            yield break;
        }

        [UnityTest]
        public IEnumerator MoveGameObjectToScene_ReparentsAllThreeObjectKindsBeforeTheOldScenesDispose()
        {
            var origin = _s.NewScene("Origin");
            var target = _s.NewScene("Target");
            var go = _s.NewObjectIn(origin);
            var probe = go.AddComponent<BindingProbe>();
            var originLifetime = SceneLifetimes.Get(origin);
            var targetLifetime = SceneLifetimes.Get(target);
            var component = probe.GetLifetime();
            var gameObjectLifetime = go.GetLifetime();
            var active = probe.GetActiveLifetime();
            component.Record(_log, "component");
            gameObjectLifetime.Record(_log, "gameObject");
            active.Record(_log, "active");
            SceneManager.MoveGameObjectToScene(go, target);

            SceneLifetimes.Dispose(origin);

            Assert.That(originLifetime.IsDisposed, Is.True);
            Assert.That(_log.Count, Is.Zero, "the moved object is out of the old scene's reach");
            Assert.That(component.Parent, Is.SameAs(targetLifetime));
            Assert.That(gameObjectLifetime.Parent, Is.SameAs(targetLifetime));
            Assert.That(active.Parent, Is.SameAs(targetLifetime));
            Assert.That(component.IsDisposed || gameObjectLifetime.IsDisposed || active.IsDisposed, Is.False);

            SceneLifetimes.Dispose(target);

            Assert.That(_log.ToArray(), Is.EquivalentTo(new[] { "component", "gameObject", "active" }), "the new scene owns them now");
            Assert.That(component.IsDisposed && gameObjectLifetime.IsDisposed && active.IsDisposed, Is.True);
            yield break;
        }

        [UnityTest]
        public IEnumerator MoveGameObjectToScene_ReparentsBeforeTheOldScenesCancel_AndLeavesTheObjectAlone()
        {
            var origin = _s.NewScene("Origin");
            var target = _s.NewScene("Target");
            var go = _s.NewObjectIn(origin);
            var probe = go.AddComponent<BindingProbe>();
            var originLifetime = SceneLifetimes.Get(origin);
            var targetLifetime = SceneLifetimes.Get(target);
            var lifetime = probe.GetLifetime();
            lifetime.Record(_log, "item");
            SceneManager.MoveGameObjectToScene(go, target);

            originLifetime.Cancel();

            Assert.That(lifetime.Parent, Is.SameAs(targetLifetime));
            Assert.That(_log.Count, Is.Zero);
            Assert.That(originLifetime.IsDisposed, Is.False);

            targetLifetime.Cancel();
            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "item" }), "the target scene's Cancel reaches it");
            yield break;
        }

        // JANITOR109

        [UnityTest]
        public IEnumerator Register_OnADisposedSceneLifetime_RaisesJanitor109AndTerminatesTheItem()
        {
            var scene = _s.NewScene("Disposed");
            var sceneLifetime = SceneLifetimes.Get(scene);
            SceneLifetimes.Dispose(scene);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR109"));
            var ran = 0;

            var registration = sceneLifetime.OnCancel(() => ran++);

            Assert.That(ran, Is.EqualTo(1), "the item is terminated at once");
            Assert.That(registration.IsActive, Is.False);
            Assert.That(_s.Logs.Count("JANITOR109"), Is.EqualTo(1));
            Assert.That(SceneLifetimes.Get(scene), Is.SameAs(sceneLifetime), "disposal is terminal: Get returns the disposed record");
            yield break;
        }

        [UnityTest]
        public IEnumerator GetLifetime_OnAnObjectOfADisposedScene_ReturnsTheSentinelWithJanitor109()
        {
            var scene = _s.NewScene("Disposed");
            SceneLifetimes.Dispose(scene);
            var probe = _s.NewProbeIn(scene);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR109"));

            var lifetime = probe.GetLifetime();

            Assert.That(lifetime, Is.SameAs(_s.Tree.Sentinel));
            Assert.That(lifetime.IsDisposed, Is.True);
            Assert.That(_s.Tree.Owners.Count, Is.Zero, "nothing is created in a scene that is going away");
            Assert.That(_s.Logs.Count("JANITOR109"), Is.EqualTo(1));
            yield break;
        }

        [UnityTest]
        public IEnumerator Register_OnADisposedSceneLifetimeAfterTheUnload_IsSilent()
        {
            var scene = _s.NewScene("Gone");
            var sceneLifetime = SceneLifetimes.Get(scene);
            SceneLifetimes.Dispose(scene);

            yield return BindingScenes.Unload(scene);
            var ran = 0;
            sceneLifetime.OnCancel(() => ran++);

            Assert.That(ran, Is.EqualTo(1));
            Assert.That(_s.Logs.Count("JANITOR109"), Is.Zero, "a registration after the unload is expected teardown noise");
            Assert.That(_s.Logs.Count("JANITOR104"), Is.Zero, "the scene was disposed before it was unloaded");
        }

        // Invalid scenes

        [UnityTest]
        public IEnumerator InvalidScene_Throws()
        {
            var unloaded = _s.NewScene("Unloaded");
            yield return BindingScenes.Unload(unloaded);

            var getDefault = Assert.Throws<ArgumentException>(() => SceneLifetimes.Get(default(Scene)));
            var disposeDefault = Assert.Throws<ArgumentException>(() => SceneLifetimes.Dispose(default(Scene)));
            var getUnloaded = Assert.Throws<ArgumentException>(() => SceneLifetimes.Get(unloaded));
            var disposeUnloaded = Assert.Throws<ArgumentException>(() => SceneLifetimes.Dispose(unloaded));

            Assert.That(getDefault.ParamName, Is.EqualTo("scene"));
            Assert.That(disposeDefault.ParamName, Is.EqualTo("scene"));
            Assert.That(getUnloaded.ParamName, Is.EqualTo("scene"));
            Assert.That(disposeUnloaded.ParamName, Is.EqualTo("scene"));
            Assert.That(_s.Tree.Scenes.Count, Is.Zero, "a rejected call creates nothing");
        }

        // Also records an engine fact: what GetSceneAt and isLoaded report while a scene loads.
        [UnityTest]
        public IEnumerator FirstAccessInAwake_OfAnObjectInAnAdditivelyLoadedScene_ResolvesToThatScene()
        {
            Lifetime resolved = null;
            var awakeCount = 0;
            var sceneWasLoaded = false;
            var sceneWasListed = false;
            ProbeBehaviour.AwakeHook = probe =>
            {
                awakeCount++;
                if (resolved != null)
                {
                    return;
                }

                var scene = probe.gameObject.scene;
                sceneWasLoaded = scene.isLoaded;
                for (var i = 0; i < SceneManager.sceneCount; i++)
                {
                    if (SceneManager.GetSceneAt(i).handle == scene.handle)
                    {
                        sceneWasListed = true;
                    }
                }

                resolved = probe.GetLifetime();
            };

            yield return BindingScenes.LoadAwakeAdditive();
            ProbeBehaviour.AwakeHook = null;
            var loaded = SceneManager.GetSceneByName(BindingScenes.AwakeSceneName);
            _s.TrackScene(loaded);

            TestContext.WriteLine("During Awake of an additively loaded scene: isLoaded=" + sceneWasLoaded + ", listed by GetSceneAt=" + sceneWasListed);
            Assert.That(awakeCount, Is.EqualTo(1), "the scene's ProbeBehaviour must run Awake during the load");
            Assert.That(loaded.IsValid() && loaded.isLoaded, Is.True);
            var sceneLifetime = SceneLifetimes.Get(loaded);
            Assert.That(sceneLifetime, Is.Not.SameAs(Lifetime.App));
            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved.Parent, Is.SameAs(sceneLifetime), "a first access in Awake must resolve to the loading scene, not to App (isLoaded=" + sceneWasLoaded + ", listed=" + sceneWasListed + ")");
            Assert.That(resolved.Kind, Is.EqualTo(LifetimeKind.Component));
        }

        // Objects that moved into the scene after their lifetimes were created

        [UnityTest]
        public IEnumerator MoveGameObjectToScene_ThenDisposeOfTheTargetScene_EndsTheMovedObjectBeforeAnyDestroy()
        {
            var origin = _s.NewScene("Origin");
            var target = _s.NewScene("Target");
            var go = SpawnWithThreeLifetimes(origin, out var component, out var gameObjectLifetime, out var active);
            Assert.That(component.Parent, Is.SameAs(SceneLifetimes.Get(origin)), "premise: the lifetimes were created under the origin scene");
            var destroyed = false;
            BindingProbe.DestroyHook = p => destroyed = true;
            SceneManager.MoveGameObjectToScene(go, target);

            SceneLifetimes.Dispose(target);

            Assert.That(_log.ToArray(), Is.EquivalentTo(new[] { "component", "gameObject", "active" }), "the target scene's Dispose reaches the object");
            Assert.That(component.IsDisposed && gameObjectLifetime.IsDisposed && active.IsDisposed, Is.True);
            Assert.That(destroyed, Is.False, "the work ended before Unity touched the object");

            SceneLifetimes.Dispose(origin);

            Assert.That(_log.Count, Is.EqualTo(3), "the origin scene does not end it a second time");
            yield break;
        }

        [UnityTest]
        public IEnumerator SetParent_ThenDisposeOfTheTargetScene_EndsTheMovedObjectBeforeAnyDestroy()
        {
            var origin = _s.NewScene("Origin");
            var target = _s.NewScene("Target");
            var go = SpawnWithThreeLifetimes(origin, out var component, out var gameObjectLifetime, out var active);
            var destroyed = false;
            BindingProbe.DestroyHook = p => destroyed = true;
            ReparentUnder(go, target);
            Assert.That(go.scene.handle, Is.EqualTo(target.handle), "premise: the parent change moved the object to the target scene");

            SceneLifetimes.Dispose(target);

            Assert.That(_log.ToArray(), Is.EquivalentTo(new[] { "component", "gameObject", "active" }), "the target scene's Dispose reaches the object");
            Assert.That(component.IsDisposed && gameObjectLifetime.IsDisposed && active.IsDisposed, Is.True);
            Assert.That(destroyed, Is.False, "the work ended before Unity touched the object");

            SceneLifetimes.Dispose(origin);

            Assert.That(_log.Count, Is.EqualTo(3), "the origin scene does not end it a second time");
            yield break;
        }

        [UnityTest]
        public IEnumerator MoveGameObjectToScene_ThenCancelOfTheTargetScene_CancelsTheMovedObjectAndKeepsItUsable()
        {
            var origin = _s.NewScene("Origin");
            var target = _s.NewScene("Target");
            var go = SpawnWithThreeLifetimes(origin, out var component, out var gameObjectLifetime, out var active);
            var targetLifetime = SceneLifetimes.Get(target);
            SceneManager.MoveGameObjectToScene(go, target);

            targetLifetime.Cancel();

            Assert.That(_log.ToArray(), Is.EquivalentTo(new[] { "component", "gameObject", "active" }), "the target scene's Cancel reaches the object");
            Assert.That(component.IsDisposed || gameObjectLifetime.IsDisposed || active.IsDisposed, Is.False, "a Cancel does not dispose");
            Assert.That(component.Generation, Is.EqualTo(1));
            Assert.That(component.Parent, Is.SameAs(targetLifetime));
            Assert.That(component.Record(_log, "again").IsActive, Is.True);

            SceneLifetimes.Dispose(origin);

            Assert.That(_log.Count, Is.EqualTo(3), "the origin scene does not touch it");
            Assert.That(component.IsDisposed, Is.False);
            yield break;
        }

        [UnityTest]
        public IEnumerator SetParent_ThenCancelOfTheTargetScene_CancelsTheMovedObjectAndKeepsItUsable()
        {
            var origin = _s.NewScene("Origin");
            var target = _s.NewScene("Target");
            var go = SpawnWithThreeLifetimes(origin, out var component, out var gameObjectLifetime, out var active);
            var targetLifetime = SceneLifetimes.Get(target);
            ReparentUnder(go, target);

            targetLifetime.Cancel();

            Assert.That(_log.ToArray(), Is.EquivalentTo(new[] { "component", "gameObject", "active" }), "the target scene's Cancel reaches the object");
            Assert.That(component.IsDisposed || gameObjectLifetime.IsDisposed || active.IsDisposed, Is.False, "a Cancel does not dispose");
            Assert.That(component.Parent, Is.SameAs(targetLifetime));

            SceneLifetimes.Dispose(origin);

            Assert.That(_log.Count, Is.EqualTo(3), "the origin scene does not touch it");
            yield break;
        }

        [UnityTest]
        public IEnumerator MoveGameObjectToScene_ThenDisposeAll_EndsTheMovedObjectOnce()
        {
            var origin = _s.NewScene("Origin");
            var target = _s.NewScene("Target");
            var go = SpawnWithThreeLifetimes(origin, out var component, out var gameObjectLifetime, out var active);
            var destroyed = false;
            BindingProbe.DestroyHook = p => destroyed = true;
            SceneManager.MoveGameObjectToScene(go, target);

            SceneLifetimes.DisposeAll();

            Assert.That(_log.ToArray(), Is.EquivalentTo(new[] { "component", "gameObject", "active" }), "each item ran once");
            Assert.That(component.IsDisposed && gameObjectLifetime.IsDisposed && active.IsDisposed, Is.True);
            Assert.That(destroyed, Is.False);
            yield break;
        }

        [UnityTest]
        public IEnumerator MoveGameObjectToScene_OfAPlacedObject_ThenDisposeOfTheTargetScene_EndsItAndKeepsTheCategory()
        {
            var origin = _s.NewScene("Origin");
            var target = _s.NewScene("Target");
            var go = _s.NewObjectIn(origin);
            var probe = go.AddComponent<BindingProbe>();
            var originLifetime = SceneLifetimes.Get(origin);
            var category = Lifetime.App.CreateChild("Outside");
            var placed = probe.GetLifetime(category);
            placed.Record(_log, "placed");
            Assert.That(placed.Membership, Is.SameAs(originLifetime), "premise: the membership is in the origin scene");
            var destroyed = false;
            BindingProbe.DestroyHook = p => destroyed = true;
            SceneManager.MoveGameObjectToScene(go, target);

            SceneLifetimes.Dispose(target);

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "placed" }), "the target scene's Dispose reaches a placed object that moved in");
            Assert.That(placed.IsDisposed, Is.True);
            Assert.That(destroyed, Is.False, "the work ended before Unity touched the object");
            Assert.That(category.IsDisposed, Is.False);
            Assert.That(category.ChildCount, Is.Zero, "a disposed lifetime leaves its category");

            SceneLifetimes.Dispose(origin);

            Assert.That(_log.Count, Is.EqualTo(1), "the origin scene does not end it a second time");
            yield break;
        }

        [UnityTest]
        public IEnumerator SetParent_OfAPlacedObjectWhoseCategoryIsInTheOriginScene_ThenDisposeOfTheTargetScene_EndsIt()
        {
            var origin = _s.NewScene("Origin");
            var target = _s.NewScene("Target");
            var go = _s.NewObjectIn(origin);
            var probe = go.AddComponent<BindingProbe>();
            var category = SceneLifetimes.Get(origin).CreateChild("InsideOrigin");
            var placed = probe.GetLifetime(category);
            placed.Record(_log, "placed");
            Assert.That(placed.Membership, Is.Null, "premise: a category inside the scene needs no membership");
            ReparentUnder(go, target);

            SceneLifetimes.Dispose(target);

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "placed" }));
            Assert.That(placed.IsDisposed, Is.True);

            SceneLifetimes.Dispose(origin);

            Assert.That(_log.Count, Is.EqualTo(1), "the origin scene does not end it a second time");
            yield break;
        }

        [UnityTest]
        public IEnumerator MoveGameObjectToScene_OfAPlacedObject_ThenCancelOfTheTargetScene_MovesTheMembershipAndLeavesTheWorkToTheCategory()
        {
            var origin = _s.NewScene("Origin");
            var target = _s.NewScene("Target");
            var go = _s.NewObjectIn(origin);
            var probe = go.AddComponent<BindingProbe>();
            var targetLifetime = SceneLifetimes.Get(target);
            var category = Lifetime.App.CreateChild("Outside");
            var placed = probe.GetLifetime(category);
            placed.Record(_log, "placed");
            SceneManager.MoveGameObjectToScene(go, target);

            targetLifetime.Cancel();

            Assert.That(_log.Count, Is.Zero, "for cancellation a placed object belongs to its category");
            Assert.That(placed.Membership, Is.SameAs(targetLifetime), "the membership followed the object");
            Assert.That(placed.Parent, Is.SameAs(category));

            SceneLifetimes.Dispose(target);

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "placed" }));
            Assert.That(placed.IsDisposed, Is.True);
            yield break;
        }

        // An object whose three object lifetimes were first used in the origin scene.
        private GameObject SpawnWithThreeLifetimes(Scene origin, out Lifetime component, out Lifetime gameObjectLifetime, out Lifetime active)
        {
            var go = _s.NewObjectIn(origin);
            var probe = go.AddComponent<BindingProbe>();
            component = probe.GetLifetime();
            gameObjectLifetime = go.GetLifetime();
            active = probe.GetActiveLifetime();
            component.Record(_log, "component");
            gameObjectLifetime.Record(_log, "gameObject");
            active.Record(_log, "active");
            return go;
        }

        // Parenting under an object of another scene moves the object there.
        private void ReparentUnder(GameObject go, Scene target)
        {
            var root = _s.NewObjectIn(target, "TargetRoot");
            go.transform.SetParent(root.transform, false);
        }
    }
}
