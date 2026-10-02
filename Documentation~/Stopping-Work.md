# Stopping work

[Back to index](index.md)

Every way of stopping work in Janitor is the same operation, `Cancel()`, applied to a different
node of the lifetime tree. This page goes through the choices from the widest to the narrowest:
everything, a scene, one object, an area, one item, one task, and the restart pattern "cancel the
previous run, then start a new one". The model behind it is in [Concepts](Concepts.md).

---

## Which call stops what

| You want to stop | Call |
|---|---|
| Everything of one feature or mode | `Cancel()` on a root area you created for it |
| Everything in the game, listeners included | `Lifetime.App.Cancel()` |
| A scene's work, and keep the scene | `SceneLifetimes.Get(scene).Cancel()` |
| A scene's work for good, right before unloading it | `SceneLifetimes.Dispose(scene)` or `SceneLifetimes.DisposeAll()` |
| The work of one component | `component.GetLifetime().Cancel()` |
| The work an object started in `OnEnable` | `SetActive(false)`: the active lifetime is cancelled for you |
| One area or category | `area.Cancel()` |
| One coroutine, subscription, disposable or cleanup action | `registration.Cancel()` |
| One tween | `tween.Kill()` |
| One task or timer | `Cancel()` on an area that holds only that task |

`Cancel()` never throws, can be called any number of times, and leaves the lifetime usable. Nothing
has to be re-created afterwards: the next registration goes into a fresh generation.

---

## Stop everything

Create one area at the root of the feature, create every other area under it, and cancel the root.
`GameplayRoot` in the Basic Usage sample is that root:

<!-- source: Samples~/BasicUsage/GameplayRoot.cs -->
```csharp
/// <summary>The root area of the gameplay: it builds the categories and cancels everything under them.</summary>
public sealed class GameplayRoot : MonoBehaviour
{
    private Lifetime _gameplay;

    public GameplayLifetimes Lifetimes { get; private set; }

    private void Awake()
    {
        _gameplay = this.GetLifetime().CreateChild("Gameplay");   // disposed with this object or its scene
        Lifetimes = new GameplayLifetimes(_gameplay);
    }

    // A signal triggers, the tree executes: one handler, no broadcast.
    public void OnResetRequested() => _gameplay.Cancel();          // every area stops; each stays usable
}
```

The demo calls it from a button:

<!-- source: Samples~/BasicUsage/Demo/BasicUsageDemo.cs -->
```csharp
private void CancelPopups() => _root.Lifetimes.Popups.Cancel();
private void CancelEverything() => _root.OnResetRequested();
```

What happens when `OnResetRequested()` runs:

1. The tokens of `Gameplay`, of the two categories created under it (`Popups`, `Combat`) and of the
   lifetimes placed in them (the shop popup in `Popups`, a combat effect that is playing in
   `Combat`) are cancelled.
2. The registered items are terminated: the popup's wallet subscription, its click listener, its
   offer-loading task and its badge coroutine, the one second timer the demo registered in `Combat`,
   and the task, the timer and the cleanup action of a playing combat effect. That action destroys
   the effect object.
3. All of these lifetimes open a new generation and stay in the tree.

Afterwards the popup is still on screen but does nothing until it is closed and opened again,
because only its `OnEnable` registers its work. The combat timer does not come back, because the
demo registered it once at start. Work outside `Gameplay` is not touched: the countdown, the pooled
coins and the platform hooks keep running.

A game signal or a button triggers the call; the tree does the stopping. No class has to subscribe
to a "stop" message and remember to react to it.

### The literal everything

`Lifetime.App.Cancel()` cancels every lifetime in every scene and every DontDestroyOnLoad object.
It disposes nothing: every lifetime stays usable.

It is rarely the call you want, because `Cancel()` also removes subscriptions (see
[Concepts](Concepts.md#cancel-removes-subscriptions-too)). After `Lifetime.App.Cancel()` no button
subscribed with `onClick.Subscribe(handler, this)` responds any more, and nothing subscribes again
by itself. Use it for a hard reset that is followed by rebuilding the game state, for example by
loading the first scene again. For "the player left gameplay", cancel a root area.

---

## Stop a scene

<!-- signature -->
```csharp
public static class SceneLifetimes
{
    public static Lifetime Get(Scene scene);
    public static void Dispose(Scene scene);
    public static void DisposeAll();
}
```

**To stop a scene's work and keep the scene**, cancel its lifetime. `AdditiveSceneFlow` in the Basic
Usage sample has a method for it:

<!-- source: Samples~/BasicUsage/AdditiveSceneFlow.cs -->
```csharp
// Stops the scene's work and keeps the scene loaded; its lifetime stays usable.
public void StopWork(Scene scene) => SceneLifetimes.Get(scene).Cancel();
```

- Every component, GameObject and active lifetime in the scene, and every area under them, ends its
  current generation. Items are terminated children first. An object that was moved into the scene
  after its lifetime was created (`SetParent`, `MoveGameObjectToScene`) is included.
- The scene lifetime and all object lifetimes stay alive. An object that registers work afterwards,
  and an object that is created in the scene afterwards, gets a live lifetime.
- The objects' listeners are removed with everything else, so a scene cancelled this way reacts to
  nothing until its objects subscribe again.
- DontDestroyOnLoad objects are not part of any scene lifetime and are not reached. Neither is an
  object of the scene whose lifetime was placed in a category that lives outside the scene (see
  [Organizing work](Organizing-Work.md#scene-membership)).

**To end a scene's work for good**, dispose its lifetime right before the scene is unloaded. The
same class does that for one additive scene:

<!-- source: Samples~/BasicUsage/AdditiveSceneFlow.cs -->
```csharp
private static async UniTask UnloadAsync(Scene scene, CancellationToken ct)
{
    // Unity refuses an unloaded scene and the last loaded one, and disposal is final, so check before it.
    if (!scene.isLoaded || SceneManager.sceneCount < 2)
    {
        return;
    }

    SceneLifetimes.Dispose(scene);     // this scene's work stops now, before the unload destroys anything
    await SceneManager.UnloadSceneAsync(scene).ToUniTask(cancellationToken: ct);
}
```

For a load that replaces everything, the sample's `SceneFlow` disposes every loaded scene with one
call:

<!-- source: Samples~/BasicUsage/SceneFlow.cs -->
```csharp
/// <summary>Ends the work of the loaded scenes right before it loads the next one.</summary>
public sealed class SceneFlow
{
    private readonly Lifetime _lifetime;   // a child of Lifetime.App: it must outlive the scenes it unloads

    public SceneFlow(Lifetime lifetime) => _lifetime = lifetime;

    public void Load(string scenePath) => _lifetime.Run(ct => LoadAsync(scenePath, ct));

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
}
```

`Dispose(scene)` and `DisposeAll()` run before the unload or the load starts, while every object of
the old scene still exists. All tokens of the scene are cancelled, then all items are terminated,
and only then does Unity begin to destroy objects.

`Dispose(scene)` and `DisposeAll()` check nothing themselves. The two sample methods above,
`UnloadAsync` and `LoadAsync`, are what test whether Unity will accept the request, and they return
before anything is disposed when it will not. Disposal cannot be taken back: a scene whose lifetime
was disposed and which then stays loaded can never register work again. Why the order matters, the
same call for a scene name and for a synchronous load, what a disposed scene lifetime does with
late registrations, loads that can fail, loading screens, and what happens when the call is left
out are covered in [Components and scenes](Components-and-Scenes.md#the-scene-call).

---

## Stop one object

Most of the time no call is needed:

- **Destroying** an object disposes its lifetimes. The class needs no `OnDestroy`.
- **Deactivating** a GameObject cancels its active lifetime, which is where work started in
  `OnEnable` belongs. The class needs no `OnDisable`. See
  [Components and scenes](Components-and-Scenes.md#what-setactivefalse-stops).

To stop the work of a component that stays alive and active, cancel its lifetime.
`TutorialController` from the DOTween Usage sample shows this next to the two narrower choices:

<!-- source: Samples~/DOTweenUsage/TutorialController.cs -->
```csharp
/// <summary>Three ways to cancel: the whole lifetime, a child area, or one registration.</summary>
public sealed class TutorialController : MonoBehaviour
{
    private RectTransform _arrow;
    private CanvasGroup _hint;
    private Button _skipButton;

    private Lifetime _step;
    private LifetimeRegistration _hintPulse;

    // The views arrive here because the demo builds its UI in code; call it before the object is enabled.
    public void Initialize(RectTransform arrow, CanvasGroup hint, Button skipButton)
    {
        _arrow = arrow;
        _hint = hint;
        _skipButton = skipButton;
    }

    private void Awake()
    {
        _step = this.GetLifetime().CreateChild("Step");
        _skipButton.onClick.Subscribe(SkipStep, this);            // lives on the whole lifetime
    }

    public void ShowStep(TutorialStep step)
    {
        _step.Cancel();                                            // the previous step's work stops
        _arrow.DOLocalMove(step.ArrowPosition, 0.6f).SetLoops(-1, LoopType.Yoyo).AddTo(_step);
        _hintPulse = _step.StartCoroutine(this, PulseHint());
        step.Target.onClick.Subscribe(CompleteStep, _step);
    }

    public void OnPlayerMoved() => _hintPulse.Cancel();            // 3. one registration: only the pulse stops
    public void SkipStep() => _step.Cancel();                      // 2. child area: the whole step stops
    public void Abort() => this.GetLifetime().Cancel();            // 1. whole lifetime: everything, the skip listener too

    private IEnumerator PulseHint()
    {
        while (true)
        {
            _hint.alpha = Mathf.PingPong(Time.time, 1f);
            yield return null;
        }
    }

    private void CompleteStep()
    {
        Debug.Log("[TutorialController] Step completed.");
        _step.Cancel();   // advance the tutorial here; the step's arrow, pulse and listener end with it
    }
}
```

(The `DOLocalMove(...).AddTo(_step)` line binds a tween to the area; see [DOTween](DOTween.md).)

`Abort()` is the widest of the three. `this.GetLifetime().Cancel()` cancels the component lifetime
and the `_step` area under it: the arrow tween, the hint pulse, the listener on the step's target
button, and also the listener on the skip button that `Awake` registered with
`Subscribe(SkipStep, this)`.

That last part is the consequence to remember. After `Abort()`, `ShowStep` still works, because it
registers its work again each time it is called. The skip button does not work any more, because
`Awake` does not run again. A component that must keep listening after it is stopped has two
options: cancel a child area instead of its whole lifetime (`SkipStep()` below), or subscribe again
after the cancel.

A component lifetime belongs to one component. Two `MonoBehaviour`s on the same GameObject have two
separate component lifetimes, and cancelling one does not touch the other. The active lifetime is
the one that is shared by the whole GameObject.

---

## Stop an area

An area is a lifetime you created with `CreateChild`, kept in a field and reused. In the class above
`_step` is one: everything `ShowStep` registers goes into it, and `SkipStep()` cancels it.

When `_step.Cancel()` runs, the arrow tween is killed, the hint pulse coroutine is stopped and the
listener on the target button is removed. The skip listener is registered on the component lifetime
and is not affected. `_step` is usable again at once, and `ShowStep` uses it for the next step.

An area can be shared. In the sample, one class registers work into the `Combat` category and
another class stops it, and neither knows the other:

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

Call `Dispose()` on an area only when it will never be used again. Disposal is terminal: later
registrations on it are terminated on the spot. An area that is reused needs `Cancel()`, not
`Dispose()` followed by a new `CreateChild`. Creating a new area for every operation and never
disposing it makes the parent grow; the Janitor window reports that as
[JANITOR106](Troubleshooting.md#janitor106). How to lay out areas and categories is the subject of
[Organizing work](Organizing-Work.md).

---

## Stop one item

Every registration that is not a task, a timer or a tween returns a `LifetimeRegistration`:

<!-- signature -->
```csharp
public readonly struct LifetimeRegistration
{
    public bool IsActive { get; }
    public void Cancel();
}
```

`HintPulse` in the Basic Usage sample keeps the handle of one coroutine and stops only that:

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

What happens at each moment:

- **`StartPulse()` is called for the first time.** `_pulse` is still a default handle, so
  `_pulse.Cancel()` does nothing. The coroutine starts on the component lifetime, and its handle is
  stored.
- **`StartPulse()` is called while the hint pulses.** `_pulse.Cancel()` stops the running coroutine,
  and a new one starts. There are never two.
- **`StopPulse()` is called.** That one coroutine stops. Anything else registered on the component
  lifetime keeps running.
- **`StopPulse()` is called twice, or after the object's lifetime was cancelled.** The handle is
  inactive, and `Cancel()` does nothing.

`TutorialController` above does the same with `_hintPulse`: `OnPlayerMoved()` stops the pulse, while
the arrow keeps moving and the target button keeps listening.

- `Cancel()` on a handle **terminates** the item: the coroutine is stopped, the subscription is
  removed, the disposable is disposed, the `OnCancel` action runs now. It is not a way to detach an
  item and keep it running, which is why the type does not implement `IDisposable`.
- A handle is stamped with the generation it was created in. After `_step.Cancel()` the stored
  `_hintPulse` is stale: `IsActive` is `false` and `Cancel()` does nothing. It cannot stop the pulse
  of the next step by accident.
- A default handle, a handle whose item already ended, and a second `Cancel()` are all harmless.
- These calls return a handle: every `Subscribe` form (`OwnedEvent`, `UnityEvent`, paired),
  `AddTo` for an `IDisposable`, `OnCancel`, and `StartCoroutine`.
- A tween is its own handle: `AddTo` returns the tween, and `tween.Kill()` stops it. See
  [DOTween](DOTween.md).
- `Run`, `After` and `Every` return nothing. The next section is the way to stop one of them.

---

## Stop one task

Tasks and timers share the token of their lifetime's generation and have no handle of their own. A
handle per task would cost one `CancellationTokenSource` per call. To stop one task without stopping
its neighbours, give it an area of its own.

`Countdown` in the Basic Usage sample has one task and one area for it:

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

`Awake` creates the area `Run` under the component lifetime. `StopCountdown()` cancels it:

- The area's token is cancelled. `TickAsync` is waiting in `UniTask.Delay` with that token; the
  delay ends with an `OperationCanceledException`, the loop is left, and `Run` drops the exception
  without logging. The label keeps the last number it showed. `"Go!"` is never written and
  `Finished` is not invoked.
- Anything else the component registered on its own lifetime is unaffected.
- `StopCountdown()` and `StartCountdown()` are **intent methods**: other classes stop or restart the
  countdown without ever seeing the area.

If the `Countdown` object is destroyed while counting, its component lifetime is disposed and the
area with it, so the same thing happens without any call. The class has no `OnDestroy`.

---

## Cancel the previous run, then start a new one

A restartable operation written by hand needs a `CancellationTokenSource` field, and the same three
lines in every method that touches it:

<!-- illustrative: before -->
```csharp
public sealed class Countdown : MonoBehaviour
{
    private TMP_Text _label;
    private CancellationTokenSource _cts;

    public event Action Finished;

    public void Initialize(TMP_Text label) => _label = label;

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

The lines that go wrong:

- `private void OnDestroy()`: the whole class is correct only as long as this method exists. Leave
  it out and a running `TickAsync` resumes after the object was destroyed and writes to a destroyed
  label.
- `_cts?.Dispose();` in `OnDestroy` leaves a disposed source in the field. A `StopCountdown()` that
  arrives afterwards calls `Cancel()` on it and throws `ObjectDisposedException`.
- `.Forget()`: an exception inside `TickAsync` has no owner and no call site attached to it.
- `public event Action Finished;`: every subscriber has to write its own `-=`.

The version from the sample replaces the field, the three lines and `OnDestroy` with an area:

<!-- source: Samples~/BasicUsage/Countdown.cs -->
```csharp
public void StartCountdown(int seconds)
{
    _run.Cancel();                                    // a countdown already running stops here
    _run.Run(ct => TickAsync(seconds, ct));
}
```

What happens at each moment:

- **First `StartCountdown(10)`.** `_run.Cancel()` ends an empty generation and allocates nothing.
  `_run.Run(...)` calls `TickAsync` with the token of the new generation. `TickAsync` writes `10` to
  the label, reaches the delay and returns; the task is now an entry in `_run`.
- **`StartCountdown(10)` again while it counts.** `_run.Cancel()` cancels the token the first
  `TickAsync` is waiting on and removes its entry. When `Cancel()` returns, the old generation is
  over. `_run.Run(...)` starts the second `TickAsync` in the new generation, with a new token. The
  first one never touches the label again: its delay ends with a cancellation instead of resuming
  the loop.
- **The count reaches zero.** `"Go!"` is written, `_finished.Invoke()` calls the subscribers, the
  method returns and the task removes its own entry.
- **`StopCountdown()`**, **destroying the object** and **disposing the scene** all end the current
  generation of `_run`, with the result described in the previous section.

The order of the two lines matters. Work registered on a lifetime while it is being cancelled is
terminated on the spot, so the new run must start after `Cancel()` has returned, never from inside a
cancel callback (see [Concepts](Concepts.md#re-entrancy)).

The same two lines work in a plain C# class that was handed a lifetime. In the DOTween Usage sample
a service restarts an asynchronous apply, and exposes the cancel as an intent method for other
services:

<!-- source: Samples~/DOTweenUsage/CustomizationService.cs -->
```csharp
public void Apply(Outfit outfit)
{
    _apply.Cancel();                                  // the previous apply stops: load, punch and save
    _apply.Run(ct => ApplyAsync(outfit, ct));
}

// Intent method: callers cancel the apply without seeing the raw area.
public void CancelApply() => _apply.Cancel();
```

One `_apply.Cancel()` stops whichever stage the previous apply had reached: the load, the awaited
tween or the save.

### Restartable work that also stops on SetActive(false)

`Countdown` keeps counting while its GameObject is inactive, because its area hangs under the
component lifetime. For work that must be restartable **and** stop when the object is hidden,
create the area under the active lifetime. `InboxPanel` in the Basic Usage sample does that for
its load:

<!-- source: Samples~/BasicUsage/InboxPanel.cs -->
```csharp
private void OnEnable()
{
    // Cancelled by SetActive(false); disposed on destroy.
    var shown = this.GetActiveLifetime();

    // An area under the active lifetime: SetActive(false) cancels it, so it is kept and reused.
    _load ??= shown.CreateChild("Load");

    OnUnreadChanged(_inbox.Unread);
    shown.StartCoroutine(this, Spin());
    _refreshButton.onClick.Subscribe(Refresh, shown);
    _inbox.UnreadChanged.Subscribe(OnUnreadChanged, shown);
    _load.Run(LoadAsync);
}

// The running load stops here; the spinner and the listeners stay.
private void Refresh()
{
    _load.Cancel();
    _load.Run(LoadAsync);
}
```

`Refresh()` restarts the load only. `SetActive(false)` cancels `shown` and `_load` together, and
the area is kept for the next activation: it is cancelled, not disposed. The complete class and
what happens at each moment are in
[Organizing work](Organizing-Work.md#an-area-under-the-active-lifetime).

---

## See also

- [Concepts](Concepts.md): generations, `Cancel` versus `Dispose`, the order in which things stop
- [Components and scenes](Components-and-Scenes.md): what destroy, `SetActive(false)` and the scene
  call stop
- [Organizing work](Organizing-Work.md): where areas and categories come from
- [Tasks and errors](Tasks-and-Errors.md): what a cancelled task does at its next `await`
- [Pooling](Pooling.md): stopping the work of a recycled object
- [Troubleshooting](Troubleshooting.md): "my listener stopped after `Cancel()`" and the `JANITOR1xx`
  IDs
