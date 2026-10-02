#pragma warning disable CS0649 // Assigned by Zenject.

using Ecanakli.Janitor;
using UnityEngine;
using Zenject;

namespace Ecanakli.Janitor.Samples.ZenjectUsage
{
    /// <summary>Creates an OfferWatcher while its object is active and disposes it when the object is deactivated.</summary>
    public sealed class OfferWatch : MonoBehaviour
    {
        [Inject] private OfferWatcher.Factory _factory;

        private void OnEnable()
        {
            var shown = this.GetActiveLifetime();
            var watcher = _factory.Create();
            watcher.AddTo(shown);              // disposed when the lifetime ends, which also ends the watcher's own area
            watcher.Start();
        }
    }
}
