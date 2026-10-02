using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.Janitor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>A popup whose work stops on SetActive(false), on a Popups cancel and on destroy.</summary>
    public sealed class ShopPopup : MonoBehaviour
    {
        private Button _buyButton;
        private Graphic _saleBadge;
        private TMP_Text _coinsLabel;
        private GameplayLifetimes _lifetimes;
        private Wallet _wallet;
        private OfferService _offers;

        // Arrives here instead of [Inject] and the Inspector; call it before the object is enabled.
        public void Initialize(
            GameplayLifetimes lifetimes,
            Wallet wallet,
            OfferService offers,
            Button buyButton,
            Graphic saleBadge,
            TMP_Text coinsLabel)
        {
            _lifetimes = lifetimes;
            _wallet = wallet;
            _offers = offers;
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
        private void OnCoinsChanged(int coins) => _coinsLabel.text = coins.ToString();
    }
}
