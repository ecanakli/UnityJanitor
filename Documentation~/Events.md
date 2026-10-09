# Events

[Back to index](index.md)

Every subscription API in Janitor takes the **owner** of the subscription in the same call that
subscribes, and the package removes the subscription when the owner's generation ends. Your code
contains no `-=`, no `RemoveListener` and no `Unsubscribe`. This page covers the first four kinds
of source in the table below (the fifth has its own page), why the removal line is gone, and how
to bind a source the package does not know.

| The event is | Subscribe with | The package removes it with |
|---|---|---|
| Declared by your game | `evt.Subscribe(handler, owner)` on an `OwnedEvent` | Removing its subscriber slot |
| A `UnityEvent` with 0 to 4 arguments | `button.onClick.Subscribe(handler, owner)` | `RemoveListener` |
| A C# event of Unity or of a third-party SDK | `owner.Subscribe(add, remove, handler)` | The `remove` lambda given in the same call |
| Anything that hands back an `IDisposable` | `subscription.AddTo(owner)` | `Dispose()` |
| A Zenject signal | `signalBus.Subscribe<TSignal>(handler, owner)` | See [Zenject](Zenject.md) |

"The owner's generation ends" means any of: `Cancel()` on the owner or on a lifetime above it,
`Dispose()`, the destruction of the owning object, `SetActive(false)` when the owner is an active
lifetime, the disposal of its scene, or the end of the application. A single subscription can also
be removed through the `LifetimeRegistration` that every call returns.

---

## Before and after

A wallet that publishes a change event, and a popup that listens to it and to a button, written by
hand:

<!-- illustrative: before -->
```csharp
public sealed class Wallet
{
    private int _coins;

    public event Action<int> CoinsChanged;

    public int Coins => _coins;

    public void Add(int amount)
    {
        _coins += amount;
        CoinsChanged?.Invoke(_coins);
    }
}

public sealed class ShopPopup : MonoBehaviour
{
    private Button _buyButton;
    private Graphic _saleBadge;
    private TMP_Text _coinsLabel;
    private Wallet _wallet;
    private OfferService _offers;

    private Coroutine _badgeBlink;
    private CancellationTokenSource _loadCts;

    public void Initialize(Wallet wallet, OfferService offers, Button buyButton, Graphic saleBadge, TMP_Text coinsLabel)
    {
        _wallet = wallet;
        _offers = offers;
        _buyButton = buyButton;
        _saleBadge = saleBadge;
        _coinsLabel = coinsLabel;
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

    // Six lines that must mirror OnEnable exactly.
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

The line that leaks is `_wallet.CoinsChanged -= OnCoinsChanged;`. The wallet outlives the popup, so
if that one line is forgotten, or `OnDisable` returns early before reaching it:

- every reopening of the popup adds one more copy of the handler, and the label is written twice,
  then three times;
- while the popup is closed, every coin change still runs the handler;
- after the popup is destroyed, the next coin change still calls `OnCoinsChanged`, now on a
  destroyed popup with a destroyed label. An exception thrown there travels up through `Wallet.Add`
  into whoever added the coins, and the handlers that come after it in the event are not called;
- the wallet's delegate keeps the popup's managed object reachable.

`_buyButton.onClick.RemoveListener(OnBuyClicked);` has the same shape: without it the handler is
added again on every `OnEnable` and one click buys twice.

The same two classes from the Basic Usage sample:

<!-- source: Samples~/BasicUsage/Wallet.cs -->
```csharp
/// <summary>Holds the coin count and publishes changes through a subscribe-only event.</summary>
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

`ShopPopup` has no `OnDisable` and no fields for handles. What happens to its two subscriptions at
each moment:

- **The popup is enabled.** `shown` is the active lifetime of the popup's GameObject, placed in the
  `Popups` category. The click listener and the wallet subscription are registered in its current
  generation.
- **Coins are added.** `Wallet.Add` invokes the event and `OnCoinsChanged` updates the label.
- **The popup is deactivated.** The active lifetime is cancelled. Both subscriptions are removed
  before `SetActive(false)` returns. From this moment a coin change does not reach the popup.
- **The popup is activated again.** `OnEnable` subscribes into the new generation. There is exactly
  one subscription of each kind again, however many times this is repeated.
- **`Popups.Cancel()` is called while the popup is open.** Both subscriptions are removed although
  the popup stays visible: the label stops following and the Buy button does nothing, until the
  popup is closed and opened.
- **The popup is destroyed, or its scene is disposed.** The lifetime is disposed and the
  subscriptions go with it. Nothing can call the popup afterwards.

---

## OwnedEvent: events the game declares

A C# `event` cannot be handed to a library, so the package cannot remove a `+=` for you. For events
your own code declares, `OwnedEvent` takes the place of the `event` field.

There are four arities with the same shape, each with its subscribe-only `IOwnedEvent` view. The
event without arguments and the event with one:

<!-- signature -->
```csharp
public sealed class OwnedEvent : IOwnedEvent
{
    public OwnedEvent(string name = null);
    public int SubscriberCount { get; }
    public LifetimeRegistration Subscribe(Action handler, Lifetime owner);
    public LifetimeRegistration Subscribe(Action handler, Component owner);
    public void Invoke();
}

public interface IOwnedEvent
{
    LifetimeRegistration Subscribe(Action handler, Lifetime owner);
}

public sealed class OwnedEvent<T> : IOwnedEvent<T>
{
    public OwnedEvent(string name = null);
    public int SubscriberCount { get; }
    public LifetimeRegistration Subscribe(Action<T> handler, Lifetime owner);
    public LifetimeRegistration Subscribe(Action<T> handler, Component owner);
    public void Invoke(T arg);
}

public interface IOwnedEvent<T>
{
    LifetimeRegistration Subscribe(Action<T> handler, Lifetime owner);
}
```

`OwnedEvent<T1, T2>` and `OwnedEvent<T1, T2, T3>` have the same members; their handlers are
`Action<T1, T2>` and `Action<T1, T2, T3>`, and their `Invoke` takes `(T1 arg1, T2 arg2)` and
`(T1 arg1, T2 arg2, T3 arg3)`. The component form on a view is an extension method, one per arity:

<!-- signature -->
```csharp
public static class OwnedEventViewExtensions
{
    public static LifetimeRegistration Subscribe(this IOwnedEvent view, Action handler, Component owner);
    public static LifetimeRegistration Subscribe<T>(this IOwnedEvent<T> view, Action<T> handler, Component owner);
    public static LifetimeRegistration Subscribe<T1, T2>(this IOwnedEvent<T1, T2> view, Action<T1, T2> handler, Component owner);
    public static LifetimeRegistration Subscribe<T1, T2, T3>(this IOwnedEvent<T1, T2, T3> view, Action<T1, T2, T3> handler, Component owner);
}
```

### Declaring and exposing

`Wallet` above shows the recommended form. The event is a private field, and the class exposes the
subscribe-only view `IOwnedEvent<int>`. Other classes can subscribe; only `Wallet` can call
`Invoke`. This is the same encapsulation the `event` keyword gives.

A `public readonly OwnedEvent<T>` field also works, for an event that anyone is allowed to invoke.

An `OwnedEvent` may be constructed in a field initializer, as `Wallet` and `Countdown` do. The
constructor only stores the name: it needs neither Play Mode nor the main thread.

The constructor's `name` is a display label. It appears in the Janitor window, where a subscription
is listed in its owner's lifetime as `OwnedEvent<Int32> "CoinsChanged"`, and in error contexts. It
is never used to find an event.

An `OwnedEvent` has no owner of its own and needs no disposal. A publisher that goes away stops
invoking; the subscriber slots go away with the subscribers' owners. (For a subscriber that lives
much longer than the publisher, see
[The owner is the side that ends first](#the-owner-is-the-side-that-ends-first).) It is not an event
bus: there is no global registry, no routing by type and no asynchronous dispatch.

The event without arguments looks the same. `Countdown` in the sample declares and exposes one, and
calls `_finished.Invoke()` when the count reaches zero:

<!-- source: Samples~/BasicUsage/Countdown.cs -->
```csharp
private readonly OwnedEvent _finished = new("Finished");
private TMP_Text _label;
private Lifetime _run;

public IOwnedEvent Finished => _finished;
```

### Subscribing

The owner is a `Lifetime` or a `Component`. `ShopPopup` above passes a lifetime (`shown`).
`CoinsLabel` in the same sample passes itself:

<!-- source: Samples~/BasicUsage/CoinsLabel.cs -->
```csharp
/// <summary>A label that follows the wallet for as long as the component exists.</summary>
public sealed class CoinsLabel : MonoBehaviour
{
    private Wallet _wallet;
    private TMP_Text _label;

    // Call it before the object is enabled, so Awake sees the wallet.
    public void Initialize(Wallet wallet, TMP_Text label)
    {
        _wallet = wallet;
        _label = label;
    }

    private void Awake()
    {
        OnCoinsChanged(_wallet.Coins);
        _wallet.CoinsChanged.Subscribe(OnCoinsChanged, this);   // owned by the component: it ends with it, not with SetActive
    }

    private void OnCoinsChanged(int coins) => _label.text = $"Wallet: {coins}";
}
```

- **`Awake` runs.** The label shows the current amount and subscribes with the component as the
  owner. The subscription is registered on the component lifetime.
- **Coins are added.** The label follows, whether the shop popup is open or closed.
- **The label's GameObject is deactivated.** Nothing changes. The component lifetime is not
  cancelled by deactivation, so the label keeps receiving changes and shows the right amount when it
  is activated again.
- **The label is destroyed, or its scene is disposed.** The subscription is removed.

The rules for the owner:

- **A lifetime** can be any lifetime: an active lifetime such as `shown` above, an area, a scene
  lifetime. `Lifetime.App` is the explicit way to say "for as long as the game runs"; there is no
  subscribe without an owner.
- **A component** resolves to a lifetime. A `MonoBehaviour` (`this`) resolves to its component
  lifetime, any other component to the lifetime of its GameObject. A destroyed component adds
  nothing.
- On an `IOwnedEvent` view the component form is an extension method, so the file needs
  `using Ecanakli.Janitor;`.
- The call returns a `LifetimeRegistration` that removes this one subscription.
- If the owner is not active (it is being cancelled or disposed, or it is disposed), nothing is
  added and a default registration is returned. Nothing is thrown.
- A `null` handler or `null` owner throws `ArgumentNullException`. Subscribing off the main thread
  throws `InvalidOperationException`, in every build.

**Which owner in `OnEnable`.** A subscription made in `OnEnable` belongs on the active lifetime, as
in `ShopPopup`. The component lifetime survives deactivation, so `Subscribe(handler, this)` in
`OnEnable` leaves the subscription in place while the object is inactive, and the next `OnEnable`
subscribes the same handler for the same owner again. That second call is ignored as a duplicate and
reported as [JANITOR105](Troubleshooting.md#janitor105). Use `Subscribe(handler, this)` in `Awake`
or `Start`, as `CoinsLabel` does, and the active lifetime in `OnEnable`.

**A subscriber that was never active.** `Awake` does not run before the first activation of the
GameObject, so a subscription made in `Awake` does not exist until then. A subscriber that has to
listen while its object is inactive, or before it was ever active, subscribes in its
initialization method instead, with the component as the owner. That is supported; see
[Components and scenes](Components-and-Scenes.md#objects-that-were-never-activated).

### The owner is the side that ends first

In the examples so far the subscriber ends before the source: the popup closes long before the
wallet goes away. The entry of a subscription lives in its owner's lifetime until that lifetime's
generation ends, and for that long it keeps the source reachable: the event object, the
`UnityEvent`, or the remove lambda and whatever it captured.

When the source is the shorter-lived side, naming the long-lived subscriber as the owner makes that
lifetime grow. A manager that lives as long as its scene and subscribes to an event of every object
it spawns, or a screen that subscribes to the button of every list cell it builds, with `this` as
the owner, adds one entry per source and loses none until the manager or the screen itself ends.
Nothing is called on a dead object, but the entries and the dead sources stay in memory, and every
further `UnityEvent` subscription on that owner gets slower, because its duplicate check walks the
owner's entries.

The rule: **the owner of a subscription is whichever side ends first.** When the source is the
shorter-lived side, pass a lifetime that ends with the source. For a source that is a component,
that is the component itself. `CountdownTracker` in the sample lives as long as its scene and
listens to countdowns that exist for a few seconds each:

<!-- source: Samples~/BasicUsage/CountdownTracker.cs -->
```csharp
/// <summary>Lives as long as the scene and counts the countdowns that finish; each countdown owns its subscription.</summary>
public sealed class CountdownTracker : MonoBehaviour
{
    private int _finished;

    public void Track(Countdown countdown)
    {
        // The owner is the countdown, not this tracker: it ends first, so the entry leaves with it instead of piling up here.
        countdown.Finished.Subscribe(OnFinished, countdown);
    }

    private void OnFinished() => Debug.Log($"[CountdownTracker] {++_finished} countdown(s) finished.");
}
```

- **A countdown is spawned.** The demo calls `Track(countdown)`. The subscription is an entry in the
  countdown's component lifetime; the tracker's lifetime holds nothing.
- **The countdown finishes.** It invokes `Finished`, and `OnFinished` runs on the tracker.
- **The countdown is destroyed.** Its lifetime is disposed and the subscription goes with it.
  Nothing of that countdown is left in the tree.
- **With `this` as the owner**, every spawned countdown would add one entry to the tracker's
  lifetime, and destroying the countdown would not remove it. The entry, and through it the event
  of the destroyed countdown, would stay until the tracker itself ends.

In the editor, the Janitor window reports a lifetime that holds more than 256 entries as
[JANITOR106](Troubleshooting.md#janitor106).

### What Invoke does

- Handlers are called in subscription order.
- Each handler runs in its own `try`/`catch`. An exception is routed to `LifetimeErrors.Handler`
  with the source `EventHandler`, the subscriber's owner and the member and line of its `Subscribe`
  call. The remaining handlers still run, and nothing propagates into the code that called `Invoke`.
- A handler whose owner is not active, or whose owner has moved on to another generation, is
  skipped.
- A subscription **removed during an `Invoke` is not called in that `Invoke`**, if it has not been
  reached yet.
- A subscription **added during an `Invoke` is first called by the next `Invoke`**.
- A handler may invoke the same event again. The nested call delivers to every subscription that is
  live when it starts, and then the outer call continues.
- An `Invoke` on an event without subscribers does nothing.
- In steady state `Invoke` allocates nothing.

The removal rule differs from a C# `event` on purpose. A C# delegate invokes a snapshot of its
handlers, so a handler removed by an earlier handler still runs once. Here it does not. In this
excerpt from the package's tests, `_owner` and `_other` are two areas, and the first handler cancels
the owner of the second:

<!-- source: Tests/Editor/OwnedEventTests.cs -->
```csharp
_event.Subscribe(v =>
{
    _t.Log.Add("a");
    _other.Cancel();
}, _owner);
_event.Subscribe(v => _t.Log.Add("b"), _other);
_event.Subscribe(v => _t.Log.Add("c"), _owner);

_event.Invoke(1);

Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a", "c" }), "a handler removed during Invoke must not run in that Invoke");
Assert.That(_event.SubscriberCount, Is.EqualTo(2));
```

If the first handler closes or destroys the object that owns the second handler, the second handler
must not run on a dead object. That is the case this rule exists for.

**Nested invokes are limited.** At most 64 invokes of one event can be on the stack. When handlers
invoke each other in a loop, the 65th call, which is the 64th nested one, is refused: it delivers
nothing and routes an `InvalidOperationException` that carries
[JANITOR115](Troubleshooting.md#janitor115) to the error handler. This turns endless event ping-pong
into a logged error instead of a stack overflow, in every build.

**Main thread only.** `Invoke` off the main thread throws `InvalidOperationException` in the editor
and in development builds, once the event has had a subscriber. Release builds skip that check.

### Duplicate subscriptions

Subscribing an equal handler (the same method on the same target) for the same owner a second time
adds nothing. The call returns the registration of the existing subscription, the handler is still
called once per `Invoke`, and the editor and development builds log
[JANITOR105](Troubleshooting.md#janitor105).

- The same handler under a **different owner** is a separate subscription and is called once per
  owner.
- After the owner was cancelled, or after the registration was cancelled, subscribing the same
  handler again is not a duplicate.
- Equality is delegate equality: the same method on the same target. Two lambda expressions at
  different places in the source are different methods and are never equal, even with identical
  bodies. One lambda expression evaluated twice is equal to itself, unless it captures local
  variables: then each evaluation has its own closure object as its target.

---

## Paired Subscribe: events the game does not own

Events of Unity and of third-party SDKs are C# events that you cannot replace. For those, one call
takes the add and the remove together:

<!-- illustrative: before -->
```csharp
public sealed class PlatformHooks : MonoBehaviour
{
    private IAdsSdk _ads;

    public void Initialize(IAdsSdk ads) => _ads = ads;

    private void Awake()
    {
        Application.lowMemory += OnLowMemory;
        _ads.RewardGranted += OnRewardGranted;
        _ads.Closed += OnAdClosed;
    }

    // Three removals that must mirror Awake.
    private void OnDestroy()
    {
        Application.lowMemory -= OnLowMemory;
        _ads.RewardGranted -= OnRewardGranted;
        _ads.Closed -= OnAdClosed;
    }

    private void OnLowMemory() => Resources.UnloadUnusedAssets();
    private void OnRewardGranted(string placement) => Debug.Log($"[PlatformHooks] Reward granted for '{placement}'.");
    private void OnAdClosed() => Debug.Log("[PlatformHooks] Ad closed.");
}
```

The line that leaks is `Application.lowMemory -= OnLowMemory;`. `Application.lowMemory` is static,
so a forgotten removal keeps the destroyed component reachable for the rest of the session and calls
it on every low-memory event. The two SDK lines leak the same way for as long as the SDK object
lives, which is usually longer than any scene.

The version from the Basic Usage sample:

<!-- source: Samples~/BasicUsage/PlatformHooks.cs -->
```csharp
/// <summary>Subscribes to events the game does not own; the add and the remove sit in one call.</summary>
public sealed class PlatformHooks : MonoBehaviour
{
    private IAdsSdk _ads;

    // Call it before the object is enabled, so Awake sees the SDK.
    public void Initialize(IAdsSdk ads) => _ads = ads;

    private void Awake()
    {
        // Custom delegate type: explicit type argument; static lambdas allocate nothing.
        this.Subscribe<Application.LowMemoryCallback>(
            static h => Application.lowMemory += h,
            static h => Application.lowMemory -= h,
            OnLowMemory);

        // Action<T> event: C# 9 cannot infer T from lambdas or method groups, so write <string>.
        this.Subscribe<string>(h => _ads.RewardGranted += h, h => _ads.RewardGranted -= h, OnRewardGranted);

        // Plain Action event: no type argument needed.
        this.Subscribe(h => _ads.Closed += h, h => _ads.Closed -= h, OnAdClosed);
    }

    private void OnLowMemory() => Resources.UnloadUnusedAssets();
    private void OnRewardGranted(string placement) => Debug.Log($"[PlatformHooks] Reward granted for '{placement}'.");
    private void OnAdClosed() => Debug.Log("[PlatformHooks] Ad closed.");
}
```

What happens at each moment:

- **`Awake` runs.** Each call runs its add lambda immediately, so the three handlers are subscribed
  when `Awake` returns. The component lifetime now holds three entries, each remembering the handler
  and its remove lambda.
- **An event is raised.** The source calls the handler directly. The package is not in the call
  path, and nothing is allocated per event.
- **The object is destroyed, its scene is disposed, or `this.GetLifetime().Cancel()` is called.**
  The package calls each remove lambda once, with the same handler instance that was added.
- **The object is deactivated.** Nothing changes: `this.Subscribe` uses the component lifetime. To
  listen only while active, call the same method on the active lifetime in `OnEnable`.

The `-=` is still written, once, and it sits in the same call as the `+=`. Nothing is left for a
second method, so nothing can be forgotten there.

<!-- signature -->
```csharp
public static LifetimeRegistration Subscribe(this Lifetime owner, Action<Action> add, Action<Action> remove, Action handler);
public static LifetimeRegistration Subscribe<T>(this Lifetime owner, Action<Action<T>> add, Action<Action<T>> remove, Action<T> handler);
public static LifetimeRegistration Subscribe<T1, T2>(this Lifetime owner, Action<Action<T1, T2>> add, Action<Action<T1, T2>> remove, Action<T1, T2> handler);
public static LifetimeRegistration Subscribe<T1, T2, T3>(this Lifetime owner, Action<Action<T1, T2, T3>> add, Action<Action<T1, T2, T3>> remove, Action<T1, T2, T3> handler);
public static LifetimeRegistration Subscribe<TDelegate>(this Lifetime owner, Action<TDelegate> add, Action<TDelegate> remove, TDelegate handler) where TDelegate : Delegate;
```

The same five forms exist with `this MonoBehaviour owner` as the receiver; they use the component
lifetime.

### Why the type arguments are written out

Unity 6000.0 compiles C# 9. In
`Subscribe<string>(h => _ads.RewardGranted += h, ..., OnRewardGranted)` the compiler has nothing to
infer `T` from: the lambda parameter `h` has no declared type, and a method group such as
`OnRewardGranted` has no type of its own. Without `<string>` the compiler binds the plain `Action`
form and rejects both lambdas with CS0029: it cannot convert `System.Action` to
`System.Action<string>`. So:

- an `Action` event needs no type argument: `this.Subscribe(add, remove, handler)`;
- an `Action<T>`, `Action<T1, T2>` or `Action<T1, T2, T3>` event needs its argument types:
  `this.Subscribe<string>(...)`;
- an event with its own delegate type needs that type:
  `this.Subscribe<Application.LowMemoryCallback>(...)`.

The two forms with one type argument do not clash. `Subscribe<string>` can only be the `Action<T>`
form, because `string` does not satisfy `where TDelegate : Delegate`, and a delegate type selects
the `TDelegate` form.

### Rules

- The add lambda runs immediately when the owner is active. The package calls the remove lambda
  exactly once, when the owner's generation ends or the returned registration is cancelled.
- If the owner is not active, the add lambda is not called and a default registration is returned.
  One case differs: on an active lifetime whose GameObject is inactive, the add lambda runs and the
  remove lambda runs right after it, with [JANITOR108](Troubleshooting.md#janitor108).
- If the add lambda throws, the exception is routed (source `EventHandler`), nothing is registered
  and the remove lambda is never called. If the remove lambda throws, the exception is routed
  (source `CancelAction`) and the rest of the cancel continues.
- A `null` owner, lambda or handler throws `ArgumentNullException`, and the add lambda is not
  called.
- **A repeat is ignored.** Calling a paired `Subscribe` again on the same owner with the same add
  lambda, remove lambda and handler, while the first subscription is live, adds nothing. The add
  lambda is not called, the existing registration is returned, and the editor and development
  builds log [JANITOR105](Troubleshooting.md#janitor105). "The same" means the same method on the
  same target, for each of the three delegates:
  - recognised: `static` lambdas, lambdas that use only `this` or fields, method groups, and
    delegates kept in a field. The three calls in `PlatformHooks` are all of this kind;
  - not recognised: a lambda that captures a local variable or a parameter. Every call creates a new
    closure object, so two such calls never compare equal, and both subscribe. The same subscription
    written at two different places in the code is not recognised either.
- **Do not write the lambdas over a field that changes.** A lambda that reads a field is compared by
  its method and target, not by the field's value, so a second call after the field was assigned a
  new source is taken for a repeat and ignored. The remove lambda also reads the field when it runs
  and would unsubscribe from the wrong source. Copy the source into a local variable first.
- In `OnEnable`, use the active lifetime so that each activation starts clean, whatever shape the
  lambdas have. On the component lifetime, a paired subscription that is not recognised as a repeat
  is added again on every activation; in the editor the Janitor window reports the second one as
  [JANITOR116](Troubleshooting.md#janitor116).
- **Delivery follows the source event.** The handler itself is what was added, so the source decides
  how it dispatches. A C# event invokes a snapshot of its handlers: a handler that is removed while
  the event is being raised can still be called once in that raise. Of the forms on this page, only
  `OwnedEvent` and `UnityEvent` guarantee that a removed subscription is not delivered.
- The package allocates nothing. Lambdas that capture allocate once, at subscribe time: two
  delegates (`h => _ads.Closed += h` captures `this`), plus a closure object when they capture local
  variables. `static` lambdas, possible for static events such as `Application.lowMemory`, allocate
  nothing.

---

## UnityEvent

<!-- signature -->
```csharp
public static LifetimeRegistration Subscribe(this UnityEvent evt, UnityAction handler, Lifetime owner);
public static LifetimeRegistration Subscribe(this UnityEvent evt, UnityAction handler, Component owner);
public static LifetimeRegistration Subscribe<T0>(this UnityEvent<T0> evt, UnityAction<T0> handler, Lifetime owner);
public static LifetimeRegistration Subscribe<T0>(this UnityEvent<T0> evt, UnityAction<T0> handler, Component owner);
```

The forms for two, three and four arguments follow the same pattern. The call reads like the
`OwnedEvent` one: event first, then handler, then owner. In `ShopPopup` the button listener lives on
the active lifetime:

<!-- source: Samples~/BasicUsage/ShopPopup.cs -->
```csharp
_buyButton.onClick.Subscribe(OnBuyClicked, shown);
```

The demo subscribes its own buttons with the component as the owner:

<!-- source: Samples~/BasicUsage/Demo/BasicUsageDemo.cs -->
```csharp
shop.onClick.Subscribe(ToggleShop, this);
addCoins.onClick.Subscribe(AddCoins, this);
startCountdown.onClick.Subscribe(StartCountdown, this);
stopCountdown.onClick.Subscribe(StopCountdown, this);
launch.onClick.Subscribe(LaunchCoins, this);
sound.onClick.Subscribe(PlaySound, this);
adReward.onClick.Subscribe(AdReward, this);
adClosed.onClick.Subscribe(AdClosed, this);
cancelPopups.onClick.Subscribe(CancelPopups, this);
cancelEverything.onClick.Subscribe(CancelEverything, this);
reload.onClick.Subscribe(ReloadScene, this);
```

### The guard

A `UnityEvent` dispatches over a snapshot of its listeners: a listener removed during `Invoke` still
runs in that `Invoke`. With a plain `AddListener`, listener A could
deactivate or destroy object B, and B's listener would still be called afterwards, on an inactive or
destroyed object.

For that reason `Subscribe` does not add your handler to the event. It adds a small guard listener,
and the guard calls your handler only while the subscription is active. The consequences:

- A subscription that ends during a dispatch is not delivered in that dispatch. `UnityEvent`
  subscriptions follow the same rule as `OwnedEvent`.
- The guard also skips a handler whose target is a destroyed `UnityEngine.Object`.
- The package removes exactly its own guard. A listener you added yourself with `AddListener`, even
  for the same method, is never removed by it.
- The reverse also holds: `RemoveListener(handler)` does not remove a subscription made with
  `Subscribe`, because the raw handler was never added. Use the returned registration, or end the
  owner.
- A handler that throws is not caught by the package and not routed to `LifetimeErrors.Handler`. It
  behaves exactly as an exception in a listener added with `AddListener`.
- Each `Subscribe` call allocates the guard and its listener delegate. Nothing is allocated per
  `Invoke`. The removal is Unity's own `RemoveListener` and costs what it costs in the engine.
- Listeners assigned in the Inspector are not affected in any way.

### Duplicates and labels

Subscribing the same handler to the same event with the same owner again returns the existing
registration and logs [JANITOR105](Troubleshooting.md#janitor105), as for `OwnedEvent`. The same
handler on another event, or under another owner, is a separate subscription. The check walks the
owner's live entries at subscribe time, never at invoke time.

In the Janitor window each subscription is labelled with the member and line of its `Subscribe`
call. A helper method that subscribes on behalf of others (`Bind(button, handler)`) makes every row
read the same, so subscribe at the place that owns the handler.

---

## IDisposable

<!-- signature -->
```csharp
public static LifetimeRegistration AddTo<T>(this T disposable, Lifetime lifetime) where T : IDisposable;
public static LifetimeRegistration AddTo<T>(this T disposable, Component owner) where T : IDisposable;
public static LifetimeRegistration AddTo<T>(this T disposable, GameObject owner) where T : IDisposable;
```

Many libraries return an `IDisposable` from their subscribe call, and so does much game code.
`AddTo` disposes it when the owner's generation ends. In the Basic Usage sample an input lock hands
out a block as an `IDisposable`:

<!-- source: Samples~/BasicUsage/Support/InputGate.cs -->
```csharp
// The caller owns the returned block and must dispose it.
public IDisposable Block()
{
    _blocks++;
    Debug.Log($"[InputGate] Blocked ({_blocks} held).");
    return new Release(this);
}
```

`ModalBlocker` holds such a block for as long as its GameObject is active:

<!-- source: Samples~/BasicUsage/ModalBlocker.cs -->
```csharp
/// <summary>Holds an input block for as long as its active lifetime lasts: until deactivation, destroy or a Cancel.</summary>
public sealed class ModalBlocker : MonoBehaviour
{
    private InputGate _gate;

    // Call it before the object is enabled, so OnEnable sees the gate.
    public void Initialize(InputGate gate) => _gate = gate;

    private void OnEnable()
    {
        var shown = this.GetActiveLifetime();
        _gate.Block().AddTo(shown);         // an IDisposable owned by the lifetime: disposed when it ends
    }
}
```

- **The object is activated.** `OnEnable` takes a block and gives it to the active lifetime.
- **The object is deactivated.** The active lifetime is cancelled, the block is disposed, and the
  gate is released. In the demo the blocker is a child of the shop popup, so this happens when the
  popup is closed.
- **The object is activated again.** `OnEnable` takes a new block in the new generation.
- **The object is destroyed while active.** The block is disposed with the lifetime.

The rules:

- The item is disposed once: by the first `Cancel()`, by `Dispose()`, by the owner's destruction, or
  by `Cancel()` on the returned registration, whichever comes first.
- If the lifetime is not active, the item is disposed inside the `AddTo` call.
- With a `null` owner the item is disposed first and then `ArgumentNullException` is thrown, so the
  item cannot leak through a programming error. A destroyed owner disposes the item at once.
- A component owner resolves as everywhere else: a `MonoBehaviour` to its component lifetime,
  another component or a GameObject to the GameObject's lifetime.
- A class instance costs no allocation. A struct is boxed once.
- Adding the same instance twice registers it twice, and it is disposed once per registration.
- An exception thrown by `Dispose()` is routed with the source `CancelAction`.

A library that defines its own `AddTo(IDisposable, Component)` extension, as R3 and UniRx do, takes
`x.AddTo(this)` for itself in a file that imports both namespaces: an extension without optional
parameters is the better match, so the call compiles and the disposable is not owned by a lifetime.
The call is ambiguous (CS0121) only when the other extension has the same optional parameters.
`x.AddTo(this.GetLifetime())` always binds to Janitor, because only Janitor has an overload that
takes a `Lifetime`.

---

## Why there is no removal line

Cleanup that depends on a line someone has to remember is the cause of the bugs this package
exists to remove. A removal written in a second method has four ways to go wrong, and all four
appear in the "before" classes above:

1. **It is forgotten**, or added to `OnDestroy` when the subscription was made in `OnEnable`.
2. **It drifts.** A subscription is added to `OnEnable` during a later change and its mirror is not.
3. **It doubles.** Without its mirror, a subscription made in `OnEnable` is added again on every
   activation.
4. **It runs too late.** The handler fires between the moment its owner stopped being valid and the
   moment the removal runs.

With the owner in the subscribe call, there is no second method. The removal happens at the moment
the owner's generation ends, which is also the moment the owner's tasks, tweens and coroutines stop,
so a handler cannot fire into a half-stopped object.

Three rules follow from this:

- **There is no subscribe without an owner.** A subscription that should last for the whole game
  names `Lifetime.App`.
- **`Cancel()` removes subscriptions.** A subscription is a registered item like a task or a tween.
  Keep long-lived listening on the object's own lifetime and put restartable activity into a child
  area, so that cancelling the activity does not stop the listening (see
  [Concepts](Concepts.md#cancel-removes-subscriptions-too)).
- **`OnCancel` is not for unsubscribing.** Writing `+=` and then
  `OnCancel(() => source.X -= handler)` brings the second place back. Use the paired `Subscribe`,
  which keeps both halves in one call.

What `OnCancel` is for is cleanup the package knows nothing about. `PreviewRenderer` in the sample
releases a temporary render texture with it:

<!-- source: Samples~/BasicUsage/PreviewRenderer.cs -->
```csharp
/// <summary>Renders a camera into a temporary texture that lives while the object is active.</summary>
public sealed class PreviewRenderer : MonoBehaviour
{
    private Camera _camera;
    private RawImage _target;
    private RenderTexture _texture;

    // The views arrive here because the demo builds its UI in code.
    public void Initialize(Camera camera, RawImage target)
    {
        _camera = camera;
        _target = target;
    }

    // Takes the texture on first use and again after any end of the lifetime, so a cancel never leaves a released one.
    public void Render()
    {
        if (!gameObject.activeInHierarchy)
        {
            return;
        }

        if (_texture == null)
        {
            Acquire();
        }

        _camera.targetTexture = _texture;
        _camera.Render();
        _camera.targetTexture = null;
    }

    private void Acquire()
    {
        _texture = RenderTexture.GetTemporary(256, 256, 16);
        _target.texture = _texture;
        this.GetActiveLifetime().OnCancel(this, static self => self.Release());   // cleanup the package knows nothing about
    }

    private void Release()
    {
        RenderTexture.ReleaseTemporary(_texture);
        _texture = null;
        if (_target != null)
        {
            _target.texture = null;        // the target may already be destroyed
        }
    }
}
```

- **`Render()` is called for the first time.** `_texture` is null, so `Acquire()` takes a texture
  and registers its release on the active lifetime in the same method. The component is the state of
  a `static` lambda, so nothing is captured.
- **`Render()` is called again.** The texture is there and is reused.
- **The object is deactivated, the lifetime is cancelled from outside, or the object is destroyed.**
  The action runs once: the texture is released and the field is cleared. The class has no
  `OnDisable` and no `OnDestroy`.
- **`Render()` is called after that**, with the object active again. The field is null, so a new
  texture is taken and a new release is registered in the new generation.

The class takes the texture where it uses it, not once in `Awake`, and that is the point. An
`OnCancel` action runs on **every** end of the generation, including a `Cancel()` after which the
object lives on. A resource that is released by a cancel action therefore has to be taken again by
the code that needs it; taken once in `Awake`, it would be gone after the first cancel while the
class kept using it.

---

## Binding another event source

A message bus, a reactive library or an SDK with its own subscribe and unsubscribe methods can be
bound to lifetimes with the public `OnCancel` overloads. The DOTween and Zenject integrations of
this package are built on these overloads and have no access to the core's internals, so anything
they can do, an adapter of yours can do.

<!-- signature -->
```csharp
public LifetimeRegistration OnCancel(Action action);
public LifetimeRegistration OnCancel<TState>(TState state, Action<TState> action) where TState : class;
public LifetimeRegistration OnCancel<TState>(TState state, Action<TState> action, Func<TState, bool> isFinished) where TState : class;
public LifetimeRegistration OnCancel<T1, T2>(T1 first, T2 second, Action<T1, T2> action) where T1 : class where T2 : class;
```

These are members of `Lifetime`; the same four exist as extension methods on `MonoBehaviour`. The
action runs once, when the current generation ends or when the returned registration is cancelled.

An adapter for another source follows the shape of the built-in ones:

1. Offer `source.Subscribe(handler, Lifetime owner)`, so the call site names the owner.
2. Subscribe to the source, then register the removal with
   `owner.OnCancel(source, subscription, static (s, sub) => s.Unsubscribe(sub))`. With a `static`
   lambda and reference-type state this allocates nothing.
3. Return the `LifetimeRegistration`, so one subscription can be cancelled on its own.

Behaviour the adapter gets for free:

- If the owner is not active, or is an active lifetime whose GameObject is inactive, `OnCancel` runs
  the action immediately and returns a default registration. An adapter that subscribes first and
  registers second is therefore correct for an ending owner too: the subscription is removed in the
  same call.
- The removal runs in the cancel pass with everything else, newest first, and an exception from it
  is routed, not thrown.
- The overload with `isFinished` is for one-shot subscriptions on a long-lived owner. The package
  calls the probe during occasional sweeps and drops an item that reports `true` without running its
  action, so finished items do not pile up. A `Cancel()` or `Dispose()` that comes before the next
  sweep still runs the action, so the action must tolerate an item that has already finished.

What the adapter has to decide itself is what the built-in forms decided above: whether a duplicate
subscription is an error, and whether a subscription removed during a dispatch may still be
delivered.

To make an adapter's own problems show up in the Janitor window, call
`LifetimeDiagnostics.Report(lifetime, diagnosticId, message)`. The call exists in the editor only
and is compiled out of players. See [Diagnostics](Diagnostics.md).

---

## See also

- [Concepts](Concepts.md): generations, and why `Cancel()` removes subscriptions
- [Components and scenes](Components-and-Scenes.md): component, GameObject and active lifetimes as
  owners
- [Tasks and errors](Tasks-and-Errors.md): where handler exceptions are routed
- [Zenject](Zenject.md): SignalBus subscriptions with an owner
- [Migration](Migration.md): converting existing `+=` and `AddListener` code
- [Troubleshooting](Troubleshooting.md#janitor105): JANITOR105, a duplicate subscription
