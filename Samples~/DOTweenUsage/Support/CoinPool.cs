using System;
using System.Collections.Generic;

namespace Ecanakli.Janitor.Samples.DOTweenUsage
{
    /// <summary>Stand-in for the game's SetActive pool: it deactivates a coin on release and reuses it on rent.</summary>
    public sealed class CoinPool
    {
        private readonly Stack<CoinPickup> _free = new();
        private readonly HashSet<CoinPickup> _held = new();
        private readonly Func<CoinPickup> _create;

        public CoinPool(Func<CoinPickup> create) => _create = create;

        // A new coin comes back inactive; Launch activates it.
        public CoinPickup Rent()
        {
            while (_free.Count > 0)
            {
                var coin = _free.Pop();
                _held.Remove(coin);
                if (coin != null)
                {
                    return coin;           // a coin that was destroyed while pooled is skipped
                }
            }

            return _create();
        }

        // Safe to call twice for one coin: the pool ignores a coin it already holds.
        public void Release(CoinPickup coin)
        {
            if (!_held.Add(coin))
            {
                return;
            }

            if (coin.gameObject.activeSelf)    // not activeInHierarchy: a coin hidden by its parent must be switched off too
            {
                coin.gameObject.SetActive(false);
            }

            _free.Push(coin);
        }
    }
}
