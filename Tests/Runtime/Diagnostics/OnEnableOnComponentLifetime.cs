using UnityEngine;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // Registers in OnEnable on its component lifetime, which does not follow activation: every enable adds one more entry.
    [AddComponentMenu("")]
    internal sealed class OnEnableOnComponentLifetime : MonoBehaviour
    {
        private void OnEnable()
        {
            this.OnCancel(Noop);
        }

        private static void Noop()
        {
        }
    }
}
