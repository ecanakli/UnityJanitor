# Limitations

[Back to index](index.md)

What the package does not do, stated plainly. Each item says what the limit is and, where there is
one, what to do instead. "Not in 0.1.0" means exactly that: the feature is absent from this version.
It is not a promise about a later one.

---

## It only knows what goes through it

**Work that was not started through a lifetime is invisible.** An `async void` method, a
`Forget()` call, a raw `+=`, a tween without `AddTo`, a coroutine started with Unity's own
`StartCoroutine`: the package has no way to see any of them, so it cannot stop them and the Janitor
window does not show them. [Migration](Migration.md) has the replacement for each pattern.

**There is no analyzer.** Nothing checks at compile time that work goes through a lifetime, that an
await takes the token, or that a scene load is preceded by the scene call. A Roslyn analyzer is not
in 0.1.0. The checks that exist run while the game runs, and most of them only in the editor.

**An await without the token is not interrupted.** Cancellation in C# is cooperative. `Cancel()`
cancels the token that `Run` gave to your method; it cannot stop code that never looks at that
token. An `await UniTask.Delay(1000)` without `cancellationToken: ct` runs to its end, and the
method continues after it on an owner that may be gone. In the editor this is reported as
[JANITOR101](Troubleshooting.md#janitor101); in a player there is no signal. Synchronous code cannot
be interrupted either: a loop that never awaits runs until it returns.

---

## Stopping

**A task has no handle of its own.** `Run`, `After` and `Every` return nothing. Every other kind of
item gives you a `LifetimeRegistration` (or, for a tween, the tween) to stop that one item; a task
does not. A lifetime has no `Stop()` for one task either, and there is no single call that cancels
a task and starts it again. This method starts two timers that can only be stopped together with
everything else the component owns:

<!-- source: Samples~/BasicUsage/SessionClock.cs -->
```csharp
private void Start()
{
    this.Every(1f, TickGame);                                   // stops while Time.timeScale is 0
    this.Every(1f, TickReal, ignoreTimeScale: true);            // keeps running while the game is paused
}
```

To cancel one task, give it an area of its own and cancel the area:

<!-- source: Samples~/BasicUsage/Countdown.cs -->
```csharp
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

The price is visible in the class: one field, one line in `Awake`, and `Cancel()` before `Run` for a
restart. Tasks share the token of their lifetime's generation on purpose; a handle per task would
mean a token source per task.

**`Cancel()` has no "keep the listeners" mode.** It ends everything in the current generation,
subscriptions included. To stop activity and keep listening, put the activity in a child area and
cancel that. See [Troubleshooting](Troubleshooting.md#my-listener-stopped-after-cancel).

**Teardown is synchronous.** `Cancel()` and `Dispose()` return when everything has been stopped.
There is no asynchronous or graceful variant: a cancel action cannot ask for time to finish, and
nothing can be awaited "until the lifetime has ended".

**There is no broadcast and no registry.** The package has no "stop all" event to subscribe to and
no way to look a lifetime up by a string or an id. A lifetime's name is a display label. Sharing a
category means passing or injecting the `Lifetime` reference; see
[Organizing work](Organizing-Work.md).

**An item belongs to one lifetime.** There is no way to say "stop this when either A or B ends".
Register it on whichever ends first, or on a child area of one that you cancel from the other.

**A subscription is not removed when the event's source goes away.** It ends with its owner. A
long-lived owner that subscribes to many short-lived sources keeps one entry per source, and the
source's event with it, until the owner's generation ends. When the source is the shorter-lived
side, pass the source as the owner, as `CountdownTracker` in the Basic Usage sample does. See
[Performance](Performance.md#bounded-memory).

**`Dispose()` is for areas.** On `Lifetime.App`, on scene lifetimes and on the lifetimes of
components and GameObjects it is ignored ([JANITOR107](Troubleshooting.md#janitor107)). Those end
only with their owner.

---

## Objects and categories

**The first access decides an object lifetime's parent, for good.** A component or active lifetime
cannot be moved to another category later; a later `GetLifetime(parent)` with a different parent is
ignored ([JANITOR113](Troubleshooting.md#janitor113)).

**A re-homed lifetime cannot be placed again.** When a category is disposed, the object lifetimes
placed in it move back under their scene lifetime
([JANITOR114](Troubleshooting.md#janitor114)) and stay there. A later request to place such a
lifetime in a category is ignored and reported once
([JANITOR113](Troubleshooting.md#janitor113)).

**A GameObject lifetime cannot be placed in a category.** `gameObject.GetLifetime()` has no overload
that takes a parent. Component lifetimes and active lifetimes have one.

**The active lifetime follows the GameObject, not a component's `enabled` flag.** Disabling a
component does not cancel anything. Enabling it again, while the GameObject stayed active, runs
its `OnEnable` a second time, and work that `OnEnable` registers on the active lifetime is then
registered twice in the same generation: two tasks, two timers, two tweens. Nothing reports it.
[JANITOR116](Troubleshooting.md#janitor116) watches component and GameObject lifetimes only; a
repeated subscription is the one thing that is caught and ignored
([JANITOR105](Troubleshooting.md#janitor105)). `SetActive(true)` on a GameObject that is already
active does not run `OnEnable` and repeats nothing. The guard is to keep that work in an area of
its own under the active lifetime and to cancel the area at the top of `OnEnable`, before
registering, so the work is never doubled and keeps running while only the component is disabled;
if it must also stop while the component is disabled, cancel the area in `OnDisable` as well, a
line the class has to carry because no lifetime follows `enabled`. See
[Components and scenes](Components-and-Scenes.md#what-setactivefalse-stops). The one component
whose `enabled` flag does matter is the package's own hidden trigger; see
[Coroutines](#coroutines) below.

**Work registered in `OnEnable` on the component lifetime is added again on every activation.** The
component lifetime does not end on `SetActive(false)`, so a `this.Run(...)`, `this.Every(...)` or
`tween.AddTo(this)` written in `OnEnable` piles up on an object that is enabled more than once.
Register such work on `this.GetActiveLifetime()`. The editor reports the second activation as
[JANITOR116](Troubleshooting.md#janitor116); a repeated subscription is ignored and reported as
[JANITOR105](Troubleshooting.md#janitor105). JANITOR116 exists in the editor only: in a player the
work piles up without any sign.

**A component that never ran `Awake` ends with its GameObject, not with the component.** A component
lifetime that is first used while the GameObject is inactive ends when the component or the
GameObject is destroyed, whichever comes first. If the GameObject is never activated, Unity sends no
destroy signal for the component, and `Destroy(component)` alone does not end the lifetime
([JANITOR103](Troubleshooting.md#janitor103)).

**Do not take a lifetime for the first time inside `OnDestroy`.** By then the object's work has
already been stopped. A first `gameObject.GetLifetime()` or `GetActiveLifetime()` there returns a
lifetime that has already ended, and Unity logs "Can't add component to object that is being
destroyed." A first `this.GetLifetime()` there creates a lifetime that Unity never
ends with the component (JANITOR103).

**Play Mode only.** `Lifetime.App`, `SceneLifetimes`, `GetLifetime()` and `GetActiveLifetime()`
throw `InvalidOperationException` outside Play Mode. There are no lifetimes for editor tools,
`OnValidate` or Edit Mode code. In the editor the tree is cleared when Play Mode has ended, so code
that is still running after the stop gets the same exception.

**A script recompile during Play Mode discards every lifetime.** The editor reloads all script code
and the lifetime tree is lost with everything registered on it. The next call into the package
starts a new, empty tree and logs one warning. Work that was running before the recompile is not
restored; restart Play Mode. See
[Troubleshooting](Troubleshooting.md#scripts-were-recompiled-during-play-mode).

---

## Scenes

**The scene call is yours to make.** Unity raises no event before it starts destroying a scene, so
the package cannot run `SceneLifetimes.DisposeAll()` for you, and it does not wrap `SceneManager`,
Addressables scene loading or Zenject's scene loader. Without the call cleanup still happens, but in
Unity's destroy order ([JANITOR104](Troubleshooting.md#janitor104)).

**Disposing a scene lifetime is terminal.** After `SceneLifetimes.Dispose(scene)` nothing can be
registered in that scene again until it has been unloaded and loaded anew. To stop a scene's work
and keep using the scene, call `SceneLifetimes.Get(scene).Cancel()`.

**A load that fails after `DisposeAll()` leaves a scene that cannot work.** The call cannot be
undone. If the load that should have replaced the scene does not happen, the old scene stays on
screen with its lifetime, and the lifetimes of its objects, ended for good. Check what can be
checked before the call, as the `SceneFlow` sample does for a scene missing from the Build
Settings. For a load that can fail later, such as downloaded content, hold the scene's activation
and dispose right before it, as the `LoadingScreenFlow` sample does; see
[Troubleshooting](Troubleshooting.md#janitor109).

**After `DisposeAll()`, an object kept with `DontDestroyOnLoad` loses its active lifetime.** An
object that was in a scene when `DisposeAll()` ran, and then survives the load, gets a new
component lifetime and a new GameObject lifetime under `Lifetime.App` the next time they are asked
for. Its active lifetime stays ended, because the hidden trigger component keeps the one that was
disposed. For an object that outlives its scene, register work on the component lifetime, or move
the object out of the scene before the scene is disposed.

---

## Tweens

**A tween nested in a Sequence cannot be killed on its own.** DOTween ignores the kill. Register and
await the root Sequence. An await on a nested tween is cancelled when its lifetime ends, but the
tween keeps playing. A nested tween registered by mistake is reported only in the editor
([JANITOR102](Troubleshooting.md#janitor102)).

**A registered tween is not recyclable.** `AddTo` and `AwaitCompletionAsync` take the tween out of
DOTween's recycling. See [DOTween](DOTween.md#why-a-registered-tween-is-not-recyclable).

**An awaited tween is killed on cancel, never completed.**

**Set a tween's callbacks before awaiting it.** The await relies on the tween's `OnComplete` and
`OnKill`. A callback set after the call replaces the await's own: the await can then end as
cancelled although the tween completed, or stay pending after a kill from elsewhere. See
[DOTween](DOTween.md#how-an-await-ends).

**Register a tween once and await it once.** Every `AddTo` or `AwaitCompletionAsync(lifetime)` call
adds an entry, and an entry is dropped only when its tween is no longer active. A tween that is kept
alive, replayed and registered again on each replay adds one entry per call until the generation
ends.

**DOTween is the only tween library with an integration** in 0.1.0. Anything else can still be
bound by hand with `OnCancel`.

---

## Coroutines

**A coroutine needs an explicit, active host.** There is no global runner object. A host that is
null, destroyed or inactive means the coroutine is not started
([JANITOR110](Troubleshooting.md#janitor110)).

**`StopAllCoroutines()` on the host is not noticed.** The coroutine stops, as Unity stops it, but
its entry stays in the lifetime until that lifetime's generation ends. Stop a lifetime-bound
coroutine through its `LifetimeRegistration` or its lifetime instead.

**The hidden `ActiveLifetimeTrigger` component is not for use by hand.** The package adds it to
GameObjects that have an active lifetime or host a lifetime-bound coroutine. It does not appear in
the Inspector, but code that walks components can still reach it:

- **Disabling it** cancels the work on the active lifetime once, because Unity reports a disabled
  component and a destroyed one in the same way. A loop that disables every `Behaviour` of an
  object does this. The trigger enables itself again the next time the package uses it: at a
  `GetActiveLifetime()` call, at a registration on the active lifetime, or at the start of a
  lifetime-bound coroutine. The cancelled work is not started again by that. A component registers
  its work again when its own `OnEnable` runs the next time.
- **One case is missed.** If the trigger was disabled and the GameObject is then deactivated before
  the package has touched the trigger again, Unity sends the trigger no message. That deactivation
  does not cancel the active lifetime, and it is not counted for coroutines.
- **Removing it** is not supported.

Leave the component alone: skip it in loops over `GetComponents<Behaviour>()`.

---

## Events

**`OwnedEvent` is not a message bus.** It has no global registry, no routing by type and no
asynchronous dispatch. It is a replacement for a C# `event` field, with owner-bound subscriptions.

**`OwnedEvent` is code only.** It is not serialized and cannot be wired in the Inspector. For
Inspector wiring keep using `UnityEvent`, and subscribe to it from code with an owner.

**`OwnedEvent` carries zero to three arguments.** The owner-taking `Subscribe` for `UnityEvent`
covers zero to four.

**There is no subscription without an owner.** A subscription that should last for the whole
session names `Lifetime.App` as its owner.

**The paired `Subscribe` needs its type argument written out** for events that carry arguments. See
[Events](Events.md).

**The duplicate check of the paired `Subscribe` compares delegates, not events.** A second call with
the same `add`, `remove` and handler on the same owner is ignored
([JANITOR105](Troubleshooting.md#janitor105)). The package cannot see which event `add` touches, so
the check has two blind spots: lambdas that capture a local variable or a parameter are new objects
on every call and are never recognised as a repeat, and lambdas that read a field compare equal
even after the field was pointed at another object.

---

## Tasks

**UniTask only.** `Run` takes a method that returns `UniTask`. There is no `Task`-based and no
`Awaitable`-based API in 0.1.0.

**`Run` does not move work to another thread.** It calls your method on the calling thread, which
must be the main thread. It is not a counterpart of `UniTask.Run`.

**`Every` stops on its first exception.** The exception is routed to `LifetimeErrors.Handler` and
that timer does not fire again.

**Timers measure game time while the application runs, not wall-clock time.** `After` and `Every`
add up frame deltas: scaled game time by default, unscaled with `ignoreTimeScale`. They are not a
clock. With scaled time, the period the application spends suspended in the background is not
counted and is not caught up afterwards. The sum is kept in a `float`, which loses precision over
many hours. Do not use timers for offline progress or for delays of hours; store a wall-clock time
and compare it when the game resumes. `seconds` must be a finite number no larger than
900000000000, or the call throws `ArgumentOutOfRangeException`.

**A cancelled wait costs an exception.** A timer or a task that is pending when its lifetime is
cancelled finishes with an exception that the package catches. See
[Performance](Performance.md#cancelling-work-that-is-in-the-middle-of-an-await).

---

## Threading

**Main thread only.** Registration, `Token` and `CreateChild` throw on any other thread. `Cancel`
and `Dispose` called from another thread are carried out on the main thread, up to one frame later.
See [Threading](Threading.md).

---

## Integrations

**No VContainer integration in 0.1.0.** The package works without a container (pass lifetimes
through constructors) and with Zenject / Extenject. In a project that uses VContainer, files that
import both namespaces need an alias for `Lifetime`; see
[Troubleshooting](Troubleshooting.md#cs0104-lifetime-is-ambiguous).

**No Addressables handle ownership in 0.1.0.** There is no `handle.AddTo(lifetime)`. A handle can be
released from an `OnCancel` action that you write.

**Zenject specifics.** Every plain class that injects `Lifetime` gets its own area, which stays
until its context ends or the class disposes it. A GameObjectContext needs its own
`LifetimeInstaller.Install` call. A SignalBus handler can belong to one owner per bus and signal
type. See [Zenject](Zenject.md#limits).

---

## Diagnostics

**The Janitor window and its recording exist only in the editor.** There is no overlay or log of
lifetimes in a player, not even in a development build. Development builds do get the console
warnings.

**Some problems are only reported in the editor.** A task that outlives its lifetime (JANITOR101), a
nested tween that was not killed (JANITOR102), an orphan lifetime (JANITOR103), growth (JANITOR106)
and work registered again in `OnEnable` (JANITOR116) are shown in the Janitor window only. They
have no console message, so a player build gives no sign of them. JANITOR103 is found only while
the window is open.

**Stack traces of registrations are off by default** and apply only to items registered after they
were turned on.

---

## Documentation

**There is no documentation website** in 0.1.0. These pages are the documentation.

## See also

- [Concepts](Concepts.md)
- [Troubleshooting](Troubleshooting.md)
- [Performance](Performance.md)
- [Threading](Threading.md)
- [Migration](Migration.md)
- [FAQ](FAQ.md)
