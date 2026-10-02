using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Ecanakli.Janitor.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.DependencyInjection.Tests.PlayMode
{
    // Per-test bundle for the Zenject PlayMode tests.
    //
    // Sessions: every test begins a fresh Janitor session through the core seam (new tree, old one shut down) and captures routed errors.
    // Scenes: every test works in scenes it creates, never in the test runner's own scene, because a SceneContext disposer ends the
    // lifetime of the scene it lives in and that is terminal.
    // ProjectContext: a DontDestroyOnLoad singleton that survives between tests. It is REUSED deliberately: created once by the first
    // access here, never installed into (LifetimeInstaller would capture one session's App for good), used only as the parent of the
    // scene contexts. A test that needs an installed parent builds a sub-container of it (NewInstalledParent) and drops it.
    internal sealed class ZenjectPlaySession
    {
        internal const float DeadlineSeconds = 30f;

        private static int _serial;

        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<Scene> _scenes = new List<Scene>();
        private readonly List<CancellationTokenSource> _exits = new List<CancellationTokenSource>();
        private readonly List<CapturingErrorHandler> _handlers = new List<CapturingErrorHandler>();

        private ZenjectPlaySession()
        {
            Warnings = new WarningSpy();
            _ = ProjectContext.Instance;
            SignalSubscriptions.ResetSession();
            Restart();
        }

        internal static ZenjectPlaySession Begin()
        {
            return new ZenjectPlaySession();
        }

        internal LifetimeTree Tree => LifetimeTree.Default;

        // Cancel it to simulate the application exit of the current session.
        internal CancellationTokenSource Exit { get; private set; }

        internal CapturingErrorHandler Errors { get; private set; }

        internal WarningSpy Warnings { get; }

        // Begins a new session with a fresh manual exit token; the previous tree is shut down.
        internal void Restart()
        {
            Exit = new CancellationTokenSource();
            _exits.Add(Exit);
            PlayModeBootstrap.BeginSession(Exit.Token);
            CaptureErrors();
        }

        // BeginSession resets LifetimeErrors.Handler, so a test that begins a session itself calls this afterwards.
        internal void CaptureErrors()
        {
            Errors = new CapturingErrorHandler();
            _handlers.Add(Errors);
        }

        // A scene that is unloaded by CompleteAsync.
        internal Scene NewScene(string name = "ZenjectScene")
        {
            var scene = SceneManager.CreateScene(name + "_" + (++_serial));
            _scenes.Add(scene);
            return scene;
        }

        internal GameObject NewObjectIn(Scene scene, string name)
        {
            var go = new GameObject(name);
            _objects.Add(go);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        // A component on a new active object in the scene; its Awake has run when this returns.
        internal T NewBehaviourIn<T>(Scene scene)
            where T : Component
        {
            return NewObjectIn(scene, typeof(T).Name).AddComponent<T>();
        }

        // A context-less sub-container of the ProjectContext's container with LifetimeInstaller installed: the stand-in for an
        // installed project level. Its context is the ProjectContext found through the ancestors, so its lifetime is App.
        internal DiContainer NewInstalledParent()
        {
            var parent = ProjectContext.Instance.Container.CreateSubContainer();
            LifetimeInstaller.Install(parent);
            return parent;
        }

        // A SceneContext in the given scene, installed and resolved. The scene is where SceneLifetimes.Get(scene) looks, so the
        // context is moved there before anything runs. With no parent it sits below the ProjectContext, as in a real game.
        internal SceneContext NewSceneContext(Scene scene, Action<DiContainer> install, DiContainer parent = null)
        {
            var context = SceneContext.Create();
            _objects.Add(context.gameObject);
            SceneManager.MoveGameObjectToScene(context.gameObject, scene);
            context.AddNormalInstaller(new ActionInstaller(install));
            if (parent != null)
            {
                SceneContext.ParentContainers = new[] { parent };
            }

            try
            {
                context.Install();
                context.Resolve();
            }
            finally
            {
                SceneContext.ParentContainers = null;
            }

            return context;
        }

        internal IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++)
            {
                yield return null;
            }
        }

        // Waits frame by frame for the condition; bounded by a real-time deadline, so a missing event fails the test.
        internal IEnumerator WaitUntil(Func<bool> condition, string what)
        {
            var deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail("Timed out after " + DeadlineSeconds + " s waiting for: " + what);
                }

                yield return null;
            }
        }

        // The unload the test asks for. sceneUnloaded has fired when this finishes.
        internal IEnumerator Unload(Scene scene)
        {
            Assert.That(scene.IsValid() && scene.isLoaded, Is.True, "The scene to unload must be loaded.");
            var operation = SceneManager.UnloadSceneAsync(scene);
            Assert.That(operation, Is.Not.Null);
            yield return WaitUntil(() => operation.isDone, "the scene unload");
        }

        // Cleans up first and unloads the scenes, and only then reports problems, so a failure never leaks scenes.
        internal IEnumerator CompleteAsync()
        {
            var problems = Cleanup();
            var scenes = _scenes.ToArray();
            _scenes.Clear();
            for (var i = scenes.Length - 1; i >= 0; i--)
            {
                yield return UnloadQuietly(scenes[i]);
            }

            Assert.That(problems, Is.Empty, problems);
        }

        // Never throws. Returns what went wrong, or an empty string.
        private string Cleanup()
        {
            var problems = new StringBuilder();
            SceneContext.ParentContainers = null;

            // End every scene lifetime first, so destroying a context below finds nothing alive and logs no hint.
            try
            {
                SceneLifetimes.DisposeAll();
            }
            catch (Exception exception)
            {
                problems.AppendLine("Ending the scene lifetimes threw: " + exception);
            }

            for (var i = _objects.Count - 1; i >= 0; i--)
            {
                var go = _objects[i];
                if (go == null)
                {
                    continue;
                }

                var name = go.name;
                try
                {
                    Object.DestroyImmediate(go);
                }
                catch (Exception exception)
                {
                    problems.AppendLine("Destroying '" + name + "' threw: " + exception);
                }
            }

            _objects.Clear();

            try
            {
                PlayModeBootstrap.BeginSession(Application.exitCancellationToken);
            }
            catch (Exception exception)
            {
                problems.AppendLine("Restoring the session threw: " + exception);
            }

            // The whole tree of the test is disposed by now, so every subscription must have been forgotten.
            var live = SignalSubscriptions.LiveCount;
            SignalSubscriptions.ResetSession();
            if (live != 0)
            {
                problems.AppendLine("The subscription table still held " + live + " record(s) after the session ended.");
            }

            var routed = 0;
            var routedText = new StringBuilder();
            for (var i = 0; i < _handlers.Count; i++)
            {
                routed += _handlers[i].Count;
                routedText.Append(_handlers[i]);
            }

            for (var i = _handlers.Count - 1; i >= 0; i--)
            {
                _handlers[i].Dispose();
            }

            _handlers.Clear();
            LifetimeErrors.Handler = null;
            Warnings.Dispose();
            for (var i = 0; i < _exits.Count; i++)
            {
                _exits[i].Dispose();
            }

            _exits.Clear();
            if (routed != 0)
            {
                problems.AppendLine("Unexpected routed errors:\n" + routedText);
            }

            return problems.ToString();
        }

        // Teardown unload: a scene that is gone, or the last one, is skipped without failing.
        private static IEnumerator UnloadQuietly(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || SceneManager.sceneCount < 2)
            {
                yield break;
            }

            var operation = SceneManager.UnloadSceneAsync(scene);
            if (operation == null)
            {
                yield break;
            }

            var deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (!operation.isDone && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }
    }
}
