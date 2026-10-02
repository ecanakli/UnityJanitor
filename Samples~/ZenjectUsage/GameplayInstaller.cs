using Ecanakli.Janitor.DependencyInjection;
using Zenject;

namespace Ecanakli.Janitor.Samples.ZenjectUsage
{
    /// <summary>The scene installer: the lifetime binding, the SignalBus, one signal and the sample's services.</summary>
    public sealed class GameplayInstaller : MonoInstaller
    {
        public override void InstallBindings()
        {
            LifetimeInstaller.Install(Container);

            SignalBusInstaller.Install(Container);
            Container.DeclareSignal<StoreRefreshedSignal>().OptionalSubscriber();

            Container.Bind<GameplayLifetimes>().AsSingle();
            Container.Bind<Wallet>().AsSingle();
            Container.Bind<OfferService>().AsSingle();
            Container.BindInterfacesAndSelfTo<FakeAdsSdk>().AsSingle();
            Container.BindFactory<OfferWatcher, OfferWatcher.Factory>();

            // Starts its own timer on its injected lifetime in Initialize(); the scene lifetime ends the timer.
            Container.BindInterfacesAndSelfTo<StoreRefresher>().AsSingle();
        }
    }
}
