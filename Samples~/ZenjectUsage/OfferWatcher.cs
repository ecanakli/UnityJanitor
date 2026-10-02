using System;
using Ecanakli.Janitor;
using UnityEngine;
using Zenject;

namespace Ecanakli.Janitor.Samples.ZenjectUsage
{
    /// <summary>A plain object that a factory creates per session; it disposes its injected lifetime when it is done.</summary>
    public sealed class OfferWatcher : IDisposable
    {
        private readonly Lifetime _lifetime;   // an area under the scene lifetime; only Dispose ends it before the scene does
        private int _checks;

        public OfferWatcher(Lifetime lifetime) => _lifetime = lifetime;

        public void Start() => _lifetime.Every(1f, Check);

        // Without this the area stays in the tree, one more per instance, until the scene ends.
        public void Dispose() => _lifetime.Dispose();

        private void Check() => Debug.Log($"[OfferWatcher] Check {++_checks}.");

        public sealed class Factory : PlaceholderFactory<OfferWatcher>
        {
        }
    }
}
