# Migration

[Back to index](index.md)

Recipes for replacing hand-written cleanup with lifetimes. Each recipe shows the complete class as
it is usually written today, names the line that leaks or throws, and then shows the replacement as
it ships in the samples.

The **Before** classes are illustrations and are not compiled. The **After** code is copied from the
samples that ship with the package; `Initialize(...)` in those classes stands in for serialized
fields or injection (see [About the code in these guides](index.md#about-the-code-in-these-guides)).

The package only knows about work that was registered through it, so converted and unconverted
classes can live side by side. Convert one class at a time; the
[checklist](#adopting-the-package-one-class-at-a-time) at the end gives the order.

| You have | Recipe |
|---|---|
| A `CancellationTokenSource` field cancelled in `OnDestroy` | [1](#1-a-cancellationtokensource-field-cancelled-in-ondestroy) |
| Cancel the previous run, start a new one | [2](#2-cancel-the-previous-run-start-a-new-one) |
| A `Tween` field killed in `OnDisable` | [3](#3-a-tween-field-killed-in-ondisable) |
| A `Coroutine` field with `StopCoroutine` | [4](#4-a-coroutine-field-with-stopcoroutine) |
| `+=` in `OnEnable` and `-=` in `OnDisable` | [5](#5--in-onenable-and---in-ondisable) |
| `AddListener` and `RemoveListener` | [6](#6-addlistener-and-removelistener) |
| `SignalBus.Subscribe` and `TryUnsubscribe` | [7](#7-signalbussubscribe-and-tryunsubscribe) |
| `async void` with a delay | [8](#8-async-void-with-a-delay) |
| A string-keyed global registry | [9](#9-a-string-keyed-global-registry) |

---

## 1. A `CancellationTokenSource` field cancelled in `OnDestroy`

### Before

<!-- illustrative: before -->
```csharp
public sealed class Countdown : MonoBehaviour
{
    [SerializeField] private TMP_Text _label;
    private CancellationTokenSource _cts;

    public event Action Finished;

    public void StartCountdown(int seconds)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        TickAsync(seconds, _cts.Token).Forget();
    }

    public void StopCountdown() => _cts?.Cancel();

    private void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private async UniTask TickAsync(int seconds, CancellationToken ct)
    {
        for (var left = seconds; left > 0; left--)
        {
            _label.text = left.ToString();
            await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: ct);
        }
        _label.text = "Go!";
        Finished?.Invoke();
    }
}
```

### The lines that fail

- `public void StopCountdown() => _cts?.Cancel();` throws `ObjectDisposedException` when it is
  called after `OnDestroy`, because `OnDestroy` disposed the source and left the field set. During a
  scene unload Unity destroys objects in an order you do not control, so another object's teardown
  can call `StopCountdown()` on a countdown that is already destroyed. `StartCountdown` has the same
  first line and the same problem.
- `private void OnDestroy()` is the only thing that stops the countdown when the object goes away.
  Delete it, or add a second token source and forget to add it here, and `TickAsync` keeps running
  on a destroyed object: it writes to the destroyed label every second and finally raises `Finished`
  from an object that no longer exists.
- `public event Action Finished;` hands the same problem to every subscriber: each one needs its own
  `-=` (recipe 5).

### After

<!-- source: Samples~/BasicUsage/Countdown.cs -->
```csharp
/// <summary>A countdown restarted with Cancel() and then Run; it stops with its object.</summary>
public sealed class Countdown : MonoBehaviour
{
    private readonly OwnedEvent _finished = new("Finished");
    private TMP_Text _label;
    private Lifetime _run;

    public IOwnedEvent Finished => _finished;

    // The view arrives here because the demo builds its UI in code.
    public void Initialize(TMP_Text label) => _label = label;

    private void Awake() => _run = this.GetLifetime().CreateChild("Run");

    public void StartCountdown(int seconds)
    {
        _run.Cancel();                                    // a countdown already running stops here
        _run.Run(ct => TickAsync(seconds, ct));
    }

    public void StopCountdown() => _run.Cancel();

    private async UniTask TickAsync(int seconds, CancellationToken ct)
    {
        for (var left = seconds; left > 0; left--)
        {
            _label.text = left.ToString();
            await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: ct);
        }

        _label.text = "Go!";
        _finished.Invoke();
    }
}
```

What happens at each moment:

- **`Awake`.** `this.GetLifetime()` is the lifetime of this component; it is disposed when the
  component is destroyed or its scene is disposed. `CreateChild("Run")` creates an area under it for
  the one operation that can be restarted. The name is a label for the Janitor window.
- **`StartCountdown`.** `_run.Cancel()` ends a countdown that is still running: its token is
  cancelled and the pending delay throws `OperationCanceledException` the next time it checks the
  token, which `Run` swallows without logging. The area stays usable, and `_run.Run(...)` starts the
  new countdown with a new token. The old task cannot touch the label any more, because every await
  in it uses the old token.
- **`StopCountdown`.** The same `Cancel()`. It can be called any number of times and at any moment,
  including after the object was destroyed: `Cancel()` and `Dispose()` never throw, and on a
  disposed lifetime they do nothing.
- **The object is destroyed, or its scene is disposed.** The component lifetime is disposed and
  `_run` is disposed with it, which cancels the countdown. There is no `OnDestroy`.
- **`StartCountdown` after the object is gone.** `Run` on a disposed lifetime does not start the
  work.
- **`Finished`.** It is an `OwnedEvent` now. Subscribers name their owner when they subscribe and
  write no removal (recipe 5).

When the work is never restarted you do not need the area or the field. Start it on the component
itself, as `ScoreReveal` in the same sample does:

<!-- source: Samples~/BasicUsage/ScoreReveal.cs -->
```csharp
// Call it once per object: a second call would start a second task. Countdown shows the restart form.
public void Reveal(int score) => this.Run(ct => RevealAsync(score, ct));
```

`this.Run(...)` uses the component lifetime, so the work is cancelled when the component is
destroyed or its scene is disposed. `Run`, the exception policy and awaits that do not accept a
token are covered in [Tasks and errors](Tasks-and-Errors.md).

---

## 2. Cancel the previous run, start a new one

### Before

<!-- illustrative: before -->
```csharp
public sealed class CustomizationService : IDisposable
{
    private readonly AvatarPreview _preview;
    private readonly IOutfitRepository _repository;
    private CancellationTokenSource _applyCts;

    public CustomizationService(AvatarPreview preview, IOutfitRepository repository)
    {
        _preview = preview;
        _repository = repository;
    }

    public void Apply(Outfit outfit)
    {
        _applyCts?.Cancel();
        _applyCts?.Dispose();
        _applyCts = new CancellationTokenSource();
        ApplyAsync(outfit, _applyCts.Token).Forget();
    }

    public void CancelApply() => _applyCts?.Cancel();

    // Runs only if the binding also exposes IDisposable.
    public void Dispose()
    {
        _applyCts?.Cancel();
        _applyCts?.Dispose();
    }

    private async UniTask ApplyAsync(Outfit outfit, CancellationToken ct)
    {
        var look = await _repository.LoadLookAsync(outfit.Id, ct);
        _preview.Show(look);
        await _preview.Root.DOPunchScale(Vector3.one * 0.1f, 0.3f)
            .AsyncWaitForCompletion().AsUniTask().AttachExternalCancellation(ct);   // on cancel the punch keeps running
        await _repository.SaveSelectionAsync(outfit.Id, ct);
    }
}

public sealed class ProfileResetService
{
    private readonly CustomizationService _customization;
    public ProfileResetService(CustomizationService customization) => _customization = customization;

    public void ResetProfile()
    {
        _customization.CancelApply();
        // reset profile data here
    }
}
```

### The lines that fail

- `public void Dispose()` is the only thing that cancels a running apply when the scene ends, and a
  plain C# class has no `OnDestroy`. It runs only if something calls it: a DI container that was
  told the class is disposable, or code you wrote. Otherwise `ApplyAsync` continues after the scene
  is gone and `_preview.Show(look)` touches destroyed objects.
- `.AttachExternalCancellation(ct)` makes the `await` stop waiting when the token is cancelled, but
  the tween it was waiting for keeps running. A cancelled apply still punches the preview, and a new
  apply starts a second punch on top of it.
- `public void CancelApply() => _applyCts?.Cancel();` throws `ObjectDisposedException` when another
  service calls it after `Dispose()`.

### After

<!-- source: Samples~/DOTweenUsage/CustomizationService.cs -->
```csharp
/// <summary>Applies an outfit: a new call restarts the work, and another service can cancel it.</summary>
public sealed class CustomizationService
{
    private readonly AvatarPreview _preview;
    private readonly IOutfitRepository _repository;
    private readonly Lifetime _apply;

    // The lifetime comes from the composition root; with Zenject it is injected.
    public CustomizationService(Lifetime lifetime, AvatarPreview preview, IOutfitRepository repository)
    {
        _preview = preview;
        _repository = repository;
        _apply = lifetime.CreateChild("Apply");
    }

    public void Apply(Outfit outfit)
    {
        _apply.Cancel();                                  // the previous apply stops: load, punch and save
        _apply.Run(ct => ApplyAsync(outfit, ct));
    }

    // Intent method: callers cancel the apply without seeing the raw area.
    public void CancelApply() => _apply.Cancel();

    private async UniTask ApplyAsync(Outfit outfit, CancellationToken ct)
    {
        var look = await _repository.LoadLookAsync(outfit.Id, ct);
        _preview.Show(look);
        await _preview.Root.DOPunchScale(Vector3.one * 0.1f, 0.3f).AwaitCompletionAsync(ct);  // a cancel kills it and throws
        await _repository.SaveSelectionAsync(outfit.Id, ct);
    }
}
```

The other service is unchanged. It still calls an intent method and never sees the area:

<!-- source: Samples~/DOTweenUsage/ProfileResetService.cs -->
```csharp
/// <summary>Resets the profile; it cancels a running apply through an intent method, not through the lifetime.</summary>
public sealed class ProfileResetService
{
    private readonly CustomizationService _customization;

    public ProfileResetService(CustomizationService customization) => _customization = customization;

    public void ResetProfile()
    {
        _customization.CancelApply();
        // reset profile data here
    }
}
```

What happens at each moment:

- **Construction.** The service receives a `Lifetime` from whoever creates it: the composition root
  passes one, or Zenject injects one (see [Zenject](Zenject.md)). `CreateChild("Apply")` makes the
  area for the one operation that restarts.
- **`Apply`.** `_apply.Cancel()` stops the previous apply wherever it is: in the load, in the punch
  or in the save. `AwaitCompletionAsync(ct)` kills the tween when the token is cancelled and ends
  the await with `OperationCanceledException`, so the punch does not keep running. `_apply.Run(...)`
  then starts the new apply.
- **`CancelApply`.** The same `Cancel()`, safe at any time and any number of times.
- **The owner goes away.** When the lifetime given to the constructor ends (its scene is disposed,
  its context is destroyed), `_apply` is disposed with it and a running apply is cancelled. The
  class does not implement `IDisposable` and nobody has to remember to call anything.

`AwaitCompletionAsync` is part of the DOTween integration ([DOTween](DOTween.md)). The restart
pattern itself is core only: `Countdown` in recipe 1 is the same three lines without a tween. Expose
an intent method such as `CancelApply()` and keep the area private, unless other classes are meant
to register work into it ([Organizing work](Organizing-Work.md)).

---

## 3. A `Tween` field killed in `OnDisable`

### Before

<!-- illustrative: before -->
```csharp
public sealed class CoinPickup : MonoBehaviour
{
    [SerializeField] private float _flyDuration = 0.5f;
    private Transform _target;
    private CoinPool _pool;
    private Tween _fly;
    private CancellationTokenSource _cts;

    public void Launch(Transform target, CoinPool pool)
    {
        _target = target;
        _pool = pool;
        gameObject.SetActive(true);
    }

    private void OnEnable()
    {
        _fly = transform.DOMove(_target.position, _flyDuration).SetEase(Ease.InQuad);
        _cts = new CancellationTokenSource();
        ReturnLaterAsync(_cts.Token).Forget();
    }

    private void OnDisable()
    {
        _fly?.Kill();              // with recycling on, this can kill another coin's tween
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async UniTask ReturnLaterAsync(CancellationToken ct)
    {
        await UniTask.Delay(TimeSpan.FromSeconds(_flyDuration), cancellationToken: ct);
        _pool.Release(this);
    }
}
```

### The lines that fail

- `_fly?.Kill();` acts on whatever the field points at. With DOTween recycling turned on, a tween
  that was killed (a finished tween is killed automatically by default) goes back to DOTween's pool
  and its object is handed out again for the next tween anyone creates. The flight has usually
  finished by the time the pool deactivates the coin, so `_fly` now refers to a tween that belongs
  to someone else, and this line kills it. The package's tests reproduce the reuse: the stale
  reference even reports `IsActive() == true`.
- Remove the `_fly?.Kill()` line instead and the opposite failure appears: a coin deactivated in
  mid-flight keeps its tween, which goes on moving an inactive object and fights the next flight
  when the coin is launched again.
- The four lines of `OnDisable` have to mirror `OnEnable` exactly, in every pooled class.

### After

<!-- source: Samples~/DOTweenUsage/CoinPickup.cs -->
```csharp
/// <summary>A pooled coin: however its flight ends, the coin goes back to the pool.</summary>
public sealed class CoinPickup : MonoBehaviour
{
    [SerializeField] private float _flyDuration = 0.5f;
    private Transform _target;
    private CoinPool _pool;

    public void Launch(Transform target, CoinPool pool)
    {
        _target = target;
        _pool = pool;
        gameObject.SetActive(true);
    }

    private void OnEnable()
    {
        var spawn = this.GetActiveLifetime();                                       // ends on SetActive(false) and on any outside Cancel
        spawn.OnCancel(this, static coin => coin._pool.Release(coin));              // the one place that returns the coin
        transform.DOMove(_target.position, _flyDuration).SetEase(Ease.InQuad).AddTo(spawn);
        spawn.After(_flyDuration, gameObject, static go => go.SetActive(false));    // the timer only ends the flight
    }
}
```

What happens at each moment:

- **`OnEnable`.** `this.GetActiveLifetime()` returns the active lifetime of the GameObject, which is
  cancelled every time the GameObject is deactivated. Three things are registered on it. The
  `OnCancel` action is the one place that returns the coin to the pool; it is registered first, so
  it runs last. `AddTo(spawn)` registers the tween and returns the same tween, so it chains like any
  DOTween call. The `After` timer only deactivates the coin when the flight time is over.
- **The flight ends.** The timer fires and deactivates the coin. The deactivation cancels the
  lifetime: a tween that has already finished is not touched (one still in its last frame is
  killed), and then the cancel action releases the coin to the pool.
- **Something deactivates the coin in mid-flight.** The same cancel kills the tween at once, cancels
  the timer and releases the coin, once.
- **Something cancels the lifetime from outside,** for example a category the coin was placed in.
  The tween and the timer stop, and the cancel action releases the coin; the pool deactivates it.
  Without the cancel action the coin would stay active with nothing left to send it back.
- **The coin is launched again.** `OnEnable` registers into a fresh generation. Nothing from the
  previous flight is left.
- **The coin is destroyed, or its scene is disposed.** The lifetime is disposed; the same things
  stop.

A registered tween is taken out of DOTween's recycling (`SetRecyclable(false)`), which is what makes
a held reference safe; the cost and the reasoning are in
[ADR-003](Design/Decisions/ADR-003-Tweens-Are-Not-Recyclable.md). A tween that must land on its end
value instead of freezing uses `TweenCancelMode.Complete`; see [DOTween](DOTween.md). The Basic
Usage sample has the same class without DOTween, and [Pooling](Pooling.md) explains the active
lifetime.

---

## 4. A `Coroutine` field with `StopCoroutine`

### Before

<!-- illustrative: before -->
```csharp
public sealed class ShopPopup : MonoBehaviour
{
    [SerializeField] private Graphic _saleBadge;
    private Coroutine _badgeBlink;

    private void OnEnable()
    {
        _badgeBlink = StartCoroutine(BlinkBadge());
    }

    private void OnDisable()
    {
        if (_badgeBlink != null) StopCoroutine(_badgeBlink);
    }

    private IEnumerator BlinkBadge()
    {
        while (true)
        {
            _saleBadge.enabled = !_saleBadge.enabled;
            yield return new WaitForSeconds(0.5f);
        }
    }
}
```

### The lines that fail

- `if (_badgeBlink != null) StopCoroutine(_badgeBlink);` needs its null check. `StartCoroutine`
  returns `null` for a routine that finishes without yielding, and for a host that is inactive (in
  that case Unity also logs an error). Every other place that stops the coroutine needs the same check, and `StopCoroutine(null)` throws.
- `_badgeBlink = StartCoroutine(BlinkBadge());` overwrites the handle. If a second code path starts
  the blink while it is already running, the first coroutine keeps running and nothing holds a
  handle to stop it.
- Only this component can stop the blink, and only for the one reason it knows: it was disabled. A
  second reason to stop, such as "close everything that belongs to popups", needs a public method on
  every class that owns a coroutine.

Unity already stops a coroutine when its host GameObject is deactivated or destroyed. A coroutine
that has no other reason to stop needs neither the field nor the package.

### After

<!-- source: Samples~/BasicUsage/ShopPopup.cs -->
```csharp
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
```

`BlinkBadge` itself does not change. What happens at each moment:

- **`OnEnable`.** `shown.StartCoroutine(this, BlinkBadge())` starts the coroutine on `this` (the
  host that runs it) and ties it to the lifetime `shown`. There is no global coroutine runner: the
  host is always a `MonoBehaviour` you name.
- **The popup is deactivated.** The active lifetime is cancelled and the coroutine is stopped.
- **`Popups` is cancelled while the popup stays open.** The coroutine stops although its host is
  still active and enabled. This is the case the handle field could not express.
- **The popup is destroyed, or its scene is disposed.** The lifetime is disposed; the coroutine
  stops.
- **The host is inactive or destroyed when the call is made.** The coroutine is not started and
  Unity logs nothing; the editor and development builds report
  [JANITOR110](Troubleshooting.md#janitor110) instead.

`StartCoroutine` returns a `LifetimeRegistration`. Keep it when one coroutine has to stop on its
own, as `HintPulse` in the same sample does:

<!-- source: Samples~/BasicUsage/HintPulse.cs -->
```csharp
public void StartPulse()
{
    _pulse.Cancel();                                            // a default or stale handle does nothing
    _pulse = this.GetLifetime().StartCoroutine(this, Pulse());
}

// Only this coroutine stops; everything else on the lifetime keeps running.
public void StopPulse()
{
    _pulse.Cancel();
    _hint.alpha = 1f;
}
```

`Cancel()` on the handle is safe on a finished, stopped or never-started coroutine, so it needs no
null check. See [Coroutines](Coroutines.md).

---

## 5. `+=` in `OnEnable` and `-=` in `OnDisable`

There are two cases: an event your own code declares, and an event that belongs to Unity or to a
third-party library.

### Before: an event you declare

<!-- illustrative: before -->
```csharp
public sealed class Wallet
{
    private int _coins;
    public event Action<int> CoinsChanged;
    public void Add(int amount) { _coins += amount; CoinsChanged?.Invoke(_coins); }
}

public sealed class ShopPopup : MonoBehaviour
{
    [SerializeField] private TMP_Text _coinsLabel;
    [Inject] private Wallet _wallet;

    private void OnEnable()
    {
        _wallet.CoinsChanged += OnCoinsChanged;
    }

    private void OnDisable()
    {
        _wallet.CoinsChanged -= OnCoinsChanged;
    }

    private void OnCoinsChanged(int coins) => _coinsLabel.text = coins.ToString();
}
```

### The lines that fail

- `_wallet.CoinsChanged -= OnCoinsChanged;` is the line that gets forgotten, or ends up in the wrong
  method. Without it the wallet, which lives longer than the popup, keeps a delegate to it. After
  the popup is destroyed the next `Add` still calls `OnCoinsChanged`, which writes to a destroyed
  label.
- `CoinsChanged?.Invoke(_coins);` stops at the first handler that throws. The handlers subscribed
  after it are never called, so one subscriber that fails on a destroyed object breaks unrelated
  listeners.
- `CoinsChanged?.Invoke(_coins);` also calls a snapshot of the handler list. If an earlier handler
  closes the popup, the popup's `-=` runs, and its handler is still called once in that same invoke,
  on an object that has already cleaned up.
- A `+=` in `OnEnable` paired with a `-=` in `OnDestroy` adds one more subscription every time the
  popup is opened, and the handler then runs once per opening.

### After: an event you declare

The publisher declares an `OwnedEvent<int>` and exposes the subscribe-only view:

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

The subscriber names its owner in the same call. This is the last line of the `OnEnable` shown in
recipe 4, where `shown` is the popup's active lifetime:

<!-- source: Samples~/BasicUsage/ShopPopup.cs -->
```csharp
_wallet.CoinsChanged.Subscribe(OnCoinsChanged, shown);
```

What happens at each moment:

- **Subscribe.** The handler is added for as long as the current generation of `shown` lasts.
- **The popup is deactivated, `Popups` is cancelled, the popup is destroyed, or its scene is
  disposed.** The owner's generation ends and the package removes the subscription.
- **`Add` is called.** Handlers run in subscription order, each in its own `try`/`catch`: an
  exception is routed to `LifetimeErrors.Handler` and the remaining handlers still run. A handler
  whose owner ended earlier in the same invoke is skipped.
- **The same handler is subscribed again for the same owner.** It is ignored, the existing
  registration is returned, and the editor and development builds report
  [JANITOR105](Troubleshooting.md#janitor105).

An owner can also be a component, as in `CoinsLabel` of the same sample:

<!-- source: Samples~/BasicUsage/CoinsLabel.cs -->
```csharp
private void Awake()
{
    OnCoinsChanged(_wallet.Coins);
    _wallet.CoinsChanged.Subscribe(OnCoinsChanged, this);   // owned by the component: it ends with it, not with SetActive
}
```

`Subscribe(handler, this)` binds the subscription to the component lifetime, which lasts until the
component is destroyed. Use that in `Awake` or `Start`. In `OnEnable` use the active lifetime, as
above; with `this` as the owner the second `OnEnable` is a duplicate.

The owner should be whichever side ends first. Here the popup ends before the wallet, so the popup
owns the subscription. When the publisher is the short-lived side (a long-lived manager listening to
objects that come and go), pass the publisher's lifetime as the owner; otherwise the manager keeps
one entry per dead publisher until its own generation ends. `CountdownTracker` in the same sample
shows it; see [Events](Events.md).

`OwnedEvent` exists with zero to three arguments. It is code only (it is not serialized and does not
show in the Inspector), and it is not an event bus. See [Events](Events.md).

### Before: an event you do not own

<!-- illustrative: before -->
```csharp
public sealed class PlatformHooks : MonoBehaviour
{
    [Inject] private IAdsSdk _ads;

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
    private void OnRewardGranted(string placement) { }
    private void OnAdClosed() { }
}
```

`Application.lowMemory` is static and the SDK object usually lives for the whole session. A fourth
`+=` added to `Awake` without its `-=` keeps the destroyed component reachable and keeps calling it.

### After: an event you do not own

A C# `event` cannot be passed to a library, so the call takes the add and the remove together:

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

- `add` runs immediately. The package keeps `remove` and the handler, and calls `remove(handler)`
  exactly once: when the current generation of the component's lifetime ends (the component is
  destroyed, its scene is disposed, or the lifetime is cancelled), or when the returned registration
  is cancelled.
- The removal sits in the same statement as the subscription, so it cannot be forgotten in another
  method. This is the only place where `-=` appears in code that uses the package.
- The type argument has to be written out for every form except the plain `Action` one (`<string>`,
  `<Application.LowMemoryCallback>`). C# 9 cannot infer it from a lambda or a method group; when it
  is missing the compiler reports `CS0029` on the add and remove lambdas.
- `static` lambdas allocate nothing. Lambdas that capture a field, like the two SDK ones here,
  allocate once when `Subscribe` is called and nothing afterwards.
- A repeat of the same call for the same owner (the same `add`, `remove` and handler) is ignored,
  with [JANITOR105](Troubleshooting.md#janitor105). The check recognises static lambdas, lambdas
  that capture only `this` or fields, method groups and cached delegates. It cannot recognise
  lambdas that capture a local variable or a parameter, because those are new objects on every call.

This form also works against a C# `event` in your own code that you have not converted yet, which
makes it the first step for a publisher with many subscribers.

---

## 6. `AddListener` and `RemoveListener`

### Before

<!-- illustrative: before -->
```csharp
public sealed class ShopPopup : MonoBehaviour
{
    [SerializeField] private Button _buyButton;
    [Inject] private OfferService _offers;

    private void OnEnable()
    {
        _buyButton.onClick.AddListener(OnBuyClicked);
    }

    private void OnDisable()
    {
        _buyButton.onClick.RemoveListener(OnBuyClicked);
    }

    private void OnBuyClicked() => _offers.BuySelected();
}
```

### The lines that fail

- `_buyButton.onClick.RemoveListener(OnBuyClicked);` is the line that gets forgotten. `AddListener`
  does not check for duplicates, so every time the popup is opened it adds one more listener, and
  one click on Buy then buys once per opening.
- `UnityEvent` calls a snapshot of its listeners. A listener removed while the event is being
  invoked still runs in that invoke, so a handler can run on an object
  that an earlier handler has closed.

### After

The subscription is one line of the `OnEnable` shown in recipe 4:

<!-- source: Samples~/BasicUsage/ShopPopup.cs -->
```csharp
_buyButton.onClick.Subscribe(OnBuyClicked, shown);
```

What happens at each moment:

- **Subscribe.** The package adds a listener to the event. It is a small guard around your handler,
  not the handler itself: the guard calls the handler only while the subscription is live.
- **The owner's generation ends.** The package calls `RemoveListener` for that guard. A handler
  whose owner ended earlier in the same invoke is not called.
- **The same handler is subscribed again for the same owner.** It is ignored, with
  [JANITOR105](Troubleshooting.md#janitor105).

`Subscribe` exists for `UnityEvent` with zero to four arguments and takes a `Lifetime` or a
`Component` as the owner. It costs one guard object and one delegate per call, and nothing per
invoke. Listeners wired in the Inspector are not affected. See [Events](Events.md).

---

## 7. `SignalBus.Subscribe` and `TryUnsubscribe`

### Before

<!-- illustrative: before -->
```csharp
public sealed class ShopPopup : MonoBehaviour
{
    [Inject] private SignalBus _signalBus;
    [Inject] private OfferService _offers;

    private void OnEnable()
    {
        _signalBus.Subscribe<StoreRefreshedSignal>(OnStoreRefreshed);
    }

    private void OnDisable()
    {
        _signalBus.TryUnsubscribe<StoreRefreshedSignal>(OnStoreRefreshed);
    }

    private void OnStoreRefreshed() => _offers.Invalidate();
}
```

### The lines that fail

- `_signalBus.TryUnsubscribe<StoreRefreshedSignal>(OnStoreRefreshed);` is the line that gets
  forgotten. Without it the signal keeps reaching a popup that is closed or destroyed.
- `_signalBus.Subscribe<StoreRefreshedSignal>(OnStoreRefreshed);` then runs a second time on the
  next `OnEnable`. SignalBus holds one subscription per signal and handler; its check for a repeated
  subscription is an assert that exists only in the editor.

### After

<!-- source: Samples~/ZenjectUsage/ShopPopup.cs -->
```csharp
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
```

The fifth line of the method is the signal subscription; the other lines are the same registrations
as in the Basic Usage popup. What happens at each moment:

- **Subscribe.** The handler is subscribed on the bus, owned by `shown`.
- **The owner's generation ends.** The package calls `TryUnsubscribe`. The popup stays quiet while
  it is closed, and a reopened popup subscribes once.
- **The same handler is subscribed again for the same owner.** It is ignored, with
  [JANITOR105](Troubleshooting.md#janitor105). The same handler for a *different* owner throws
  `InvalidOperationException`, because SignalBus cannot hold two subscriptions for one handler.

This needs the Zenject integration to be active. The signal has to be declared as usual, the signal
type argument has to be written out, and the package adds no SignalBus binding of its own. See
[Zenject](Zenject.md).

---

## 8. `async void` with a delay

### Before

<!-- illustrative: before -->
```csharp
public sealed class SfxPlayer : MonoBehaviour
{
    private readonly List<AudioSource> _busy = new();

    // async void and a token-less Delay: after a scene change it touches a destroyed AudioSource.
    public async void PlayAndRelease(AudioSource source, float duration)
    {
        _busy.Add(source);
        source.Play();
        await UniTask.Delay((int)(duration * 1000));
        source.clip = null;
        _busy.Remove(source);
    }
}
```

### The lines that fail

- `await UniTask.Delay((int)(duration * 1000));` has no token, so nothing can stop it. If the scene
  changes during the delay, the method resumes anyway and `source.clip = null` throws on the
  destroyed `AudioSource` (a `MissingReferenceException` in the editor).
- `source.clip = null;` also runs for an earlier call while a later call is playing on the same
  source, and cuts the newer sound.
- `public async void` means no caller can observe that exception or the completion of the method.

### After

<!-- source: Samples~/BasicUsage/SfxPlayer.cs -->
```csharp
/// <summary>Plays clips on its own source and clears the clip after it ends; the timer ends with this component.</summary>
public sealed class SfxPlayer : MonoBehaviour
{
    private AudioSource _source;
    private Lifetime _release;

    // The source arrives here because the demo builds its objects in code.
    public void Initialize(AudioSource source) => _source = source;

    private void Awake() => _release = this.GetLifetime().CreateChild("Release");

    public void PlayAndRelease(AudioClip clip)
    {
        _release.Cancel();                 // the earlier release would clear this clip, so it is dropped
        _source.clip = clip;
        _source.Play();

        // Audio runs in real time, so the release ignores the time scale.
        _release.After(clip.length, _source, static source => source.clip = null, ignoreTimeScale: true);
    }
}
```

The class now says what it owns: one `AudioSource`, given once, and one area for the pending
release. What happens at each moment:

- **`Awake`.** `_release` is an area under the component's lifetime, for the one timer that can be
  replaced.
- **`PlayAndRelease`.** `_release.Cancel()` drops the release of an earlier sound, which would
  otherwise clear the clip that is about to play. `_release.After(...)` registers the new timer and
  returns at once. The source is passed as state to a `static` callback, so the call allocates no
  closure, and `ignoreTimeScale: true` keeps the timer in real time, like the audio.
- **The clip ends.** The callback runs once, on the main thread, and clears the clip.
- **The `SfxPlayer` is destroyed, or its scene is disposed, while a sound plays.** The timer is
  cancelled and the callback never runs.
- **The callback throws.** The exception is routed to `LifetimeErrors.Handler` with the source
  `Timer` and the line that registered it.

The timer belongs to the `SfxPlayer`, and so does the source it clears: both sit on the same object
and end together. A timer whose callback touches an object that can go away earlier belongs on that
object's lifetime.

Longer async work goes through `Run`, which hands the method a token to pass to every await. See
[Tasks and errors](Tasks-and-Errors.md).

---

## 9. A string-keyed global registry

### Before

<!-- illustrative: before -->
```csharp
public sealed class WorkRegistry
{
    public static readonly WorkRegistry Instance = new WorkRegistry();

    private readonly Dictionary<string, List<Tween>> _tweens = new();

    public void Register(string key, Tween tween)
    {
        if (!_tweens.TryGetValue(key, out var list))
        {
            list = new List<Tween>();
            _tweens.Add(key, list);
        }

        list.Add(tween);
    }

    public void CancelAll(string key)
    {
        if (!_tweens.TryGetValue(key, out var list)) return;
        foreach (var tween in list) tween.Kill();
        list.Clear();
    }
}

public sealed class DamageNumbers
{
    public void Pop(Transform label) =>
        WorkRegistry.Instance.Register("combat", label.DOPunchScale(Vector3.one * 0.3f, 0.2f));
}

public sealed class CombatFlow
{
    public void OnCombatInterrupted() => WorkRegistry.Instance.CancelAll("Combat");
}
```

### The lines that fail

- `WorkRegistry.Instance.CancelAll("Combat");` cancels nothing. The tweens were registered under
  `"combat"`, the lookup misses, and nothing reports it. A key is a reference that the compiler
  cannot check.
- `list.Add(tween);` keeps every tween until someone cancels the key. Tweens that finished stay in
  the list for the whole session, and with DOTween recycling on `tween.Kill()` later hits whatever
  those objects were reused for.
- `public static readonly WorkRegistry Instance` has no owner. Nothing cancels a key when the scene
  that registered into it goes away, any class can cancel any key, and with domain reload disabled
  the contents survive from one Play Mode session into the next.

### After

A category is an area with an owner, reached through a typed property:

<!-- source: Samples~/DOTweenUsage/GameplayLifetimes.cs -->
```csharp
/// <summary>Named areas that objects join, or that other classes register work into.</summary>
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

One class registers into the category:

<!-- source: Samples~/DOTweenUsage/DamageNumbers.cs -->
```csharp
/// <summary>Registers its tweens into the Combat category.</summary>
public sealed class DamageNumbers
{
    private readonly GameplayLifetimes _lifetimes;

    public DamageNumbers(GameplayLifetimes lifetimes) => _lifetimes = lifetimes;

    public void Pop(Transform label) => label.DOPunchScale(Vector3.one * 0.3f, 0.2f).AddTo(_lifetimes.Combat);
}
```

Another class stops it, without knowing who registered what:

<!-- source: Samples~/DOTweenUsage/CombatFlow.cs -->
```csharp
/// <summary>Stops the Combat category without knowing who registered what into it.</summary>
public sealed class CombatFlow
{
    private readonly GameplayLifetimes _lifetimes;

    public CombatFlow(GameplayLifetimes lifetimes) => _lifetimes = lifetimes;

    public void OnCombatInterrupted() => _lifetimes.Combat.Cancel();
}
```

What happens at each moment:

- **Construction.** `GameplayLifetimes` receives a lifetime and creates its categories under it. In
  the samples that lifetime belongs to the scene (an area of a scene object, or the lifetime Zenject
  injects in a SceneContext), so both categories end when the scene is disposed.
- **`Pop`.** The tween is registered in `Combat`. A tween that finishes by itself is dropped from
  the lifetime's list by an amortized sweep, so the list does not grow with the number of pops.
- **`OnCombatInterrupted`.** `Combat.Cancel()` kills every tween still running in the category and
  cancels everything else registered there. The category stays usable.
- **A typo.** `_lifetimes.Combta` does not compile.

The package has no registry of lifetimes keyed by string or GUID. The `"Popups"` and `"Combat"`
strings above are labels for the Janitor window; nothing looks them up, and two areas may have the
same label. Objects join a category with `GetLifetime(category)` or `GetActiveLifetime(category)`,
as the popup on the [index page](index.md#one-class-from-start-to-end) does. See
[Organizing work](Organizing-Work.md).

---

## Adopting the package one class at a time

1. **Install the package and open the window.** Import the Basic Usage sample, run it once, and keep
   `Window > Analysis > Janitor` open while you convert. It shows every lifetime and what is
   registered in it.
2. **Add the scene call where scenes are loaded.** Put `SceneLifetimes.DisposeAll()` right before
   every Single-mode load (and `SceneLifetimes.Dispose(scene)` right before unloading one scene). It
   is one line in one place, it does nothing for classes that are not converted yet, and it gives
   every converted class its cleanup before Unity starts destroying objects. Disposal is final, so
   check what can be checked before the call, and hold the scene activation for a load that can
   fail. See [Components and scenes](Components-and-Scenes.md).
3. **Pick one class, starting with the one that has the most removal lines.** A popup or a pooled
   object usually pays back first.
4. **Choose the owner for each piece of work in it.**
   - Work started in `OnEnable`: `this.GetActiveLifetime()`, so that it stops on deactivation.
     Registered on `this` it would be added again by every activation; the window reports that as
     [JANITOR116](Troubleshooting.md#janitor116).
   - Work that should last as long as the component: `this` (for example `this.Run(...)`,
     `Subscribe(handler, this)`, `tween.AddTo(this)`).
   - An operation that restarts or is cancelled on its own: a child area in a field,
     `this.GetLifetime().CreateChild()` in `Awake`, then `Cancel()` and `Run`.
   - A plain C# service: a `Lifetime` constructor parameter.
5. **Convert every registration in the class with the recipes above, then delete what is left
   over:** the removal lines, the handle and token-source fields, and `OnDisable` or `OnDestroy` if
   they are now empty. A class that is half converted works, but it keeps the lines that can be
   forgotten.
6. **Pass the token on.** Inside `Run` work, give `ct` to every await that accepts one. Work that
   ignores its token keeps running after a cancel; the window reports it as
   [JANITOR101](Troubleshooting.md#janitor101).
7. **Convert the events the class declares last.** Changing `event Action<T>` to `OwnedEvent<T>`
   breaks every `+=` on it at compile time, which gives you the list of subscribers to convert.
   Until you are ready for that, subscribers can bind to the old event with the paired form from
   recipe 5.
8. **Run the game and read the window.** The object's row should gain its entries when it is enabled
   and drop to zero when it is disabled or cancelled, and the Warnings tab should stay empty. The
   warnings a conversion typically produces are [JANITOR105](Troubleshooting.md#janitor105) (a
   subscription owned by the component but made in `OnEnable`),
   [JANITOR116](Troubleshooting.md#janitor116) (a task, timer, coroutine or tween registered on the
   component in `OnEnable`), [JANITOR108](Troubleshooting.md#janitor108) (work registered on an
   active lifetime while the object is inactive) and [JANITOR113](Troubleshooting.md#janitor113) (a
   category passed after the lifetime already existed).
9. **Remove the old helper when its last caller is gone:** the cleanup base class, the disposable
   bag, the keyed registry.

Things that are different from the code you are replacing:

- `Cancel()` stops everything registered in a lifetime, subscriptions included. Keep long-lived
  listening on the object's own lifetime and put activity that starts and stops in a child area;
  cancel the area.
- `Dispose()` is for areas you created. On `GetLifetime()`, `GetActiveLifetime()`, a scene lifetime
  and `Lifetime.App` it is ignored ([JANITOR107](Troubleshooting.md#janitor107)); use `Cancel()`.
- `lifetime.Token` returns a different token after each `Cancel()`. Read it when you start the work;
  do not store it in a field.
- Register the root `Sequence` only, never the tweens nested in it.
- Registration is main thread only.

## See also

- [Concepts](Concepts.md): generations, `Cancel` versus `Dispose`, ordering
- [Stopping work](Stopping-Work.md): every granularity of stop
- [Events](Events.md): `OwnedEvent`, paired `Subscribe`, `UnityEvent`, `IDisposable`
- [Limitations](Limitations.md): what stays invisible to the package
- [FAQ](FAQ.md)
