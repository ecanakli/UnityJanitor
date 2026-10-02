using System.Collections.Generic;

namespace Ecanakli.Janitor
{
    // Instance id to lifetime map for component, GameObject and trigger (active) lifetimes.
    internal sealed class OwnerIndex
    {
        private readonly Dictionary<int, Lifetime> _map = new Dictionary<int, Lifetime>();

        internal int Count => _map.Count;

        internal void Add(int key, Lifetime lifetime)
        {
            _map[key] = lifetime;
        }

        internal bool TryGet(int key, out Lifetime lifetime)
        {
            return _map.TryGetValue(key, out lifetime);
        }

        // Allocation-free walk for the scene pre-pass; the index must not change while it runs. Public so foreach binds to it.
        public Dictionary<int, Lifetime>.Enumerator GetEnumerator()
        {
            return _map.GetEnumerator();
        }

        // Removes only when the key still maps to expected, so a newer entry is never dropped.
        internal bool Remove(int key, Lifetime expected)
        {
            if (_map.TryGetValue(key, out var current) && ReferenceEquals(current, expected))
            {
                return _map.Remove(key);
            }

            return false;
        }
    }
}
