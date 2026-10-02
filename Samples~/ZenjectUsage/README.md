# Zenject Usage

Requires the package's Zenject integration to be active
(`Ecanakli.Janitor.DependencyInjection.Zenject`, gated behind `ECANAKLI_JANITOR_DI_ZENJECT`). Also needs the uGUI
package (`com.unity.ugui`, which includes TextMeshPro). No DOTween.

## Setup

1. If your Zenject/Extenject install lives under `Assets` (not UPM/OpenUPM), run `Tools/Janitor/Zenject Integration`
   once and commit the resulting `ProjectSettings` change. Installs via UPM or OpenUPM define the symbol
   automatically through `versionDefines` and need no menu step.
2. Import TextMeshPro's Essential Resources once: `Window > TextMeshPro > Import TMP Essential Resources`.
3. Open an empty scene (`File > New Scene`) and add a Scene Context with `GameObject > Zenject > Scene Context`.
4. Select the Scene Context object, add the `GameplayInstaller` component to it, and drag that component into the
   **Mono Installers** list of the Scene Context.
5. Create an empty GameObject and add the `ZenjectUsageDemo` component. The Scene Context injects it. If there is
   no Scene Context, or `GameplayInstaller` is not in its Mono Installers list, the demo logs one error that says
   which and stops.
6. Add an Event System with `GameObject > UI > Event System`. The demo creates none, because the right input
   module depends on your project's input handling; it logs one warning when the scene has no Event System. If
   the Input System package is your only input handling, use the button the Event System's Inspector offers to
   replace the input module.
7. Optional, for the **Reload scene** button (it is disabled and says why until both parts exist):
   - Create a ProjectContext with `Edit > Zenject > Create Project Context`, add the `ProjectInstaller` component
     to the prefab it creates in `Resources`, and drag it into the prefab's **Mono Installers** list.
   - Add the scene to the Build Settings scene list.
8. Press Play and open `Window > Analysis > Janitor`. The demo picks a landscape or a portrait layout from the shape of the Game view when Play starts, and stays centered at any size.

## What to do

- **Shop: open / close** opens `ShopPopup`. Its row appears under `Popups`, which belongs to the injected
  `GameplayLifetimes`. It holds a coroutine, a task and three subscriptions: the button, a
  `Signal` (the SignalBus subscription; a handler that takes the signal shows as
  `Signal<StoreRefreshedSignal>`) and the wallet's owned event.
- **Fire StoreRefreshedSignal** with the popup open makes `OfferService` log "Offers invalidated". With the popup
  closed nothing is logged: the SignalBus subscription ended with `SetActive(false)`, and Zenject stays quiet
  because the signal is declared with `OptionalSubscriber()`.
- **Add 25 coins** updates the HUD. With the popup open its coin label follows; with the popup closed it does
  not.
- **Cancel popups** with the popup open calls `GameplayLifetimes.Popups.Cancel()`. The popup stays on screen, but
  its row drops to zero entries: the signal no longer reaches it, the badge stops blinking and the coin label
  stops following. Close and open it again and everything is registered again.
- **Ad: reward granted** and **Ad: closed** raise the fake SDK's events; `PlatformHooks` logs them. Its row holds
  the three paired subscriptions.
- **Reload scene** goes through `SceneFlow`: the loaded scene's lifetimes are disposed first, then the scene
  loads. The old scene's rows leave the window and `SceneFlow` stays under `App`.
- Opening the shop also makes `OfferWatch` create an `OfferWatcher` through its factory. The Console shows an
  `[OfferWatcher] Check n.` line every second while the popup is open. In the window an `OfferWatcher` area
  appears under the scene lifetime when the popup opens and leaves the tree when it closes, because the watcher
  disposes its injected lifetime.
- The **Store refreshed** label in the HUD is `StoreBanner`. **Fire StoreRefreshedSignal** increments it whether
  the popup is open or not, and **Cancel popups** does not touch it: its subscription belongs to its own component.

## What to look at

- `GameplayInstaller.cs` - `LifetimeInstaller.Install(Container)` in the scene context, the SignalBus install, the
  signal declaration, and `Container.Bind<GameplayLifetimes>().AsSingle()`. Janitor adds no SignalBus binding. If
  your ProjectContext already installs `SignalBusInstaller`, remove that line.
- `GameplayLifetimes.cs` - the categories service. Its constructor receives the injected `Lifetime`, a child of the
  scene lifetime, so both categories end with the scene.
- `StoreRefreshedSignal.cs` - the signal; `GameplayInstaller` declares it.
- `Wallet.cs` - an `OwnedEvent<int>` kept private and exposed as a subscribe-only `IOwnedEvent<int>`.
- `ShopPopup.cs` - `[Inject]` fields, then one `OnEnable` that registers a coroutine, a task, a button click, a
  SignalBus subscription (`_signalBus.Subscribe<StoreRefreshedSignal>(handler, shown)`) and an owned event on the
  category's active lifetime. There is no `OnDisable`.
- `PlatformHooks.cs` - the three paired `Subscribe` forms, with the SDK injected.
- `StoreBanner.cs` - `[Inject] Lifetime` on a MonoBehaviour, which is its own component lifetime and ends when the
  component is destroyed, although Zenject injects the object while it is inactive; and a SignalBus subscription
  with the `Action<TSignal>` handler form.
- `OfferWatcher.cs` and `OfferWatch.cs` - a plain class that a `PlaceholderFactory` creates per session. It injects a
  `Lifetime`, which is a new area under the scene lifetime that nothing ends but the scene, so the class
  implements `IDisposable` and disposes it. `GameplayInstaller` binds the factory with
  `Container.BindFactory<OfferWatcher, OfferWatcher.Factory>()`. `OfferWatch` is the owner: it creates the
  watcher in `OnEnable` and hands it to `AddTo(activeLifetime)`, so it is disposed when the object is deactivated.
- `SceneFlow.cs` and `ProjectInstaller.cs` - `SceneFlow` is a ProjectContext service. `LifetimeInstaller.Install` in
  the ProjectContext captures `Lifetime.App`, so the lifetime injected into `SceneFlow` is a child of `App` and
  outlives every scene. Bound in a SceneContext instead, it would be a child of that scene's lifetime:
  `SceneLifetimes.DisposeAll()` would end it, and the task that is loading the next scene would be cancelled while
  it awaits the load. That is why the sample has a second installer for a second context. `SceneFlow` refuses a
  scene that is not in the Build Settings before it disposes anything; a load that can fail at runtime should hold
  the scene activation and dispose right before it.
- `Support/` - stand-ins for the game's own code (`OfferService`, `IAdsSdk` and its fake).
- `Demo/` - the bootstrap and the uGUI builder. It creates the popup and the hooks through `IInstantiator`, so
  their `[Inject]` fields are filled, and its own button subscriptions use `Subscribe(handler, this)`.
- `StoreRefresher.cs` - a plain service bound with `BindInterfacesAndSelfTo<StoreRefresher>().AsSingle()` that
  injects `Lifetime` and `SignalBus`, implements `IInitializable` and starts an `Every` timer on its lifetime in
  `Initialize()`. The timer fires `StoreRefreshedSignal` every 30 seconds, so the HUD banner also counts up on its
  own, and the scene lifetime ends the timer. It is the one `Fire` outside the demo.

The documented classes receive their views through `Initialize(...)` because the demo builds its UI in code; in
a scene you would use `[SerializeField]` fields. Components that read their injected fields in `Awake` or
`OnEnable` are created inactive, injected, and then activated.
