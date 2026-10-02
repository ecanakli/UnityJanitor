using UnityEngine;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // Registers in OnEnable on its active lifetime, which is cancelled on every deactivation: nothing doubles.
    [AddComponentMenu("")]
    internal sealed class OnEnableOnActiveLifetime : MonoBehaviour
    {
        private void OnEnable()
        {
            this.GetActiveLifetime().OnCancel(Noop);
        }

        private static void Noop()
        {
        }
    }
}
