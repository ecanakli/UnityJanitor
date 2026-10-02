using System.Collections.Generic;

namespace Ecanakli.Janitor
{
    // Scene handle to lifetime map plus the handles known to be persistent (DontDestroyOnLoad, preview scenes).
    // A disposed scene lifetime stays mapped until the scene is unloaded, so later registrations on it are refused.
    internal sealed class SceneIndex
    {
        private readonly Dictionary<int, Lifetime> _map = new Dictionary<int, Lifetime>();
        private readonly HashSet<int> _persistent = new HashSet<int>();

        internal int Count => _map.Count;

        internal void Add(int handle, Lifetime lifetime)
        {
            _map[handle] = lifetime;
        }

        internal bool TryGet(int handle, out Lifetime lifetime)
        {
            return _map.TryGetValue(handle, out lifetime);
        }

        // Removes only when the handle still maps to expected.
        internal bool Remove(int handle, Lifetime expected)
        {
            if (_map.TryGetValue(handle, out var current) && ReferenceEquals(current, expected))
            {
                return _map.Remove(handle);
            }

            return false;
        }

        internal void MarkPersistent(int handle)
        {
            _persistent.Add(handle);
        }

        internal bool IsPersistent(int handle)
        {
            return _persistent.Contains(handle);
        }

        internal void ClearPersistent(int handle)
        {
            _persistent.Remove(handle);
        }
    }
}
