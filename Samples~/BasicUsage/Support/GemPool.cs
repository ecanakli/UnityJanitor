using System;
using System.Collections.Generic;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>Stand-in for the game's SetActive pool of gems: it makes an object inactive, then hands it out.</summary>
    public sealed class GemPool
    {
        private readonly Stack<GemPickup> _free = new();
        private readonly HashSet<GemPickup> _held = new();
        private readonly Func<GemPickup> _create;

        public GemPool(Func<GemPickup> create) => _create = create;

        public GemPickup Rent()
        {
            while (_free.Count > 0)
            {
                var gem = _free.Pop();
                _held.Remove(gem);
                if (gem != null)
                {
                    return gem;            // a gem that was destroyed while pooled is skipped
                }
            }

            var created = _create();
            created.gameObject.SetActive(false);   // a prefab saved active arrives active; Launch switches it on
            return created;
        }

        // Safe to call twice for one gem: the pool ignores a gem it already holds.
        public void Release(GemPickup gem)
        {
            if (!_held.Add(gem))
            {
                return;
            }

            if (gem.gameObject.activeSelf)     // not activeInHierarchy: a gem hidden by its parent must be switched off too
            {
                gem.gameObject.SetActive(false);   // the gem's own cleanup calls Release again from inside this call
            }

            _free.Push(gem);
        }
    }
}
