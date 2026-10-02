using System.Diagnostics;

namespace Ecanakli.Janitor
{
    // Editor-only recording points for the Unity binding.
    internal static class UnityBindingHooks
    {
        // JANITOR114 (info): a category was disposed while this placed object lifetime lives, so it was re-homed.
        [Conditional("UNITY_EDITOR")]
        internal static void Rehomed(Lifetime lifetime)
        {
#if UNITY_EDITOR
            LifetimeDiagnostics.RecordRehomed(lifetime);
#endif
        }
    }
}
