using UnityEngine;
using Zenject;

#pragma warning disable CS0649 // Assigned by Zenject.

namespace Ecanakli.Janitor.DependencyInjection.Tests.PlayMode
{
    // A pooled item: it injects its component lifetime and registers its work on its active lifetime in OnEnable.
    [AddComponentMenu("")]
    internal sealed class PooledCoin : MonoBehaviour
    {
        [Inject]
        private Lifetime _lifetime;

        internal Lifetime Injected => _lifetime;

        // The active lifetime of the latest activation (the same object for the life of the item).
        internal Lifetime Active { get; private set; }

        internal int EnableCount { get; private set; }

        // How many times the registered work was stopped.
        internal int CancelCount { get; private set; }

        // Registrations that were not stopped yet.
        internal int Live => EnableCount - CancelCount;

        private void OnEnable()
        {
            EnableCount++;
            Active = this.GetActiveLifetime();
            Active.OnCancel(this, static coin => coin.CancelCount++);
        }

        internal sealed class Pool : MonoMemoryPool<PooledCoin>
        {
        }
    }
}
