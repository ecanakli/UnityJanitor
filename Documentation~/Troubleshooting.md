# Troubleshooting

[Back to index](index.md)

This page has two parts. The first has one section per diagnostic ID, `JANITOR101` to `JANITOR116`:
what the diagnostic means, what triggers it, how to fix it, and whether it is harmless. The Docs
button in the Janitor window opens these sections. The second part starts from symptoms: things you
see in the game or in the compiler output.

How to read the "Shown in" line of each ID:

- **Window** means the Warnings tab of `Window > Analysis > Janitor`, in the editor, while Tracking
  is on. See [Diagnostics](Diagnostics.md).
- **Console** means a `Debug.LogWarning` in the editor and in development builds. Release builds
  contain none of these warnings, with the one exception noted under JANITOR115.

Console messages always have the form `[JANITOR1xx] message (see Troubleshooting#janitor1xx)`.

---

## JANITOR101

**Task overrun: a `Run`, `After` or `Every` task is still running after its generation ended.**
Shown in: Window. The task also appears as an `Outlived` entry on the Details tab.

**What it means.** A lifetime was cancelled or disposed, and a task that was started on it did not
stop. The package cancelled the token it gave to the task; the task's code did not react.

**What triggers it.** The task is still running more than a set number of frames after its
generation ended. The number is the "Overrun frames" setting of the window: three by default, and
never less than one, because a task that honours its token needs a frame to notice the cancel. The
message names the kind of task, the place it was started and the lifetime:

```text
The Run task started at StartCountdown:27 is still running more than 3 frames after the generation of the lifetime 'Run' ended. Pass the lifetime's token to every await in it, and check cancellation inside loops. A task that already does so is reported too when it waits on something that observes the token late, such as a FixedUpdate await while Time.timeScale is 0 or a thread-pool job in flight; it ends when that wait does.
```

The usual cause is an await that was not given the token:

<!-- illustrative: before -->
```csharp
public sealed class Countdown : MonoBehaviour
{
    [SerializeField] private TMP_Text _label;
    private Lifetime _run;

    private void Awake() => _run = this.GetLifetime().CreateChild("Run");

    public void StartCountdown(int seconds)
    {
        _run.Cancel();
        _run.Run(ct => TickAsync(seconds, ct));
    }

    public void StopCountdown() => _run.Cancel();

    private async UniTask TickAsync(int seconds, CancellationToken ct)
    {
        for (var left = seconds; left > 0; left--)
        {
            _label.text = left.ToString();
            await UniTask.Delay(TimeSpan.FromSeconds(1));   // no token: Cancel() cannot end this await
        }

        _label.text = "Go!";
    }
}
```

`StopCountdown()` cancels the token, but `UniTask.Delay` was never told about it, so the loop keeps
counting. Call `StartCountdown` twice and two loops write to the same label.

Other causes: a loop that never awaits and never checks the token, and an await on something that
takes no token at all.

A task that does pass its token everywhere can be reported as well, when the thing it waits on
notices the token late:

- an await on `FixedUpdate` timing while `Time.timeScale` is 0. No fixed update runs, so nothing
  looks at the token until time moves again;
- a job on the thread pool that is in the middle of a long step between two token checks.

Such a task ends when that wait does. If this is expected in your game, raise "Overrun frames".

**How to fix it.** Pass the token to every await:

<!-- source: Samples~/BasicUsage/Countdown.cs -->
```csharp
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
```

A loop that does not await has to look at the token itself, on every pass. This one runs on the
thread pool, where nothing else can stop it:

<!-- source: Samples~/BasicUsage/BackgroundWork.cs -->
```csharp
for (var n = 2; n < limit; n++)
{
    ct.ThrowIfCancellationRequested();     // a thread-pool loop does not stop unless it checks the token
    if (IsPrime(n))
    {
        count++;
    }
}
```

For awaits that take no token, see [Tasks and errors](Tasks-and-Errors.md). If the work really must
finish whatever happens to the object (a save, for example), it does not belong to that lifetime:
start it from a longer-lived one.

**Is it harmless?** No. The code runs on after its owner ended. It can touch destroyed objects, and
on a pooled object it carries on into the next spawn. This is the one gap the package cannot close
for you, because it cannot interrupt code that does not look at its token.

---

## JANITOR102

**Unkillable tween: a tween is still active right after it was killed, most likely because it is
nested in a Sequence.**
Shown in: Window. Requires the DOTween integration.

**What it means.** A lifetime ended and killed a tween that was registered with `AddTo` or awaited
with `AwaitCompletionAsync`, and DOTween ignored the kill. The tween is still playing on a target
whose owner has ended.

**What triggers it.** DOTween ignores `Kill` on a tween that is inside a Sequence, and logs nothing
when it does. So the trigger is registering a nested tween instead of its root:

<!-- illustrative: before -->
```csharp
public sealed class PanelIntro : MonoBehaviour
{
    [SerializeField] private RectTransform _panel;

    private void OnEnable()
    {
        var shown = this.GetActiveLifetime();

        var grow = _panel.DOScale(1.1f, 0.25f).AddTo(shown);   // nested below: this kill will be ignored
        var settle = _panel.DOScale(1f, 0.1f).AddTo(shown);    // and this one
        DOTween.Sequence().Append(grow).Append(settle);        // the root is never registered
    }
}
```

When `shown` is cancelled, both kills are ignored, the Sequence keeps playing, and the panel keeps
scaling. The warning is attached to the lifetime and to the `AddTo` line, and it is recorded once
per tween.

**How to fix it.** Register the root Sequence, and only the root:

<!-- source: Samples~/DOTweenUsage/PanelIntro.cs -->
```csharp
DOTween.Sequence()
    .Append(_panel.DOScale(1.1f, 0.25f).SetEase(Ease.OutQuad))
    .Append(_panel.DOScale(1f, 0.1f))
    .Join(_panel.DOShakeRotation(0.1f, 4f))
    .AddTo(shown, TweenCancelMode.Complete);   // the root only; a cut-short intro lands on its end pose
```

The sample registers the root in `Complete` mode, so a cut-short intro lands on its end pose. The
default `Kill` mode fixes this warning too; what matters is that `AddTo` is called on the Sequence.

**Is it harmless?** No. And it is silent outside the editor: in a player nothing tells you that the
tween survived. See [DOTween](DOTween.md#register-the-root-sequence-only).

---

## JANITOR103

**Orphan lifetime: the owner of a lifetime was destroyed but the lifetime is still alive.**
Shown in: Window. It is detected only while the Janitor window is open and refreshing in Play Mode.

```text
The owner of the lifetime 'HintPulse' was destroyed, but the lifetime is still alive, so the work registered on it was not stopped. A lifetime first used while its GameObject was inactive ends only with the GameObject, and one first used inside OnDestroy does not end with its owner; destroy the GameObject instead of only the component, and do not register work from OnDestroy.
```

**What it means.** A component, GameObject or active lifetime normally ends the moment its owner is
destroyed. This one did not, so the work registered on it was not stopped by the destroy. In the
tree its state reads `Active (orphan)`.

**What triggers it.** The lifetime never received the destroy signal of its owner. Two ways to get
there:

- **A component on a GameObject that was never active is destroyed on its own.** A component
  lifetime that is first used while its GameObject is inactive is bound to two signals, the
  destruction of the component and the destruction of the GameObject, and ends with whichever comes
  first. But Unity sends no destroy signal for a component that never ran `Awake`.
  If the object is never activated, only the GameObject signal is left: `Destroy(component)` alone
  ends nothing, and destroying the GameObject does. Once the object has been active, destroying the
  component ends its lifetime as usual.
- **The first access to a component's lifetime happens inside that component's own `OnDestroy`.**
  Unity never cancels a destroy token that is first read inside `OnDestroy`, so a
  `this.Run(...)` or `this.GetLifetime()` written there creates a lifetime that nothing ends.

The warning is recorded when two refreshes of the window, in different frames, both see the orphan,
so the row appears about a quarter of a second after the window first notices it. The delay is
deliberate: the lifetime of a never-activated object learns about the destruction one frame late,
and a single look in that frame would report an object that is cleaned up correctly.

**How to fix it.** Destroy the GameObject instead of a single component when the object may never
have been active. Do not start work through a lifetime from inside `OnDestroy`; by then the object's
work has already been stopped, and cleanup written there needs no lifetime.

**Is it harmless?** No, but it is bounded. The orphan's work keeps running until its scene lifetime
is disposed, which ends everything under it. That happens at the latest when the scene is unloaded.

---

## JANITOR104

**A scene was unloaded without `SceneLifetimes.Dispose` or `DisposeAll`.**
Shown in: Console and Window, once per scene.

```text
[JANITOR104] The scene 'Main' was unloaded without SceneLifetimes.Dispose(scene) or DisposeAll(), so its lifetime was disposed after Unity had already destroyed its objects. Call SceneLifetimes.DisposeAll() right before a Single-mode load, or Dispose(scene) right before UnloadSceneAsync. (see Troubleshooting#janitor104)
```

**What it means.** The scene's lifetime was still alive when the scene went away. The package
disposed it at that point, which is late: Unity had already destroyed the scene's objects, in an
order you do not control.

**What triggers it.** A scene that has a lifetime is unloaded, or replaced by a Single-mode load,
and nobody called the scene call first:

<!-- illustrative: before -->
```csharp
public sealed class SceneFlow
{
    // Objects of the old scene are destroyed in undefined order while their work still runs.
    public async UniTask LoadAsync(string sceneName, CancellationToken ct)
    {
        await SceneManager.LoadSceneAsync(sceneName).ToUniTask(cancellationToken: ct);
    }
}
```

Unity raises no event before it starts destroying a scene. On a Single-mode load the order is
`OnDisable`, `OnDestroy`, `sceneUnloaded`, `activeSceneChanged`, `sceneLoaded`. The
package's fallback listens to `sceneUnloaded`, which comes after every `OnDestroy`. With the Zenject
integration the scene disposer catches it a little earlier and raises the same diagnostic.

**How to fix it.** Call `SceneLifetimes.DisposeAll()` right before a Single-mode load, or
`SceneLifetimes.Dispose(scene)` right before unloading one scene:

<!-- source: Samples~/BasicUsage/SceneFlow.cs -->
```csharp
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

`DisposeAll()` cannot be undone, so the method first makes sure the load can start: a path that is
not in the Build Settings is reported and nothing is disposed. For a load that can still fail later,
dispose right before the activation instead; see [JANITOR109](#janitor109).

For one additive scene, dispose that scene's lifetime right before the unload. The other loaded
scenes are not touched:

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

The class that makes the call must not be owned by a scene it unloads. See
[Components and scenes](Components-and-Scenes.md).

**Is it harmless?** Mostly, which is why it is a warning and not an error. Every object lifetime was
still disposed when its object was destroyed, so nothing leaked. What you lose is the ordering:
plain C# work of one object may have run against another object that Unity had already destroyed.
That is the "MissingReferenceException during a scene change" class of bug. The message is
suppressed once Unity has signalled that the application, or Play Mode, is exiting.

---

## JANITOR105

**A duplicate subscription was ignored.**
Shown in: Console and Window.

```text
[JANITOR105] A duplicate subscription was ignored: OwnedEvent<Int32> "CoinsChanged" already has this handler for the lifetime 'ShopPopup'. (see Troubleshooting#janitor105)
```

**What it means.** The same handler was subscribed to the same event for the same owner a second
time, while the first subscription was still alive. The package added nothing and returned the
registration of the first one. The handler is still called once per event.

**What triggers it.** A second `Subscribe` with an equal handler (the same target object and the
same method) on the same `OwnedEvent`, UnityEvent or SignalBus signal, with the same owner in the
same generation. For the paired `Subscribe(add, remove, handler)` it is a second call whose `add`,
`remove` and handler are all equal to those of a live subscription of that owner; `add` is not
called again, and the message reads `Paired Subscribe already has this handler`. Nearly always the
cause is a subscription made in `OnEnable` with an owner that does not end in `OnDisable`:

<!-- illustrative: before -->
```csharp
public sealed class ShopPopup : MonoBehaviour
{
    [SerializeField] private TMP_Text _coinsLabel;
    [Inject] private Wallet _wallet;

    private void OnEnable()
    {
        // Wrong owner: "this" is the component lifetime, which does not end on SetActive(false).
        _wallet.CoinsChanged.Subscribe(OnCoinsChanged, this);
    }

    private void OnCoinsChanged(int coins) => _coinsLabel.text = coins.ToString();
}
```

The first `OnEnable` subscribes. `SetActive(false)` removes nothing, because the owner is the
component lifetime and the component was not destroyed. The second `OnEnable` subscribes the same
handler again: ignored, and reported.

**How to fix it.** Use the owner that matches where the call is. Work started in `OnEnable` belongs
to the active lifetime:

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

Or subscribe once, in `Awake`, with `this` as the owner, if the handler should keep running while
the object is inactive.

**Is it harmless?** The duplicate itself is: it was ignored. What it points at usually is not. In
the example above the handler keeps running while the popup is closed, which is the bug the warning
is really telling you about.

Limits of the check:

- **The paired `Subscribe` is compared by its three delegates, not by the event.** The package
  cannot see which event `add` touches. A repeat is recognised when `add`, `remove` and the handler
  each have the same target object and the same method as before. That covers static lambdas,
  lambdas that use only `this` or fields, method groups and cached delegates.
- **A paired repeat it cannot recognise:** an `add` or `remove` lambda that captures a local
  variable or a parameter is a new object on every call, and two lambdas written at two places in
  the source are two different methods. Such calls add the handler twice, as two `+=` would.
- **A paired call it takes for a repeat although it is not one:** lambdas that read a field, such
  as `h => _ads.Closed += h`, compare equal even after the field was pointed at another object.
  A second call for the new object is ignored while the first subscription is alive. Cancel the
  first registration, or its lifetime, before subscribing to the new source.
- `disposable.AddTo(...)` does not detect duplicates.
- The same handler under a **different** owner is a separate subscription for `OwnedEvent`,
  UnityEvent and the paired form, with no warning. For SignalBus it throws
  `InvalidOperationException`; see [Zenject](Zenject.md#duplicates).

---

## JANITOR106

**Growth: more than 256 entries or 64 live child areas on one lifetime.**
Shown in: Window, once per lifetime for each of the two limits.

```text
The lifetime 'Main' has 65 live children that are areas (the limit is 64). Areas are created faster than they are disposed; dispose the areas you no longer need.
```

**What it means.** One lifetime is collecting items, or child areas, faster than they end. It is a
leak indicator: everything a lifetime holds stays in memory until its generation ends, and an area
stays until it is disposed or its parent ends.

**What triggers it.**

- **More than 256 entries.** A task removes its entry when it finishes, and the entries of finished
  tweens and coroutines are swept out, so these rarely add up. Subscriptions, `IDisposable` items
  and `OnCancel` actions stay until the generation ends. A registration made every frame, or
  inside a loop, on a lifetime that is never cancelled reaches the limit quickly.
- **More than 64 live child areas.** Usually `CreateChild()` called once per operation, where the
  area is never disposed. Disposed areas do not count. The limit applies under every kind of
  parent, `Lifetime.App` and scene lifetimes included.
- **With Zenject, plain objects created at run time.** Every plain class that injects a `Lifetime`
  receives an area under its context lifetime. A factory that keeps creating such objects, without
  disposing their lifetimes, raises this on the scene lifetime.
  [Zenject](Zenject.md#objects-created-at-run-time) shows the fix with the `OfferWatcher` sample
  class: the object disposes its injected lifetime when it is done.

Only areas are counted. Object lifetimes (component, GameObject and active lifetimes) are children
of their scene or of the category they were placed in by design, so a scene with thousands of
objects, or a category that holds hundreds of placed objects, raises nothing.

**How to fix it.** Create an area once, keep it in a field, and reuse it: `Cancel()` empties it and
leaves it usable. `Dispose()` an area you no longer need. Register repeated work on the shortest
lifetime that fits, such as an active lifetime that is cancelled on every deactivation.

**Is it harmless?** The warning is a heuristic, so it can be. A lifetime that legitimately holds a
few hundred long-lived subscriptions will raise it once and can be ignored. A count that keeps
climbing in the tree cannot.

---

## JANITOR107

**`Dispose()` was ignored on a package-owned lifetime; use `Cancel()`.**
Shown in: Console and Window.

```text
[JANITOR107] Dispose() was ignored on the package-owned lifetime 'ShopPopup'. Use Cancel() instead. (see Troubleshooting#janitor107)
```

**What it means.** `Dispose()` was called on a lifetime that the package owns, and nothing happened.
Package-owned lifetimes are `Lifetime.App`, scene lifetimes, and the component, GameObject and
active lifetimes of objects. They are disposed only when their owner goes away: the object is
destroyed, the scene is disposed, the application exits.

**What triggers it.** A direct `Dispose()` call, a `using` statement around such a lifetime, or code
that disposes every `IDisposable` it is handed.

**Why it is refused.** An object's own lifetime backs every `this.Run(...)`, `tween.AddTo(this)` and
`Subscribe(handler, this)` call of that object. If it could be disposed while the object lives,
every later registration would be terminated on the spot and the object would stop working without
any error.

**How to fix it.** To stop the work, call `Cancel()`: it stops everything and keeps the lifetime
usable. To have something you can end for good, create an area with `CreateChild()` and dispose
that.

**Is it harmless?** The call did nothing, so yes. But whatever you meant to stop is still running.

---

## JANITOR108

**Registration refused: the active lifetime's GameObject is inactive.**
Shown in: Console and Window.

```text
[JANITOR108] A registration on the active lifetime 'Coin' was terminated immediately because its GameObject is inactive. Register in OnEnable or later. (see Troubleshooting#janitor108)
```

**What it means.** Work was registered on the lifetime returned by `GetActiveLifetime()`, or on an
area created under it, while its GameObject was inactive. An active lifetime only holds work while
the object is active, and so does everything below it. The item was ended at once: a tween is
killed (and an await on it is cancelled), a disposable is disposed, a task is not started, a handler
is not subscribed.

**What triggers it.** Another object calls a method on an inactive object, and that method registers
on the active lifetime or on one of its areas. Or a callback runs after the object was deactivated
and tries to start the next step. The message names the active lifetime, also when the call was made
on an area below it, and selecting the warning in the window pings its GameObject.

Two related cases log nothing, because nothing was refused:

- Reading `Token` on such a lifetime while the GameObject is inactive returns a token that is
  already cancelled. Work started with it by hand ends at its first await.
- `CreateChild()` on an active lifetime is not a registration. It is allowed while the GameObject
  is inactive, and before it was ever active. Only work registered on the new area is refused.

**How to fix it.** Register from `OnEnable` or later, while the object is active. If the work should
run while the object is inactive, it belongs to the component lifetime (`this.GetLifetime()`), or to
an area created under the component lifetime, not to the active one.

**Is it harmless?** The package did the safe thing. The work you asked for did not start, though, so
treat it as a logic error in the caller.

---

## JANITOR109

**Registration on a disposed scene lifetime while the scene is still loaded.**
Shown in: Console and Window, once per scene lifetime.

```text
[JANITOR109] A registration on the disposed scene lifetime 'Main' was terminated immediately while the scene is still loaded. Disposal is terminal; register only after the scene has been replaced. (see Troubleshooting#janitor109)
```

**What it means.** `SceneLifetimes.Dispose(scene)` or `DisposeAll()` has already run for a scene,
and something in that scene tried to start new work. Disposal is terminal: the registration was
ended at once.

**What triggers it.** Between the dispose call and the moment the scene is really gone, any of
these:

- a registration on the scene lifetime;
- `CreateChild()` on the scene lifetime (the new area is disposed from the start);
- a first use of the lifetime of an object in that scene.

That window should be very short. It becomes long when the dispose call is made too early, for
example at the start of a load that takes seconds while the old scene keeps updating; when a scene
is disposed and then not unloaded; or when the load that should have replaced the scene fails.

The warning is raised once per scene lifetime, so one line can stand for many refused calls. It is
not raised once the scene has finished unloading, and never while the application, or Play Mode, is
exiting.

**How to fix it.** Make the scene call the last thing before the old scene is replaced. With a
loading screen that holds the new scene back, that is right before the activation, not at the start
of the load:

<!-- source: Samples~/BasicUsage/LoadingScreenFlow.cs -->
```csharp
private async UniTask LoadAsync(string sceneName, CancellationToken ct)
{
    if (_loading)
    {
        return;                        // a second load would replace the scene this one is loading
    }

    _loading = true;
    _screen.Show();
    try
    {
        var operation = SceneManager.LoadSceneAsync(sceneName);
        if (operation == null)
        {
            return;                    // unknown scene: nothing started, nothing was disposed
        }

        operation.allowSceneActivation = false;
        try
        {
            await UniTask.WaitUntil(() => operation.progress >= 0.9f, cancellationToken: ct);   // loaded, not yet activated
        }
        finally
        {
            SceneLifetimes.DisposeAll();               // right before activation, also when the wait was cancelled
            operation.allowSceneActivation = true;     // a started load cannot be aborted, and a held one stalls later loads
        }

        await operation.ToUniTask(cancellationToken: ct);
    }
    finally
    {
        _screen.Hide();
        _loading = false;
    }
}
```

What happens at each moment:

1. **A load is already running.** The method returns. A second load would replace the scene the
   first one is loading.
2. **The load starts with its activation held.** `LoadSceneAsync` returns `null` for a scene it
   does not know; nothing has been disposed at that point, so the method can return and the old
   scene carries on untouched.
3. **While the new scene loads,** the old scene is still running and its lifetime is alive, so it
   may start work as usual.
4. **The load reaches 90 percent,** which is as far as a held load goes. The `finally` block calls
   `DisposeAll()`, which stops the old scene's work at the last moment, and releases the
   activation, which replaces the scene.
5. **The flow's own lifetime is cancelled during the wait.** The `finally` block runs all the same.
   A load that has started cannot be aborted, and a held one blocks every later load, so the old
   scene's work is ended and the activation released on this path too.
6. **The outer `finally`** hides the loading screen and allows the next load, however the method
   ended.

Had `DisposeAll()` been the first line of the method, every registration the old scene made during
the load would have been refused, and a load that failed would have left the old scene on screen
with its lifetime gone for good.

To stop a scene's work and keep the scene, cancel its lifetime instead of disposing it. The scene
lifetime stays usable:

<!-- source: Samples~/BasicUsage/AdditiveSceneFlow.cs -->
```csharp
// Stops the scene's work and keeps the scene loaded; its lifetime stays usable.
public void StopWork(Scene scene) => SceneLifetimes.Get(scene).Cancel();
```

**Is it harmless?** The refused work did not run, which is what you want from a scene that is going
away. If the scene stays on screen after the warning, the dispose call sits in the wrong place: the
scene's lifetime cannot be used again, and neither can the lifetimes of its objects.

---

## JANITOR110

**Coroutine not started: the host is null, destroyed or inactive.**
Shown in: Console and Window.

```text
[JANITOR110] A coroutine requested by OnEnable:44 on the lifetime 'Popup' was not started because its host 'Popup' is inactive. A coroutine needs an active host; start it from an active MonoBehaviour, usually 'this', in OnEnable or later. (see Troubleshooting#janitor110)
```

**What it means.** `lifetime.StartCoroutine(host, routine)` was called with a host that cannot run
coroutines. The routine was not started and nothing was registered.

**What triggers it.** The `host` argument is `null`, is a destroyed `MonoBehaviour`, or sits on a
GameObject that is inactive in the hierarchy. Unity itself would log an error for the inactive case;
the package checks first and reports this instead. It is not raised when the **lifetime** has
already ended: in that case the coroutine is not started either, silently, like any other
registration on an ended lifetime.

**How to fix it.** Start the coroutine from an active `MonoBehaviour`, usually `this`, in `OnEnable`
or later. See [Coroutines](Coroutines.md).

**Is it harmless?** Nothing broke, but the coroutine you asked for is not running.

---

## JANITOR111

**`Cancel` or `Dispose` was called off the main thread and deferred to it.**
Shown in: Console and Window.

```text
[JANITOR111] Lifetime.Cancel() was called off the main thread and is marshalled to the next main-thread tick. (see Troubleshooting#janitor111)
```

**What it means.** `Lifetime.Cancel()`, `Lifetime.Dispose()` or `LifetimeRegistration.Cancel()` was
called from a thread other than the main thread. The call did not run there. It was queued and runs
on the main thread at the next update.

**What triggers it.** A continuation that resumed on a thread-pool thread, a timer callback, or a
callback of a library that calls back on its own thread.

**How to fix it.** If "stops at the next update" is acceptable, nothing has to change. If the stop
must take effect before the calling code continues, switch to the main thread first and call it
there. See [Threading](Threading.md).

**Is it harmless?** Yes, as far as correctness of the package goes: nothing ran on the wrong thread
and nothing was lost. The stop is late by up to one frame, and the code after the call must not
assume that the work has already ended.

---

## JANITOR112

**Zenject: a service received a lifetime from an outer context, because its SceneContext or
GameObjectContext never called `LifetimeInstaller.Install`.**
Shown in: Console and Window, once per scene for a SceneContext and once per GameObject name for a
GameObjectContext. Requires the Zenject integration.

```text
[JANITOR112] 'OfferService' in the scene 'Main' received a lifetime from 'App' instead of the scene lifetime, because its SceneContext never called LifetimeInstaller.Install(Container). Work registered on it would outlive the scene. Call LifetimeInstaller.Install(Container) from an installer of that SceneContext. (see Troubleshooting#janitor112)
```

```text
[JANITOR112] 'InventoryService' in the GameObjectContext 'PlayerFacade' received the lifetime 'Main' of an outer context, because that GameObjectContext never called LifetimeInstaller.Install(Container). Its plain services live as long as that lifetime (usually the scene), not as long as the GameObjectContext. Call LifetimeInstaller.Install(Container) from an installer of that GameObjectContext. (see Troubleshooting#janitor112)
```

**What it means.** A plain class asked for a `Lifetime` and got a child of the wrong parent: a
scene's service a child of `Lifetime.App`, or a GameObjectContext's service a child of the scene
lifetime. Everything it registers on that lifetime survives the scene, or the GameObjectContext, it
belongs to.

**What triggers it.** `LifetimeInstaller.Install(Container)` was called in an outer context but not
in this context's own installer. Zenject then answers the request from the outer binding.

- **A SceneContext without `Install`,** while the ProjectContext has it. Reported once per scene;
  the message names the first service that was affected.
- **A GameObjectContext without `Install`,** while the scene (or the project) has it. Reported once
  per GameObject name, so a prefab that is spawned many times is reported once.

**How to fix it.** Call `LifetimeInstaller.Install(Container)` in an installer of every
SceneContext, and of every GameObjectContext whose plain services inject a `Lifetime`:

<!-- source: Samples~/ZenjectUsage/GameplayInstaller.cs -->
```csharp
LifetimeInstaller.Install(Container);
```

**Is it harmless?** No. Every plain service of that context keeps its work alive after the context
is gone. A `MonoBehaviour` that injects `Lifetime` is not affected; it always receives its own
component lifetime. See [Zenject](Zenject.md#install-in-every-context).

---

## JANITOR113

**Parent mismatch: `GetLifetime(parent)` or `GetActiveLifetime(parent)` was ignored, or the parent
was disposed.**
Shown in: Console and Window.

```text
[JANITOR113] GetLifetime(parent) or GetActiveLifetime(parent) was called for 'ShopPopup' after its lifetime already existed under 'Main'. The existing parent is kept and 'Popups' is ignored. Call it before anything else touches this object's lifetime, usually the first line of Awake or OnEnable. (see Troubleshooting#janitor113)
```

**What it means.** You asked for an object's lifetime to be placed in a category, and it was not.
The object is not part of that category, so `category.Cancel()` does not reach it.

**What triggers it.** One of three things.

1. **The lifetime already existed under another parent.** The first access to an object's lifetime
   creates it and decides its parent; the parent is never changed afterwards. The existing lifetime
   is returned unchanged. Everything below counts as a first access to the **component** lifetime:
   `this.GetLifetime()`, `this.Run(...)`, `this.After(...)`, `this.Every(...)`,
   `this.OnCancel(...)`, `tween.AddTo(this)`, `disposable.AddTo(this)`, any
   `Subscribe(handler, this)`, and a Zenject `[Inject] Lifetime` field. The **active** lifetime is
   a separate lifetime with its own first access: the first `GetActiveLifetime(...)` call.
2. **The parent passed in was already disposed.** The lifetime is then created under the scene
   lifetime. The message for this case says so and names the disposed parent.
3. **The lifetime was placed in a category, and that category was disposed.** The lifetime was
   moved back under its scene at that moment ([JANITOR114](#janitor114)), and a lifetime that was
   moved this way cannot be placed again. A later call that names a category is ignored. This
   case is reported once per lifetime, with its own message:

   ```text
   [JANITOR113] The lifetime of 'ShopPopup' was moved under 'Main' when its category was disposed, and a re-homed lifetime cannot be placed again. The request to place it under 'Popups' is ignored; the object keeps working under its scene. (see Troubleshooting#janitor113)
   ```

A wrong order looks like this:

<!-- illustrative: before -->
```csharp
public sealed class ShopPopup : MonoBehaviour
{
    [SerializeField] private Button _buyButton;
    [Inject] private GameplayLifetimes _lifetimes;
    [Inject] private OfferService _offers;

    private void OnEnable()
    {
        // First access: the active lifetime is created here, under the scene lifetime.
        _buyButton.onClick.Subscribe(OnBuyClicked, this.GetActiveLifetime());

        // Too late: JANITOR113. The popup is not in Popups.
        var shown = this.GetActiveLifetime(_lifetimes.Popups);
        shown.Run(LoadOffersAsync);
    }

    private async UniTask LoadOffersAsync(CancellationToken ct)
    {
        var offers = await _offers.FetchAsync(ct);
        _offers.Show(offers);
    }

    private void OnBuyClicked() => _offers.BuySelected();
}
```

**How to fix it.** Make the call with the parent the first thing that touches that lifetime: the
first line of `OnEnable` for an active lifetime (as in the `ShopPopup` sample shown under
[JANITOR105](#janitor105)), the first line of `Awake` for a component lifetime. Calling it again
later with the **same** parent is fine and raises nothing. For the third case, do not dispose a
category that objects are placed in while those objects live: `Cancel()` stops their work and keeps
the category.

**Is it harmless?** No. The object works, but it is in the wrong place in the tree: the category's
`Cancel()` does not stop it. See [Organizing work](Organizing-Work.md).

---

## JANITOR114

**Re-homed: a category was disposed while an object lifetime placed in it lives.**
Shown in: Window, marked as information. One row per lifetime that was moved.

**What it means.** An object had placed its lifetime in a category with `GetLifetime(category)` or
`GetActiveLifetime(category)`, and that category was disposed while the object is still alive. The
package did not dispose the object's lifetime. It cancelled it, so the work registered on it
stopped, and moved it back under its scene lifetime (under `Lifetime.App` for a `DontDestroyOnLoad`
object).

**What triggers it.** `Dispose()` on a category, or on a parent of a category, that still has object
lifetimes placed in it.

**Why it is not disposed.** A category groups cancellation; it does not own the objects in it. An
object whose own lifetime was disposed would silently stop working: every later `this.Run(...)`
would be terminated at once. So the object keeps a working lifetime.

**How to fix it.** Nothing needs fixing. Two things to know:

- If you meant "stop the work of everything in this category", `Cancel()` does that and keeps the
  category.
- After the move the object is in no category, and it cannot be placed again. A later
  `GetLifetime(category)` on it is ignored and reported, once, as [JANITOR113](#janitor113).

**Is it harmless?** Yes. It is recorded so that a row moving in the tree has an explanation.

---

## JANITOR115

**`OwnedEvent` re-entrancy depth exceeded 64; the invoke was skipped.**
Shown in: every build, as an error routed to `LifetimeErrors.Handler`; and in the Window.

**What it means.** An `OwnedEvent` was invoked from inside its own handlers, 64 calls deep, and the
next nested `Invoke` was refused: it returned without calling any handler. With the default error
handler you see an exception in the console:

```text
InvalidOperationException: [JANITOR115] Re-entrancy depth exceeded 64 on OwnedEvent<Int32> "CoinsChanged"; the invoke was skipped. (see Troubleshooting#janitor115)
```

**What triggers it.** Event ping-pong. A handler of event A invokes event B, a handler of B invokes
A again, and nothing ends the cycle. A handler that invokes its own event without a stop condition
does the same. Nested invokes are allowed and safe; only the 65th in a row is refused.

**How to fix it.** Find the cycle and break it: compare the new value with the old one before
invoking, or do not raise an event from the handler of the event it answers.

**Is it harmless?** No. Without the limit this would be a stack overflow, which ends the process.
The limit turns it into a logged error and one skipped invoke, but the cycle is a logic bug. This is
the only diagnostic that is active in release builds.

---

## JANITOR116

**Work registered in `OnEnable` on a lifetime that does not follow activation was registered
again.**
Shown in: Window, once per lifetime and call-site line.

```text
Work registered by OnEnable:9 on the lifetime 'CoinSpin' was registered again while the earlier registration from the same line is still live. This lifetime does not follow activation, so every time the object is enabled again the work is added once more. Register it on GetActiveLifetime() instead; that lifetime is cancelled when the GameObject is deactivated.
```

**What it means.** Work started in `OnEnable` (a `Run`, `After` or `Every`, a tween, an `OnCancel`
action, a disposable) was registered on the **component** lifetime or the **GameObject** lifetime
of the object. Those lifetimes end when the object is destroyed, not when it is deactivated. So
every time the object is enabled again, `OnEnable` adds the same work once more, and the earlier
registration is still live. On a pooled object, which is enabled and disabled for its whole life,
the work grows with every spawn and never stops growing.

A coroutine behaves differently. Unity itself stops a coroutine when the GameObject of its host is
deactivated, so a coroutine hosted on the object is no longer live when `OnEnable` runs again.
Starting it again there is not a repeat: nothing doubles and nothing is reported. A coroutine
whose host is another object that stayed active is still running, and is reported like the rest.

**What triggers it.** A registration whose call site is `OnEnable`, on a component or GameObject
lifetime that already holds a live entry registered from the same line in an earlier frame. A
component that is enabled once never raises it.

<!-- illustrative: before -->
```csharp
public sealed class CoinSpin : MonoBehaviour
{
    private void OnEnable()
    {
        // The component lifetime survives SetActive(false): the second spawn starts a second timer.
        this.Every(1f, Pulse);
    }

    private void Pulse() => transform.Rotate(0f, 0f, 45f);
}
```

The first spawn starts one timer. `SetActive(false)` stops nothing, because the timer belongs to the
component lifetime. The second spawn starts a second timer from the same line, and the coin now
turns twice per second; this is the moment the warning is recorded.

What is not reported:

- A loop inside one `OnEnable` that registers several times from one line, and a disable and enable
  within one frame. The check compares frames, so these look like one call.
- A tween that has finished or was killed, a coroutine that has ended or that Unity stopped when
  its host was deactivated, and a timer that has fired. Only work that is still live counts.
- Registrations from any other method, and registrations on an active lifetime, an area or a scene
  lifetime.
- A call made through a helper method: the call site is then the helper, not `OnEnable`.
- A repeated `OwnedEvent`, UnityEvent, SignalBus or paired subscription that the duplicate check
  recognises. It is ignored before it is added and reported as [JANITOR105](#janitor105) instead.
- **Work on the active lifetime that is registered again after a component was disabled and
  enabled.** `enabled = false` followed by `enabled = true`, while the GameObject stays active,
  runs `OnEnable` a second time. The active lifetime follows the GameObject, so it was not
  cancelled in between, and a `Run`, a timer or a tween registered in that `OnEnable` is now there
  twice in the same generation. This warning watches component and GameObject lifetimes only, so
  it stays silent; only a repeated subscription is caught, by JANITOR105. Keep such work in an
  area of its own under the active lifetime and cancel that area at the top of `OnEnable`, before
  registering; if the work must also stop while the component is disabled, cancel the area in
  `OnDisable` too. See
  [Components and scenes](Components-and-Scenes.md#what-setactivefalse-stops).

A lambda written in `OnEnable` reports `OnEnable` as its call site too, because the compiler names
the enclosing method. If that lambda registers work on purpose every time it runs, for example in a
click handler, the warning can be ignored.

**How to fix it.** Register the work on the active lifetime. It is cancelled on every deactivation,
so each enable starts from an empty lifetime:

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

Work that should last for the whole life of the object, such as a subscription to a source that
never goes away, belongs in `Awake`, where it is registered once on the component lifetime. See
[Pooling](Pooling.md).

**Is it harmless?** No. The earlier registrations keep running after the object was deactivated, so
timers fire twice and a task starts a second copy of itself. The warning is raised on the second
activation, before the pile has grown. It exists in the editor only; in a player nothing reports
this mistake.

---

## My listener stopped after `Cancel()`

**Symptom:** a button, an event or a signal no longer calls its handler. Nothing was destroyed. The
last thing that happened was a `Cancel()`.

**Cause:** `Cancel()` ends **everything** registered in the current generation of a lifetime, and a
subscription is an item like any other. There is one rule, with no exceptions: after `Cancel()`
returns, the lifetime is empty and usable.

<!-- source: Samples~/DOTweenUsage/TutorialController.cs -->
```csharp
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

The skip listener is subscribed in `Awake` with `this` as its owner, so it lives on the component
lifetime. `SkipStep()` cancels only the `_step` area: the arrow, the pulse and the target listener
stop, and the skip button keeps working. `Abort()` cancels the component lifetime itself, which
contains the skip listener and the `_step` area. After `Abort()` the skip button does nothing, and
nothing subscribes it again because `Awake` does not run twice.

**Fix:** keep long-lived listening on the owner's lifetime and put activity in a child area; cancel
the area. If you do cancel the owner's lifetime, subscribe again afterwards.

The same thing happens one step removed when a **category** is cancelled. `Popups.Cancel()` reaches
every popup placed in `Popups` and removes its subscriptions too. The popup stays on screen and
stops reacting until it is enabled again. And `Lifetime.App.Cancel()` stops listening everywhere,
which is why the everyday "stop everything" button should be a root area of your own, not `App`.
See [Stopping work](Stopping-Work.md).

## `GetLifetime(parent)` was ignored

**Symptom:** an object was placed in a category, but cancelling the category does not stop it. In
the editor a JANITOR113 warning was logged.

**Cause and fix:** something used the object's lifetime before the call that names the parent. See
[JANITOR113](#janitor113).

## CS0104: `Lifetime` is ambiguous

**Symptom:**

```text
error CS0104: 'Lifetime' is an ambiguous reference between 'Ecanakli.Janitor.Lifetime' and 'VContainer.Lifetime'
```

**Cause:** another package in the project also defines a type named `Lifetime`, and the file imports
both namespaces. VContainer is the common case: its `Lifetime` is the enum used in registrations
(`Lifetime.Singleton`).

**Fix:** tell the compiler which one the short name means in that file. Add the alias
`using Lifetime = Ecanakli.Janitor.Lifetime;` below the other `using` lines, and write the other
type with its namespace (`VContainer.Lifetime.Singleton`). Or do it the other way round in files
that mostly talk to the other package. Only files that import both namespaces and use the short name
are affected.

## CS0029 on a paired `Subscribe`

**Symptom:** a paired `Subscribe(add, remove, handler)` call for an event that carries an argument
does not compile. The compiler reports the same error twice, once on the add lambda and once on the
remove lambda:

```text
error CS0029: Cannot implicitly convert type 'System.Action' to 'System.Action<string>'
```

**Cause:** the type argument is missing. C# 9 cannot infer it from a lambda or from a method group,
which is what these calls are made of. Without it the compiler binds the form for a plain `Action`
event, takes the lambda parameter for an `Action`, and rejects `event += h`. The message points at
the two lambdas, not at the missing argument. Two families of methods need the type argument:

- the paired `Subscribe(add, remove, handler)` for any event that carries arguments, or that uses
  its own delegate type;
- `signalBus.Subscribe<TSignal>(handler, owner)`.

**Fix:** write the type argument.

<!-- source: Samples~/BasicUsage/PlatformHooks.cs -->
```csharp
// Custom delegate type: explicit type argument; static lambdas allocate nothing.
this.Subscribe<Application.LowMemoryCallback>(
    static h => Application.lowMemory += h,
    static h => Application.lowMemory -= h,
    OnLowMemory);

// Action<T> event: C# 9 cannot infer T from lambdas or method groups, so write <string>.
this.Subscribe<string>(h => _ads.RewardGranted += h, h => _ads.RewardGranted -= h, OnRewardGranted);

// Plain Action event: no type argument needed.
this.Subscribe(h => _ads.Closed += h, h => _ads.Closed -= h, OnAdClosed);
```

For an `Action<T>` event the argument is `T`. For an event with its own delegate type the argument
is that delegate type. A plain `Action` event needs none. `OwnedEvent` and UnityEvent subscriptions
never need one. See [Events](Events.md).

## Another library also has an `AddTo` for disposables

**Symptom:** a file imports this package and another library whose `AddTo` extension takes an
`IDisposable` and a `Component` or a `GameObject` (R3 and UniRx each have one). A disposable passed
to `disposable.AddTo(this)` is not listed in the Janitor window and is not disposed by a `Cancel()`
of the lifetime. Usually nothing is reported at compile time.

**Cause:** the compiler prefers the other library's extension. This package's `AddTo` ends in two
optional caller-information parameters, and an extension that needs no default arguments is the
better match, so the call binds to the other library without an error. The disposable is then owned
by that library and follows its rules. Only when the other extension has the same optional
parameters is the call ambiguous, and the compiler says so:

```text
error CS0121: The call is ambiguous between the following methods or properties: 'Ecanakli.Janitor.LifetimeSubscriptionExtensions.AddTo<T>(T, UnityEngine.Component, string, int)' and '...AddTo<T>(T, UnityEngine.Component, string, int)'
```

**Fix:** pass a lifetime instead of the component. Only this package has an `AddTo` that takes a
`Lifetime`, so the call can bind nowhere else:

<!-- source: Samples~/BasicUsage/ModalBlocker.cs -->
```csharp
private void OnEnable()
{
    var shown = this.GetActiveLifetime();
    _gate.Block().AddTo(shown);         // an IDisposable owned by the lifetime: disposed when it ends
}
```

Here the block is disposed when the object is deactivated or destroyed. For the lifetime that
`AddTo(this)` would have used, pass `this.GetLifetime()`. Tweens are not affected:
`tween.AddTo(this)` has only one candidate, because a tween is not an `IDisposable`.

## A tween was not killed

**Symptom:** a lifetime was cancelled, or its object was destroyed or deactivated, and a tween kept
playing.

Work through these:

1. **Is the tween inside a Sequence?** DOTween ignores a kill on a nested tween. Register the root
   Sequence instead. In the editor this case is reported as [JANITOR102](#janitor102).
2. **Was it registered on the lifetime you cancelled?** `tween.AddTo(this)` uses the component
   lifetime, which ends on destroy, not on `SetActive(false)`. A tween that should stop when the
   object is deactivated is registered on `this.GetActiveLifetime()`. When the call sits in
   `OnEnable`, the editor reports the second activation as [JANITOR116](#janitor116).
3. **Was it registered at all?** A tween without `AddTo` is unknown to the package. The Janitor
   window shows the number of tweens each lifetime holds.
4. **Is the object in the category you cancelled?** If a JANITOR113 was logged for it, it is not.

See [DOTween](DOTween.md).

## Work keeps running after a scene change

**Symptom:** after loading another scene, a task, a tween or a handler from the old scene still
runs, or throws `MissingReferenceException`.

Work through these:

1. **Was it started through a lifetime?** The package only knows work that was started through it.
   An `async void` method, a `Forget()` call, a raw `+=` or a tween without `AddTo` is invisible to
   it. [Migration](Migration.md) has the replacements.
2. **Does every await in it take the token?** If not, the token is cancelled and the method keeps
   going. In the editor: [JANITOR101](#janitor101).
3. **Is its owner supposed to survive?** Work on `Lifetime.App`, on a child of it, or on a
   `DontDestroyOnLoad` object is not touched by a scene change. That is by design, and it is the
   right home for a scene loader, but the wrong one for a scene's popup.
4. **Zenject: did the scene's installer call `LifetimeInstaller.Install(Container)`?** If not, its
   services hold lifetimes that belong to the application. The same goes for a GameObjectContext
   without its own call: its services live as long as the scene. In the editor:
   [JANITOR112](#janitor112).
5. **Did it stop, but late?** Without `SceneLifetimes.DisposeAll()` before the load, work stops as
   objects are destroyed, in Unity's order, and one object's work can run against another object
   that is already gone. In the editor: [JANITOR104](#janitor104).

## `Token` is different after `Cancel`

**Symptom:** a `CancellationToken` taken from a lifetime is cancelled although the lifetime is still
in use; or two reads of `lifetime.Token` return different tokens.

**Cause:** this is how a reusable lifetime works. A token belongs to one generation. `Cancel()`
cancels the token of the current generation, for good, and opens a new generation with a new token.
The old token never becomes usable again, which is what makes work from before the cancel stop
reliably.

So this goes wrong: read `lifetime.Token` once, keep it in a field, and pass that field to work
started after a later `Cancel()`. The work receives a token that is already cancelled and ends at
its first await.

**Fix:** do not keep the token. Read `lifetime.Token` at the moment you start the work, or start
the work with `Run`, which hands the current token to your method. While a lifetime is ending or
disposed, `Token` returns a token that is already cancelled. The same is true for an active
lifetime, and for an area created under one, while its GameObject is inactive. See
[Concepts](Concepts.md).

## `AddTo` for tweens or `LifetimeInstaller` does not exist

**Symptom:** `CS1061` or `CS0246` for `tween.AddTo(...)`, `AwaitCompletionAsync`, `TweenCancelMode`
or `LifetimeInstaller`, although the package is installed.

**Cause:** the optional assembly is not being compiled. The DOTween integration needs the `DOTWEEN`
define (or a DOTween package), the Zenject integration needs `ECANAKLI_JANITOR_DI_ZENJECT`.

**Fix:** see "How the integration turns on" in [DOTween](DOTween.md#how-the-integration-turns-on)
and [Zenject](Zenject.md#how-the-integration-turns-on). If your code is in an assembly definition,
also reference the integration assembly there.

## Scripts were recompiled during Play Mode

**Symptom:** a script was changed while the game was running in the editor. After the recompile,
work that was started earlier no longer runs: timers do not fire, handlers are not called, tasks
never continue. The console shows this warning once:

```text
Janitor: a script reload during Play Mode discarded every lifetime and the work they owned. A new lifetime tree was started. Restart Play Mode for a clean state.
```

**Cause:** to recompile during Play Mode the editor reloads all script code, and that reload throws
away every static and every non-serialized value. The lifetime tree is one of them, and with it
goes everything registered on it. Unity does not start the package again after such a reload, so
the package does it at the first call that needs the tree: it starts a new session with a new,
empty tree and logs the warning above.

From that call on the package works normally, and work registered afterwards is owned and stopped
as usual. What is lost stays lost:

- Work registered before the reload is not restored. The reload discarded it together with the
  tree.
- Lifetimes that your own classes kept in fields (areas, categories) are gone. A `Lifetime` cannot
  be carried across the reload, so such a field is empty afterwards.
- `LifetimeErrors.Handler` is the default again, and the Warnings tab of the Janitor window starts
  empty.

This exists in the editor only. A player never reloads its scripts.

**Fix:** stop Play Mode and start it again. To avoid the situation, set the editor preference
`Script Changes While Playing` to `Recompile After Finished Playing` or
`Stop Playing And Recompile`.

If the first call after the reload comes from a thread other than the main thread, the package
cannot start a session there and the call throws the exception of the next section.

## "Available in Play Mode only"

**Symptom:**

```text
InvalidOperationException: Janitor: Lifetime.App is available in Play Mode only. No lifetime tree exists.
```

**Cause:** `Lifetime.App`, `SceneLifetimes`, `GetLifetime()` or `GetActiveLifetime()` was called
while no lifetime tree exists. Lifetimes exist only while the game runs:

- The call comes from an editor script, from `OnValidate`, or from an Edit Mode test.
- The call comes from code that outlived Play Mode in the editor, such as a delayed editor callback
  or an async method that continues after the stop. The tree is cleared when the editor is back in
  Edit Mode.
- Scripts were recompiled during Play Mode and the first call afterwards came from another thread.
  See [the section above](#scripts-were-recompiled-during-play-mode).

While Play Mode or the application is shutting down, the calls do not throw. They return lifetimes
that have already ended, so work registered at that point does not start.

**Fix:** guard editor-time code with `Application.isPlaying`, and exercise lifetime code from Play
Mode tests.

## Reading an error the package routed

When work owned by a lifetime throws (a task, a timer, a handler, a cancel action), the package
catches the exception and passes it to `LifetimeErrors.Handler`. The default handler logs it wrapped
in a `LifetimeWorkException` that names the kind of work, the lifetime, and the place where the
work was registered:

```text
Janitor Task failed in lifetime 'ShopPopup' (OnEnable:45): Object reference not set to an instance of an object
```

Unity prints the innermost exception first. In the console you therefore see your own exception at
the top, and the line above appears further down as `Rethrow as LifetimeWorkException`. The log
entry carries the owner object as its context when the lifetime has one. A cancellation
(`OperationCanceledException`) is never routed.

To send these errors to your own reporting as well, replace the handler:

<!-- source: Samples~/BasicUsage/ErrorReporting.cs -->
```csharp
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

1. **`Install` runs at startup.** It keeps the handler that was there, which is the default one,
   and puts its own in place. The package restores the default handler at the start of every play
   session, so the call belongs in startup code that runs each session. A second call in the same
   session only replaces the reporter.
2. **Work owned by a lifetime throws.** The package catches the exception and calls `Forward` with
   it and with a context: `Source` says what kind of work failed (`Task`, `Timer`, `CancelAction`,
   `Coroutine`, `TokenCallback` or `EventHandler`), `Owner` is the Unity object behind the lifetime
   or null, and `LifetimeName`, `Member` and `Line` say which lifetime and which registration.
3. **`Forward` reports it and then calls the default handler,** so the console entry above still
   appears.
4. **If a handler itself throws,** the package catches that too and logs both exceptions. An error
   handler can never break a `Cancel()` or a `Dispose()`.

See [Tasks and errors](Tasks-and-Errors.md).

## See also

- [Diagnostics](Diagnostics.md)
- [Concepts](Concepts.md)
- [Stopping work](Stopping-Work.md)
- [Threading](Threading.md)
- [Limitations](Limitations.md)
- [FAQ](FAQ.md)
