using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.ZenjectUsage
{
    /// <summary>Stand-in for the game's store code: a fake fetch and a few console lines.</summary>
    public sealed class OfferService
    {
        private readonly Wallet _wallet;

        public OfferService(Wallet wallet) => _wallet = wallet;

        public async UniTask<IReadOnlyList<string>> FetchAsync(CancellationToken ct)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(1.5), cancellationToken: ct);
            return new[] { "Coin pack S", "Coin pack M", "Coin pack L" };
        }

        public void Show(IReadOnlyList<string> offers) => Debug.Log($"[OfferService] Showing {offers.Count} offers.");

        public void Invalidate() => Debug.Log("[OfferService] Offers invalidated.");

        public void BuySelected()
        {
            Debug.Log("[OfferService] Bought a coin pack.");
            _wallet.Add(100);
        }
    }
}
