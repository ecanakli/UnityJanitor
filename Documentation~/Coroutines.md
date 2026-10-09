# Coroutines

[Back to index](index.md)

By itself, Unity stops a coroutine only when its host is destroyed or its GameObject is
deactivated. `lifetime.StartCoroutine(host, routine)` adds every other reason to stop: an area or a
category is cancelled, one handle is cancelled, a scene is disposed before its objects are
destroyed. This page covers the call, the rules for the host, what Unity stops and what the lifetime
stops, and how the package keeps its bookkeeping bounded when Unity stops a coroutine on its own.

---

## Binding a coroutine to a lifetime

<!-- signature -->
```csharp
public static LifetimeRegistration StartCoroutine(this Lifetime lifetime, MonoBehaviour host, IEnumerator routine);
```

The **host** is the `MonoBehaviour` that runs the coroutine, exactly as in Unity. The **lifetime**
is what decides when it stops. They are separate arguments because they answer separate questions.

A coroutine managed by hand needs a field for its handle and a stop line that mirrors the start:

<!-- illustrative: before -->
```csharp
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

The coroutine lines are `_badgeBlink = StartCoroutine(BlinkBadge());` and
`if (_badgeBlink != null) StopCoroutine(_badgeBlink);`. Three things are fragile about them:

- The handle can be `null`. `StartCoroutine` returns `null` for a routine that ends on its first
  step, and for a start on an inactive GameObject, which also logs an error.
  Every stop therefore needs the null check.
- Each coroutine needs its own field, and each start needs its own stop line somewhere else.
- There is no way to say "stop this when the popups are closed as a group" without more fields and
  more methods.

The same popup in the Basic Usage sample starts the badge on its active lifetime:

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

<!-- source: Samples~/BasicUsage/ShopPopup.cs -->
```csharp
private IEnumerator BlinkBadge()
{
    while (true)
    {
        _saleBadge.enabled = !_saleBadge.enabled;
        yield return new WaitForSeconds(0.5f);
    }
}
```

`this` is the host and `shown` is the owner. What happens at each moment:

- **The popup is activated.** The first step of `BlinkBadge` runs inside the `StartCoroutine` call,
  as in Unity: the badge toggles once and the routine waits for half a second.
- **The popup is deactivated.** Unity stops every coroutine hosted on the popup. In the same moment
  the active lifetime is cancelled, and the package removes the coroutine's entry.
- **The popup is activated again.** `OnEnable` starts a new coroutine in the new generation. The old
  one is not resumed; Unity never restarts a stopped coroutine.
- **`Popups.Cancel()` is called while the popup is visible.** This is the case Unity alone does not
  cover. The popup's active lifetime is a child of `Popups`, so the badge stops blinking although
  its host is still active.
- **The popup is destroyed, or its scene is disposed.** The coroutine is stopped with everything
  else on the lifetime.

### One coroutine and its handle

`HintPulse` in the same sample starts a coroutine on its component lifetime and keeps the handle, so
that it can stop that coroutine alone:

<!-- source: Samples~/BasicUsage/HintPulse.cs -->
```csharp
/// <summary>A pulsing hint whose coroutine is stopped alone, through the registration handle.</summary>
public sealed class HintPulse : MonoBehaviour
{
    private CanvasGroup _hint;
    private LifetimeRegistration _pulse;

    // The view arrives here because the demo builds its UI in code.
    public void Initialize(CanvasGroup hint) => _hint = hint;

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

    private IEnumerator Pulse()
    {
        while (true)
        {
            _hint.alpha = Mathf.PingPong(Time.time, 1f);
            yield return null;
        }
    }
}
```

- **`StartPulse()` is called.** `_pulse.Cancel()` stops a pulse that is already running; on the
  first call `_pulse` is a default handle and nothing happens. Then
  `this.GetLifetime().StartCoroutine(this, Pulse())` starts the routine: the component lifetime is
  the owner and the component is the host.
- **`StopPulse()` is called.** The handle stops this one coroutine. The next line resets the alpha
  by hand, because a stopped coroutine gets no chance to tidy up after itself (see
  [below](#finally-blocks-do-not-run-on-a-stop)).
- **The GameObject is deactivated.** Unity stops the coroutine. The component lifetime is not
  cancelled by deactivation, so the coroutine's entry stays behind for now; the next
  `_pulse.Cancel()` removes it, or it is reclaimed as described in
  [How stopped coroutines are reclaimed](#how-stopped-coroutines-are-reclaimed). The pulse does not
  resume when the object is activated again.
- **The object is destroyed.** The component lifetime is disposed and the coroutine with it.

### A coroutine in an area

`TutorialController` in the DOTween Usage sample binds a coroutine to an area that is cancelled far
more often than its host is deactivated:

<!-- source: Samples~/DOTweenUsage/TutorialController.cs -->
```csharp
public void ShowStep(TutorialStep step)
{
    _step.Cancel();                                            // the previous step's work stops
    _arrow.DOLocalMove(step.ArrowPosition, 0.6f).SetLoops(-1, LoopType.Yoyo).AddTo(_step);
    _hintPulse = _step.StartCoroutine(this, PulseHint());
    step.Target.onClick.Subscribe(CompleteStep, _step);
}
```

- Each `ShowStep` cancels `_step` first, which stops the pulse of the previous step, and then starts
  a new pulse. `_step.Cancel()` anywhere else in the class stops the pulse with the rest of the
  step.
- The controller stays active through all of it. Without the lifetime, the pulse would run until the
  controller itself is deactivated or destroyed.
- After `_step.Cancel()` the stored `_hintPulse` is a handle of an ended generation. Its `Cancel()`
  does nothing, so a late call cannot stop the pulse of the next step.

---

## Host rules

- **The host is always explicit.** There is no global runner. A hidden DontDestroyOnLoad object that
  runs everybody's coroutines would make every coroutine survive its real owner, which is the
  failure this package removes.
- **The host must be active in the hierarchy when the coroutine starts.** If the host is `null`,
  destroyed or inactive, the coroutine is not started, a default registration is returned, and the
  editor and development builds log [JANITOR110](Troubleshooting.md#janitor110). The package checks
  this before it calls Unity, so Unity's own "inactive" error does not appear.
- **The lifetime must be active.** On a lifetime that is being cancelled or is disposed the
  coroutine is not started, and nothing is logged. A `null` lifetime or a `null` routine throws
  `ArgumentNullException`.
- **`enabled = false` on the host changes nothing.** A disabled `MonoBehaviour` on an active
  GameObject can start a coroutine, and disabling it does not stop one. This is Unity's rule and the
  package keeps it.
- **The receiver is a lifetime.** `StartCoroutine(BlinkBadge())` inside a `MonoBehaviour` is Unity's
  own method and binds nothing. The owned form always starts from a lifetime:
  `this.GetLifetime().StartCoroutine(this, routine)` as in `HintPulse`,
  `shown.StartCoroutine(this, routine)`, `_step.StartCoroutine(this, routine)`.
- **Host and owner may be different objects.** Usually the host is `this` and the owner is one of
  `this`'s lifetimes or an area.
- The call must be made on the main thread.

The routine itself is an ordinary coroutine. What it yields reaches Unity unchanged: `null`,
`WaitForSeconds`, `WaitUntil`, a nested `IEnumerator`. A routine that finishes without ever yielding
runs to its end inside the call, leaves nothing registered and returns a default registration.

**To wait for an inner routine, yield the routine itself**: `yield return Inner();`. The inner
routine then runs as part of the outer coroutine and stops with it. The two other spellings do
something else:

- `yield return lifetime.StartCoroutine(this, Inner());` does not wait. The call returns a
  `LifetimeRegistration`, which is not a yield instruction Unity knows, so the outer routine
  continues on the next frame while the inner one runs beside it.
- `yield return StartCoroutine(Inner());`, Unity's own method, waits, but the inner coroutine is
  not bound to the lifetime. Stopping the lifetime ends the outer routine only; the inner one keeps
  running until Unity stops it with its host.

A coroutine that should stop only when its host is deactivated or destroyed needs none of this. Use
Unity's `StartCoroutine` for it.

---

## What Unity stops and what the lifetime stops

| What happens | Who stops the coroutine | The entry in the lifetime |
|---|---|---|
| The host's GameObject is deactivated | Unity. It does not resume on reactivation | Removed at once when the coroutine is bound to that object's active lifetime; otherwise reclaimed later (see below) |
| The host is destroyed | Unity | Removed at once when the coroutine is bound to a lifetime of the host; otherwise reclaimed later |
| `enabled = false` on the host | Nobody. It keeps running | Stays |
| The lifetime is cancelled or disposed | The package, with `StopCoroutine` on the host | Removed |
| `registration.Cancel()` | The package. That coroutine only | Removed |
| The routine reaches its end | Nobody; it is finished | Removed by the coroutine itself |
| The routine throws | The package. The routine does not continue | Removed |
| `StopCoroutine` or `StopAllCoroutines()` is called on the host by hand | Unity | Stays until the generation ends |

### Exceptions

An exception thrown inside a lifetime-bound routine is caught by the package and routed to
`LifetimeErrors.Handler` with the source `Coroutine`, the lifetime's owner object and the member and
line of the `StartCoroutine` call. The routine stops. Other coroutines on the same lifetime keep
running. An `OperationCanceledException` thrown inside a routine ends the routine and is not routed.
See [Tasks and errors](Tasks-and-Errors.md#what-happens-to-exceptions).

### A routine that cancels its own lifetime

A routine may call `Cancel()` on the lifetime it is bound to, or `Cancel()` on its own handle. The
rest of the current step runs to its next `yield`, and the routine is not resumed after that.

### `finally` blocks do not run on a stop

Unity does not dispose the enumerator of a stopped coroutine. No `finally` block of the routine runs
when a coroutine is stopped by `StopCoroutine`, by the deactivation of its host or by the
destruction of its host. A lifetime-bound coroutine behaves the same way:
a cancel stops it and does not run its `finally` blocks.

Cleanup that must happen when a coroutine is stopped therefore does not belong in a `finally` inside
the routine. Do it next to the stop, as `HintPulse.StopPulse()` resets the alpha right after it
cancels the handle, or register it on the same lifetime with `OnCancel`; that action runs when the
lifetime's generation ends, in the same cancel that stops the coroutine.

---

## How stopped coroutines are reclaimed

Every lifetime-bound coroutine has an entry in its lifetime. In most cases the entry goes away at
the right moment by itself:

- the routine ends or throws: the coroutine removes its own entry;
- the lifetime is cancelled or disposed, or the handle is cancelled: the entry is removed with the
  stop.

One case is left. Unity can stop a coroutine without the package being told: the host is
deactivated or destroyed while the coroutine is bound to a lifetime that lives on, such as an area
or the host's own component lifetime. Because Unity does not dispose a stopped enumerator, the
package's wrapper around the routine receives no signal at all. Without a countermeasure, a
long-lived lifetime would collect one dead entry for every such coroutine.

The countermeasure has two parts.

**A deactivation counter.** The first lifetime-bound coroutine started on a GameObject adds a hidden
`ActiveLifetimeTrigger` component to it. Here the trigger does not create an active lifetime; it
only counts how often the GameObject was deactivated. Only a real deactivation is counted: the
destruction of the object and a disable of the trigger component alone are not. A coroutine
remembers the count at its start. Its entry is considered dead when any of these holds:

- the routine has finished;
- the host is destroyed;
- the host is inactive in the hierarchy;
- the host's GameObject has been deactivated since the coroutine started, even if it is active again
  now. Unity stopped the coroutine at that deactivation and never restarts it.

**An occasional sweep.** When a new registration brings a lifetime's entry count to a threshold, the
lifetime checks its coroutine entries and drops the dead ones. The threshold is 16 entries at first
and afterwards twice the number of entries that survived the last sweep. Dead entries therefore
never exceed the live ones by more than a constant: a lifetime holds at most about twice its live
entries plus 16. The package's tests cycle a pooled host 240 times on one long-lived lifetime and
check that bound.

What this means for you:

- A host that is switched off and on by a pool can start lifetime-bound coroutines on a long-lived
  lifetime without the lifetime growing. See [Pooling](Pooling.md).
- An entry that was dropped by the sweep belonged to a coroutine Unity had already stopped. Nothing
  is stopped by the sweep itself.
- **A coroutine that Unity stopped at a deactivation is not live any more.** Its entry is a
  leftover that runs nothing. Starting the same routine again in the next `OnEnable`, on the
  component lifetime or on an area that outlives the deactivation, leaves one running coroutine,
  not two. It is not a repeat registration, and
  [JANITOR116](Troubleshooting.md#janitor116) is not raised for it. A task, a timer or a tween
  registered the same way is different: deactivation does not stop those, so the next `OnEnable`
  adds a second one.
- A trigger that was added only for its deactivation counter does not claim the object's active
  lifetime. A later `GetActiveLifetime(category)` on the same object still decides the parent, with
  no [JANITOR113](Troubleshooting.md#janitor113).

Two limits remain:

- `StopCoroutine` or `StopAllCoroutines()` called by hand on the host stops the coroutine without a
  deactivation. That entry stays until its lifetime's generation ends. Stop a lifetime-bound
  coroutine through its registration or its lifetime, not through Unity's stop methods.
- The trigger is hidden in the Inspector, but code that disables every `Behaviour` on an object
  still reaches it. Such a disable is not counted, so the entries of coroutines that are still
  running are kept and a cancel still stops them. If the object has an active lifetime, the disable
  cancels that lifetime's work once (see
  [Components and scenes](Components-and-Scenes.md#what-setactivefalse-stops)). The trigger enables
  itself again the next time the package uses it, which includes the start of the next
  lifetime-bound coroutine on that object. Until then it does not see a deactivation: a coroutine
  that Unity stops in that window keeps its entry until its lifetime's generation ends, unless a
  sweep finds the host inactive. Do not remove the component.

### Cost

Each `lifetime.StartCoroutine` call allocates one small wrapper object, which is not pooled, in
addition to what Unity and the C# iterator allocate for any coroutine. The hidden trigger is added
once per host GameObject. The package does no per-frame work of its own; the wrapper only forwards
each step to the routine.

---

## See also

- [Stopping work](Stopping-Work.md#stop-one-item): cancelling one coroutine through its handle
- [Components and scenes](Components-and-Scenes.md): the active lifetime and what deactivation stops
- [Pooling](Pooling.md): coroutines on objects that are recycled with `SetActive`
- [Tasks and errors](Tasks-and-Errors.md): `Run`, the async counterpart, and the error handler
- [Concepts](Concepts.md): generations and stale handles
- [Troubleshooting](Troubleshooting.md#janitor110): JANITOR110, a coroutine that was not started
