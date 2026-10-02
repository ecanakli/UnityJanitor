using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using DG.Tweening;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.TweenBinding
{
    // A value a float tween drives; it keeps the tests free of scene objects.
    internal sealed class FloatBox
    {
        internal float Value;
    }

    // Per-test bundle for the DOTween tests. Tweens run on DOTween's manual update, so time moves only through Step.
    // A fresh lifetime tree comes from the 4a session seam; Complete kills and destroys what was created and restores
    // every global it touched (defaultRecyclable, the DOTween pool, the session, the error handler).
    internal sealed class TweenSession
    {
        // The most frames any test waits for DOTween or UniTask to act on its own.
        internal const int MaxFrames = 10;

        // The most frames an additive scene gets to unload.
        private const int MaxUnloadFrames = 300;

        private static int _sceneSerial;

        private readonly List<Scene> _scenes = new List<Scene>();
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<Tween> _tweens = new List<Tween>();
        private readonly List<CapturingErrorHandler> _handlers = new List<CapturingErrorHandler>();
        private readonly List<CancellationTokenSource> _sources = new List<CancellationTokenSource>();
        private readonly bool _previousRecyclable;
        private bool _recyclingUsed;

        private TweenSession()
        {
            DOTween.Init();
            _previousRecyclable = DOTween.defaultRecyclable;
            DOTween.defaultRecyclable = false;
            Exit = NewSource();
            PlayModeBootstrap.BeginSession(Exit.Token);
            Errors = new CapturingErrorHandler();
            _handlers.Add(Errors);
        }

        internal static TweenSession Begin()
        {
            return new TweenSession();
        }

        internal LifetimeTree Tree => LifetimeTree.Default;

        // Cancel it to simulate the application exit of the current session.
        internal CancellationTokenSource Exit { get; }

        internal CapturingErrorHandler Errors { get; }

        // A fixed number of frames; never a condition wait.
        internal static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++)
            {
                yield return null;
            }
        }

        // A plain area under App.
        internal Lifetime Area(string name = "TweenArea")
        {
            return Tree.App.CreateChild(name);
        }

        internal FloatBox Box(float value = 0f)
        {
            return new FloatBox { Value = value };
        }

        // Moves every manual-update tween forward.
        internal void Step(float seconds)
        {
            DOTween.ManualUpdate(seconds, seconds);
        }

        // Adopts a tween the test built itself, so Complete kills it if the test leaves it running.
        internal T Track<T>(T tween)
            where T : Tween
        {
            _tweens.Add(tween);
            return tween;
        }

        // A float tween on the manual update; it is killed by Complete if the test leaves it running.
        internal Tween NewTween(FloatBox box, float end = 10f, float duration = 1f)
        {
            var tween = DOTween.To(() => box.Value, value => box.Value = value, end, duration).SetUpdate(UpdateType.Manual);
            _tweens.Add(tween);
            return tween;
        }

        // A float tween on the manual update that never completes by itself.
        internal Tween NewLoop(FloatBox box, float end = 10f, float duration = 1f)
        {
            var tween = DOTween.To(() => box.Value, value => box.Value = value, end, duration).SetLoops(-1).SetUpdate(UpdateType.Manual);
            _tweens.Add(tween);
            return tween;
        }

        // A float tween on the normal update, for tweens a Sequence will own.
        internal Tween NewNestedTween(FloatBox box, float end = 10f, float duration = 1f)
        {
            var tween = DOTween.To(() => box.Value, value => box.Value = value, end, duration);
            _tweens.Add(tween);
            return tween;
        }

        // A long tween on DOTween's own update that dies with the GameObject (SetLink).
        internal Tween NewLinkedTween(GameObject link, float duration = 30f)
        {
            var box = new FloatBox();
            var tween = DOTween.To(() => box.Value, value => box.Value = value, 10f, duration).SetLink(link);
            _tweens.Add(tween);
            return tween;
        }

        // A root Sequence on the manual update; the nested tweens are driven by it.
        internal Sequence NewSequence(params Tween[] nested)
        {
            var sequence = DOTween.Sequence();
            for (var i = 0; i < nested.Length; i++)
            {
                sequence.Append(nested[i]);
            }

            sequence.SetUpdate(UpdateType.Manual);
            _tweens.Add(sequence);
            return sequence;
        }

        // Fresh tweens, each with its own box, already started so a measured Complete does no startup work.
        internal Tween[] NewSteppedTweens(int count, float duration = 1f)
        {
            var tweens = new Tween[count];
            for (var i = 0; i < count; i++)
            {
                tweens[i] = NewTween(Box(), 10f, duration);
            }

            Step(0.01f);
            return tweens;
        }

        // From now on new tweens are recyclable (DOTween's pool is emptied first, so reuse is deterministic).
        internal void EnableRecycling()
        {
            DOTween.ClearCachedTweens();
            DOTween.defaultRecyclable = true;
            _recyclingUsed = true;
        }

        internal CancellationTokenSource NewSource()
        {
            var source = new CancellationTokenSource();
            _sources.Add(source);
            return source;
        }

        internal GameObject NewObject(string name = "TweenObject")
        {
            var go = new GameObject(name);
            _objects.Add(go);
            return go;
        }

        internal TweenTestHost NewHost(string name = "TweenHost")
        {
            return NewObject(name).AddComponent<TweenTestHost>();
        }

        // A scene that CompleteAsync unloads.
        internal Scene NewScene(string name = "TweenScene")
        {
            var scene = SceneManager.CreateScene(name + "_" + (++_sceneSerial));
            _scenes.Add(scene);
            return scene;
        }

        // For fixtures that create no scenes.
        internal void Complete()
        {
            var problems = Cleanup();
            Assert.That(problems, Is.Empty, problems);
            Assert.That(_scenes.Count, Is.Zero, "This fixture creates scenes: complete it with CompleteAsync.");
        }

        // Cleans up first and unloads the scenes (bounded), and only then reports problems, so a failure never leaks a scene.
        internal IEnumerator CompleteAsync()
        {
            var problems = Cleanup();
            for (var i = _scenes.Count - 1; i >= 0; i--)
            {
                var scene = _scenes[i];
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                var operation = SceneManager.UnloadSceneAsync(scene);
                for (var frame = 0; operation != null && !operation.isDone && frame < MaxUnloadFrames; frame++)
                {
                    yield return null;
                }
            }

            _scenes.Clear();
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

            for (var i = _tweens.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (_tweens[i].IsActive())
                    {
                        _tweens[i].Kill();
                    }
                }
                catch (Exception exception)
                {
                    problems.AppendLine("Killing a tween threw: " + exception);
                }
            }

            _tweens.Clear();

            // A tween killed inside an update loop is only marked; its onKill runs in the next pass. Run that pass here,
            // so a test that failed halfway cannot leak a running callback into the next test.
            try
            {
                DOTween.ManualUpdate(0f, 0f);
            }
            catch (Exception exception)
            {
                problems.AppendLine("Draining killed tweens threw: " + exception);
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

            if (_recyclingUsed)
            {
                DOTween.ClearCachedTweens();
            }

            DOTween.defaultRecyclable = _previousRecyclable;

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
            for (var i = 0; i < _sources.Count; i++)
            {
                _sources[i].Dispose();
            }

            _sources.Clear();
            if (routed != 0)
            {
                problems.AppendLine("Unexpected routed errors:\n" + routedText);
            }

            return problems.ToString();
        }
    }
}
