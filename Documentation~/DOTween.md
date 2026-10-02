# DOTween

[Back to index](index.md)

This page covers the optional DOTween integration: binding a tween to a lifetime with `AddTo`,
choosing between `Kill` and `Complete`, awaiting a tween with `AwaitCompletionAsync`, why only the
root `Sequence` is registered, and why a registered tween is taken out of DOTween's recycling.

The core ideas (lifetimes, generations, `Cancel` versus `Dispose`) are explained in
[Concepts](Concepts.md). This page assumes them.

---

## How the integration turns on

The DOTween support is a separate assembly, `Ecanakli.Janitor.DOTween`. Unity compiles it only when
one of two scripting defines exists:

- `DOTWEEN`. A DOTween install under `Assets/` adds this define from DOTween's own setup panel.
- `ECANAKLI_JANITOR_DOTWEEN`. The assembly sets it for itself, through `versionDefines`, when a
  package named `com.demigiant.dotween` is installed. DOTween publishes no UPM package itself;
  this is for a project that wraps DOTween (`DOTween.dll` and its Modules) in a package of its own
  under that name.

There is nothing to toggle by hand. Without DOTween the assembly is not compiled, the types on this
page do not exist, and the rest of the package works unchanged.

If DOTween is deleted from `Assets/` while `DOTWEEN` is still set, the integration assembly is still
compiled and fails, because `DOTween.dll` is gone. Delete `DOTWEEN` from the scripting define
symbols of every build target.

Three more facts that save a search:

- The types live in the namespace `Ecanakli.Janitor`, the same as the core.
  `using Ecanakli.Janitor;` is all a file needs.
- If your code sits in an assembly definition, add `Ecanakli.Janitor` and `Ecanakli.Janitor.DOTween`
  to its references. A definition that overrides its references also lists `DOTween.dll`, as the
  DOTween sample's definition does.
- The assembly references the precompiled `DOTween.dll` only. It does not need DOTween's module
  scripts, and it never calls them.

## The API

<!-- signature -->
```csharp
public enum TweenCancelMode { Kill, Complete }

public static class LifetimeTweenExtensions
{
    public static T AddTo<T>(this T tween, Lifetime owner, TweenCancelMode onCancel = TweenCancelMode.Kill) where T : Tween;
    public static T AddTo<T>(this T tween, Component owner, TweenCancelMode onCancel = TweenCancelMode.Kill) where T : Tween;
    public static T AddTo<T>(this T tween, GameObject owner, TweenCancelMode onCancel = TweenCancelMode.Kill) where T : Tween;

    public static UniTask AwaitCompletionAsync(this Tween tween, Lifetime lifetime);
    public static UniTask AwaitCompletionAsync(this Tween tween, CancellationToken token);
}
```

All five methods must be called on the main thread. Off the main thread they throw
`InvalidOperationException` and leave the tween untouched.

---

## A tween that ends with its owner

### Before

A pooled coin flies to a target and returns itself to the pool. The cleanup is written by hand:

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

Three things can go wrong here.

- `OnDisable` has to mirror `OnEnable` line by line. Leave out `_fly?.Kill()` and the tween keeps
  moving a coin that is back in the pool.
- The coin goes back to the pool only when the delay runs to its end. If anything else deactivates
  the coin, the delay is cancelled and the pool never gets the coin back.
- `_fly?.Kill()` itself is the subtle one. The flight has usually finished by the time the pool
  deactivates the coin, so DOTween has already killed the tween. With DOTween recycling turned on,
  that tween object may by now be driving a different coin, and this line kills that one. See
  [Why a registered tween is not recyclable](#why-a-registered-tween-is-not-recyclable).

### After

<!-- source: Samples~/DOTweenUsage/CoinPickup.cs -->
```csharp
using DG.Tweening;
using Ecanakli.Janitor;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.DOTweenUsage
{
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
}
```

There is no tween field, no `OnDisable` and no `Kill` call. The class has one place that returns
the coin, and every way the flight can end leads to it. What happens, moment by moment:

1. **`Launch` activates the coin and `OnEnable` runs.** `this.GetActiveLifetime()` returns the
   lifetime that is cancelled every time this GameObject is deactivated
   ([Pooling](Pooling.md) explains it).
2. **`OnCancel` registers the return to the pool.** It is the first entry of this generation.
   Entries end newest first, so it runs last, after the tween and the timer are gone.
3. **`AddTo(spawn)` registers the tween.** It marks the tween as non-recyclable, stores one entry in
   the current generation of `spawn` ("kill this tween when the generation ends"), and returns the
   same tween. It does not touch the tween's `OnKill` or `OnComplete` callbacks.
4. **`After` registers the timer.** Its callback only deactivates the coin. It does not return it.
5. **The coin lands.** The tween completes and DOTween kills it, as it does with any auto-kill
   tween. Nothing in the package runs at that moment. The entry now points at an inactive tween and
   is dropped later, without doing anything.
6. **The timer fires and calls `SetActive(false)`.** The active lifetime is cancelled inside that
   call. The tween entry finds the tween inactive and skips it. (If the timer fires a frame before
   the tween finishes, the entry kills the tween instead.) Then the `OnCancel` entry runs and hands
   the coin to the pool. The sample pool sees that the coin is already inactive and only stores it.
7. **Something else deactivates the coin in mid-flight.** The same cancel runs, but now the tween is
   alive: it is killed immediately, inside `SetActive(false)`. No completion callback runs, the
   coin stays where it was, the timer never fires, and the coin is returned to the pool.
8. **The lifetime is cancelled from outside while the coin is active**, for example through the
   Janitor window. The tween is killed, the timer is dropped, and the `OnCancel` entry hands the
   coin to the pool, which deactivates it.
9. **The next `Launch`** starts a fresh generation with a new return entry, a new tween and a new
   timer. Nothing from the previous spawn can reach it.
10. **`Destroy(coin)`** ends the active lifetime as well: Unity disables the object's components
    before it destroys them, and that cancels the lifetime as in step 7. A tween that was still
    running is dead before the coin's `OnDestroy` runs. The return entry runs here too, which is why
    the sample pool skips a destroyed coin when it rents.
11. **`SceneLifetimes.DisposeAll()`** before a scene load kills the tween while the coin still
    exists, before Unity destroys anything. See [Components and scenes](Components-and-Scenes.md).

---

## Choosing the owner

`AddTo` has three owner forms. They differ only in which lifetime receives the entry.

| Call | Lifetime used | The tween ends when |
|---|---|---|
| `tween.AddTo(lifetime)` | That lifetime: an area, a category, an active lifetime, a scene lifetime | Its current generation ends: `Cancel`, `Dispose`, or its owner going away |
| `tween.AddTo(this)` from a `MonoBehaviour` | The component lifetime, `this.GetLifetime()` | The component is destroyed, its scene is disposed, or the lifetime is cancelled |
| `tween.AddTo(transform)`, or any component that is not a `MonoBehaviour` | The lifetime of its GameObject | The GameObject is destroyed (destroying one component does not end it) |
| `tween.AddTo(gameObject)` | The lifetime of the GameObject | The same |

Two consequences worth knowing:

- `tween.AddTo(this)` does **not** follow `SetActive(false)`. A component lifetime only ends on
  destroy. For anything started in `OnEnable`, register on `this.GetActiveLifetime()`, as the coin
  does. The `ToastView` class [further down](#the-two-overloads) uses `AddTo(this)` for a loop that
  should run for as long as the component exists.
- `tween.AddTo(this)` creates the component lifetime if it does not exist yet. That counts as the
  first access, which decides the lifetime's parent. If the object should join a category, call
  `this.GetLifetime(category)` first. See [Organizing work](Organizing-Work.md).

A category works like any other lifetime. One class registers tweens into it, another stops them,
and neither knows the other:

<!-- source: Samples~/DOTweenUsage/DamageNumbers.cs -->
```csharp
public sealed class DamageNumbers
{
    private readonly GameplayLifetimes _lifetimes;

    public DamageNumbers(GameplayLifetimes lifetimes) => _lifetimes = lifetimes;

    public void Pop(Transform label) => label.DOPunchScale(Vector3.one * 0.3f, 0.2f).AddTo(_lifetimes.Combat);
}
```

<!-- source: Samples~/DOTweenUsage/CombatFlow.cs -->
```csharp
public sealed class CombatFlow
{
    private readonly GameplayLifetimes _lifetimes;

    public CombatFlow(GameplayLifetimes lifetimes) => _lifetimes = lifetimes;

    public void OnCombatInterrupted() => _lifetimes.Combat.Cancel();
}
```

`OnCombatInterrupted()` kills every punch that is still running, in the middle of the punch. The
labels keep the scale they had reached, because the default mode is `Kill`.

### What `AddTo` does in the edge cases

- **The owner has already ended** (it is being cancelled, it is disposed, it is a destroyed
  component or GameObject, or it is an active lifetime, or an area under one, whose GameObject is
  inactive): the tween is killed at once and nothing is registered. This is true in both modes; see
  below.
- **The owner is `null`:** the tween is killed first, then `ArgumentNullException` is thrown. A
  failed registration never leaves a tween running without an owner.
- **The tween is already inactive:** nothing is registered and nothing is killed.
- **The tween is `null`:** `ArgumentNullException`.

### The tween is its own handle

`AddTo` returns the tween it was given, with its static type (a `Sequence` stays a `Sequence`), so
it can sit at the end of a fluent chain. There is no `LifetimeRegistration` for a tween. To stop one
tween early, keep the tween and call `tween.Kill()`. That is safe at any time: when the lifetime
ends later it finds the tween inactive and does nothing.

Register a given tween once. Each `AddTo` call adds one entry to the lifetime, and an entry is
dropped only when its tween is inactive. A tween that is kept alive and replayed (auto-kill off) and
registered again on every replay adds one entry per call until the generation ends.

### `AddTo` and `SetLink`

`AddTo` neither uses nor replaces `SetLink`; the two are independent and can be combined. They
differ in timing. `AddTo` kills synchronously, inside the cancel or the destroy that ended the
owner. `SetLink` kills on a later DOTween update: on Unity 6000.3 a linked tween is still active in
the frame of `Destroy` and dead one frame later. Keep `SetLink` for its pause and restart link
behaviours; use `AddTo` for cleanup.

---

## `Kill` versus `Complete`

The second argument of `AddTo` decides what happens to a tween that is still running when its
generation ends.

<!-- source: Samples~/DOTweenUsage/CoinCounter.cs -->
```csharp
public sealed class CoinCounter : MonoBehaviour
{
    private TMP_Text _label;
    private Lifetime _roll;
    private int _shown;

    // The view arrives here because the demo builds its UI in code.
    public void Initialize(TMP_Text label) => _label = label;

    private void Awake() => _roll = this.GetLifetime().CreateChild("Roll");

    public void RollTo(int target)
    {
        _roll.Cancel();   // Complete mode: a roll still running jumps to its end value instead of freezing midway
        DOTween.To(() => _shown, SetShown, target, 0.5f).AddTo(_roll, TweenCancelMode.Complete);
    }

    private void SetShown(int value)
    {
        _shown = value;
        _label.text = value.ToString();
    }
}
```

Call `RollTo(200)`, and 0.2 seconds later `RollTo(400)`:

1. **`RollTo(200)`.** `_roll.Cancel()` finds nothing to stop. A tween from 0 to 200 starts and is
   registered in `Complete` mode.
2. **`RollTo(400)`, while the first roll shows some value between 0 and 200.** `_roll.Cancel()` ends
   the generation. The entry calls `tween.Kill(true)`: DOTween writes the end value, so
   `SetShown(200)` runs, then the tween's completion callbacks, then its kill callback. The label
   jumps to 200.
3. **The new tween** reads `_shown`, which is now 200, and rolls from 200 to 400.

With the default `Kill` mode step 2 would leave the label on the value it had reached, and the
second roll would start from there.

| | `Kill` (default) | `Complete` |
|---|---|---|
| DOTween call | `tween.Kill(false)` | `tween.Kill(true)` |
| Target value | Stays where it is | Lands on the end value |
| `OnComplete` callbacks | Do not run | Run once |
| `OnKill` callbacks | Run once | Run once |

Rules that hold for both modes:

- **`Complete` applies only to a tween that was running for that owner.** A tween registered on an
  owner that had already ended is killed, never completed. Completing it would write to targets and
  fire callbacks for an animation that never played.
- **`Complete` applies to every way a generation ends**, including `Destroy` and the scene call. The
  setter and the completion callbacks then run while the owner is going away. For a component
  lifetime that is after the component's `OnDisable` and before its `OnDestroy` (Unity 6000.3).
  Choose `Complete` only when those callbacks are safe to run at that point; this is why `Kill` is
  the default.
- **Tweens of one lifetime end newest first**, like every other item.

---

## Awaiting a tween

### Before

A service applies an outfit: load, show, play a punch animation, save. A new call restarts it, and
another service can cancel it.

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
```

The line to look at is the tween await. `AttachExternalCancellation(ct)` releases the awaiting
method when the token is cancelled, but nothing kills the tween: the preview keeps punching after
the apply was cancelled. And if nobody calls `Dispose`, a scene change does not cancel anything at
all.

### After

<!-- source: Samples~/DOTweenUsage/CustomizationService.cs -->
```csharp
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

What happens at each moment:

1. **`Apply(red)`.** `_apply.Cancel()` has nothing to stop. `Run` starts `ApplyAsync` with the token
   of the area's current generation.
2. **The look is loaded and shown, and the punch tween is created.** `AwaitCompletionAsync(ct)`
   marks the tween non-recyclable, takes over its `onComplete` and `onKill` callbacks for the
   duration of the await, and registers one callback on `ct`.
3. **The punch finishes.** The method continues inside DOTween's completion callback, in the same
   frame, and saves the selection.
4. **`Apply(blue)` or `CancelApply()` arrives during the punch.** `_apply.Cancel()` cancels the
   token. The token callback kills the tween, the kill ends the await with an
   `OperationCanceledException`, and `ApplyAsync` stops there: the save never runs. `Run` treats a
   cancellation as a normal end and logs nothing. Then, for `Apply(blue)`, a new `Run` starts in the
   fresh generation.
5. **The cancel arrives during the load instead.** `LoadLookAsync` throws the cancellation and the
   tween is never created.
6. **The lifetime given to the constructor ends** (its scene or its context goes away). The area is
   disposed with it, which stops a running apply the same way, for good.

The other service is unchanged and never sees the area:

<!-- source: Samples~/DOTweenUsage/ProfileResetService.cs -->
```csharp
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

### The two overloads

**`AwaitCompletionAsync(token)`** is the one to use inside `Run`, where a token is already at hand:

<!-- source: Samples~/DOTweenUsage/RewardView.cs -->
```csharp
            // Run already ends silently on cancel: no log, no try/catch needed.
            await _chest.DOShakeRotation(0.4f, 15f).AwaitCompletionAsync(ct);
```

The token owns the await. Cancelling it kills the tween and cancels the await. The tween is not
registered on any lifetime; it is reached through the token, and a lifetime cancels all its tokens
before it ends any item. If the token is cancelled from another thread, the kill is moved to the
next main-thread update, because DOTween is not thread safe. This overload costs one token
registration per call.

**`AwaitCompletionAsync(lifetime)`** registers the tween on the lifetime, exactly as
`AddTo(lifetime)` in `Kill` mode would, and then awaits it:

<!-- source: Samples~/DOTweenUsage/ToastView.cs -->
```csharp
public sealed class ToastView : MonoBehaviour
{
    private Transform _panel;
    private Transform _badge;
    private TMP_Text _label;
    private Lifetime _toast;

    // The views arrive here because the demo builds its UI in code; call it before the object is enabled.
    public void Initialize(Transform panel, Transform badge, TMP_Text label)
    {
        _panel = panel;
        _badge = badge;
        _label = label;
    }

    private void Awake()
    {
        _toast = this.GetLifetime().CreateChild("Toast");
        _badge.DOScale(1.2f, 0.6f).SetLoops(-1, LoopType.Yoyo).AddTo(this);   // the component owner: it ends with this component
    }

    public void Show(string message)
    {
        _toast.Cancel();                                   // the previous toast stops
        _toast.Run(ct => ShowAsync(message, ct));
    }

    private async UniTask ShowAsync(string message, CancellationToken ct)
    {
        _label.text = message;
        _panel.localScale = Vector3.zero;

        // The lifetime overload registers the tween on _toast and awaits it: a cancel kills it and throws.
        await _panel.DOScale(1f, 0.25f).SetEase(Ease.OutBack).AwaitCompletionAsync(_toast);
        await UniTask.Delay(TimeSpan.FromSeconds(1.5), cancellationToken: ct);
        await _panel.DOScale(0f, 0.2f).AwaitCompletionAsync(_toast);
    }
}
```

1. **`Awake`.** The badge loop is registered with `AddTo(this)`, the component owner form. It runs
   for as long as the component exists and is killed when the component is destroyed; an endless
   loop ends no other way.
2. **`Show("Saved")`.** `ShowAsync` starts. The scale-in tween is registered on `_toast` and
   awaited. While it plays, the `_toast` row in the Janitor window holds one task and one tween.
3. **The scale-in completes.** The method continues, waits 1.5 seconds on the token, then awaits
   the scale-out the same way.
4. **`Show("Loaded")` arrives during the scale-in.** `_toast.Cancel()` ends the generation: the
   tween entry kills the tween, the kill ends the await with a cancellation, and the first
   `ShowAsync` stops there. The new call then starts in the fresh generation.
5. **`Show` arrives during the 1.5 seconds instead.** No tween is registered at that moment. The
   cancelled token ends the delay, with the same result.
6. **The component is destroyed.** Its lifetime is disposed, and `_toast` with it: the badge loop
   and a toast that is showing both stop.

Inside `Run` either overload works, because the token and the lifetime end together. Outside
`Run`, in a method that has a lifetime but no token, the lifetime overload is the one available.
Once warm, it allocates nothing for a tween that completes; a cancelled await allocates its
exception.

### An awaited tween that restarts and stops on deactivation

`ToastView` and `CustomizationService` keep their area under a lifetime that ends on destroy. When
the awaited tween should also stop the moment the object is deactivated, the area goes under the
**active** lifetime:

<!-- source: Samples~/DOTweenUsage/GateOpener.cs -->
```csharp
public sealed class GateOpener : MonoBehaviour
{
    private Transform _gate;
    private Collider _passage;
    private Lifetime _open;

    // The parts arrive here because the demo builds its objects in code.
    public void Initialize(Transform gate, Collider passage)
    {
        _gate = gate;
        _passage = passage;
    }

    public void Open()
    {
        // An active lifetime accepts no work while its object is inactive.
        if (!gameObject.activeInHierarchy)
        {
            return;
        }

        // An area under the active lifetime: SetActive(false) cancels it, so it is kept and reused.
        _open ??= this.GetActiveLifetime().CreateChild("Open");

        _open.Cancel();                    // a second call kills the first tween before the new one starts
        _open.Run(OpenAsync);
    }

    private async UniTask OpenAsync(CancellationToken ct)
    {
        _passage.enabled = false;
        await _gate.DOLocalMoveY(2f, 0.6f).AwaitCompletionAsync(ct);   // a cancel kills the tween and throws
        _passage.enabled = true;
    }
}
```

What happens at each moment:

1. **The first `Open()` on an active object.** The area `Open` is created under the object's active
   lifetime and kept in the field. `_open.Cancel()` finds nothing to stop. `Run` starts `OpenAsync`:
   the passage collider is switched off, the move tween starts, and the method waits at the
   `await`.
2. **The tween completes.** The method continues in the same frame and switches the collider on.
3. **`Open()` again while the gate is moving.** `_open.Cancel()` cancels the token. The token kills
   the tween, the kill ends the await with a cancellation, and the first `OpenAsync` stops at the
   `await`: its last line never runs. `Run` then starts a second `OpenAsync` with a new tween that
   moves on from where the gate is. There is one tween, not two.
4. **`SetActive(false)` while the gate is moving.** The active lifetime is cancelled, and the area
   under it with it. The tween is killed inside the `SetActive(false)` call and the line after the
   `await` never runs, so the collider stays off: that line is the only place that switches it on.
   The area is cancelled, not disposed. It stays in the field and is used again.
5. **`Open()` while the object is inactive.** The method returns at its first check. Without that
   check `Run` would be refused: an active lifetime, and every area under it, accepts no work
   while its GameObject is inactive, and the refusal is reported as
   [JANITOR108](Troubleshooting.md#janitor108). A public method that another object may call at
   any time checks `activeInHierarchy` first and returns.
6. **`SetActive(true)`, then `Open()`.** The same area starts a fresh generation with a new tween.
7. **`Destroy`.** The active lifetime is disposed and the area with it. A tween that was running is
   killed before the object's `OnDestroy`.

Two details of the class are deliberate:

- **The area is created in `Open()`, not in `Awake`.** `Awake` does not run before an object is
  activated for the first time. If `Open()` is called on an object that was never active, a field
  assigned in `Awake` is still empty. `_open ??= ...` creates the area at the first call that needs
  it. Creating the area is allowed at any time, also while the object is inactive, and logs
  nothing; only registering work is refused.
- **`Cancel()` comes before `Run`.** That is the whole restart. `Run` returns nothing, so there is
  no handle of the previous run to stop; the area is the handle.

[Organizing work](Organizing-Work.md) explains areas, and
[Tasks and errors](Tasks-and-Errors.md) shows where to put cleanup that must also run on a cancel,
such as switching the collider back on.

### How an await ends

An awaited tween never resumes the method after something stopped it early. That is the difference
from awaiting a tween through other helpers, where a killed tween can look like a finished one.

| What happens | At the `await` |
|---|---|
| The tween completes | The method continues |
| `tween.Complete()` or `tween.Kill(true)` is called by hand | The method continues: that is a completion |
| The lifetime is cancelled or disposed, its owner is destroyed, or the token is cancelled | The tween is killed; `OperationCanceledException` |
| Anything else kills the tween: `tween.Kill()`, a destroyed `SetLink` target | `OperationCanceledException` |
| The lifetime had already ended, or the token was already cancelled, at the call | The tween is killed; `OperationCanceledException` |
| The tween was already inactive at the call | The method continues at once |
| The tween loops forever (`SetLoops(-1)`) | It never completes; only a kill ends the await, with `OperationCanceledException` |

Details behind the table:

- **A cancellation is delivered at the `await`, not at the call.** `AwaitCompletionAsync` returns a
  cancelled task for an ended lifetime or a cancelled token; it does not throw synchronously. Only
  argument errors (`null`) and a call from the wrong thread throw at the call.
- **Cancellation wins over "already inactive".** A dead tween on a dead lifetime gives a
  cancellation, not a completion.
- **Set the tween's callbacks before the call.** The await chains the tween's `onComplete` and
  `onKill` fields. An `OnComplete` or `OnKill` that is already set is kept, runs first, and is put
  back when the await is over. If one of them throws, the exception goes to
  `LifetimeErrors.Handler` and the await still ends correctly.
- **A callback set after the call replaces the await's own**, and the await then sees less:
  - An `OnComplete` set afterwards hides the completion. The await notices only the kill that
    follows it on an auto-kill tween, so it ends with `OperationCanceledException` although the
    tween completed.
  - An `OnKill` set afterwards hides a kill that comes from elsewhere (`tween.Kill()`,
    `DOTween.KillAll()`, a destroyed `SetLink` target). The await then stays pending until its
    lifetime ends or its token is cancelled. The lifetime and the token themselves always end the
    await: they do not depend on the callback, and the lifetime keeps the entry of an awaited
    tween for as long as its await is pending.
- **Await a given tween once.** With the lifetime overload each call also adds one entry to the
  lifetime, like `AddTo`: a tween that is kept alive, replayed and awaited again on the same
  lifetime adds one entry per call until the generation ends.
- **An awaited tween is killed, not completed, on cancel.** Combining an await with
  `AddTo(..., TweenCancelMode.Complete)` on the same tween depends on the order of the two calls and
  is best avoided. Whatever the order, the await ends with a cancellation.

The three ways to react to a cancellation inside an async method (let `Run` end it, catch and
rethrow, or branch on a bool) are not specific to tweens. `RewardView.cs` in the DOTween sample
shows all three, and [Tasks and errors](Tasks-and-Errors.md) explains them.

---

## Register the root Sequence only

<!-- source: Samples~/DOTweenUsage/PanelIntro.cs -->
```csharp
public sealed class PanelIntro : MonoBehaviour
{
    private RectTransform _panel;

    // The view arrives here because the demo builds its UI in code.
    public void Initialize(RectTransform panel) => _panel = panel;

    private void OnEnable()
    {
        var shown = this.GetActiveLifetime();       // cancelled by SetActive(false) and by an outside Cancel

        _panel.localScale = Vector3.zero;
        _panel.localRotation = Quaternion.identity;
        DOTween.Sequence()
            .Append(_panel.DOScale(1.1f, 0.25f).SetEase(Ease.OutQuad))
            .Append(_panel.DOScale(1f, 0.1f))
            .Join(_panel.DOShakeRotation(0.1f, 4f))
            .AddTo(shown, TweenCancelMode.Complete);   // the root only; a cut-short intro lands on its end pose
    }
}
```

`AddTo` is called once, on the Sequence. The three tweens inside it are not registered.

- **`SetActive(false)` during the intro** cancels `shown`. The Sequence is registered in `Complete`
  mode, so it is completed and then killed, and every nested tween lands on its end value: the
  panel is at full scale.
- **A `Cancel()` of the lifetime from outside**, while the panel stays visible, does the same. This
  is why the class uses `Complete`: in `Kill` mode the panel would stay frozen at the scale it had
  reached, possibly too small to see.
- **In the default `Kill` mode** the Sequence is killed and the nested tweens stop with it: the one
  that was playing keeps the value it had reached, and the ones that had not started never start.
- **In the Janitor window** the Sequence counts as one tween on that lifetime.

### What happens to a nested tween registered on its own

DOTween ignores `Kill` on a tween that is inside a Sequence, and it does so without logging
anything (Unity 6000.3). So if a nested tween is registered instead of its root:

1. The owner is cancelled and the package calls `Kill` on the nested tween.
2. DOTween ignores the call. The tween stays active.
3. The Sequence keeps playing and keeps driving the nested tween, on a target whose owner has ended.

Nothing is thrown and nothing is routed to the error handler, because from the lifetime's side the
kill succeeded. In the editor the package checks whether a tween is still active right after it
killed it and records [JANITOR102](Troubleshooting.md#janitor102) in the Janitor window, once per
tween. In a player there is no signal at all.

The same applies to awaiting: await the root Sequence, not a tween inside it. The await on a nested
tween is cancelled at once when its lifetime ends or its token is cancelled, so the awaiting method
stops there, but the tween itself keeps playing.

---

## Why a registered tween is not recyclable

DOTween can recycle tweens. With recycling on (`DOTween.defaultRecyclable`, or `SetRecyclable(true)`
on a tween), a killed tween's object goes back to a pool and is handed out again for the next tween
that is created. A reference you kept to the old tween then points at the new one:

- `IsActive()` on the old reference returns `true`, because the object is active again as
  somebody else's tween.
- `Kill()` on the old reference kills that other tween.

That is the fault in the `_fly?.Kill()` line of the "before" class above, and it would be the same
fault inside this package: a lifetime keeps a reference to every tween registered in it, and ends
them later.

What the package does about it: `AddTo` and both `AwaitCompletionAsync` overloads call
`SetRecyclable(false)` on the tween. A registered tween that was killed is never reused, so:

- a reference you hold stays dead after the kill, and `IsActive()` stays `false`;
- cancelling a lifetime can never kill a tween that was created later;
- `tween.Kill()` on your own reference is always safe.

The package's tests run this exact scenario with `DOTween.defaultRecyclable = true`: kill a
registered tween, create a new one, cancel the old owner, and check that the new tween is untouched.

What it costs: a registered tween skips DOTween's pool. In a project that turned recycling on, every
registered tween is a new object for the garbage collector when it ends. In a project that never
turned recycling on, nothing changes.

Do not call `SetRecyclable(true)` on a tween after registering it. That brings the hazard back.

Registration does not use the tween's callbacks for its bookkeeping either. `AddTo` never reads or
writes `onKill`, `onComplete` or any other callback, so it cannot overwrite yours. A tween that ends
by itself is noticed by asking `IsActive()` during an occasional sweep of the lifetime's entries.
An awaited tween whose await is still pending is not dropped by the sweep.

---

## Allocation

A warm `AddTo` allocates nothing, for all three owner forms and both modes. A warm
`AwaitCompletionAsync(lifetime)` that runs to completion allocates nothing either. These numbers are
measured on Mono in the editor only; [Performance](Performance.md) lists the exact tests and what is
not measured yet.

## Limits

- A tween nested in a Sequence cannot be killed on its own. Register and await the root.
- An awaited tween is always killed on cancel; there is no `Complete` option for an await.
- Set a tween's `OnComplete` and `OnKill` before awaiting it. A callback set afterwards replaces
  the one the await relies on.
- Register a given tween once and await it once. Every further call on the same live tween adds an
  entry that stays until the generation ends.
- Main thread only.

More in [Limitations](Limitations.md).

## See also

- [Concepts](Concepts.md)
- [Pooling](Pooling.md)
- [Tasks and errors](Tasks-and-Errors.md)
- [Organizing work](Organizing-Work.md)
- [Troubleshooting: JANITOR102](Troubleshooting.md#janitor102)
- [Performance](Performance.md)
- [ADR-003 Tweens are not recyclable](Design/Decisions/ADR-003-Tweens-Are-Not-Recyclable.md)
