using Ecanakli.Janitor;
using TMPro;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>A label that follows the wallet for as long as the component exists.</summary>
    public sealed class CoinsLabel : MonoBehaviour
    {
        private Wallet _wallet;
        private TMP_Text _label;

        // Call it before the object is enabled, so Awake sees the wallet.
        public void Initialize(Wallet wallet, TMP_Text label)
        {
            _wallet = wallet;
            _label = label;
        }

        private void Awake()
        {
            OnCoinsChanged(_wallet.Coins);
            _wallet.CoinsChanged.Subscribe(OnCoinsChanged, this);   // owned by the component: it ends with it, not with SetActive
        }

        private void OnCoinsChanged(int coins) => _label.text = $"Wallet: {coins}";
    }
}
