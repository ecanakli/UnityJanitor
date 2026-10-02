using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Ecanakli.Janitor.Tests.Probes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.Binding
{
    // Per-test bundle for the Unity binding tests. The component, scene and App lifetimes live in the default tree,
    // so every test begins a fresh session through the 4a seam, captures routed errors, and in Complete destroys
    // what it created and restores a normal session (real exit token, default error handler).
    internal sealed class BindingSession
    {
        private static int _serial;

        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<Scene> _scenes = new List<Scene>();
        private readonly List<CapturingErrorHandler> _handlers = new List<CapturingErrorHandler>();
        private readonly List<CancellationTokenSource> _exits = new List<CancellationTokenSource>();

        private BindingSession()
        {
            Logs = new BindingLogSpy();
            Restart();
        }

        internal static BindingSession Begin()
        {
            return new BindingSession();
        }

        // The tree of the current session.
        internal LifetimeTree Tree => LifetimeTree.Default;

        // Cancel it to simulate the application exit of the current session.
        internal CancellationTokenSource Exit { get; private set; }

        internal CapturingErrorHandler Errors { get; private set; }

        internal BindingLogSpy Logs { get; }

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

        // Makes After and Every of the current tree deterministic: time moves only through Advance.
        internal ManualClock UseManualClock()
        {
            var clock = new ManualClock();
            Tree.Delay = clock.Delay;
            return clock;
        }

        // A new object in the active scene.
        internal GameObject NewObject(string name = "BindingObject")
        {
            var go = new GameObject(name);
            _objects.Add(go);
            return go;
        }

        // A new object in the given scene, before any component exists on it.
        internal GameObject NewObjectIn(Scene scene, string name = "BindingObject")
        {
            var go = NewObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        // Inactive, so AddComponent runs no Awake or OnEnable.
        internal GameObject NewInactiveObject(string name = "BindingObject")
        {
            var go = NewObject(name);
            go.SetActive(false);
            return go;
        }

        // A probe on a new active object; Awake and OnEnable have run when this returns.
        internal BindingProbe NewProbe(string name = "BindingObject")
        {
            return NewObject(name).AddComponent<BindingProbe>();
        }

        internal BindingProbe NewProbeIn(Scene scene, string name = "BindingObject")
        {
            return NewObjectIn(scene, name).AddComponent<BindingProbe>();
        }

        // A scene that is unloaded by CompleteAsync.
        internal Scene NewScene(string name = "BindingScene")
        {
            var scene = SceneManager.CreateScene(name + "_" + (++_serial));
            _scenes.Add(scene);
            return scene;
        }

        internal void TrackScene(Scene scene)
        {
            _scenes.Add(scene);
        }

        // For fixtures that create no scenes.
        internal void Complete()
        {
            var problems = Cleanup();
            Assert.That(problems, Is.Empty, problems);
            Assert.That(_scenes.Count, Is.Zero, "This fixture creates scenes: complete it with CompleteAsync.");
        }

        // Cleans up first and unloads the scenes, and only then reports problems, so a failure never leaks scenes.
        internal IEnumerator CompleteAsync()
        {
            var problems = Cleanup();
            var scenes = _scenes.ToArray();
            _scenes.Clear();
            for (var i = scenes.Length - 1; i >= 0; i--)
            {
                yield return BindingScenes.UnloadQuietly(scenes[i]);
            }

            Assert.That(problems, Is.Empty, problems);
        }

        // Never throws. Returns what went wrong, or an empty string.
        private string Cleanup()
        {
            var problems = new StringBuilder();
            var tree = LifetimeTree.Default;
            if (tree != null && tree.Teardown.RunningCount != 0)
            {
                problems.AppendLine("A Cancel or Dispose is still on the teardown stack.");
            }

            BindingProbe.ResetStatics();
            ProbeBehaviour.AwakeHook = null;
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
            Logs.Dispose();
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
    }
}
