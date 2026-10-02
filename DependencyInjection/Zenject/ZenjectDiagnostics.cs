using System;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Zenject;
using Debug = UnityEngine.Debug;

namespace Ecanakli.Janitor.DependencyInjection
{
    // Console warnings and error routing for this assembly. The core's DevWarnings is internal, so the message format is
    // repeated here; the ids come from the public DiagnosticIds. Each warning is also reported to the editor window
    // through the public LifetimeDiagnostics.Report hook, which compiles out of players.
    internal static class ZenjectDiagnostics
    {
        internal const string SceneDisposedLateId = DiagnosticIds.SceneDisposedLate;
        internal const string DuplicateSubscriptionId = DiagnosticIds.DuplicateSubscription;
        internal const string MissingSceneInstallId = DiagnosticIds.MissingSceneInstall;

        // Same text as the core's sceneUnloaded fallback: the game skipped the one-line call, and the Zenject disposer caught it.
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        internal static void SceneDisposedLate(Lifetime scene)
        {
            var message = "The scene '" + Describe(scene) + "' was unloaded without SceneLifetimes.Dispose(scene) or DisposeAll(), so its lifetime was disposed after Unity had already destroyed its objects. Call SceneLifetimes.DisposeAll() right before a Single-mode load, or Dispose(scene) right before UnloadSceneAsync.";
            Debug.LogWarning(Format(SceneDisposedLateId, message));
            LifetimeDiagnostics.Report(scene, SceneDisposedLateId, message);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        internal static void DuplicateSignalSubscription(Type signal, Lifetime owner)
        {
            var message = "A duplicate subscription was ignored: Signal<" + signal.Name + "> already has this handler for the lifetime '" + Describe(owner) + "'.";
            Debug.LogWarning(Format(DuplicateSubscriptionId, message));
            LifetimeDiagnostics.Report(owner, DuplicateSubscriptionId, message);
        }

        // A plain service of a SceneContext that never called LifetimeInstaller.Install got a lifetime of an outer context.
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        internal static void MissingSceneInstall(Type service, SceneContext sceneContext, Lifetime received)
        {
            var serviceName = service != null ? service.Name : "a direct Resolve<Lifetime>() call";
            var message = "'" + serviceName + "' in the scene '" + sceneContext.gameObject.scene.name + "' received a lifetime from '" + Describe(received)
                + "' instead of the scene lifetime, because its SceneContext never called LifetimeInstaller.Install(Container). Work registered on it would outlive the scene."
                + " Call LifetimeInstaller.Install(Container) from an installer of that SceneContext.";
            Debug.LogWarning(Format(MissingSceneInstallId, message), sceneContext);
            LifetimeDiagnostics.Report(received, MissingSceneInstallId, message, sceneContext);
        }

        // A plain service of a GameObjectContext that never called LifetimeInstaller.Install got a lifetime of an outer context.
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        internal static void MissingContextInstall(Type service, GameObjectContext objectContext, Lifetime received)
        {
            var serviceName = service != null ? service.Name : "a direct Resolve<Lifetime>() call";
            var message = "'" + serviceName + "' in the GameObjectContext '" + objectContext.gameObject.name + "' received the lifetime '" + Describe(received)
                + "' of an outer context, because that GameObjectContext never called LifetimeInstaller.Install(Container). Its plain services live as long as that lifetime (usually the scene), not as long as the GameObjectContext."
                + " Call LifetimeInstaller.Install(Container) from an installer of that GameObjectContext.";
            Debug.LogWarning(Format(MissingSceneInstallId, message), objectContext);
            LifetimeDiagnostics.Report(received, MissingSceneInstallId, message, objectContext);
        }

        internal static string Format(string id, string message)
        {
            return "[" + id + "] " + message + " (see Troubleshooting#" + id.ToLowerInvariant() + ")";
        }

        internal static void EnsureMainThread(string operation)
        {
            var mainThreadId = PlayerLoopHelper.MainThreadId;
            if (mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                throw new InvalidOperationException("Janitor: " + operation + " must be called on the main thread.");
            }
        }

        // Mirrors the core dispatch: cancellation is dropped, a failing handler is logged, nothing throws.
        internal static void Route(Exception exception, LifetimeErrorSource source, string lifetimeName, string member, int line)
        {
            if (exception == null || exception is OperationCanceledException)
            {
                return;
            }

            try
            {
                var context = new LifetimeErrorContext(source, null, lifetimeName, member, line);
                LifetimeErrors.Handler(exception, in context);
            }
            catch (Exception handlerFailure)
            {
                try
                {
                    Debug.LogException(handlerFailure);
                    Debug.LogException(exception);
                }
                catch
                {
                    // Logging itself failed; nothing left to report to.
                }
            }
        }

        private static string Describe(Lifetime lifetime)
        {
            return lifetime?.Name ?? "unnamed";
        }
    }
}
