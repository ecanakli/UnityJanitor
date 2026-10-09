# Performance

[Back to index](index.md)

This page says what the package allocates and what it does not, how its memory stays bounded, what
the editor diagnostics cost, and what is left in a player build.

**Read this first.** Every number on this page comes from an allocation test in the package's
`Tests/` folder. "Allocates nothing" below means: the test named next to it passes.

---

## What "steady state" means

All "allocates nothing" results are for a **warm** call: the same kind of call has already run at
least once on that lifetime. The first use of anything allocates, once:

- the lifetime object itself, and its entry array when the first item is registered;
- the entry array growing to the largest number of items the lifetime has held (it starts at four
  slots and doubles);
- a few cached delegates per generic type the first time a method is used with that type;
- the pools the package keeps for running tasks, timers and tween awaits. They grow to the largest
  number that was pending at the same time; see [Bounded memory](#bounded-memory).

After that, a lifetime reuses what it has. `Cancel()` empties the entry array and keeps it; the
next registration writes into a free slot.

The tests measure with Unity Test Framework's `Is.Not.AllocatingGCMemory()` constraint, run the
measured code before measuring it, and check afterwards that the measured code really did the work.

---

## What allocates nothing

### Registration and stopping

| Operation, warm | Measured as | Test |
|---|---|---|
| `disposable.AddTo(lifetime)`, class instance | 64 registrations | `Tests/Runtime/Allocation/LifetimeAllocationTests.cs` |
| `lifetime.OnCancel(state, static action)`, one or two states | 64 registrations | same |
| `lifetime.OnCancel(cachedAction)`, and `OnCancel` with an `isFinished` function | 16 registrations | `Tests/Runtime/Allocation/PooledContinuationAllocationTests.cs` |
| `lifetime.Cancel()` | A lifetime with eight descendants and 64 entries spread over them | same |
| `area.Dispose()` | A subtree of nine lifetimes with 64 entries | same |
| `registration.Cancel()`, `registration.IsActive` | 64 handles | same |
| Fill and cancel, repeated | 100 cycles of 64 registrations and a `Cancel()` | same |
| `this.GetLifetime()`, `GetActiveLifetime()`, `gameObject.GetLifetime()` after the first call | 100 lookups | `Tests/Runtime/Binding/BindingAllocationTests.cs` |
| `disposable.AddTo(this)` from a `MonoBehaviour` | 16 registrations | same |

### Tasks and timers

| Operation, warm | Measured as | Test |
|---|---|---|
| `lifetime.Run(state, static work)`, work completes synchronously | 16 runs | `Tests/Runtime/Allocation/TaskAndEventAllocationTests.cs` |
| `lifetime.Run(state, static work)`, work is pending and completes later | 16 runs on pooled task sources | same |
| `this.Run(state, static work)` from a `MonoBehaviour` | 16 runs | `Tests/Runtime/Binding/BindingAllocationTests.cs` |
| `lifetime.After(seconds, state, static callback)`, start and fire | 16 timers | `Tests/Runtime/Allocation/TaskAndEventAllocationTests.cs` |
| `lifetime.Every(seconds, state, static callback)`, start | 16 timers | same |
| `Every`, running | 16 timers, 100 ticks each | same |
| `Cancel()` and `Dispose()` of a lifetime holding pending tasks, timers and event subscriptions | Eight of each | same |
| `After`, `Every` and `Run` with a struct as the state | 16 of each; the timers also tick | `Tests/Runtime/Allocation/PooledContinuationAllocationTests.cs` |
| `After`, `Every` and `Run` with a cached delegate and no state | 16 of each | same |
| A burst of timers, or of pending tasks, the third time it happens | 100 at once | same |
| Fill a lifetime with `Run`, `After` and `Every`, cancel it, fill it again | 70 of each per cycle; the fill and the `Cancel()` call are measured | same |

Three conditions apply to this table.

- **The timer tests replace the delay with a test clock.** They prove that the package's own timer
  code allocates nothing. In a game the delay behind `After` and `Every` is `UniTask.Delay`, whose
  cost is UniTask's.
- **The token is created outside the measured code.** See [the token](#the-token) below.
- **The `Cancel()` rows measure the call, not the frame after it.** Tasks and timers that were
  pending when the lifetime was cancelled finish as cancelled afterwards, and that costs an
  exception each. See
  [Cancelling work that is in the middle of an await](#cancelling-work-that-is-in-the-middle-of-an-await).

### Events

| Operation, warm | Measured as | Test |
|---|---|---|
| `ownedEvent.Subscribe(handler, owner)` with a cached handler delegate | 16 owners; and 16 handlers on one owner | `Tests/Runtime/Allocation/TaskAndEventAllocationTests.cs` |
| Subscribe, then cancel the owner, repeated | 100 cycles of 16 | same |
| `ownedEvent.Invoke(...)` | 16 handlers, 100 invokes; zero, one, two and three arguments | same |
| `ownedEvent.Subscribe(handler, this)` from a `MonoBehaviour` | 16 subscriptions | `Tests/Runtime/Binding/BindingAllocationTests.cs` |
| Paired `Subscribe(add, remove, handler)` with `static` lambdas and cached handlers | 32 subscriptions with 32 different handlers, so the duplicate check runs in full; plain and generic form | `Tests/Runtime/Allocation/TaskAndEventAllocationTests.cs` |
| Invoking a UnityEvent whose listeners were added with `Subscribe(handler, owner)` | 16 listeners; zero, one and four arguments | `Tests/Runtime/Events/UnityEventAllocationTests.cs` |

### Integrations

| Operation, warm | Measured as | Test |
|---|---|---|
| `tween.AddTo(owner)`: lifetime (both modes), `MonoBehaviour`, `Transform`, GameObject | 16 tweens each | `Tests/DOTween/TweenAllocationTests.cs` |
| `tween.AwaitCompletionAsync(lifetime)`: the call | 16 awaits on a warm pool | same |
| `tween.AwaitCompletionAsync(lifetime)`: await, tween completes, result consumed | 16 tweens | same |
| `signalBus.Subscribe<TSignal>(handler, owner)` and the owner's `Cancel()` | No more allocations than Zenject's own `Subscribe` and `TryUnsubscribe` over 64 cycles | `Tests/DependencyInjection/Zenject/SignalSubscriptionTableTests.cs` |

---

## What does allocate

These are by design. None of them is per frame.

### Once per object

- **`parent.CreateChild()`**: one lifetime object. Create an area once and keep it; `Cancel()` makes
  it reusable. Lifetime objects are not pooled.
- **The first `GetLifetime()` for a component or a GameObject**: one lifetime, its display name (a
  string), and one registration on the object's destroy token. Unity creates the token source of a
  component's `destroyCancellationToken` on that first read. A GameObject lifetime also adds
  UniTask's destroy trigger component to the GameObject.
- **The first `GetLifetime()` for a component whose GameObject is inactive** costs more. The
  lifetime is bound to the component and to the GameObject, so there is UniTask's destroy trigger
  component, one more small object and a second token registration. Until the GameObject becomes
  active for the first time, UniTask also checks once per frame whether it was destroyed. Objects
  that Zenject instantiates take this path, because they are injected while inactive.
- **The first `GetActiveLifetime()` for a GameObject**: the hidden `ActiveLifetimeTrigger`
  component, the active lifetime and its display name.

### Once per call

- **`lifetime.StartCoroutine(host, routine)`**: one small wrapper object per coroutine, next to
  whatever Unity allocates for the coroutine itself. The first coroutine on a GameObject also adds
  the hidden trigger component.
- **`unityEvent.Subscribe(handler, owner)`**: one guard object and its delegate per subscription,
  next to what `AddListener` allocates. Invoking the event afterwards allocates nothing (table
  above).
- **A struct passed to `AddTo`** is boxed once.
- **`tween.AwaitCompletionAsync(token)`**: one registration on the token.
- **`SceneLifetimes.DisposeAll()`**: one small array of the loaded scenes per call.

### What your call site allocates

The package cannot remove allocations that the C# compiler puts at your call site:

- A lambda that captures something (`ct => TickAsync(seconds, ct)`) is a closure object and a
  delegate per call.
- A method group (`shown.Run(LoadOffersAsync)`, `Subscribe(OnCoinsChanged, shown)`) is a delegate
  per call.
- The `add` and `remove` lambdas of a paired `Subscribe` that capture a field are a closure and two
  delegates per call.

For code that runs on a button press or once per activation, none of this matters. For code that
runs many times per second, every one of these methods has a form that keeps the call site free:
pass the data as a state argument and make the lambda `static`. One method of the Basic Usage
sample shows both kinds side by side:

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

The `Run` line captures `damage`, so each `Play` allocates a closure and a delegate. That is fine
here: the effect plays once. The `OnCancel` line in `Awake` and the `After` line in `Play` pass
their data as state (`gameObject`, `_life`) to a `static` lambda; the compiler caches that delegate
once and the call allocates nothing. The state can be the component itself:

<!-- source: Samples~/BasicUsage/PreviewRenderer.cs -->
```csharp
this.GetActiveLifetime().OnCancel(this, static self => self.Release());   // cleanup the package knows nothing about
```

`Run` has the state form too. No sample needs it; this is the statement the allocation test
measures:

<!-- source: Tests/Runtime/Allocation/TaskAndEventAllocationTests.cs -->
```csharp
area.Run(counter, static (c, ct) =>
{
    c.Value++;
    return UniTask.CompletedTask;
});
```

For subscriptions, keep the handler delegate in a field and subscribe that field. For a paired
`Subscribe` on a static event, `static` lambdas need no closure; the `PlatformHooks` sample does
this for `Application.lowMemory`.

### The token

A lifetime creates its `CancellationTokenSource` lazily, the first time `Token` is read in a
generation. `Run`, `After` and `Every` read it. So:

- a lifetime that only holds tweens, subscriptions and disposables never creates one;
- a lifetime that runs tasks creates **one per generation**, however many tasks it runs;
- the first `Run` after a `Cancel()` creates the next one.

That one object per generation is the floor, and it is the same cost as the hand-written
"cancel the old source, create a new one" pattern it replaces. The package creates no linked token
sources and no per-task token source.

### Cancelling work that is in the middle of an await

The `Cancel()` call itself allocates nothing. Work that was waiting when it was cancelled still has
to finish, and in C# a cancelled wait finishes with an exception. That costs an allocation and a
throw and catch, shortly after the cancel:

| Cancelled while it waits | What it costs | When |
|---|---|---|
| A pending `After` or `Every` timer | One exception object, thrown and caught once | In the next frame |
| A `Run` task waiting in `UniTask.Delay(..., cancellationToken: ct)`, `UniTask.Yield(ct)` or a similar await | One exception object, thrown and caught twice: once in your method, once where the package observes the end of the task | In the next frame |
| A method waiting in `tween.AwaitCompletionAsync(...)` | One exception object and its throw and catch | Inside the `Cancel()` call, at the moment the tween is killed |

The package catches these exceptions; none reaches the error handler or the console. The exception
for a timer or a delay is created by UniTask when the cancelled delay is resumed, which is why it
appears in the frame after the cancel and not in the measured `Cancel()` rows above.

This is a cost per cancelled wait, when the cancel happens. It is not a per-frame cost. It does add
up when many objects are cancelled in the same frame: a pool that takes back 200 coins in
mid-flight, each with one pending timer and one flight task, pays for 200 timers and 200 tasks in
the next frame. A timer that has fired and a task that has finished cost nothing when their
lifetime is cancelled later. No test in the package measures this cost, and none measures the time
a throw takes.

### Removing listeners from third-party events

When a lifetime ends, a `UnityEvent` subscription is removed with `RemoveListener` and a SignalBus
subscription with `TryUnsubscribe`. What those calls cost is decided by Unity and Zenject, and it is
the same cost as removing the listener by hand. `RemoveListener` searches the listeners of that
event, so removing many listeners from one busy event is linear for each of them. The package's
tests measure the SignalBus case (no more allocations than Zenject's own calls, table above); the
allocation of `RemoveListener` is not measured.

---

## Bounded memory

Everything a lifetime holds is released when its generation ends. The rules below are about what
happens **between** two cancels, on a lifetime that lives long.

**Items that end by themselves are removed.**

- A task removes its entry when it completes.
- A coroutine removes its entry when the routine ends.
- A tween that completed or was killed by someone else, and a coroutine that Unity stopped, cannot
  tell the lifetime. They are found by a **sweep**: when a registration brings the number of entries
  to a threshold, the lifetime asks each such entry whether it is finished and drops the ones that
  are. The threshold is 16, or twice the number of entries left after the previous sweep, whichever
  is larger.

The result is a bound, and the tests pin it: on a lifetime with `live` running items, the number of
entries never exceeds `2 x live + 16`, over 10,000 register-and-finish cycles
(`Tests/Editor/EntrySweepTests.cs`). The same bound is tested for tweens
(`Tests/DOTween/TweenBookkeepingTests.cs`) and for coroutines on a host that is deactivated and
reactivated (`Tests/Runtime/Coroutines/LifetimeCoroutinePooledHostTests.cs`).

Three things to know about that bound:

- **`live` counts everything that has not ended,** including subscriptions and other items that
  never end by themselves. A lifetime with 1,000 permanent subscriptions can hold up to as many
  finished tweens before the next sweep.
- **A sweep happens only when something is registered.** A lifetime that stops registering keeps
  the entries of its finished tweens and coroutines, and the tween objects they refer to, until it
  registers again, is cancelled or is disposed.
- **The bound is per lifetime.** A thousand objects that each played a few tweens on their own
  component lifetime and then went idle each keep those few entries.

**Items that do not end by themselves stay.** A subscription, an `IDisposable` and an `OnCancel`
action have no "finished" state. They stay registered until the generation ends or their
`LifetimeRegistration` is cancelled. A long-lived lifetime that keeps receiving such items grows.
That is the case [JANITOR106](Troubleshooting.md#janitor106) reports in the editor: more than 256
entries on one lifetime.

**The owner of a subscription is whichever side ends first.** The common way to grow a long-lived
lifetime is a manager that lives as long as the scene and subscribes to an event of every object
that is created, with itself as the owner. When such an object is destroyed, the manager's entry for
it stays, and it keeps the object's event alive in memory. Nothing removes it before the scene
ends, and every further `UnityEvent` or paired `Subscribe` on the manager walks all those entries.
When the source of an event is shorter-lived than the subscriber, pass the **source** as the owner.
The tracker of the Basic Usage sample lives as long as the scene and listens to every countdown that
is spawned:

<!-- source: Samples~/BasicUsage/CountdownTracker.cs -->
```csharp
public void Track(Countdown countdown)
{
    // The owner is the countdown, not this tracker: it ends first, so the entry leaves with it instead of piling up here.
    countdown.Finished.Subscribe(OnFinished, countdown);
}
```

The subscription sits on the countdown's component lifetime and ends when the countdown is
destroyed. The tracker itself holds no entry for it.

**Areas that are never disposed stay.** A child area is unlinked from its parent when it is
disposed, or when its parent is. `CreateChild()` in a method that runs repeatedly, without a
`Dispose()`, adds a lifetime every time. So does a Zenject factory whose plain objects inject a
`Lifetime` ([Zenject](Zenject.md#objects-created-at-run-time)). JANITOR106 reports this as well:
more than 64 live child areas on one lifetime, whatever kind of lifetime the parent is. Object
lifetimes are not counted.

**Buffers keep their size.**

- The entry array of a lifetime is kept across generations and never shrinks. A lifetime that once
  held 500 items keeps room for 500 until it is disposed.
- An `OwnedEvent` keeps its subscriber array. Removed subscribers leave a hole that is closed by an
  order-preserving compaction; the compaction waits until no `Invoke` of that event is running. The
  100-cycle test above ends with zero slots in use.
- The buffers that `Cancel()` and `Dispose()` use to walk a subtree are pooled per play session and
  grow to the largest subtree and the deepest nesting they have seen.
- The pooled objects behind `Run`, `After` and `Every` are not capped. Each pool grows to the
  largest number of tasks, or of timers of one state type, that were pending at the same time, and
  keeps that many objects for the rest of the session. A burst therefore allocates once; the same
  burst later allocates nothing (the 100-at-once rows above). The pool behind awaited tweens
  behaves the same way.

**Token sources are dropped, not disposed.** The token source of an ended generation is cancelled
and released to the garbage collector. Tokens that running code still holds stay valid and stay
cancelled.

---

## Costs that are not allocations

- **Looking up an object's lifetime.** `this.Run(...)`, `tween.AddTo(this)` and
  `Subscribe(handler, this)` find the component's lifetime on every call: an instance id, a
  dictionary lookup and a Unity null check. In a loop, call `this.GetLifetime()` once and use the
  result.
- **`GetActiveLifetime()` does not use that dictionary.** Every call asks the engine for the
  GameObject and for the trigger component (`TryGetComponent`), and reads the trigger's `enabled`
  flag. Call it once per `OnEnable` and keep the result in a local, as the samples do.
- **An active lifetime, and every area under one, checks its GameObject** on every registration,
  with one call into the engine (`activeInHierarchy`).
- **A pending timer is polled every frame.** Each `After` or `Every` that is waiting is one item in
  UniTask's player loop, which reads the frame's delta time and checks the token for it once per
  frame. The cost follows the number of pending timers, not what they do. For thousands of objects
  that tick at the same rate, one `Every` that walks a list is cheaper than one timer per object.
- **`Cancel()` is synchronous and proportional** to the number of lifetimes in the subtree plus the
  number of items they hold. Your cancel actions, `OnKill` callbacks and `Dispose` methods run
  inside it. `Lifetime.App.Cancel()` and a scene dispose walk everything below them; they are meant
  for rare moments.
- **A scene `Cancel()` or `Dispose` looks at every object lifetime once** before it starts, to take
  in objects that were moved into the scene and to leave out objects that were moved away. That
  pass is linear in the number of object lifetimes that exist, with a call into the engine for
  each.
- **Subscribing checks for a duplicate.** `OwnedEvent.Subscribe` compares the handler with the
  event's existing subscribers of the same owner; `UnityEvent.Subscribe` and the paired `Subscribe`
  walk the owner's entries. All three are linear, and all happen at subscribe time only, never at
  invoke time.
- **`OwnedEvent.Invoke`** costs, per handler, a check that the subscriber's owner is still in the
  same generation, a `try` region, and the delegate call.
- **Registered tweens skip DOTween's recycling.** See [DOTween](DOTween.md#why-a-registered-tween-is-not-recyclable).
- **Timers add up frame deltas.** They are not a clock and are not meant for delays of hours; see
  [Limitations](Limitations.md#tasks).

---

## The cost of diagnostics

The recording behind the Janitor window runs in the editor whenever Tracking is on, which is the
default, whether or not the window is open.

| In the editor | Cost | Test |
|---|---|---|
| A registration with Tracking on, Stack traces off | A counter, a frame number; allocates nothing | `Tests/Editor/Diagnostics/DiagnosticsAllocationTests.cs` |
| A registration with Tracking off | Nothing is recorded; allocates nothing | same |
| A registration with **Stack traces on** | One captured stack trace per registration. This allocates and is slow; the test asserts that it allocates | same |
| `Cancel()` and `Dispose()` with Tracking on | Allocate nothing | same |
| A window refresh of an unchanged tree | Walks every lifetime and every entry four times per second; allocates nothing | same, and `Tests/Runtime/Diagnostics/PlayModeDiagnosticsAllocationTests.cs`, `Tests/EditorTools/JanitorViewModelAllocationTests.cs` |
| A diagnostic being raised | Builds its message string. Diagnostics are rare by nature | |

So: leave Tracking on, turn Stack traces on only while you are looking for one registration, and
expect editor frame times with the window open to include one tree walk every 250 ms.

### In a player build

The diagnostics are compiled out of players, not switched off:

- Every recording call is marked conditional on `UNITY_EDITOR`. The compiler removes the call and
  the evaluation of its arguments from a player build. This includes `LifetimeDiagnostics.Report`,
  the public hook for integrations.
- The fields that hold the recorded data, and the types behind the window, exist only under
  `#if UNITY_EDITOR`.
- What remains in a player is the empty `LifetimeDiagnostics` class and the `DiagnosticIds`
  constants with their title and anchor strings.

Console warnings are a separate switch. The `[JANITOR1xx]` warnings are compiled into the editor and
into **development** builds, and removed from release builds. JANITOR115 is the exception: it is
routed to the error handler in every build.

Two things are **not** removed from a release build, because they are behaviour and not
diagnostics: the main-thread check on registration (a read of the current thread's id and a
comparison, made up to three times in one `Run`, `After` or `Every` call; see
[Threading](Threading.md)), and the isolation of your callbacks in `try`/`catch`.

---

## What has not been measured

- **Timings.** The tests measure allocation, not duration. This page states no milliseconds.
- **`After` and `Every` together with the real `UniTask.Delay`.** The package's part is measured
  with a test clock.
- **The frame after a cancel.** The exceptions of cancelled waits, described above, are not
  measured by any test.
- **`RemoveListener` on a `UnityEvent`** when a lifetime ends.
- **A full `SetActive(false)` and `SetActive(true)` cycle** of an object that uses `Run`, `After`
  or `Every` on its active lifetime, as one measured block.
- **Code size.** Generic types such as `OwnedEvent<T>` are instantiated per value-type argument by
  IL2CPP; the effect on build size has not been measured.

## See also

- [Concepts](Concepts.md)
- [Diagnostics](Diagnostics.md)
- [Threading](Threading.md)
- [DOTween](DOTween.md)
- [Troubleshooting: JANITOR106](Troubleshooting.md#janitor106)
- [Limitations](Limitations.md)
