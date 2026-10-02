using UnityEngine;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // Registers in OnEnable on the lifetime of its GameObject, which does not follow activation either.
    [AddComponentMenu("")]
    internal sealed class OnEnableOnGameObjectLifetime : MonoBehaviour
    {
        private void OnEnable()
        {
            gameObject.GetLifetime().OnCancel(Noop);
        }

        private static void Noop()
        {
        }
    }
}
