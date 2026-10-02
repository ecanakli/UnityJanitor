using Ecanakli.Janitor;
using Zenject;

namespace Ecanakli.Janitor.Samples.ZenjectUsage
{
    /// <summary>A plain service that starts its own timer and fires a signal from it; the scene lifetime ends the timer.</summary>
    public sealed class StoreRefresher : IInitializable
    {
        private readonly Lifetime _lifetime;   // an area under the scene lifetime, injected by LifetimeInstaller
        private readonly SignalBus _signalBus;

        public StoreRefresher(Lifetime lifetime, SignalBus signalBus)
        {
            _lifetime = lifetime;
            _signalBus = signalBus;
        }

        // Zenject builds no service while it validates a scene, so starting work here is safe.
        public void Initialize() => _lifetime.Every(30f, Refresh);

        private void Refresh() => _signalBus.Fire(new StoreRefreshedSignal());
    }
}
