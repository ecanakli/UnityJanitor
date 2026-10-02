using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace Ecanakli.Janitor
{
    // Console warnings compiled out of release builds. Ids are the stable JANITOR1xx diagnostics (DiagnosticIds).
    // Each warning is also recorded for the editor window; that call is conditional on UNITY_EDITOR, so it never runs in a player.
    internal static class DevWarnings
    {
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        internal static void DisposeIgnored(Lifetime lifetime)
        {
            var message = "Dispose() was ignored on the package-owned lifetime '" + lifetime.Label + "'. Use Cancel() instead.";
            Debug.LogWarning(Format(DiagnosticIds.DisposeIgnored, message));
            LifetimeDiagnostics.Record(DiagnosticIds.DisposeIgnored, lifetime, message);
        }

        // Raised on the calling thread, which is a worker thread here; the record is thread safe.
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        internal static void Marshalled(string operation, Lifetime lifetime)
        {
            var message = operation + " was called off the main thread and is marshalled to the next main-thread tick.";
            Debug.LogWarning(Format(DiagnosticIds.Marshalled, message));
            LifetimeDiagnostics.Record(DiagnosticIds.Marshalled, lifetime, message);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        internal static void DuplicateSubscription(string eventLabel, Lifetime owner)
        {
            var message = "A duplicate subscription was ignored: " + eventLabel + " already has this handler for the lifetime '" + owner.Label + "'.";
            Debug.LogWarning(Format(DiagnosticIds.DuplicateSubscription, message));
            LifetimeDiagnostics.Record(DiagnosticIds.DuplicateSubscription, owner, message);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        internal static void SceneUnloadedWithoutDispose(Lifetime scene)
        {
            var message = "The scene '" + Describe(scene) + "' was unloaded without SceneLifetimes.Dispose(scene) or DisposeAll(), so its lifetime was disposed after Unity had already destroyed its objects. Call SceneLifetimes.DisposeAll() right before a Single-mode load, or Dispose(scene) right before UnloadSceneAsync.";
            Debug.LogWarning(Format(DiagnosticIds.SceneDisposedLate, message));
            LifetimeDiagnostics.Record(DiagnosticIds.SceneDisposedLate, scene, message);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        internal static void RegistrationOnInactiveObject(Lifetime lifetime)
        {
            var message = "A registration on the active lifetime '" + Describe(lifetime) + "' was terminated immediately because its GameObject is inactive. Register in OnEnable or later.";
            Debug.LogWarning(Format(DiagnosticIds.RegistrationOnInactiveObject, message), lifetime.OwnerObject);
            LifetimeDiagnostics.Record(DiagnosticIds.RegistrationOnInactiveObject, lifetime, message, lifetime.OwnerObject);
        }

        // Unity logs an error for a coroutine on an inactive host; the pre-check replaces it with this warning.
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        internal static void CoroutineNotStarted(Lifetime lifetime, UnityEngine.MonoBehaviour host, string member, int line)
        {
            var missing = ReferenceEquals(host, null);
            var destroyed = !missing && host == null;
            var reason = missing ? "is null" : destroyed ? "was destroyed" : "'" + host.name + "' is inactive";
            var message = "A coroutine requested by " + member + ":" + line + " on the lifetime '" + Describe(lifetime) + "' was not started because its host " + reason
                + ". A coroutine needs an active host; start it from an active MonoBehaviour, usually 'this', in OnEnable or later.";
            UnityEngine.Object context = missing || destroyed ? null : host;
            Debug.LogWarning(Format(DiagnosticIds.CoroutineNotStarted, message), context);
            LifetimeDiagnostics.Record(DiagnosticIds.CoroutineNotStarted, lifetime, message, context, member, line);
        }

        // Only while the scene is still loaded; a registration after the unload finished is expected teardown noise.
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        internal static void RegistrationOnDisposedScene(Lifetime scene)
        {
            if (!SceneBinding.IsLoaded(scene.SceneKey))
            {
                return;
            }

            var message = "A registration on the disposed scene lifetime '" + Describe(scene) + "' was terminated immediately while the scene is still loaded. Disposal is terminal; register only after the scene has been replaced.";
            Debug.LogWarning(Format(DiagnosticIds.RegistrationOnDisposedScene, message));
            LifetimeDiagnostics.Record(DiagnosticIds.RegistrationOnDisposedScene, scene, message);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        internal static void ParentMismatch(Lifetime existing, Lifetime requested)
        {
            // A re-homed lifetime can never be placed again, so this is raised once per lifetime with that cause.
            if (existing.Rehomed)
            {
                if (existing.RehomedWarned)
                {
                    return;
                }

                existing.RehomedWarned = true;
                var rehomed = "The lifetime of '" + Describe(existing) + "' was moved under '" + Describe(existing.Parent)
                    + "' when its category was disposed, and a re-homed lifetime cannot be placed again. The request to place it under '"
                    + Describe(requested) + "' is ignored; the object keeps working under its scene.";
                Debug.LogWarning(Format(DiagnosticIds.ParentMismatch, rehomed), existing.OwnerObject);
                LifetimeDiagnostics.Record(DiagnosticIds.ParentMismatch, existing, rehomed, existing.OwnerObject);
                return;
            }

            var message = "GetLifetime(parent) or GetActiveLifetime(parent) was called for '" + Describe(existing) + "' after its lifetime already existed under '" + Describe(existing.Parent)
                + "'. The existing parent is kept and '" + Describe(requested) + "' is ignored. Call it before anything else touches this object's lifetime, usually the first line of Awake or OnEnable.";
            Debug.LogWarning(Format(DiagnosticIds.ParentMismatch, message), existing.OwnerObject);
            LifetimeDiagnostics.Record(DiagnosticIds.ParentMismatch, existing, message, existing.OwnerObject);
        }

        // The object's lifetime does not exist yet, so the record is attached to the object only.
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        internal static void ParentDisposed(UnityEngine.Object owner, Lifetime requested)
        {
            var message = "The parent lifetime '" + Describe(requested) + "' passed to GetLifetime(parent) or GetActiveLifetime(parent) is already disposed. The lifetime of '" + owner.name + "' was created under its scene lifetime instead.";
            Debug.LogWarning(Format(DiagnosticIds.ParentMismatch, message), owner);
            LifetimeDiagnostics.Record(DiagnosticIds.ParentMismatch, null, message, owner);
        }

        // Also used for diagnostics that log in every build (JANITOR115).
        internal static string Format(string id, string message)
        {
            return "[" + id + "] " + message + " (see Troubleshooting#" + id.ToLowerInvariant() + ")";
        }

        private static string Describe(Lifetime lifetime)
        {
            return lifetime?.Label ?? "unnamed";
        }
    }
}
