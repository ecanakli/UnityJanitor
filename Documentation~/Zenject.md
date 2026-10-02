# Zenject

[Back to index](index.md)

This page covers the optional Zenject / Extenject integration: how it turns on,
`LifetimeInstaller.Install(Container)`, which lifetime each injectee receives, the categories
service bound in a context, owner-bound `SignalBus` subscriptions, the scene disposer, and where to
bind services that must outlive scenes.

The integration adds no new concepts. An injected `Lifetime` is an ordinary lifetime from
[Concepts](Concepts.md); the installer only decides where in the tree it hangs.

---

## How the integration turns on

The integration is a separate assembly, `Ecanakli.Janitor.DependencyInjection.Zenject`. Unity
compiles it only when the scripting define `ECANAKLI_JANITOR_DI_ZENJECT` exists. How the define gets
there depends on how Zenject is installed:

- **Extenject as a package** (`com.svermeulen.extenject`, version 9.0.0 or newer, from UPM or
  OpenUPM): the assembly sets the define for itself through `versionDefines`. There is nothing to
  do.
- **Zenject copied under `Assets/`:** choose `Tools/Janitor/Zenject Integration` once. The menu item
  adds the define to the Player Settings of **every** build target, and it is ticked while the
  integration is active. Commit the `ProjectSettings` change. Choosing it again removes the define.

The menu item protects against two mistakes. If no assembly named `Zenject` exists in the project it
refuses to turn the integration on and says so. If Extenject is installed as a package it explains
that there is nothing to toggle.

If Zenject is removed while the define is still set, the integration assembly can no longer
compile. Turn the menu item off, or delete `ECANAKLI_JANITOR_DI_ZENJECT` from the scripting define
symbols by hand.

Namespaces and references:

- `LifetimeInstaller` is in `Ecanakli.Janitor.DependencyInjection`.
- The `SignalBus` extension methods are in `Ecanakli.Janitor`, next to every other `Subscribe`.
- If your code sits in an assembly definition, reference `Ecanakli.Janitor`,
  `Ecanakli.Janitor.DependencyInjection.Zenject` and `Zenject`.

## The API

<!-- signature -->
```csharp
namespace Ecanakli.Janitor.DependencyInjection
{
    public static class LifetimeInstaller
    {
        public static void Install(DiContainer container);
    }
}

namespace Ecanakli.Janitor
{
    public static class LifetimeSignalBusExtensions
    {
        public static LifetimeRegistration Subscribe<TSignal>(this SignalBus bus, Action<TSignal> handler, Lifetime owner);
        public static LifetimeRegistration Subscribe<TSignal>(this SignalBus bus, Action handler, Lifetime owner);
        public static LifetimeRegistration Subscribe<TSignal>(this SignalBus bus, Action<TSignal> handler, Component owner);
        public static LifetimeRegistration Subscribe<TSignal>(this SignalBus bus, Action handler, Component owner);
    }
}
```

---

## A popup in a scene context

### Before

A popup that starts a coroutine and a load, and listens to a button, a signal and a wallet event
(`Wallet.CoinsChanged` is a plain C# `event` here). Everything is undone by hand:

<!-- illustrative: before -->
```csharp
public sealed class ShopPopup : MonoBehaviour
{
    [SerializeField] private Button _buyButton;
    [SerializeField] private Graphic _saleBadge;
    [SerializeField] private TMP_Text _coinsLabel;
    [Inject] private SignalBus _signalBus;
    [Inject] private Wallet _wallet;
    [Inject] private OfferService _offers;

    private Coroutine _badgeBlink;
    private CancellationTokenSource _loadCts;

    private void OnEnable()
    {
        _badgeBlink = StartCoroutine(BlinkBadge());
        _loadCts = new CancellationTokenSource();
        LoadOffersAsync(_loadCts.Token).Forget();
        _buyButton.onClick.AddListener(OnBuyClicked);
        _signalBus.Subscribe<StoreRefreshedSignal>(OnStoreRefreshed);
        _wallet.CoinsChanged += OnCoinsChanged;
    }

    // Seven lines that must mirror OnEnable exactly; forgetting one leaks or throws.
    private void OnDisable()
    {
        if (_badgeBlink != null) StopCoroutine(_badgeBlink);
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        _buyButton.onClick.RemoveListener(OnBuyClicked);
        _signalBus.TryUnsubscribe<StoreRefreshedSignal>(OnStoreRefreshed);
        _wallet.CoinsChanged -= OnCoinsChanged;
    }

    private async UniTask LoadOffersAsync(CancellationToken ct)
    {
        var offers = await _offers.FetchAsync(ct);
        _offers.Show(offers);
    }

    private IEnumerator BlinkBadge()
    {
        while (true)
        {
            _saleBadge.enabled = !_saleBadge.enabled;
            yield return new WaitForSeconds(0.5f);
        }
    }

    private void OnBuyClicked() => _offers.BuySelected();
    private void OnStoreRefreshed() => _offers.Invalidate();
    private void OnCoinsChanged(int coins) => _coinsLabel.text = coins.ToString();
}
```

Every line of `OnDisable` is a line that can be forgotten. Take the signal: without
`TryUnsubscribe`, the handler keeps running on a closed popup, the next `OnEnable` subscribes the
same handler a second time (which SignalBus rejects), and a bus configured to require strict
unsubscription throws when the scene is torn down. And nothing here lets another class say "close
the work of every popup" without knowing each popup.

### After

Three files replace it. The installer:

<!-- source: Samples~/ZenjectUsage/GameplayInstaller.cs -->
```csharp
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
```

The last two bindings are explained further down: the factory under
[Objects created at run time](#objects-created-at-run-time), and `StoreRefresher` under
[A service that starts its own work](#a-service-that-starts-its-own-work).

The categories service, a plain class that receives its lifetime in the constructor:

<!-- source: Samples~/ZenjectUsage/GameplayLifetimes.cs -->
```csharp
public sealed class GameplayLifetimes
{
    public Lifetime Popups { get; }
    public Lifetime Combat { get; }

    public GameplayLifetimes(Lifetime life)
    {
        Popups = life.CreateChild("Popups");
        Combat = life.CreateChild("Combat");
    }
}
```

And the popup, with no `OnDisable`:

<!-- source: Samples~/ZenjectUsage/ShopPopup.cs -->
```csharp
public sealed class ShopPopup : MonoBehaviour
{
    [Inject] private SignalBus _signalBus;
    [Inject] private Wallet _wallet;
    [Inject] private OfferService _offers;
    [Inject] private GameplayLifetimes _lifetimes;

    private Button _buyButton;
    private Graphic _saleBadge;
    private TMP_Text _coinsLabel;

    // The views arrive here because the demo builds its UI in code; the services above are injected.
    public void Initialize(Button buyButton, Graphic saleBadge, TMP_Text coinsLabel)
    {
        _buyButton = buyButton;
        _saleBadge = saleBadge;
        _coinsLabel = coinsLabel;
    }

    private void OnEnable()
    {
        // Cancelled by SetActive(false) or _lifetimes.Popups.Cancel(); disposed on destroy.
        var shown = this.GetActiveLifetime(_lifetimes.Popups);

        _coinsLabel.text = _wallet.Coins.ToString();
        shown.StartCoroutine(this, BlinkBadge());
        shown.Run(LoadOffersAsync);
        _buyButton.onClick.Subscribe(OnBuyClicked, shown);
        _signalBus.Subscribe<StoreRefreshedSignal>(OnStoreRefreshed, shown);
        _wallet.CoinsChanged.Subscribe(OnCoinsChanged, shown);
    }

    private async UniTask LoadOffersAsync(CancellationToken ct)
    {
        var offers = await _offers.FetchAsync(ct);
        _offers.Show(offers);
    }

    private IEnumerator BlinkBadge()
    {
        while (true)
        {
            _saleBadge.enabled = !_saleBadge.enabled;
            yield return new WaitForSeconds(0.5f);
        }
    }

    private void OnBuyClicked() => _offers.BuySelected();
    private void OnStoreRefreshed() => _offers.Invalidate();
    private void OnCoinsChanged(int coins) => _coinsLabel.text = coins.ToString();
}
```

What happens at each moment:

1. **The scene context installs.** `LifetimeInstaller.Install(Container)` sees that this container
   belongs to a `SceneContext`, captures the lifetime of that scene, binds `Lifetime` in the
   container, and binds the scene disposer described [below](#the-scene-disposer).
2. **`GameplayLifetimes` is created** the first time something asks for it. Its constructor
   parameter is filled with a new lifetime: a child of the scene lifetime, named
   `GameplayLifetimes`. The constructor creates `Popups` and `Combat` under it. The tree now reads
   `App > (scene) > GameplayLifetimes > Popups, Combat`.
3. **The popup is activated.** `this.GetActiveLifetime(_lifetimes.Popups)` returns the popup's
   active lifetime and, because this is the first access, places it under `Popups`. The coroutine,
   the task and the three subscriptions are registered on it. The signal line subscribes the handler
   on the bus and records, on `shown`, that it must be unsubscribed.
4. **`SetActive(false)`.** The active lifetime is cancelled. In this order: its token is cancelled,
   so `LoadOffersAsync` stops at its await if it is still running; then its items end, newest first:
   the wallet subscription, the signal subscription (`TryUnsubscribe`), the button listener, the
   task entry and the coroutine. A signal fired now does not reach the popup.
5. **`SetActive(true)` again.** `OnEnable` runs in a fresh generation and registers everything
   again. Nothing from the previous activation is left, so nothing is doubled.
6. **`_lifetimes.Popups.Cancel()` from any class.** The same stop as step 4 reaches every popup
   placed in `Popups`, while the popups stay on screen. `Combat` is not touched. A popup registers
   its work again the next time it is enabled.
7. **`Destroy(popup)`.** The active lifetime is disposed, and the popup leaves `Popups`.
8. **The scene changes after `SceneLifetimes.DisposeAll()`.** The scene lifetime is disposed with
   everything under it: the `GameplayLifetimes` lifetime, both categories and every popup placed in
   them. All of that work stops before Unity destroys the first object.
9. **The scene changes without that call.** See [the scene disposer](#the-scene-disposer).

---

## Install in every context

Call `LifetimeInstaller.Install(Container)` from one installer of **each** context that injects a
`Lifetime`: the ProjectContext, every SceneContext, and every GameObjectContext whose plain services
inject a `Lifetime`.

The reason is that the lifetime is captured per container, at install time:

| Context whose installer calls `Install` | Lifetime captured | It ends when |
|---|---|---|
| ProjectContext | `Lifetime.App` | The application or Play Mode exits |
| SceneContext | The lifetime of that scene, `SceneLifetimes.Get(scene)` | The scene lifetime is disposed |
| GameObjectContext | The component lifetime of the context component | That component is destroyed, or its scene lifetime is disposed |
| A sub-container that is not a context | The lifetime of the nearest enclosing context, or `Lifetime.App` when there is none | As above |

Zenject resolves a binding from the nearest container that has it. So when a scene installer does
**not** call `Install`, the scene's services silently fall through to the ProjectContext's binding
and receive children of `Lifetime.App`. Work registered on those lifetimes outlives the scene, which
is the very thing the package exists to prevent.

A GameObjectContext has the same problem one step further in. Without its own `Install`, its plain
services fall through to the scene's binding and receive children of the scene lifetime. Their work
then lasts as long as the scene and keeps running after the GameObjectContext was destroyed.

In the editor and in development builds both cases are reported as
[JANITOR112](Troubleshooting.md#janitor112): once per scene for a SceneContext, and once per
GameObject name for a GameObjectContext, so a prefab that is spawned many times is reported once.

Other properties of `Install`:

- **Calling it twice on the same container does nothing.** Two installers of one context may both
  call it.
- **`MonoBehaviour` injectees do not depend on it.** They receive their own component lifetime,
  whichever context's binding answers. The rule above is about plain classes.
- **Scene validation works.** While a container is validating, `Install` captures no lifetime, so
  Zenject's validation can run outside Play Mode. Zenject does not construct the bound classes
  while it validates (unless a class is marked with Zenject's own attribute that allows it), so no
  `Lifetime` is resolved then: neither the constructor nor `Initialize()` of a service runs, and
  no work is registered.
- **It throws** `ArgumentNullException` for a null container, and `InvalidOperationException` when
  it is called outside Play Mode, where no lifetime tree exists. Like the rest of the package it is
  for the main thread only.

### What each injectee receives

The binding is transient: every injection point gets its own answer.

**A plain class** receives a new area: a child of the context lifetime, named after the class.

- It ends with the context. The class never has to implement `IDisposable` for cleanup.
- The class may call `Cancel()` on it to stop its own work. That affects no other service.
- It is an ordinary area, so `Dispose()` works on it too and ends it for good. Zenject itself never
  disposes it.
- In the Janitor window it appears with kind `Area` and the name of the class.

**A `MonoBehaviour`** receives its own component lifetime, the same object `this.GetLifetime()`
returns:

<!-- source: Samples~/ZenjectUsage/StoreBanner.cs -->
```csharp
public sealed class StoreBanner : MonoBehaviour
{
    [Inject] private SignalBus _signalBus;

    // A MonoBehaviour gets its own component lifetime, the same as GetLifetime(); it ends when the component is destroyed.
    [Inject] private Lifetime _lifetime;

    private TMP_Text _label;
    private int _refreshes;

    // The view arrives here because the demo builds its UI in code.
    public void Initialize(TMP_Text label) => _label = label;

    private void Awake()
    {
        // The handler form that receives the signal.
        _signalBus.Subscribe<StoreRefreshedSignal>(OnStoreRefreshed, _lifetime);
    }

    private void OnStoreRefreshed(StoreRefreshedSignal signal)
    {
        _refreshes++;
        _label.text = $"Store refreshed {_refreshes} time(s)";
    }
}
```

The banner is injected before its `Awake` runs. `Awake` subscribes on the component lifetime, so
the handler is called on every `StoreRefreshedSignal` for as long as the component exists, whether
the banner's GameObject is active or not and whether a popup is open or not. When the banner is
destroyed, or its scene lifetime is disposed, the lifetime ends and the package unsubscribes the
handler. There is no `OnDestroy`.

- It is disposed when the component is destroyed, whichever context's binding answered the
  injection.
- A prefab that Zenject instantiates (`InstantiatePrefab`, a factory or a memory pool bound to a
  prefab) is injected while it is still inactive. A component lifetime that is first used on an
  inactive object is bound to the component and to its GameObject, and ends with whichever is
  destroyed first. The one exception is an object that is never activated: its components never
  run `Awake`, Unity does not signal their destruction, and the lifetime ends when the GameObject
  is destroyed.
- The injection counts as the **first access** to that lifetime, and the first access decides the
  parent. A later `this.GetLifetime(category)` is ignored and reported as
  [JANITOR113](Troubleshooting.md#janitor113). A component that should live in a category does not
  inject `Lifetime`; it calls `this.GetLifetime(category)` or `this.GetActiveLifetime(category)`
  itself, as `ShopPopup` does.

### When a `MonoBehaviour` is injected

Whether `[Inject]` fields are filled before a component's first `OnEnable` depends on how the object
came to exist. The three cases below are read from the Zenject source (version 9.2.0):

- **An object that is in the scene when the scene loads.** The SceneContext collects every
  `MonoBehaviour` of its scene in its own `Awake` and injects them there. Zenject gives
  `SceneContext` the script execution order -9999, so this happens before `Awake` and `OnEnable` of
  scripts that keep the default order. It holds while the SceneContext runs by itself (its "Auto
  Run" setting, on by default) and for scripts that are not ordered before -9999. Objects below a
  GameObjectContext are left out; that context injects them when it runs.
- **A prefab instantiated through the container** (`InstantiatePrefab`, a prefab factory, a memory
  pool). Zenject keeps the new instance inactive until it has been injected, then activates it if
  the prefab was active. `OnEnable` sees its dependencies.
- **A component added with `InstantiateComponent`.** Zenject calls `AddComponent` first and injects
  afterwards. On an active GameObject Unity runs `Awake` and `OnEnable` inside `AddComponent`, so
  the `[Inject]` fields are still empty in that first `OnEnable`. Add the component to an inactive
  GameObject and activate it afterwards. The demo of the Zenject sample builds its popup that way.

An `OnEnable` that calls `_signalBus.Subscribe<TSignal>(handler, owner)` with an empty `_signalBus`
field throws `ArgumentNullException`, so the third case shows itself at once.

### A service that starts its own work

`GameplayLifetimes` only creates areas, and `OfferWatcher` below is started by its owner. A plain
service that should start working by itself, without anything calling it, implements Zenject's
`IInitializable`:

<!-- source: Samples~/ZenjectUsage/StoreRefresher.cs -->
```csharp
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
```

It is bound in the scene installer with `BindInterfacesAndSelfTo`:

<!-- source: Samples~/ZenjectUsage/GameplayInstaller.cs -->
```csharp
// Starts its own timer on its injected lifetime in Initialize(); the scene lifetime ends the timer.
Container.BindInterfacesAndSelfTo<StoreRefresher>().AsSingle();
```

What happens at each moment:

1. **The scene context installs.** The line binds the class under its own type and under
   `IInitializable`. A class bound with `AsSingle()` alone is constructed only when something asks
   for it; the `IInitializable` binding is what makes Zenject create a service that nothing
   injects.
2. **The scene context resolves.** Zenject constructs every `IInitializable` of the container. The
   `Lifetime` parameter is filled with a new area under the scene lifetime, named `StoreRefresher`,
   and `SignalBus` with the scene's bus. Nothing has been started yet.
3. **Zenject calls `Initialize()`,** once, from the `Start` of the scene's kernel, after the scene's
   objects have been injected. `Every` registers a timer on the area.
4. **Every 30 seconds** the timer calls `Refresh`, which fires the signal. Every handler that is
   subscribed at that moment runs inside the `Fire` call: the `StoreBanner`, and the `ShopPopup`
   while it is open.
5. **The scene lifetime ends,** through `SceneLifetimes.DisposeAll()` or through
   [the scene disposer](#the-scene-disposer). The area ends with it and the timer stops. The class
   has no `Dispose()` and no stop method: a service that lives as long as its context needs none.
6. **While Zenject validates the scene** none of this happens. The class is not constructed, so
   `Initialize()` is not called and no lifetime is asked for.

The work could be started in the constructor as well, since the constructor does not run during
validation either. `Initialize()` is the place Zenject provides for it, and by then every object of
the scene has its dependencies.

### Objects created at run time

**A plain object that is created at run time and injects a `Lifetime` disposes that lifetime when
it is done.**

Every plain injection adds one area under the context lifetime, and that area stays until the
context ends, whether or not anything still references the object. For a service bound with
`AsSingle()` that is one area for the life of the context, which is the intent. For objects that a
factory creates and drops many times, it is one more area per object: the tree grows for as long as
the scene lasts, and so does the walk that ends the scene.

The Zenject sample has such an object. A factory creates an `OfferWatcher` every time the shop
popup is opened:

<!-- source: Samples~/ZenjectUsage/OfferWatcher.cs -->
```csharp
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
```

Its owner is a component on a child object of the popup, so it is enabled and disabled with the
popup. It creates the watcher and hands it to its active lifetime:

<!-- source: Samples~/ZenjectUsage/OfferWatch.cs -->
```csharp
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
```

What happens at each moment:

1. **The popup opens and `OnEnable` runs.** `_factory.Create()` makes Zenject construct a watcher.
   The constructor parameter is filled from the scene's binding: a new area under the scene
   lifetime, named `OfferWatcher`. The tree has one more row.
2. **`watcher.AddTo(shown)`** registers the watcher, which is an `IDisposable`, on the active
   lifetime of the component's GameObject. `watcher.Start()` then starts a timer on the watcher's
   own area.
3. **The popup closes.** The active lifetime is cancelled and disposes the watcher. `Dispose()`
   disposes the injected area: the timer stops and the area leaves the tree. Nothing of this
   watcher is left, and no cleanup line was written in the component.
4. **The popup opens again.** A new watcher gets a new area. However often the popup is opened,
   there is one `OfferWatcher` row while it is open and none while it is closed.
5. **The popup is destroyed, or the scene is disposed.** The watcher is disposed the same way. When
   the scene lifetime ends first, the area ends with it, and the later `Dispose()` call finds
   nothing to do.

Take the `Dispose()` method away, or the `AddTo` line, and every opening leaves one more
`OfferWatcher` area under the scene lifetime, with its timer still firing, until the scene ends.

The other way to follow the rule is not to inject `Lifetime` into such objects at all. Hand them a
lifetime that already has the right length, for example an area of the service that creates them,
or the component lifetime of the view they drive.

The package reports the case it can see. Injected areas are ordinary areas, so they count for the
growth warning: when more than 64 areas are alive under one lifetime,
[JANITOR106](Troubleshooting.md#janitor106) is raised on that lifetime, here the scene lifetime or
the context lifetime. It is an editor warning, shown in the Janitor window with Tracking on.

---

## The categories service

`GameplayLifetimes` above is the pattern for sharing categories across classes: one class, bound
once per context with `AsSingle()`, creates the areas, and everything else injects that class.

- A class that **registers** into a category injects `GameplayLifetimes` and uses the area as an
  owner.
- A class that **stops** a category injects `GameplayLifetimes` and calls `Cancel()` on the area.
- An object **joins** a category with `this.GetLifetime(area)` or `this.GetActiveLifetime(area)`.

Because the service is bound in the scene context, its lifetime is a child of the scene lifetime and
both categories end with the scene. Two scenes loaded together each get their own instance and their
own categories; cancelling `Combat` of one scene does not reach the other.

[Organizing work](Organizing-Work.md) explains categories, placement and the rule that the first
access decides the parent.

---

## SignalBus subscriptions

<!-- source: Samples~/ZenjectUsage/ShopPopup.cs -->
```csharp
            _signalBus.Subscribe<StoreRefreshedSignal>(OnStoreRefreshed, shown);
```

The call subscribes the handler on the bus now, and the package calls `TryUnsubscribe` when the
owner's current generation ends (cancel, dispose, destroy, deactivation of an active lifetime) or
when the returned `LifetimeRegistration` is cancelled. User code never writes the unsubscribe.

What you still do yourself is plain Zenject: install the bus, declare the signal, and fire it. The
package adds no `SignalBus` binding and no way to fire. The sample's signal is an empty class:

<!-- source: Samples~/ZenjectUsage/StoreRefreshedSignal.cs -->
```csharp
/// <summary>Fired when the store content changed; declared in GameplayInstaller.</summary>
public sealed class StoreRefreshedSignal
{
}
```

The scene installer installs the bus and declares the signal:

<!-- source: Samples~/ZenjectUsage/GameplayInstaller.cs -->
```csharp
SignalBusInstaller.Install(Container);
Container.DeclareSignal<StoreRefreshedSignal>().OptionalSubscriber();
```

And `StoreRefresher`, shown [above](#a-service-that-starts-its-own-work), fires it:

<!-- source: Samples~/ZenjectUsage/StoreRefresher.cs -->
```csharp
private void Refresh() => _signalBus.Fire(new StoreRefreshedSignal());
```

The rules:

- **The signal type argument must be written.** `Subscribe<StoreRefreshedSignal>(handler, owner)`.
  It cannot be inferred from a method group or a lambda.
- **Two handler shapes:** `Action<TSignal>` receives the signal, as in `StoreBanner` above;
  `Action` ignores it, as in `ShopPopup`.
- **Two owner shapes:** a `Lifetime`, or a `Component`. A `MonoBehaviour` owner uses its component
  lifetime; any other component uses the lifetime of its GameObject. A destroyed owner subscribes
  nothing.
- **An owner that is not active subscribes nothing.** If the lifetime is being cancelled or is
  disposed, the bus is never called and the method returns a default (inactive) registration.
- **An earlier handler can end a later one in the same `Fire`.** If handler A cancels the owner of
  handler B while a signal is being delivered, B is not called for that signal.
- **An undeclared signal still throws.** The exception is Zenject's own, and nothing is left
  registered on the owner.
- **A handler's exception is not caught by the package.** The handler is subscribed on the bus as
  it is, and the bus calls it directly. An exception it throws is not routed to
  `LifetimeErrors.Handler`; it leaves through the `Fire` call, as with plain Zenject. In
  `StoreRefresher` the `Fire` call sits in an `Every` callback, so there the exception does reach
  `LifetimeErrors.Handler`, as an error of that timer, and the timer does not fire again.
- **Arguments:** a null bus, handler or owner throws `ArgumentNullException`. A call off the main
  thread throws `InvalidOperationException`.

### Duplicates

SignalBus keeps one subscription per signal type and handler. The package checks before it reaches
the bus:

- **Same handler, same signal, same owner again:** nothing is added. The existing registration is
  returned and [JANITOR105](Troubleshooting.md#janitor105) is raised in the editor and in
  development builds. This is the "subscribed twice in `OnEnable`" mistake, made harmless.
- **Same handler, same signal, a different owner:** `InvalidOperationException`. The bus cannot
  hold the handler twice, and handing back the other owner's registration would end the subscription
  at the wrong time. Use one owner, or a different handler instance.

"Same handler" means an equal delegate: the same target object and the same method. Only
subscriptions made through these extension methods are known to the package. A handler subscribed
with Zenject's own `SignalBus.Subscribe` is not tracked and not removed.

### Strict unsubscription

Subscriptions whose owner lives under the scene lifetime are gone before that scene's bus is torn
down, on both scene paths described below. A bus configured with Zenject's
`RequireStrictUnsubscribe` setting therefore passes its check for them.

Two kinds of subscription still fail that check:

- One whose owner outlives the scene: a lifetime under `Lifetime.App`, or the lifetime of an object
  kept with `DontDestroyOnLoad`, subscribed to a bus that belongs to the scene. Nothing ends that
  owner when the scene goes away, so the subscription is still there when the bus is torn down.
  Subscribe to a scene's bus with an owner of that scene.
- One made with the raw `SignalBus.Subscribe` and never removed, as it always did.

---

## The scene disposer

In a SceneContext, `Install` binds one more thing: an internal `IDisposable` that disposes the scene
lifetime. It is bound with the highest disposable execution order, so when Zenject tears the
SceneContext down it runs **before every other `IDisposable` of that scene container**.

This is a safety net, not the recommended path. The recommended path is the one-line call before the
load:

1. `SceneLifetimes.DisposeAll()` runs. Every scene lifetime is disposed: tokens first, then items,
   deepest and newest first. Nothing has been destroyed yet.
2. The scene loads. Unity destroys the old scene's objects, whose lifetimes are already disposed.
3. Zenject's disposables run. The scene disposer finds the scene lifetime already disposed and does
   nothing.

When the call is skipped:

1. The scene loads. Unity destroys the old scene's objects in an order you do not control. Each
   component lifetime is disposed as its component is destroyed.
2. When the SceneContext is destroyed, Zenject runs its disposables. The scene disposer runs first.
   It finds the scene lifetime still alive, logs [JANITOR104](Troubleshooting.md#janitor104) (in
   the editor and in development builds), and disposes the scene lifetime. Every lifetime injected
   into a scene service ends here.
3. The remaining disposables of the scene run. Each of them finds its injected lifetime already
   ended.

What the disposer guarantees:

- When a scene service's own `Dispose()` runs, the work on its injected lifetime has already
  stopped.
- A service `Dispose()` that throws cannot prevent that. Zenject stops running the remaining
  disposables after one throws, but the scene disposer has already run by then.
- The disposer itself never throws. A failure inside it is routed to `LifetimeErrors.Handler`.
- It touches only its own scene. Another loaded scene's lifetime is left alone.

What it does not guarantee is the order against the rest of the scene. By the time the SceneContext
is destroyed, other objects of the scene may already be gone, and plain C# work that was still
running may have touched them. That is why the skipped call is reported.

The hint is skipped once Unity has signalled that the application, or Play Mode, is exiting;
everything ends together there.

---

## Services that outlive scenes

A service that runs the scene transition must not be owned by a scene. Bind it in the
ProjectContext, where `Install` captures `Lifetime.App`:

<!-- source: Samples~/ZenjectUsage/ProjectInstaller.cs -->
```csharp
public sealed class ProjectInstaller : MonoInstaller
{
    public override void InstallBindings()
    {
        LifetimeInstaller.Install(Container);    // here the context lifetime is Lifetime.App
        Container.Bind<SceneFlow>().AsSingle();  // so its injected lifetime outlives every scene
    }
}
```

<!-- source: Samples~/ZenjectUsage/SceneFlow.cs -->
```csharp
public sealed class SceneFlow
{
    private readonly Lifetime _lifetime;   // injected in the ProjectContext: a child of Lifetime.App

    public SceneFlow(Lifetime lifetime) => _lifetime = lifetime;

    public void Load(string scenePath) => _lifetime.Run(ct => LoadAsync(scenePath, ct));

    // Disposal is final, so a load that can fail at runtime should hold the activation and dispose right before it.
    private static async UniTask LoadAsync(string scenePath, CancellationToken ct)
    {
        if (SceneUtility.GetBuildIndexByScenePath(scenePath) < 0)
        {
            Debug.LogError($"Scene '{scenePath}' is not in the Build Settings; nothing was disposed.");
            return;
        }

        SceneLifetimes.DisposeAll();       // everything owned by the loaded scenes stops now, before any destroy
        await SceneManager.LoadSceneAsync(scenePath).ToUniTask(cancellationToken: ct);
    }
}
```

What happens when `Load("Assets/Scenes/Main.unity")` is called:

1. `_lifetime` is a child of `Lifetime.App`, named `SceneFlow`. `Run` starts `LoadAsync` with its
   token.
2. The path is checked against the Build Settings first. `DisposeAll()` cannot be undone, so a
   scene that cannot be loaded must be found out before it, not after: for an unknown path the
   method logs an error and returns, and the running scene keeps all its work.
3. `SceneLifetimes.DisposeAll()` disposes every loaded scene's lifetime. `App`, and therefore
   `_lifetime`, is not touched: the task that is doing the loading keeps running.
4. The load is awaited. The old scene's objects are destroyed with nothing left to stop; the new
   scene's context installs and captures the new scene's lifetime.

The check in step 2 covers a scene that is missing from the build. It does not cover a load that
can fail later, such as a scene delivered as downloaded content. For those, hold the scene's
activation and call `DisposeAll()` right before it; `LoadingScreenFlow` in the Basic Usage sample
does that, and [Components and scenes](Components-and-Scenes.md) explains it.

Now suppose `SceneFlow` were bound in the scene installer instead. Its lifetime would be a child of
the scene lifetime. Step 3 would dispose it, which cancels `ct`, and the task would be cancelled by
its own `DisposeAll()` call while awaiting the load: nothing written after the `await` would ever
run. This is the reason the Zenject sample has a second installer for a second context.

Both installers call `LifetimeInstaller.Install(Container)`. The ProjectContext's call does not
cover the scenes; see [Install in every context](#install-in-every-context).

Without a DI container the same service is built by hand from `Lifetime.App.CreateChild()`; the
Basic Usage sample does that.

---

## Diagnostics raised by the integration

| ID | Raised when |
|---|---|
| [JANITOR104](Troubleshooting.md#janitor104) | The scene disposer found the scene lifetime still alive: `SceneLifetimes.Dispose` or `DisposeAll` was not called before the scene went away |
| [JANITOR105](Troubleshooting.md#janitor105) | The same handler was subscribed to the same signal for the same owner again |
| [JANITOR106](Troubleshooting.md#janitor106) | More than 64 areas are alive under one lifetime; with Zenject, usually plain objects created at run time that inject a `Lifetime` and never dispose it |
| [JANITOR112](Troubleshooting.md#janitor112) | A plain service received a lifetime from an outer context, because its SceneContext or GameObjectContext never called `LifetimeInstaller.Install(Container)` |

JANITOR104, JANITOR105 and JANITOR112 are logged to the console in the editor and in development
builds, and recorded in the Janitor window in the editor. JANITOR106 comes from the core and is
recorded in the Janitor window only.

## Limits

- Zenject / Extenject only. There is no VContainer integration in 0.1.0.
- The lifetime injected into a plain class is one area per injection, kept until the context ends
  or the class disposes it (see [Objects created at run time](#objects-created-at-run-time)).
- A GameObjectContext needs its own `Install` call. Without it, its plain services live as long as
  the scene.
- The integration has run on Mono in the editor only. The constructor of the scene disposer, which
  only Zenject's reflection calls, is marked to be kept by managed code stripping; a stripped
  IL2CPP build has not been tested.
- A `[Inject] Lifetime` on a `MonoBehaviour` fixes that component's parent to its scene lifetime; it
  cannot be combined with a category placement on the same component.
- A handler can belong to one owner per bus and signal type.
- The lifetime is captured when `Install` runs. A ProjectContext object that survived from an
  earlier play session, with domain reload disabled, would still hold that session's `Lifetime.App`.
  This combination is not covered by the package's tests.

More in [Limitations](Limitations.md).

## See also

- [Concepts](Concepts.md)
- [Organizing work](Organizing-Work.md)
- [Events](Events.md)
- [Components and scenes](Components-and-Scenes.md)
- [Diagnostics](Diagnostics.md)
- [Troubleshooting](Troubleshooting.md)
