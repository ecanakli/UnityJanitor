# Janitor

Cleans up Tasks, Tweens, Coroutines & Events. Nothing outlives its owner.

In a Unity project a task, a tween, a coroutine or an event subscription is started in one method,
and whether it ever stops depends on a matching line somewhere else: a `Kill` in `OnDisable`, a `-=`
in `OnDestroy`, a `cts.Cancel()` that someone has to remember. When that line is missing, the work
outlives its owner: a continuation resumes on a destroyed object, a handler fires on a closed popup,
a tween moves something that is gone. Janitor gives every piece of work an owner, a `Lifetime`, in
the same call that starts it. The work stops when the owner is destroyed, deactivated or cancelled,
or when its scene is disposed, and there is no removal line to write.

## Requirements

| | |
|---|---|
| Unity | 6000.0 or newer. The package is developed and tested on 6000.3. |
| [UniTask](https://github.com/Cysharp/UniTask) | Required. Install it before this package; a package installed from a git URL cannot declare it as a dependency. |
| DOTween | Optional, for binding and awaiting tweens. |
| Zenject / Extenject | Optional, for injected lifetimes and SignalBus subscriptions. |
| `com.unity.ugui` | Needed by the samples only. |

## Install

In the Package Manager, open the **+** menu and choose **Add package from git URL** (newer editors
label it **Install package from git URL**):

```
https://github.com/ecanakli/UnityJanitor.git#v0.1.0
```

Or add the line to `Packages/manifest.json`:

```json
"com.ecanakli.janitor": "https://github.com/ecanakli/UnityJanitor.git#v0.1.0"
```

Always install a tag. Without `#v0.1.0` the install tracks the default branch.

### Then, depending on your setup

- **Your code is in assembly definitions.** Reference `Ecanakli.Janitor`, and
  `Ecanakli.Janitor.DOTween` or `Ecanakli.Janitor.DependencyInjection.Zenject` when you use them.
  Code outside assembly definitions sees them without a reference.
- **DOTween.** The integration compiles when the `DOTWEEN` define is present, which DOTween's own
  setup panel sets, or when a UPM package named `com.demigiant.dotween` is installed. DOTween
  publishes no UPM package itself; that is the name a project uses when it wraps DOTween in a
  package of its own. There is nothing to switch on in Janitor.
- **Zenject or Extenject under `Assets/`.** Enable `Tools/Janitor/Zenject Integration` once. It sets
  a scripting define on every build target; commit `ProjectSettings` afterwards.
- **Extenject as a UPM package, 9.0.0 or newer.** The integration turns itself on.

Every call below is an extension method or a type in the namespace `Ecanakli.Janitor`. The Zenject
installer is in `Ecanakli.Janitor.DependencyInjection`.

## Getting started in 5 minutes

Six short steps: five with code from the samples, then a look at the window.

### 1. Bind work to a component

<!-- source: Samples~/BasicUsage/SessionClock.cs -->
```csharp
private void Start()
{
    this.Every(1f, TickGame);                                   // stops while Time.timeScale is 0
    this.Every(1f, TickReal, ignoreTimeScale: true);            // keeps running while the game is paused
}
```

`this.Every`, `this.After` and `this.Run` register the work on the lifetime of the component. Both
timers stop when the component is destroyed or its scene is disposed. There is no `OnDestroy`.

With the DOTween integration a tween is bound the same way:

<!-- source: Samples~/DOTweenUsage/ToastView.cs -->
```csharp
_badge.DOScale(1.2f, 0.6f).SetLoops(-1, LoopType.Yoyo).AddTo(this);   // the component owner: it ends with this component
```

The loop is killed when the component is destroyed.

### 2. Subscribe without writing the unsubscribe

The publisher declares an `OwnedEvent` and exposes its subscribe-only view:

<!-- source: Samples~/BasicUsage/Wallet.cs -->
```csharp
public sealed class Wallet
{
    private readonly OwnedEvent<int> _coinsChanged = new("CoinsChanged");
    private int _coins;

    public int Coins => _coins;

    public IOwnedEvent<int> CoinsChanged => _coinsChanged;   // outsiders subscribe; only Wallet invokes

    public void Add(int amount)
    {
        _coins += amount;
        _coinsChanged.Invoke(_coins);
    }
}
```

The subscriber names its owner in the same call:

<!-- source: Samples~/BasicUsage/CoinsLabel.cs -->
```csharp
private void Awake()
{
    OnCoinsChanged(_wallet.Coins);
    _wallet.CoinsChanged.Subscribe(OnCoinsChanged, this);   // owned by the component: it ends with it, not with SetActive
}

private void OnCoinsChanged(int coins) => _label.text = $"Wallet: {coins}";
```

The subscription is removed when the `CoinsLabel` component is destroyed. Nobody writes `-=`.

### 3. Restart work with an area and `Cancel()`

<!-- source: Samples~/BasicUsage/Countdown.cs -->
```csharp
private void Awake() => _run = this.GetLifetime().CreateChild("Run");

public void StartCountdown(int seconds)
{
    _run.Cancel();                                    // a countdown already running stops here
    _run.Run(ct => TickAsync(seconds, ct));
}

public void StopCountdown() => _run.Cancel();
```

`_run` is an **area**: a lifetime you create and keep. `Cancel()` stops the countdown that is
running and leaves the area usable for the next one. Destroying the object ends the area for good.
`Run` returns no handle, so the area is what stops this one task. For a run that also has to stop on
`SetActive(false)`, the area goes under the active lifetime; the `InboxPanel` sample shows it, see
[Organizing work](Documentation~/Organizing-Work.md).

### 4. Group work into categories that another class cancels

<!-- source: Samples~/BasicUsage/GameplayLifetimes.cs -->
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

An object joins a category by passing it when it first asks for its lifetime:

<!-- source: Samples~/BasicUsage/CombatEffect.cs -->
```csharp
// The first access decides the parent, so join Combat before anything else uses this lifetime.
_life = this.GetLifetime(_lifetimes.Combat);
```

Everything registered in `Combat`, and every object placed in it, stops when any class calls
`Combat.Cancel()`. The classes that registered the work take no part in stopping it, and the
category stays usable.

### 5. Make the scene call before a load

<!-- source: Samples~/BasicUsage/SceneFlow.cs -->
```csharp
// Disposal is final, so a load that can fail at runtime should use LoadingScreenFlow, which disposes right before activation.
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
```

`SceneLifetimes.DisposeAll()` ends all work owned by the loaded scenes while every object still
exists, so nothing runs against a half-destroyed scene. That includes objects that were moved into a
scene after their lifetimes were created. `Lifetime.App` and DontDestroyOnLoad objects are not
touched. Without the call, cleanup still happens, but in the order in which Unity destroys the
objects.

Disposal is final: a scene whose lifetime was disposed accepts no more work. That is why the sample
checks that the scene is in the Build Settings before it disposes anything. A load that can still
fail at run time should hold the scene activation and dispose right before it, as
`LoadingScreenFlow` in the same sample does; see
[Components and scenes](Documentation~/Components-and-Scenes.md).

### 6. Look at the tree

Enter Play Mode and open `Window > Analysis > Janitor`. The window shows every lifetime, what is
registered in it and where it was registered. Its Warnings tab lists what the package noticed, each
with one of sixteen stable IDs (`JANITOR101` to `JANITOR116`) and a link to its section in
[Troubleshooting](Documentation~/Troubleshooting.md).

## The main example

A popup that blinks a badge, loads offers, listens to a button and follows the wallet, written by
hand. `Wallet.CoinsChanged` is still a C# `event` here:

<!-- illustrative: before -->
```csharp
public sealed class ShopPopup : MonoBehaviour
{
    [SerializeField] private Button _buyButton;
    [SerializeField] private Graphic _saleBadge;
    [SerializeField] private TMP_Text _coinsLabel;
    private Wallet _wallet;
    private OfferService _offers;

    private Coroutine _badgeBlink;
    private CancellationTokenSource _loadCts;

    public void Initialize(Wallet wallet, OfferService offers)
    {
        _wallet = wallet;
        _offers = offers;
    }

    private void OnEnable()
    {
        _coinsLabel.text = _wallet.Coins.ToString();
        _badgeBlink = StartCoroutine(BlinkBadge());
        _loadCts = new CancellationTokenSource();
        LoadOffersAsync(_loadCts.Token).Forget();
        _buyButton.onClick.AddListener(OnBuyClicked);
        _wallet.CoinsChanged += OnCoinsChanged;
    }

    // Six lines that must mirror OnEnable exactly; forgetting one leaks or throws.
    private void OnDisable()
    {
        if (_badgeBlink != null) StopCoroutine(_badgeBlink);
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        _buyButton.onClick.RemoveListener(OnBuyClicked);
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
    private void OnCoinsChanged(int coins) => _coinsLabel.text = coins.ToString();
}
```

The lines that must mirror each other:

| Started in `OnEnable` | Must be undone in `OnDisable` | When the second line is missing |
|---|---|---|
| `StartCoroutine(BlinkBadge())` | `StopCoroutine(_badgeBlink)`, with its field and its null check | After `enabled = false` the blink keeps running, and the next `OnEnable` starts a second one |
| `new CancellationTokenSource()` | `Cancel()`, `Dispose()`, `= null` | The load continues and shows offers in a closed popup |
| `onClick.AddListener(OnBuyClicked)` | `onClick.RemoveListener(OnBuyClicked)` | Every opening adds a listener; one click buys several times |
| `CoinsChanged += OnCoinsChanged` | `CoinsChanged -= OnCoinsChanged` | The wallet keeps calling a closed or destroyed popup |

The same popup with Janitor, as it ships in the Basic Usage sample:

<!-- source: Samples~/BasicUsage/ShopPopup.cs -->
```csharp
/// <summary>A popup whose work stops on SetActive(false), on a Popups cancel and on destroy.</summary>
public sealed class ShopPopup : MonoBehaviour
{
    private Button _buyButton;
    private Graphic _saleBadge;
    private TMP_Text _coinsLabel;
    private GameplayLifetimes _lifetimes;
    private Wallet _wallet;
    private OfferService _offers;

    // Arrives here instead of [Inject] and the Inspector; call it before the object is enabled.
    public void Initialize(
        GameplayLifetimes lifetimes,
        Wallet wallet,
        OfferService offers,
        Button buyButton,
        Graphic saleBadge,
        TMP_Text coinsLabel)
    {
        _lifetimes = lifetimes;
        _wallet = wallet;
        _offers = offers;
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
    private void OnCoinsChanged(int coins) => _coinsLabel.text = coins.ToString();
}
```

`Initialize(...)` stands in for serialized fields or injection, because the sample builds its UI in
code. `Wallet.CoinsChanged` is the `OwnedEvent` from step 2, and `_lifetimes.Popups` is the category
from step 4.

What happens at each moment:

- **The popup is activated.** `OnEnable` takes the popup's *active lifetime*, placed in the `Popups`
  category, and registers four things on it: the coroutine, the task, the click listener and the
  wallet subscription.
- **The popup is deactivated.** The active lifetime is cancelled. The load's token is cancelled, the
  two subscriptions are removed and the coroutine is stopped. There is no `OnDisable`.
- **The popup is activated again.** `OnEnable` registers everything into a fresh generation. Nothing
  is registered twice, because nothing from before is left.
- **Another class calls `Popups.Cancel()`.** The same things stop while the popup stays on screen.
- **The popup is destroyed, or its scene is disposed.** The lifetime is disposed, and the same
  things stop for good. There is no `OnDestroy`.

The same walk-through in full detail is on the [documentation index](Documentation~/index.md). The
popup with a DOTween tween and with Zenject injection and a SignalBus subscription is in the DOTween
Usage and Zenject Usage samples and in the [DOTween](Documentation~/DOTween.md) and
[Zenject](Documentation~/Zenject.md) guides.

## API at a glance

The caller-information parameters that every registration method ends with are left out; the
compiler fills them in. This section shows the main shapes. Every public type with its complete
member list is in the [API reference](Documentation~/API-Reference.md).

**Get a lifetime.** A component, a GameObject, an active GameObject, a scene and the app each have
one; you create areas under any of them. See [Concepts](Documentation~/Concepts.md) and
[Components and scenes](Documentation~/Components-and-Scenes.md).

<!-- signature -->
```csharp
// LifetimeComponentExtensions
public static Lifetime GetLifetime(this MonoBehaviour behaviour);
public static Lifetime GetLifetime(this MonoBehaviour behaviour, Lifetime parent);
public static Lifetime GetLifetime(this GameObject gameObject);
public static Lifetime GetActiveLifetime(this Component component);
public static Lifetime GetActiveLifetime(this Component component, Lifetime parent);

// Lifetime
public static Lifetime App { get; }
public Lifetime CreateChild(string name = null);

// LifetimeInstaller (Zenject): binds Lifetime for injection in a context
public static void Install(DiContainer container);
```

**Run and time work.** Each method also exists on `MonoBehaviour`, where it uses the component's
lifetime. `seconds` must be a finite number no larger than 900000000000; zero or less means "now"
for `After` and "every frame" for `Every`. See
[Tasks and errors](Documentation~/Tasks-and-Errors.md).

<!-- signature -->
```csharp
// LifetimeTaskExtensions
public static void Run(this Lifetime lifetime, Func<CancellationToken, UniTask> work);
public static void Run<TState>(this Lifetime lifetime, TState state, Func<TState, CancellationToken, UniTask> work);
public static void After(this Lifetime lifetime, float seconds, Action action, bool ignoreTimeScale = false);
public static void After<TState>(this Lifetime lifetime, float seconds, TState state, Action<TState> action, bool ignoreTimeScale = false);
public static void Every(this Lifetime lifetime, float seconds, Action action, bool ignoreTimeScale = false);
public static void Every<TState>(this Lifetime lifetime, float seconds, TState state, Action<TState> action, bool ignoreTimeScale = false);
```

**Subscribe.** Every form takes its owner and returns a `LifetimeRegistration`. The owner can also
be a component. See [Events](Documentation~/Events.md) and [Zenject](Documentation~/Zenject.md).

<!-- signature -->
```csharp
// OwnedEvent<T>; also OwnedEvent, OwnedEvent<T1, T2> and OwnedEvent<T1, T2, T3>
public LifetimeRegistration Subscribe(Action<T> handler, Lifetime owner);
public LifetimeRegistration Subscribe(Action<T> handler, Component owner);
public void Invoke(T arg);

// UnityEvent; also UnityEvent<T0> up to four arguments
public static LifetimeRegistration Subscribe(this UnityEvent evt, UnityAction handler, Lifetime owner);
public static LifetimeRegistration Subscribe(this UnityEvent evt, UnityAction handler, Component owner);

// Events you do not own; also for two and three arguments, for any delegate type, and on MonoBehaviour
public static LifetimeRegistration Subscribe(this Lifetime owner, Action<Action> add, Action<Action> remove, Action handler);
public static LifetimeRegistration Subscribe<T>(this Lifetime owner, Action<Action<T>> add, Action<Action<T>> remove, Action<T> handler);

// Any IDisposable; the owner can also be a Component or a GameObject
public static LifetimeRegistration AddTo<T>(this T disposable, Lifetime lifetime) where T : IDisposable;

// Custom cleanup, on Lifetime and on MonoBehaviour
public LifetimeRegistration OnCancel(Action action);
public LifetimeRegistration OnCancel<TState>(TState state, Action<TState> action) where TState : class;

// Zenject SignalBus
public static LifetimeRegistration Subscribe<TSignal>(this SignalBus bus, Action<TSignal> handler, Lifetime owner);
public static LifetimeRegistration Subscribe<TSignal>(this SignalBus bus, Action handler, Lifetime owner);
```

**Tweens.** `AddTo` returns the tween, so it chains. See [DOTween](Documentation~/DOTween.md).

<!-- signature -->
```csharp
public enum TweenCancelMode { Kill, Complete }

// LifetimeTweenExtensions
public static T AddTo<T>(this T tween, Lifetime owner, TweenCancelMode onCancel = TweenCancelMode.Kill) where T : Tween;
public static T AddTo<T>(this T tween, Component owner, TweenCancelMode onCancel = TweenCancelMode.Kill) where T : Tween;
public static T AddTo<T>(this T tween, GameObject owner, TweenCancelMode onCancel = TweenCancelMode.Kill) where T : Tween;
public static UniTask AwaitCompletionAsync(this Tween tween, Lifetime lifetime);
public static UniTask AwaitCompletionAsync(this Tween tween, CancellationToken token);
```

**Coroutines.** The host runs the coroutine; the lifetime decides when it stops. See
[Coroutines](Documentation~/Coroutines.md).

<!-- signature -->
```csharp
public static LifetimeRegistration StartCoroutine(this Lifetime lifetime, MonoBehaviour host, IEnumerator routine);
```

**Stop work.** `Cancel()` stops everything in a lifetime and below it and leaves it usable;
`Dispose()` ends an area for good; a registration stops one item. See
[Stopping work](Documentation~/Stopping-Work.md).

<!-- signature -->
```csharp
// Lifetime
public void Cancel();
public void Dispose();
public CancellationToken Token { get; }
public bool IsDisposed { get; }

// LifetimeRegistration
public bool IsActive { get; }
public void Cancel();
```

**Scenes.** See [Components and scenes](Documentation~/Components-and-Scenes.md).

<!-- signature -->
```csharp
public static class SceneLifetimes
{
    public static Lifetime Get(Scene scene);
    public static void Dispose(Scene scene);
    public static void DisposeAll();
}
```

## The six rules

1. **Register work through a lifetime.** The package only stops what it was given.
   [Concepts](Documentation~/Concepts.md)
2. **Subscribe with an owner and never write a removal.** The owner is whichever side ends first:
   when the source of the event is shorter-lived than the subscriber, pass the source's lifetime.
   [Events](Documentation~/Events.md)
3. **Dispose scene lifetimes before loading.** `SceneLifetimes.DisposeAll()` goes right before the
   load. [Components and scenes](Documentation~/Components-and-Scenes.md)
4. **Work started in `OnEnable` uses the active lifetime,** so it stops on `SetActive(false)`. On
   the component's own lifetime it would be added again by every activation; the editor reports that
   as [JANITOR116](Documentation~/Troubleshooting.md#janitor116).
   [Pooling](Documentation~/Pooling.md)
5. **Register root Sequences only,** never the tweens nested in them.
   [DOTween](Documentation~/DOTween.md)
6. **Main thread only.** Registering from another thread throws; stopping from another thread is
   deferred to the main thread. [Threading](Documentation~/Threading.md)

## Samples

Import them from the Package Manager: select **Janitor**, open the **Samples** tab and press
**Import**. Each sample has its own README with the setup steps and a list of things to try. The
demos build their UI in code, so there is no scene or prefab to open; all three need
`com.unity.ugui`.

| Sample | Needs | Shows |
|---|---|---|
| [Basic Usage](Samples~/BasicUsage/README.md) | The core only | Lifetimes, areas and categories, tasks and timers, coroutines, owned events, paired subscriptions, pooling, error routing, the scene call |
| [DOTween Usage](Samples~/DOTweenUsage/README.md) | DOTween | Tweens bound to lifetimes, `Kill` and `Complete`, awaiting a tween, root Sequences |
| [Zenject Usage](Samples~/ZenjectUsage/README.md) | Zenject or Extenject | Injected lifetimes, a categories service, a service that starts its own timer, SignalBus subscriptions, a factory-created object that disposes its lifetime, the scene call from a ProjectContext service |

## Documentation

- [Start here](Documentation~/index.md): which guide answers which question, and one class from
  start to end
- [Concepts](Documentation~/Concepts.md): the tree, generations, `Cancel` versus `Dispose`, ordering
- [Stopping work](Documentation~/Stopping-Work.md): everything, a scene, an object, an area, one
  item, one task
- [Tasks and errors](Documentation~/Tasks-and-Errors.md): `Run`, `After`, `Every` and where
  exceptions go
- [Events](Documentation~/Events.md): `OwnedEvent`, paired `Subscribe`, `UnityEvent`, `IDisposable`
- [Components and scenes](Documentation~/Components-and-Scenes.md): component and active lifetimes
  and the scene call
- [Migration](Documentation~/Migration.md): recipes from hand-written cleanup to lifetimes
- [API reference](Documentation~/API-Reference.md): every public type and member as signatures
- [Troubleshooting](Documentation~/Troubleshooting.md): one section per `JANITOR1xx` diagnostic

For AI tools, [llms.txt](llms.txt) holds the rules, the API as signatures and a one-line description
of every page, and [llms-full.txt](llms-full.txt) is the whole documentation in one file.

## Status and limits

- Version 0.1.0. See the [changelog](CHANGELOG.md).
- The Janitor window and the recording behind it exist only in the editor, and so do fifteen of the
  sixteen diagnostics; their console warnings are compiled into the editor and development builds.
  The exception is `JANITOR115` (an event invoked more than 64 calls deep), which is routed to the
  error handler in every build.
- The active lifetime works through one component that the package adds to the GameObject. It is
  hidden in the Inspector.
- A script recompile during Play Mode discards every lifetime and the work it owned. The next call
  into the package starts a new session and logs one warning; restart Play Mode for a clean state.
- The allocation figures in the documentation are measured on Mono in the editor. They have not been
  measured on IL2CPP yet.
- The package only knows work that was registered through it. What it deliberately does not do is
  listed in [Limitations](Documentation~/Limitations.md).

## License

MIT. See [LICENSE.md](LICENSE.md).
