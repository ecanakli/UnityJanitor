using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;
using Zenject;

namespace Ecanakli.Janitor.DependencyInjection
{
    // Disposed first among the scene container's disposables (LifetimeInstaller binds it with the highest priority), so the
    // scene lifetime ends before any service disposes. This is the second fallback when the game skips SceneLifetimes.Dispose;
    // when it finds the scene lifetime still alive it logs the JANITOR104 hint, once, then disposes it.
    // Dispose never throws: DisposableManager skips every remaining disposable after one throw.
    [Preserve]
    internal sealed class SceneContextLifetimeDisposer : IDisposable
    {
        private readonly Scene _scene;
        private readonly string _sceneName;
        private bool _disposed;

        // The scene is captured now: the context's GameObject is being destroyed when Dispose runs.
        // Only Zenject's reflection calls this constructor, so it is preserved explicitly for managed stripping.
        [Preserve]
        public SceneContextLifetimeDisposer(SceneContext sceneContext)
        {
            _scene = sceneContext.gameObject.scene;
            _sceneName = _scene.name;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                // An unloaded scene is handled by the sceneUnloaded fallback in the core.
                if (_scene.IsValid())
                {
                    ReportSkippedDispose();
                    SceneLifetimes.Dispose(_scene);
                }
            }
            catch (Exception exception)
            {
                ZenjectDiagnostics.Route(exception, LifetimeErrorSource.CancelAction, _sceneName, nameof(Dispose), 0);
            }
        }

        // JANITOR104: the scene lifetime is still alive, so the one-line call was skipped. Not at application exit, where
        // everything ends together, and not for a DontDestroyOnLoad scene (its lifetime is App, which is never disposed here).
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        private void ReportSkippedDispose()
        {
            try
            {
                if (Application.exitCancellationToken.IsCancellationRequested)
                {
                    return;
                }

                var lifetime = SceneLifetimes.Get(_scene);
                if (!lifetime.IsDisposed && !ReferenceEquals(lifetime, Lifetime.App))
                {
                    ZenjectDiagnostics.SceneDisposedLate(lifetime);
                }
            }
            catch (Exception)
            {
                // A diagnostic must never stop the disposal that follows it.
            }
        }
    }
}
