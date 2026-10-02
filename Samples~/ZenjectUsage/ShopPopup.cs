#pragma warning disable CS0649 // Assigned by Zenject.

using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.Janitor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace Ecanakli.Janitor.Samples.ZenjectUsage
{
    /// <summary>A popup whose work stops on SetActive(false), on a Popups cancel and on destroy.</summary>
    public sealed class ShopPopup : MonoBehaviour
    {
        [Inject] private SignalBus _signalBus;
        [Inject] private Wallet _wallet;
        [Inject] private OfferService _offers;
        [Inject] private GameplayLifetimes _lifetimes;

        private Button _buyButton;
        private Graphic _saleBadge;
        private TMP_Text _coinsLabel;

        // The views arrive here because the demo builds its UI in code; the services above are injected.
        public void Initialize(Button buyButton, Graphic saleBadge, TMP_Text coinsLabel)
        {
            _buyButton = buyButton;
            _saleBadge = saleBadge;
            _coinsLabel = coinsLabel;
        }

        private void OnEnable()
        {
            // Cancelled by SetActive(false) or _lifetimes.Popups.Cancel(); disposed on destroy.
            var shown = this.GetActiveLifetime(_lifetimes.Popups);

            _coinsLabel.text = _wallet.Coins.ToString();
            shown.StartCoroutine(this, BlinkBadge());
            shown.Run(LoadOffersAsync);
            _buyButton.onClick.Subscribe(OnBuyClicked, shown);
            _signalBus.Subscribe<StoreRefreshedSignal>(OnStoreRefreshed, shown);
            _wallet.CoinsChanged.Subscribe(OnCoinsChanged, shown);
        }

        private async UniTask LoadOffersAsync(CancellationToken ct)
        {
            var offers = await _offers.FetchAsync(ct);
            _offers.Show(offers);
        }

        private IEnumerator BlinkBadge()
        {
            while (true)
            {
                _saleBadge.enabled = !_saleBadge.enabled;
                yield return new WaitForSeconds(0.5f);
            }
        }

        private void OnBuyClicked() => _offers.BuySelected();
        private void OnStoreRefreshed() => _offers.Invalidate();
        private void OnCoinsChanged(int coins) => _coinsLabel.text = coins.ToString();
    }
}
