using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#if UNITY_EDITOR
namespace Ecanakli.Janitor.Tests.Binding
{
    // A script reload during Play Mode drops the default tree while the scene objects survive. The reload is simulated by
    // clearing the default tree and the trigger's unserialised state; the next entry point must start a new session.
    [TestFixture]
    public sealed class ScriptReloadTests
    {
        private BindingSession _s;
        private ReloadWarningSpy _warnings;

        [SetUp]
        public void SetUp()
        {
            _s = BindingSession.Begin();
            _warnings = new ReloadWarningSpy();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _warnings.Dispose();
            yield return _s.CompleteAsync();
        }

        [UnityTest]
        public IEnumerator FirstEntryPointAfterAScriptReload_StartsOneFreshSession_AndWarnsOnce()
        {
            var oldTree = _s.Tree;
            var started = 0;
            var activeToken = default(CancellationToken);
            var componentToken = default(CancellationToken);
            BindingProbe.EnableHook = probe =>
            {
                probe.GetActiveLifetime().Run(async ct =>
                {
                    activeToken = ct;
                    started++;
                    await UniTask.Delay(TimeSpan.FromSeconds(60), cancellationToken: ct);
                });
                probe.Run(async ct =>
                {
                    componentToken = ct;
                    await UniTask.Delay(TimeSpan.FromSeconds(60), cancellationToken: ct);
                });
            };
            var go = _s.NewInactiveObject("Survivor");
            var survivor = go.AddComponent<BindingProbe>();
            survivor.GetActiveLifetime();
            Assert.That(go.TryGetComponent<ActiveLifetimeTrigger>(out var trigger), Is.True);

            SimulateScriptReload(trigger);
            var sessions = PlayModeBootstrap.SessionCount;
            Assert.That(LifetimeTree.Default, Is.Null);

            go.SetActive(true);

            var tree = _s.Tree;
            Assert.That(tree, Is.Not.Null.And.Not.SameAs(oldTree), "the first entry point started a new session");
            Assert.That(PlayModeBootstrap.SessionCount, Is.EqualTo(sessions + 1), "exactly one session was started");
            Assert.That(oldTree.App.IsDisposed, Is.True, "nothing of the old tree stays alive");
            Assert.That(_warnings.Count, Is.EqualTo(1), "exactly one warning names the reload");
            Assert.That(PlayModeBootstrap.IsExiting, Is.False);
            Assert.That(started, Is.EqualTo(1), "the work registered in OnEnable runs");
            var active = trigger.BoundLifetime;
            Assert.That(active, Is.Not.Null, "the surviving trigger was bound again");
            Assert.That(active.Tree, Is.SameAs(tree));
            Assert.That(active.EntryCount, Is.EqualTo(1));
            Assert.That(survivor.GetLifetime().EntryCount, Is.EqualTo(1));

            go.SetActive(false);

            Assert.That(activeToken.IsCancellationRequested, Is.True, "deactivation stops the active work");
            Assert.That(active.EntryCount, Is.Zero);
            Assert.That(componentToken.IsCancellationRequested, Is.False, "the component work outlives a deactivation");

            go.SetActive(true);
            _ = Lifetime.App;

            Assert.That(started, Is.EqualTo(2), "the next activation registers again");
            Assert.That(PlayModeBootstrap.SessionCount, Is.EqualTo(sessions + 1), "no second session");
            Assert.That(_warnings.Count, Is.EqualTo(1), "later entry points log nothing more");
            yield break;
        }

        [UnityTest]
        public IEnumerator EveryEntryPointFamily_AfterAScriptReload_WorksWithoutAnException()
        {
            var probe = _s.NewProbe();
            var go = probe.gameObject;
            var evt = new OwnedEvent("reload");
            var calls = 0;

            SimulateScriptReload();

            Assert.That(() =>
            {
                probe.GetLifetime();
                go.GetLifetime();
                probe.GetActiveLifetime();
                SceneLifetimes.Get(go.scene);
                evt.Subscribe(() => calls++, probe);
                new DisposeProbe().AddTo(probe);
            }, Throws.Nothing);

            evt.Invoke();
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(_warnings.Count, Is.EqualTo(1));
            yield break;
        }

        [UnityTest]
        public IEnumerator ScriptReload_BringsBackTheSessionHandlers()
        {
            var scene = _s.NewScene("AfterReload");
            PlayModeBootstrap.EndSession();
            Assert.That(LifetimeTree.Default, Is.Null);
            Assert.That(PlayModeBootstrap.SceneUnloadedHandlerCount, Is.Zero, "a reloaded domain has no handlers");
            Assert.That(PlayModeBootstrap.QuittingHandlerCount, Is.Zero);
            Assert.That(PlayModeBootstrap.PlayModeStateHandlerCount, Is.Zero);

            _ = Lifetime.App;

            Assert.That(PlayModeBootstrap.SceneUnloadedHandlerCount, Is.EqualTo(1));
            Assert.That(PlayModeBootstrap.QuittingHandlerCount, Is.EqualTo(1));
            Assert.That(PlayModeBootstrap.PlayModeStateHandlerCount, Is.EqualTo(1));
            var unloaded = PlayModeBootstrap.SceneUnloadedCount;
            yield return BindingScenes.Unload(scene);

            Assert.That(PlayModeBootstrap.SceneUnloadedCount, Is.EqualTo(unloaded + 1), "one unload is handled once");
            Assert.That(_warnings.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator EntryPoint_OnAWorkerThread_WithoutATree_StillThrowsAndStartsNothing()
        {
            SimulateScriptReload();
            var sessions = PlayModeBootstrap.SessionCount;

            var failure = ThreadRunner.Run(() => { _ = Lifetime.App; });

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(failure.Message, Does.Contain("Play Mode"));
            Assert.That(LifetimeTree.Default, Is.Null, "only the main thread starts a session");
            Assert.That(PlayModeBootstrap.SessionCount, Is.EqualTo(sessions));
            Assert.That(_warnings.Count, Is.Zero);
            yield break;
        }

        // The tree is dropped. A trigger that was bound loses its lifetime; the objects survive.
        private static void SimulateScriptReload(params ActiveLifetimeTrigger[] triggers)
        {
            LifetimeTree.Default = null;
            for (var i = 0; i < triggers.Length; i++)
            {
                triggers[i].Bind(null);
            }
        }

        private sealed class ReloadWarningSpy : IDisposable
        {
            private readonly object _gate = new object();
            private int _count;

            internal ReloadWarningSpy()
            {
                Application.logMessageReceived += OnLog;
            }

            internal int Count
            {
                get
                {
                    lock (_gate)
                    {
                        return _count;
                    }
                }
            }

            public void Dispose()
            {
                Application.logMessageReceived -= OnLog;
            }

            private void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Warning && condition.Contains("script reload during Play Mode"))
                {
                    lock (_gate)
                    {
                        _count++;
                    }
                }
            }
        }
    }
}
#endif
