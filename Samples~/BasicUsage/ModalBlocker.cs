using Ecanakli.Janitor;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>Holds an input block for as long as its active lifetime lasts: until deactivation, destroy or a Cancel.</summary>
    public sealed class ModalBlocker : MonoBehaviour
    {
        private InputGate _gate;

        // Call it before the object is enabled, so OnEnable sees the gate.
        public void Initialize(InputGate gate) => _gate = gate;

        private void OnEnable()
        {
            var shown = this.GetActiveLifetime();
            _gate.Block().AddTo(shown);         // an IDisposable owned by the lifetime: disposed when it ends
        }
    }
}
