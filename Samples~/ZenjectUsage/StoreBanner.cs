#pragma warning disable CS0649 // Assigned by Zenject.

using Ecanakli.Janitor;
using TMPro;
using UnityEngine;
using Zenject;

namespace Ecanakli.Janitor.Samples.ZenjectUsage
{
    /// <summary>A banner that counts store refreshes for as long as the component exists, popup open or not.</summary>
    public sealed class StoreBanner : MonoBehaviour
    {
        [Inject] private SignalBus _signalBus;

        // A MonoBehaviour gets its own component lifetime, the same as GetLifetime(); it ends when the component is destroyed.
        [Inject] private Lifetime _lifetime;

        private TMP_Text _label;
        private int _refreshes;

        // The view arrives here because the demo builds its UI in code.
        public void Initialize(TMP_Text label) => _label = label;

        private void Awake()
        {
            // The handler form that receives the signal.
            _signalBus.Subscribe<StoreRefreshedSignal>(OnStoreRefreshed, _lifetime);
        }

        private void OnStoreRefreshed(StoreRefreshedSignal signal)
        {
            _refreshes++;
            _label.text = $"Store refreshed {_refreshes} time(s)";
        }
    }
}
