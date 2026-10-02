using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Ecanakli.Janitor.Tests.Probes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.Binding
{
    // Long runs that must end where they began: the owner index and the scene index at their baseline, no JANITOR warning
    // on the console or in the recorded diagnostics, and nothing routed (the session teardown checks that). The sizes keep the
    // whole fixture to a few seconds. Memory is not asserted: a managed heap size is not stable enough for a test.
    [TestFixture]
    public sealed class SoakTests
    {
        private BindingSession _s;
        private JanitorLogWatch _watch;
#if UNITY_EDITOR
        private DiagnosticsRecordingKit _recording;
#endif

        [SetUp]
        public void SetUp()
        {
            _s = BindingSession.Begin();
            _watch = new JanitorLogWatch();
#if UNITY_EDITOR
            _recording = new DiagnosticsRecordingKit(manualFrames: false);
#endif
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
            {
                yield return _s.CompleteAsync();
            }
            finally
            {
                _watch.Dispose();
#if UNITY_EDITOR
                _recording.Dispose();
#endif
            }
        }

        [UnityTest]
        public IEnumerator RegisterAndCancel_AcrossManyLifetimes_RunsEveryItemOnceAndLeavesNothingBehind()
        {
            const int lifetimeCount = 40;
            const int rounds = 1000;
            var appChildren = Lifetime.App.ChildCount;
            var owners = _s.Tree.Owners.Count;
            var cancelled = new Counter();
            var delivered = new Counter();
            var evt = new OwnedEvent("soak");

            // A lifetime that is not cancelled for two rounds holds two subscriptions, so the handlers differ by round.
            Action[] handlers = { () => delivered.Value++, () => delivered.Value++, () => delivered.Value++ };
            var areas = new Lifetime[lifetimeCount];
            for (var i = 0; i < lifetimeCount; i++)
            {
                areas[i] = Lifetime.App.CreateChild("Soak" + i);
            }

            var registered = 0;
            var cancels = 0;
            for (var round = 0; round < rounds; round++)
            {
                for (var i = 0; i < lifetimeCount; i++)
                {
                    var area = areas[i];
                    area.OnCancel(cancelled, static c => c.Value++);
                    area.OnCancel(cancelled, static c => c.Value++);
                    evt.Subscribe(handlers[round % 3], area);
                    registered += 2;

                    // Even lifetimes are cancelled every round, odd ones every third round, so entries of several rounds pile up and end together.
                    if (i % 2 == 0 || round % 3 == 0)
                    {
                        area.Cancel();
                        cancels++;
                    }
                }
            }

            for (var i = 0; i < lifetimeCount; i++)
            {
                areas[i].Cancel();
                cancels++;
            }

            Assert.That(cancelled.Value, Is.EqualTo(registered), "every registered item ran exactly once");
            Assert.That(evt.SubscriberCount, Is.Zero);
            var generations = 0;
            for (var i = 0; i < lifetimeCount; i++)
            {
                Assert.That(areas[i].EntryCount, Is.Zero, "lifetime " + i);
                generations += areas[i].Generation;
            }

            Assert.That(generations, Is.EqualTo(cancels), "every Cancel ended exactly one generation");

            for (var i = 0; i < lifetimeCount; i++)
            {
                areas[i].Dispose();
            }

            Assert.That(Lifetime.App.ChildCount, Is.EqualTo(appChildren), "the areas left the tree");
            Assert.That(_s.Tree.Owners.Count, Is.EqualTo(owners));
            AssertQuiet();
            yield break;
        }

        [UnityTest]
        public IEnumerator PooledObject_ThousandsOfActivationsWithTasksTimersAndSubscriptions_LeavesNothingBehind()
        {
            const int cycles = 3000;
            var owners = _s.Tree.Owners.Count;
            var cancelled = new Counter();
            var started = new Counter();
            var fired = new Counter();
            var delivered = new Counter();
            var evt = new OwnedEvent("pooled");
            Action handler = () => delivered.Value++;
            BindingProbe.EnableHook = enabled =>
            {
                var current = enabled.GetActiveLifetime();
                current.OnCancel(cancelled, static c => c.Value++);
                current.Run(started, static async (c, ct) =>
                {
                    c.Value++;
                    await UniTask.Delay(TimeSpan.FromSeconds(60), cancellationToken: ct);
                });
                current.After(60f, fired, static c => c.Value++);
                evt.Subscribe(handler, current);
            };
            var go = _s.NewObject("Pooled");
            var probe = go.AddComponent<BindingProbe>();
            var lifetime = probe.GetActiveLifetime();

            for (var i = 0; i < cycles; i++)
            {
                go.SetActive(false);
                go.SetActive(true);
            }

            Assert.That(_s.Tree.Owners.Count, Is.EqualTo(owners + 1));
            Assert.That(lifetime.EntryCount, Is.EqualTo(4));
            Assert.That(lifetime.Generation, Is.EqualTo(cycles));
            Assert.That(cancelled.Value, Is.EqualTo(cycles));
            Assert.That(evt.SubscriberCount, Is.EqualTo(1));

            // The cancelled delays and timers end with an exception each in the following frames; they must route and log nothing.
            yield return BindingScenes.Frames(3);
            Assert.That(fired.Value, Is.Zero, "no timer of an earlier use fired");

            Object.Destroy(go);
            yield return BindingScenes.Frames(2);

            Assert.That(lifetime.IsDisposed, Is.True);
            Assert.That(_s.Tree.Owners.Count, Is.EqualTo(owners), "the owner index is back at its baseline");
            Assert.That(evt.SubscriberCount, Is.Zero);
            LogAssert.NoUnexpectedReceived();
            AssertQuiet();
        }

        [UnityTest]
        public IEnumerator AdditiveScenes_LoadedAndUnloadedManyTimesWithDispose_LeaveBothIndexesAtTheirBaseline()
        {
            const int createdScenes = 40;
            const int loadedScenes = 15;
            var owners = _s.Tree.Owners.Count;
            var scenes = _s.Tree.Scenes.Count;
            var cancelled = new Counter();

            for (var i = 0; i < createdScenes; i++)
            {
                var scene = _s.NewScene("Soak");
                var probe = _s.NewProbeIn(scene);
                var sceneLifetime = SceneLifetimes.Get(scene);
                Assert.That(sceneLifetime.IsDisposed, Is.False, "cycle " + i + " got a fresh scene lifetime, whatever the scene handle was");
                probe.GetLifetime().OnCancel(cancelled, static c => c.Value++);
                probe.GetActiveLifetime().OnCancel(cancelled, static c => c.Value++);
                sceneLifetime.OnCancel(cancelled, static c => c.Value++);

                SceneLifetimes.Dispose(scene);
                yield return BindingScenes.Unload(scene);
            }

            var awakes = new Counter();
            ProbeBehaviour.AwakeHook = probe =>
            {
                awakes.Value++;
                probe.GetLifetime().OnCancel(cancelled, static c => c.Value++);
            };
            for (var i = 0; i < loadedScenes; i++)
            {
                yield return BindingScenes.LoadAwakeAdditive();
                var scene = SceneManager.GetSceneByName(BindingScenes.AwakeSceneName);
                _s.TrackScene(scene);
                Assert.That(awakes.Value, Is.EqualTo(i + 1), "cycle " + i + ": the scene's object woke up");
                var sceneLifetime = SceneLifetimes.Get(scene);
                Assert.That(sceneLifetime.IsDisposed, Is.False, "cycle " + i + " got a fresh scene lifetime");

                SceneLifetimes.Dispose(scene);
                yield return BindingScenes.Unload(scene);
            }

            Assert.That(cancelled.Value, Is.EqualTo((3 * createdScenes) + loadedScenes), "every item ended before its scene was unloaded");
            Assert.That(_s.Tree.Owners.Count, Is.EqualTo(owners), "the owner index is back at its baseline");
            Assert.That(_s.Tree.Scenes.Count, Is.EqualTo(scenes), "the scene index is back at its baseline");
            AssertQuiet();
        }

        // No JANITOR line on the console, none recorded for the window, and no leftover teardown.
        private void AssertQuiet()
        {
            Assert.That(_watch.Lines, Is.Empty, "console: " + string.Join(" | ", _watch.Lines));
#if UNITY_EDITOR
            Assert.That(_recording.Total, Is.Zero, "recorded: " + _recording.Describe());
#endif
            Assert.That(_s.Tree.Teardown.RunningCount, Is.Zero);
            Assert.That(_s.Errors.Count, Is.Zero, "nothing was routed: " + _s.Errors);
        }

        private sealed class JanitorLogWatch : IDisposable
        {
            internal JanitorLogWatch()
            {
                Application.logMessageReceived += OnLog;
            }

            internal List<string> Lines { get; } = new List<string>();

            public void Dispose()
            {
                Application.logMessageReceived -= OnLog;
            }

            private void OnLog(string condition, string stackTrace, LogType type)
            {
                if (condition.Contains("[JANITOR"))
                {
                    Lines.Add(type + ": " + condition);
                }
            }
        }
    }
}
