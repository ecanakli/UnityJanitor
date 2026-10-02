# Tasks and errors

[Back to index](index.md)

`Run`, `After` and `Every` start asynchronous work and timers that end with a lifetime. They replace
`async void`, `.Forget()` and hand-written `CancellationTokenSource` fields. This page covers the
three calls with all their overloads, where exceptions go, how cancellation looks from inside an
async method, and what a cancel can and cannot stop.

All three calls return nothing. There is no handle to keep, and no single call that cancels a task
and runs it again. To stop or restart one piece of work, give it an area of its own; see
[Stopping work](Stopping-Work.md#stop-one-task).

---

## From `async void` to an owned timer

A delayed callback written the usual way:

<!-- illustrative: before -->
```csharp
public sealed class SfxPlayer : MonoBehaviour
{
    private AudioSource _source;

    public void Initialize(AudioSource source) => _source = source;

    public async void PlayAndRelease(AudioClip clip)
    {
        _source.clip = clip;
        _source.Play();
        await UniTask.Delay(TimeSpan.FromSeconds(clip.length), ignoreTimeScale: true);
        _source.clip = null;
    }
}
```

The line that fails is the `await UniTask.Delay(...)`. The delay has no cancellation token and
nothing owns the method, so it resumes whatever happened in the meantime:

- After a scene change the player and its `AudioSource` are destroyed, and `_source.clip = null`
  throws on the destroyed source. Because the method is `async void`, that exception surfaces as an
  unhandled exception with no hint of which object started the work.
- When a second clip is played while the first delay is still waiting, the first delay ends in the
  middle of the second clip and clears it. The second sound is cut.

The same class from the Basic Usage sample:

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

What happens at each moment:

- **`Awake` runs.** The class creates one area, `Release`, under its component lifetime. The pending
  release of the clip is the only thing that will ever be registered in it.
- **`PlayAndRelease` is called.** `_release.Cancel()` drops a release that is still pending from an
  earlier clip; on the first call it does nothing. The clip starts, and `_release.After(...)`
  registers a one-shot timer in the area. The source is passed as the timer's state, so the
  callback is a `static` lambda and nothing is captured. Audio runs in real time, so the timer
  ignores the time scale.
- **The clip's length later**, the callback runs once, on the main thread, and clears the clip. The
  timer removes its own entry.
- **`PlayAndRelease` is called again while a clip plays.** The cancel removes the earlier timer, so
  the new clip is not cleared by the old release.
- **The `SfxPlayer` is destroyed first, or its scene is disposed first.** The component lifetime is
  disposed, the area with it, and the timer with the area. The callback never runs, so nothing
  touches the source.
- **The GameObject is deactivated.** Nothing changes: the area hangs under the component lifetime,
  which deactivation does not cancel. Work that should stop on `SetActive(false)` is registered on
  the active lifetime instead (see [Components and scenes](Components-and-Scenes.md)).
- **The callback throws.** The exception is routed to the error handler together with the name of
  the area and the member and line of the `After` call
  (see [What happens to exceptions](#what-happens-to-exceptions)).

One limit to state plainly: a cancelled timer does not run its callback, so whatever the callback
would have tidied up stays as it was. Here that is what the restart wants: the old release must not
touch the new clip. Cleanup that has to happen on every end of the work belongs in an `OnCancel`
action; `CombatEffect` under [After](#after) shows that shape.

---

## Run

<!-- signature -->
```csharp
public static void Run(this Lifetime lifetime, Func<CancellationToken, UniTask> work);
public static void Run<TState>(this Lifetime lifetime, TState state, Func<TState, CancellationToken, UniTask> work);

public static void Run(this MonoBehaviour behaviour, Func<CancellationToken, UniTask> work);
public static void Run<TState>(this MonoBehaviour behaviour, TState state, Func<TState, CancellationToken, UniTask> work);
```

`Run` starts an async method and hands it the token of the lifetime's current generation. The shop
popup of the Basic Usage sample loads its offers this way:

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

private async UniTask LoadOffersAsync(CancellationToken ct)
{
    var offers = await _offers.FetchAsync(ct);
    _offers.Show(offers);
}
```

What `shown.Run(LoadOffersAsync)` does, in order:

1. If the lifetime is not active (it is in the middle of a `Cancel()` or `Dispose()`, or it is
   disposed), the work is **not called at all**. Nothing is thrown and nothing is logged.
2. It registers an entry for the task in the current generation and calls
   `LoadOffersAsync(token)` synchronously, on the calling thread. The method runs up to its first
   `await` that is not already complete, here the fetch. Everything before that `await` has run by
   the time `Run` returns.
3. `Run` returns nothing. There is no task to store, await or forget.
4. When the method finishes, the task removes its own entry. If it finishes with an exception, the
   exception is routed to the error handler. If it finishes with an `OperationCanceledException`,
   nothing is routed and nothing is logged.

If the popup is closed during the fetch, the generation ends, the token is cancelled, the fetch
throws `OperationCanceledException` at its `await`, and `_offers.Show(offers)` never runs.

More rules:

- `Run` is not `UniTask.Run`. It does not move the work to the thread pool; the work starts on the
  thread that called `Run`, which must be the main thread (`InvalidOperationException` otherwise).
  The work itself may switch threads, as `BackgroundWork` does
  [further down](#what-a-token-less-await-does). When it finishes on another thread, the bookkeeping
  and the error handler still run on the main thread.
- A `null` lifetime or `null` work throws `ArgumentNullException`, and nothing is started.
- On an active lifetime whose GameObject is inactive, and on an area created under one, the work is
  not started either, and [JANITOR108](Troubleshooting.md#janitor108) is logged in the editor and in
  development builds.
- If the work throws before its first `await`, that exception is routed like any other; it is not
  thrown to the caller of `Run`.
- The `MonoBehaviour` forms use the component lifetime: `this.Run(work)` is
  `this.GetLifetime().Run(work)`. The work ends when the component is destroyed, and on any
  `Cancel()` of that lifetime or of a category it was placed in. It does not end on deactivation,
  so `this.Run`, `this.After` and `this.Every` in `OnEnable` start the work again on every
  activation while the earlier work is still live. In the editor the Janitor window reports the
  second activation as [JANITOR116](Troubleshooting.md#janitor116). Work started in `OnEnable`
  belongs on the active lifetime.
- `Run` has no per-task handle. To stop one task and nothing else, give it its own area (see
  [Stopping work](Stopping-Work.md#stop-one-task)).

`ScoreReveal` in the sample is the `MonoBehaviour` form in its plainest use: a task that is started
once per object, with a parameter and no area:

<!-- source: Samples~/BasicUsage/ScoreReveal.cs -->
```csharp
/// <summary>Counts a score up once; nothing restarts it, and it ends with the object (or with a cancel of its scene).</summary>
public sealed class ScoreReveal : MonoBehaviour
{
    private TMP_Text _label;

    // The view arrives here because the demo builds its UI in code.
    public void Initialize(TMP_Text label) => _label = label;

    // Call it once per object: a second call would start a second task. Countdown shows the restart form.
    public void Reveal(int score) => this.Run(ct => RevealAsync(score, ct));

    private async UniTask RevealAsync(int score, CancellationToken ct)
    {
        for (var shown = 0; shown < score; shown += Mathf.Max(1, score / 20))
        {
            _label.text = shown.ToString();
            await UniTask.Delay(TimeSpan.FromMilliseconds(50), cancellationToken: ct);
        }

        _label.text = score.ToString();
    }
}
```

- **`Reveal(score)` is called.** `this.Run` starts `RevealAsync` on the component lifetime, and the
  lambda carries `score` into it.
- **The object is destroyed while the score counts up.** The component lifetime is disposed, the
  delay ends with a cancellation, and the label is not written again. The class has no area and no
  stop method: apart from the destruction, only a `Cancel()` from above (its scene, `App`) ends the
  task.
- **`Reveal` is called a second time.** A second task starts beside the first, and both write the
  label. A task that can be restarted needs the form `Countdown` uses (see
  [Stopping work](Stopping-Work.md#cancel-the-previous-run-then-start-a-new-one)).

### The state overload

`Run(LoadOffersAsync)` converts a method group to a delegate, which allocates one delegate per
call. A lambda that captures a local or a parameter, as `ct => RevealAsync(score, ct)` above does,
allocates a closure as well. The `TState` overload passes the value as an argument instead, so the
lambda can be `static`:

<!-- source: Tests/Editor/LifetimeRunTests.cs -->
```csharp
_area.Run(counter, static (c, ct) =>
{
    c.Value++;
    return UniTask.CompletedTask;
});
```

With a `static` lambda, `Run` itself allocates nothing once the package's internal pools are warm.
This is measured in the editor on Mono; it has not been measured on IL2CPP. The async method that
is started has its own cost, as any async method has. `TState` has no constraint, and a struct
state is not boxed. For work that starts a few times per scene the plain forms are fine; the state
forms are for hot paths. See [Performance](Performance.md).

---

## After

<!-- signature -->
```csharp
public static void After(this Lifetime lifetime, float seconds, Action action, bool ignoreTimeScale = false);
public static void After<TState>(this Lifetime lifetime, float seconds, TState state, Action<TState> action, bool ignoreTimeScale = false);

public static void After(this MonoBehaviour behaviour, float seconds, Action action, bool ignoreTimeScale = false);
public static void After<TState>(this MonoBehaviour behaviour, float seconds, TState state, Action<TState> action, bool ignoreTimeScale = false);
```

`After` runs a callback once, after a delay, unless the lifetime's generation ends first. The pooled
coin of the sample ends its own flight this way: the timer deactivates the coin's GameObject, which
is passed as the state of a `static` lambda:

<!-- source: Samples~/BasicUsage/CoinPickup.cs -->
```csharp
private void OnEnable()
{
    var spawn = this.GetActiveLifetime();                                       // ends on SetActive(false) and on any outside Cancel
    spawn.OnCancel(this, static coin => coin._pool.Release(coin));              // the one place that returns the coin
    spawn.Run(FlyAsync);
    spawn.After(_flyDuration, gameObject, static go => go.SetActive(false));    // the timer only ends the flight
}
```

- With `seconds` above zero, the timer is registered and the call returns. The delay is a
  `UniTask.Delay` on the generation token, in the Update phase of the player loop. The callback runs
  on the main thread.
- The delay is measured in scaled time. While `Time.timeScale` is zero it does not advance. Pass
  `ignoreTimeScale: true` to measure unscaled time, for timers that must run while the game is
  paused.
- With `seconds` at or below zero, the callback runs immediately, inside the `After` call, and only
  if the lifetime accepts work, by the same rule as for a delay above zero: the lifetime is active,
  and for an active lifetime or an area under one, its GameObject is active.
- If the generation ends before the delay has elapsed, the callback **never** runs. This holds even
  when the cancel and the end of the delay fall into the same frame.
- `NaN`, an infinite value or a value above 900000000000 for `seconds` throws
  `ArgumentOutOfRangeException`. A `null` lifetime or `null` action throws `ArgumentNullException`.
- An exception thrown by the callback is routed with the source `Timer`.
- The delay counts frame time while the game runs. It does not count time the application spent
  suspended, and it is not a tool for delays of hours or for offline progress: compare wall-clock
  time for those.

The state overload avoids the closure, exactly as for `Run`. `CombatEffect` in the sample removes
its own GameObject one second after it starts playing. `_life` is the effect's component lifetime:

<!-- source: Samples~/BasicUsage/CombatEffect.cs -->
```csharp
private void Awake()
{
    // The first access decides the parent, so join Combat before anything else uses this lifetime.
    _life = this.GetLifetime(_lifetimes.Combat);
    _life.OnCancel(gameObject, static go => Destroy(go));      // the one place that removes the effect
}

public void Play(int damage)
{
    _life.Run(ct => PlayAsync(damage, ct));
    _life.After(1f, _life, static life => life.Cancel());      // the normal end is a cancel too
}
```

- **`Awake` runs.** One cleanup action is registered on `_life`: it destroys the GameObject. It is
  the only place that removes the effect.
- **`Play` is called.** The task and the timer are registered on `_life`.
- **One second passes.** The timer's callback receives the lifetime as its state and cancels it. The
  task stops, and the cleanup action runs in that cancel and destroys the GameObject.
- **The lifetime is cancelled first** (the effect sits in the `Combat` category, and the category is
  cancelled). The timer never fires. The same cleanup action runs and destroys the GameObject.

This is the shape for work that must be tidied up however it ends: one `OnCancel` action does the
cleanup, and the normal end is a cancel too. The cleanup then runs exactly once on every path. A
timer callback may cancel its own lifetime; it is ordinary code, not a cancel callback.

---

## Every

<!-- signature -->
```csharp
public static void Every(this Lifetime lifetime, float seconds, Action action, bool ignoreTimeScale = false);
public static void Every<TState>(this Lifetime lifetime, float seconds, TState state, Action<TState> action, bool ignoreTimeScale = false);

public static void Every(this MonoBehaviour behaviour, float seconds, Action action, bool ignoreTimeScale = false);
public static void Every<TState>(this MonoBehaviour behaviour, float seconds, TState state, Action<TState> action, bool ignoreTimeScale = false);
```

`Every` runs a callback repeatedly until the generation ends. `SessionClock` in the Basic Usage
sample runs two of them on its component lifetime:

<!-- source: Samples~/BasicUsage/SessionClock.cs -->
```csharp
/// <summary>Two repeating timers on the component: one follows the time scale, one ignores it.</summary>
public sealed class SessionClock : MonoBehaviour
{
    private TMP_Text _label;
    private int _gameSeconds;
    private int _realSeconds;

    // The view arrives here because the demo builds its UI in code.
    public void Initialize(TMP_Text label) => _label = label;

    private void Start()
    {
        this.Every(1f, TickGame);                                   // stops while Time.timeScale is 0
        this.Every(1f, TickReal, ignoreTimeScale: true);            // keeps running while the game is paused
    }

    private void TickGame()
    {
        _gameSeconds++;
        Refresh();
    }

    private void TickReal()
    {
        _realSeconds++;
        Refresh();
    }

    private void Refresh() => _label.text = $"Game {_gameSeconds}s  Real {_realSeconds}s";
}
```

What happens at each moment:

- **`Start` runs.** Both timers are registered on the component lifetime. Neither callback runs yet:
  the first call comes after `seconds`, not immediately.
- **Each second.** `TickGame` and `TickReal` run, on the main thread.
- **The game is paused with `Time.timeScale = 0`.** The first timer stands still, because its delay
  is measured in scaled time. The second was started with `ignoreTimeScale: true` and keeps
  counting.
- **The clock's GameObject is deactivated.** Both timers keep running: `this.Every` uses the
  component lifetime.
- **The clock is destroyed, or its scene is disposed.** Both timers stop.

More rules:

- The interval restarts after each callback returns. The timer does not correct for drift, so it is
  a repeating delay, not a clock: use it for "about once a second", not for timekeeping.
- With `seconds` at or below zero the callback runs once per frame. `NaN`, an infinite value or a
  value above 900000000000 throws `ArgumentOutOfRangeException`, as for `After`.
- The timer stops when its generation ends, and nothing restarts it. The demo of the sample also
  registers a timer directly in the `Combat` category (`lifetimes.Combat.Every(1f, OnCombatTick)`).
  Its **Cancel everything** button stops that counter for good, because the demo registered the
  timer once at start and nothing registers it again.
- The timer also stops when its callback throws. The exception is routed once, with the source
  `Timer`, and other timers on the same lifetime keep running.
- A callback may cancel its own lifetime; the timer then does not fire again.

Like `Run`, the two timer calls return nothing. A timer that must be stopped on its own gets its own
area.

---

## What happens to exceptions

An exception from work that the package runs for you does not escape into the caller, into
Unity's player loop or into a teardown. It is caught where it happens and takes one of two paths
(the calls the package does not make itself are listed under
[What is not caught or routed](#what-is-not-caught-or-routed)):

- **`OperationCanceledException` is silent.** It is the normal way a cancelled `await` ends, so it
  is never logged and never routed. This includes its subclasses, such as `TaskCanceledException`.
- **Every other exception is routed** to `LifetimeErrors.Handler`, together with a context that says
  where it came from.

A failure that happens after the generation ended is still routed. A task that was cancelled and
then throws something other than a cancellation has a real bug, and hiding it would be wrong.

Silent does not mean free. `Cancel()` itself allocates nothing, but a task or a timer that was
still pending when its generation ended finishes through that exception: UniTask ends a cancelled
await by throwing, and the package catches it on the following frame. Each cancelled pending
`After` or `Every` costs one exception object and one throw and catch; a cancelled `Run` task
throws twice, once inside your async method and once where the package observes the task.
Cancelling hundreds of pending tasks and timers at once shows up as a spike in the next frame. See
[Performance](Performance.md).

<!-- signature -->
```csharp
public static class LifetimeErrors
{
    public static LifetimeErrorHandler Handler { get; set; }
}

public delegate void LifetimeErrorHandler(Exception exception, in LifetimeErrorContext context);

public readonly struct LifetimeErrorContext
{
    public LifetimeErrorSource Source { get; }
    public UnityEngine.Object Owner { get; }
    public string LifetimeName { get; }
    public string Member { get; }
    public int Line { get; }
}

public enum LifetimeErrorSource
{
    Task,
    Timer,
    CancelAction,
    Coroutine,
    TokenCallback,
    EventHandler,
}

public sealed class LifetimeWorkException : Exception
{
    public LifetimeErrorContext Context { get; }
}
```

### The context

| Field | Content |
|---|---|
| `Source` | The kind of work that failed; see the next table |
| `Owner` | The Unity object that owns the lifetime: the component for a component lifetime, the GameObject for a GameObject lifetime, the hidden trigger component for an active lifetime. It is `null` for an area, a scene lifetime and `App` |
| `LifetimeName` | The lifetime's name. For an area created without a name it is the call site of its `CreateChild`, in the form `Member:Line` |
| `Member`, `Line` | The call site of the registration: the method that called `Run`, `After`, `Subscribe`, `OnCancel` and so on, and the line of that call. `null` and `0` for a token callback |

`Member` and `Line` are filled in by the compiler at the call site. A helper method that wraps a
registration call therefore reports the helper's own name and line for every item it registers.

| `Source` | Raised when |
|---|---|
| `Task` | Work started with `Run` throws before its first `await`, or fails later |
| `Timer` | A callback of `After` or `Every` throws |
| `CancelAction` | Cleanup throws while a lifetime or a single item is being cancelled or disposed: an `OnCancel` action, the `Dispose()` of a disposable added with `AddTo`, the remove lambda of a paired `Subscribe`, the kill of a tween. An `isFinished` probe that throws is reported with this source too |
| `Coroutine` | A coroutine started with `lifetime.StartCoroutine` throws |
| `TokenCallback` | A callback registered on a lifetime's `Token` throws while the token is being cancelled |
| `EventHandler` | A handler of an `OwnedEvent` throws, the add lambda of a paired `Subscribe` throws, or an `OwnedEvent` refuses a nested `Invoke` beyond its depth limit ([JANITOR115](Troubleshooting.md#janitor115)) |

### What is not caught or routed

The package catches what it calls itself. The following never reach `LifetimeErrors.Handler`:

- **A handler subscribed to a `UnityEvent`.** The listener the package adds calls your handler
  while the subscription is live and catches nothing. An exception behaves exactly as in a
  listener added with `AddListener`.
- **A handler of a paired `Subscribe`.** The handler itself is what the add lambda receives, so the
  source event calls it, not the package, and an exception goes where that source sends it. (The
  add lambda and the remove lambda are called by the package, and their exceptions are routed; see
  the table above.)
- **A handler of a Zenject signal** subscribed with `signalBus.Subscribe<TSignal>(handler, owner)`.
  The handler is handed to the SignalBus unchanged, and the SignalBus calls it. For a signal that
  is not declared `RunAsync`, the exception leaves through the `Fire` call. When `Fire` is called
  from inside a `Run` task or an `After` or `Every` callback, the exception therefore does arrive
  at `LifetimeErrors.Handler`, as an error of that task or timer (source `Task` or `Timer`), and
  an `Every` timer whose callback fails this way stops, as after any exception in its callback.
- **Argument and usage errors.** A `null` lifetime, owner, handler or work throws
  `ArgumentNullException`, an invalid `seconds` throws `ArgumentOutOfRangeException`, and a
  registration from another thread throws `InvalidOperationException`. They are thrown to the
  caller, at the call, because they are programming errors at the call site.
- **Cancellations.** `OperationCanceledException` and its subclasses are dropped without a log,
  wherever they come from, a custom handler included: it is never called for them.

### The default handler

`LifetimeErrors.Handler` never returns `null`. While no handler is assigned, the getter returns the
default handler. That handler has no public name of its own: code that wants to keep it reads
`Handler` before it assigns, as `ErrorReporting` does [below](#your-own-handler).

The default handler logs the exception to the console, wrapped in a `LifetimeWorkException`, with
`context.Owner` as the log's context object. Clicking the console entry highlights the owner in the
Hierarchy. Unity prints the original exception first and the wrapper below it, for example:

```text
InvalidOperationException: No offers for this region
  ... stack trace of the original exception ...
Rethrow as LifetimeWorkException: Janitor Task failed in lifetime 'ShopPopup' (OnEnable:45): No offers for this region
```

### Your own handler

Assign `LifetimeErrors.Handler` to send errors somewhere else, for example to a crash reporter.
Assigning `null` restores the default. `ErrorReporting` in the Basic Usage sample forwards every
error to the game's own reporter and keeps the console log:

<!-- source: Samples~/BasicUsage/ErrorReporting.cs -->
```csharp
/// <summary>Forwards every error Janitor catches to the game's reporter and keeps the console logging.</summary>
public static class ErrorReporting
{
    private static readonly LifetimeErrorHandler Forwarder = Forward;
    private static IErrorReporter _reporter;
    private static LifetimeErrorHandler _console;

    // Call it in every Play Mode session, as Janitor resets the handler at its start; a repeat call only swaps the reporter.
    public static void Install(IErrorReporter reporter)
    {
        _reporter = reporter;
        if (LifetimeErrors.Handler == Forwarder)
        {
            return;
        }

        _console = LifetimeErrors.Handler;      // the default handler, which logs to the console
        LifetimeErrors.Handler = Forwarder;
    }

    private static void Forward(Exception exception, in LifetimeErrorContext context)
    {
        var owner = context.Owner != null ? context.Owner.name : "no owner";
        var location = $"{context.LifetimeName} ({owner}) {context.Member}:{context.Line}";
        _reporter.Report(context.Source.ToString(), location, exception);
        _console(exception, in context);
    }
}
```

The demo installs it once, at the top of its `Start`:

<!-- source: Samples~/BasicUsage/Demo/BasicUsageDemo.cs -->
```csharp
ErrorReporting.Install(new ConsoleErrorReporter());        // every routed error also reaches the game's reporter
```

What happens at each moment:

- **`Install` is called at startup.** `LifetimeErrors.Handler` still returns the default handler,
  which is kept in `_console`. Then the forwarder becomes the handler.
- **Work fails somewhere.** The demo's **Raise an error** button starts a `Run` task that throws
  after 0.3 seconds. The package catches the exception and calls `Forward` with it and with the
  context. `Forward` passes the source, the lifetime's name, the owner's name and the call site to
  the reporter, and then calls the default handler, so the console still shows the logged
  exception.
- **`Install` is called again in the same session**, for example after a scene reload. The handler
  is already the forwarder, and only the reporter is replaced.
- **A new Play Mode session starts.** The package has reset the handler to the default, so the next
  `Install` call installs the forwarder again.

Rules for a handler:

- The package resets the handler to the default at the start of every Play Mode session, in a
  `RuntimeInitializeLoadType.SubsystemRegistration` callback. Assign yours after that point: in a
  `RuntimeInitializeOnLoadMethod` with `BeforeSceneLoad`, or in the `Awake` or `Start` of your
  bootstrap. With domain reload disabled, a handler assigned in a static constructor is lost at the
  next session. In the editor the handler is also reset when a script reload during Play Mode makes
  the package start a new session.
- The handler is one static property for the whole game. Subscribing to it per object is the wrong
  tool; route by `context.Source` or `context.Owner` inside one handler.
- A handler should not throw. If it does, the package catches that too and logs both exceptions with
  `Debug.LogException`.
- Errors of tasks and timers are routed on the main thread, also when the task finished on a
  thread-pool thread.

---

## Silent cancellation inside an async method

A cancelled `await` ends by throwing `OperationCanceledException`. `Run` treats that as a normal
end, so in most methods cancellation needs no code at all. When a method does need to react,
`RewardView` from the DOTween Usage sample shows the three choices in one method:

<!-- source: Samples~/DOTweenUsage/RewardView.cs -->
```csharp
/// <summary>Awaits a tween and shows the three ways to handle a cancel inside an async method.</summary>
public sealed class RewardView : MonoBehaviour
{
    private Transform _chest;
    private TMP_Text _amountLabel;
    private Lifetime _show;

    // The views arrive here because the demo builds its UI in code.
    public void Initialize(Transform chest, TMP_Text amountLabel)
    {
        _chest = chest;
        _amountLabel = amountLabel;
    }

    private void Awake() => _show = this.GetLifetime().CreateChild("Show");

    public void Show(int amount)
    {
        _show.Cancel();                                    // a show already running stops here
        _show.Run(ct => ShowAsync(amount, ct));
    }

    private async UniTask ShowAsync(int amount, CancellationToken ct)
    {
        // Reset, so the view can be shown again after a cancel.
        _chest.gameObject.SetActive(true);
        _chest.localRotation = Quaternion.identity;
        _amountLabel.text = "0";

        // Run already ends silently on cancel: no log, no try/catch needed.
        await _chest.DOShakeRotation(0.4f, 15f).AwaitCompletionAsync(ct);

        // Cleanup on cancel: catch, clean up, rethrow.
        try
        {
            await CountUpAsync(amount, ct);
        }
        catch (OperationCanceledException)
        {
            if (this != null) _amountLabel.text = amount.ToString();   // land on the final value unless destroyed
            throw;
        }

        // A bool instead of an exception: UniTask's SuppressCancellationThrow.
        var cancelled = await UniTask.Delay(TimeSpan.FromSeconds(2), cancellationToken: ct).SuppressCancellationThrow();
        if (cancelled) return;
        _chest.gameObject.SetActive(false);
    }

    private async UniTask CountUpAsync(int amount, CancellationToken ct)
    {
        for (var shown = 0; shown <= amount; shown += Mathf.Max(1, amount / 30))
        {
            _amountLabel.text = shown.ToString();

            // Immediate: the catch above runs inside Cancel(), before a restarted show resets the label.
            await UniTask.Yield(PlayerLoopTiming.Update, ct, cancelImmediately: true);
        }

        _amountLabel.text = amount.ToString();   // the last step may stop short of the amount
    }
}
```

(`AwaitCompletionAsync(ct)` awaits a tween and throws when the tween is cancelled or killed; see
[DOTween](DOTween.md).)

`Show(250)` cancels a show that is still running and starts `ShowAsync` in the `Show` area of the
component. What a cancel does depends on which `await` the method is waiting in:

1. **During the shake: nothing to write.** The await throws `OperationCanceledException`, the
   exception leaves `ShowAsync`, and `Run` drops it. No log, no `try`/`catch`.
2. **During the count-up: clean up, then rethrow.** The `catch (OperationCanceledException)` block
   writes the final amount to the label, so a cancelled count-up lands on its end value. The
   `throw;` matters: without it the method would carry on after the `catch` as if nothing had
   happened. The `this != null` check is there because the cancel may have been caused by the
   object being destroyed, and cleanup may only touch what still exists.
3. **During the two second wait: a `bool` instead of an exception.** UniTask's
   `SuppressCancellationThrow()` turns the cancellation into a return value of `true`. The method
   checks it and returns by itself, and the chest stays visible.

One detail of step 2 matters whenever cleanup in a `catch` meets the restart pattern. By default a
cancelled UniTask await notices the cancel on the next player-loop tick, so its `catch` block runs
one frame after `Cancel()`. `Show` starts the next run right after the cancel; with the default,
the old run's cleanup would then write the old amount into the label after the new run had reset
it. The count-up therefore yields with `cancelImmediately: true`: the await ends inside the
`Cancel()` call, and the cleanup has run before the new run begins.

A broad `catch (Exception)` around an `await` swallows the cancellation too, and the method then
continues with the code after the `catch`. Catch `OperationCanceledException` first and rethrow it,
or filter it out.

---

## What a token-less await does

`Cancel()` cannot interrupt running C# code. It cancels a token, and an async method stops only at
an `await` that was given that token. An `await` without the token knows nothing about the cancel:
it completes when its own work completes, and the method continues with the next line.

`BackgroundWork` in the Basic Usage sample has both kinds of stopping point: a loop that has no
`await` at all, and an `await` that must not resume after a cancel:

<!-- source: Samples~/BasicUsage/BackgroundWork.cs -->
```csharp
/// <summary>CPU work on the thread pool that stops on cancel and returns to the main thread before it touches Unity.</summary>
public sealed class BackgroundWork : MonoBehaviour
{
    private TMP_Text _label;
    private Lifetime _job;

    // The view arrives here because the demo builds its UI in code.
    public void Initialize(TMP_Text label) => _label = label;

    private void Awake() => _job = this.GetLifetime().CreateChild("Job");

    public void CountPrimes(int limit)
    {
        _job.Cancel();                                 // a job already running stops here, so only the latest answer lands
        _job.Run(ct => CountPrimesAsync(limit, ct));
    }

    private async UniTask CountPrimesAsync(int limit, CancellationToken ct)
    {
        await UniTask.SwitchToThreadPool();
        var count = 0;
        for (var n = 2; n < limit; n++)
        {
            ct.ThrowIfCancellationRequested();     // a thread-pool loop does not stop unless it checks the token
            if (IsPrime(n))
            {
                count++;
            }
        }

        // The token form throws after a cancel; the token-less form would resume here and touch a dead label.
        await UniTask.SwitchToMainThread(ct);
        _label.text = $"{count} primes below {limit}";
    }

    private static bool IsPrime(int n)
    {
        for (var d = 2; d * d <= n; d++)
        {
            if (n % d == 0)
            {
                return false;
            }
        }

        return true;
    }
}
```

What happens at each moment:

- **`CountPrimes` is called.** `_job.Cancel()` stops a job that is still running, so only the answer
  of the latest call can reach the label. `Run` starts the method on the main thread. At
  `SwitchToThreadPool()` it moves to a thread-pool thread, and `Run` returns.
- **The loop runs.** Each iteration calls `ct.ThrowIfCancellationRequested()`.
- **The job is cancelled while the loop runs**, by the next `CountPrimes` call or because the
  component's lifetime ends. The token is cancelled on the main thread. The loop sees it at its next
  iteration and throws `OperationCanceledException`, which `Run` drops. Without that check the loop
  would run to its end: a cancel cannot stop a loop that never looks at the token.
- **The loop finishes.** `SwitchToMainThread(ct)` returns to the main thread. If the job was
  cancelled in the meantime, it throws instead, and the label is not touched. Written without `ct`,
  the method would resume on the main thread and write to a label that may be destroyed.

So after a `Cancel()`:

- every `await` that has the token ends with a cancellation, and the code after it does not run;
- an `await` without the token resumes, and the code after it runs, possibly against objects that
  have been destroyed or recycled in the meantime;
- an operation that was already started and takes no token (a web request made through an SDK, a
  scene load) keeps running to its end. Giving the `await` a token stops the waiting, not the
  operation.

How to give an `await` the token:

| Await | Token-aware form |
|---|---|
| `UniTask.Delay(...)` | `UniTask.Delay(..., cancellationToken: ct)` |
| `UniTask.Yield()` | `UniTask.Yield(ct)` |
| `UniTask.WaitUntil(condition)` | `UniTask.WaitUntil(condition, cancellationToken: ct)` |
| `UniTask.SwitchToMainThread()` | `UniTask.SwitchToMainThread(ct)` |
| A Unity `AsyncOperation`, such as a scene load | `.ToUniTask(cancellationToken: ct)` |
| A tween | `tween.AwaitCompletionAsync(ct)` (see [DOTween](DOTween.md)) |
| Your own async method | Give it a `CancellationToken` parameter and pass `ct` on, as `LoadOffersAsync` passes it to `FetchAsync` |
| A task from a library that takes no token | `.AttachExternalCancellation(ct)`: the await ends on cancel, the library's work continues |
| No `await` at all (a loop), or nothing of the above is possible | Check `ct.IsCancellationRequested`, or call `ct.ThrowIfCancellationRequested()`, in the loop or right after the await |

In the editor, the Janitor window reports a task that is still running a few frames after its
generation ended as [JANITOR101](Troubleshooting.md#janitor101) (three frames by default). That
warning is the sign of a token-less await.

On a lifetime that is reused, such as the active lifetime of a pooled object, a task that outlives
its generation has one more consequence, described in
[Pooling](Pooling.md#the-one-hole-that-remains).

---

## See also

- [Stopping work](Stopping-Work.md): a task in its own area, and the restart pattern
- [Concepts](Concepts.md): generations, and what `Token` returns after a `Cancel()`
- [Components and scenes](Components-and-Scenes.md): which lifetime `this.Run` uses, and the active
  lifetime
- [Coroutines](Coroutines.md): the coroutine counterpart of `Run`
- [Threading](Threading.md): the main-thread contract
- [Performance](Performance.md): allocation behaviour of the calls on this page
- [Troubleshooting](Troubleshooting.md#janitor101): JANITOR101, a task that outlived its generation
