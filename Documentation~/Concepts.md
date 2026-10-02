# Concepts

[Back to index](index.md)

This page is the model behind every other guide: what a lifetime is, how lifetimes nest, what a
generation is, how `Cancel()` differs from `Dispose()`, what the `Token` property returns after a
`Cancel()`, in which order registered work stops, and what happens when code registers, cancels or
disposes from inside a cancel callback. It ends with a reference table of what each kind of stop
does.

---

## The lifetime tree

A `Lifetime` is an owner of cleanup. Work is registered into it (a task, a timer, a tween, a
coroutine, a subscription, a disposable), and the work stops when the lifetime is cancelled or
disposed. Lifetimes form a tree: stopping one lifetime stops everything registered in it and in
every lifetime below it.

The Basic Usage sample builds part of its tree in two small classes. `GameplayRoot` is a component;
it asks for its own lifetime and creates one area under it:

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

`GameplayLifetimes` creates two more areas under the lifetime it is given:

<!-- source: Samples~/BasicUsage/GameplayLifetimes.cs -->
```csharp
/// <summary>Named areas that objects join with GetLifetime(area) or GetActiveLifetime(area).</summary>
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

A lifetime appears in the tree the first time it is asked for. Once every feature of the demo has
been used at least once, the whole tree looks like this. Each line is one lifetime, and one row in
the Janitor window (`Window > Analysis > Janitor`).

```text
App                               Lifetime.App
+- SceneFlow                      area created by the demo under App; it survives scene loads
+- <scene name>                   scene lifetime, SceneLifetimes.Get(scene)
   +- GameplayRoot                component lifetime, this.GetLifetime()
   |  +- Gameplay                 area, CreateChild("Gameplay")
   |     +- Popups                area used as a category
   |     |  +- ShopPopup          active lifetime of the popup, placed in Popups
   |     +- Combat                area used as a category
   |        +- CombatEffect       component lifetime placed in Combat, while an effect plays
   +- Countdown                   component lifetime; each spawned countdown adds one more
   |  +- Run                      area: the running countdown
   +- ScoreReveal                 component lifetime, while a revealed score is on screen
   +- BasicUsageDemo              component lifetime (the demo's own subscriptions)
   +- PlatformHooks               component lifetime
   +- SfxPlayer                   component lifetime
   |  +- Release                  area: the pending release of the last clip
   +- SessionClock                component lifetime
   +- CoinsLabel                  component lifetime
   +- HintPulse                   component lifetime
   +- ModalBlocker                active lifetime of a child object of the popup
   +- Coin                        active lifetime of a pooled coin, one per coin
```

What happens at each moment follows from the shape:

- `GameplayRoot.OnResetRequested()` cancels `Gameplay`. Everything registered in `Gameplay`,
  `Popups` and `Combat`, and in the lifetimes placed in them (the popup, a combat effect that is
  playing), stops. The countdown, the coins and the platform hooks are not under `Gameplay` and keep
  working. The cancelled lifetimes stay in the tree and can be used again.
- `SetActive(false)` on the popup cancels the popup's active lifetime only.
- Destroying the `GameplayRoot` object disposes its component lifetime and, with it, `Gameplay`,
  `Popups` and `Combat`. The popup's lifetime is not disposed with them, because the popup object is
  still alive: its work is cancelled and it moves under the scene lifetime (see
  [Organizing work](Organizing-Work.md#when-a-category-is-disposed)).
- `SceneLifetimes.DisposeAll()` disposes the scene lifetime and everything under it. `SceneFlow` is
  under `App` and survives.
- When the application quits or Play Mode ends, `App` is disposed and everything goes with it.

### The kinds of lifetime

There are six kinds. Five are created and disposed by the package and are called **package-owned**
on these pages. The sixth, the **area**, is the one you create and may dispose yourself.

| Kind | You get it with | Its parent | It is disposed when |
|---|---|---|---|
| App | `Lifetime.App` | none: it is the root | The application quits or Play Mode ends |
| Scene | `SceneLifetimes.Get(scene)` | `App` | `SceneLifetimes.Dispose(scene)` or `DisposeAll()` is called; without that call, late, after the scene was unloaded |
| Component | `this.GetLifetime()`, and implicitly `this.Run(...)`, `x.AddTo(this)`, `evt.Subscribe(handler, this)` | The scene lifetime of the object, `App` for a DontDestroyOnLoad object, or a category passed to `GetLifetime(parent)` | The component is destroyed, its scene lifetime is disposed, or the app exits |
| GameObject | `gameObject.GetLifetime()`, and implicitly an owner argument that is a component but not a `MonoBehaviour` | The scene lifetime of the object, or `App` | The GameObject is destroyed, its scene lifetime is disposed, or the app exits |
| Active | `this.GetActiveLifetime()` | The scene lifetime of the object, `App`, or a category passed to `GetActiveLifetime(parent)` | The GameObject is destroyed, its scene lifetime is disposed, or the app exits. It is cancelled, not disposed, every time the GameObject is deactivated |
| Area | `parent.CreateChild()` | The lifetime `CreateChild` was called on | `Dispose()` is called on it, or its parent is disposed |

Three rules hold for every kind:

- **The parent is always explicit.** `Lifetime` has no public constructor. An area exists only under
  the lifetime it was created from, so no work can end up under a hidden default owner that outlives
  its scene.
- **`Cancel()` works on every lifetime.** It stops the work and keeps the lifetime.
- **`Dispose()` works on areas only.** A package-owned lifetime is disposed when its owner goes away
  and at no other time. Calling `Dispose()` on one is ignored, and the editor and development builds
  log [JANITOR107](Troubleshooting.md#janitor107).

`Lifetime.App`, `SceneLifetimes` and the component accessors exist in Play Mode only. Outside Play
Mode they throw `InvalidOperationException`; the tree is cleared when the editor returns to Edit
Mode. A new Play Mode session gets a new tree, also when domain reload is disabled. A script
recompile during Play Mode (editor only) discards every lifetime and the work it owned: the next
call into the package starts a new tree and logs one warning that says so.

The component, GameObject and active lifetimes are described in
[Components and scenes](Components-and-Scenes.md); areas and categories in
[Organizing work](Organizing-Work.md). With the Zenject integration a plain class can have a
lifetime injected; that lifetime is an area under the lifetime of its context (see
[Zenject](Zenject.md)).

---

## Generations

Every lifetime has a **current generation**: the work registered since the last `Cancel()`, plus one
cancellation token. `Cancel()` ends the current generation and opens the next one on the same
lifetime object. A lifetime can go through any number of generations.

Everything that refers to registered work is stamped with the generation it was created in:

- the token returned by `Token`, and the token `Run` hands to your work;
- every `LifetimeRegistration` handle;
- the entry of every task and timer;
- every subscription.

When the generation ends, all of these go dead together. A handle from an earlier generation reports
`IsActive == false` and its `Cancel()` does nothing. A token from an earlier generation is cancelled
and stays cancelled. A timer scheduled in an earlier generation never fires, and an event handler
subscribed in an earlier generation is never called. This is what makes it safe to reuse one
lifetime again and again: a restarted countdown, a popup that is opened and closed, a pooled object
(see [Pooling](Pooling.md#generations-guard-against-stale-handles)).

The lifetime **object** is the one thing that is not stamped. It always refers to the current
generation, which is what makes it reusable. The consequence is stated in
[Pooling](Pooling.md#the-one-hole-that-remains): code that ignores its token and registers through
the lifetime object after a `Cancel()` registers into the new generation.

---

## Cancel and Dispose

There are two ways to stop a lifetime, and no others. There is no `Stop()` and no `End()`.

`Cancel()` is the everyday call. It ends the current generation of the lifetime and of every
lifetime below it: all their tokens are cancelled, then all their registered items are terminated.
Fresh generations open, and every lifetime stays where it is in the tree, ready for new work. It
can be called any number of times.

`Dispose()` ends an area for good. The same tokens are cancelled and the same items terminated, and
then the area and every lifetime below it leave the tree. Anything registered on a disposed
lifetime afterwards is terminated on the spot.

| | `Cancel()` | `Dispose()` |
|---|---|---|
| Allowed on | Any lifetime | Areas. On a package-owned lifetime it is ignored ([JANITOR107](Troubleshooting.md#janitor107)) |
| Effect on this lifetime | The current generation ends, a new one opens | It ends for good and is detached from its parent |
| Effect on lifetimes below it | Each is cancelled the same way and stays attached | Each is disposed. The exception: the lifetime of a live object that was placed in the area is cancelled and moved under its scene lifetime |
| A registration made afterwards | Goes into the new generation | Is terminated immediately, every time |
| `Token` afterwards | A new token that is not cancelled | An already cancelled token |
| `IsDisposed` afterwards | `false` | `true` |
| Calling it again | Ends the next generation | Does nothing |
| Can it throw | No | No |

"Terminated immediately" means the item never becomes live: a task is not started, a timer is not
scheduled, a coroutine is not started, a subscription is not added, a disposable is disposed, an
`OnCancel` action runs at once, a tween is killed. A call that returns a `LifetimeRegistration`
returns a default one, whose `IsActive` is `false`.

Neither call throws. An exception thrown by a cleanup action, a `Dispose()` of a registered
disposable, an unsubscribe lambda or a token callback is caught and routed to
`LifetimeErrors.Handler`, and the remaining items are still terminated (see
[Tasks and errors](Tasks-and-Errors.md#what-happens-to-exceptions)).

Both calls are meant for the main thread. Called from another thread they do not run in place: they
are queued and run on the next main-thread tick, and the editor and development builds log
[JANITOR111](Troubleshooting.md#janitor111). Registering work, reading `Token` and `CreateChild` are
stricter: off the main thread they throw `InvalidOperationException`. See [Threading](Threading.md).

### Cancel removes subscriptions too

A subscription is a registered item like any other, so `Cancel()` removes it. This is one rule
without exceptions, and it has a consequence worth planning for: cancelling the lifetime an object
listens on also stops the listening, until the object subscribes again.

The usual arrangement is therefore:

- long-lived listening is registered on the object's own lifetime (`Subscribe(handler, this)`), as
  `CoinsLabel` in the sample does in its `Awake`;
- activity that is restarted or stopped is registered in a child area, and the area is what gets
  cancelled.

`Countdown` in the sample is built this way: its `Run` area is cancelled on every restart, while
whoever listens to its `Finished` event keeps listening. See
[Stopping work](Stopping-Work.md#stop-one-object) for what happens when a whole component lifetime
is cancelled instead.

---

## What `Token` is after a `Cancel`

`Token` returns the cancellation token of the **current generation**. It is not one token for the
whole life of the object, which is the difference from `destroyCancellationToken` or a
`CancellationTokenSource` of your own. This excerpt from the package's tests shows every case on an
area (`_t.App` is the root lifetime of the test's own tree):

<!-- source: Tests/Editor/LifetimeCancelReuseTests.cs -->
```csharp
var area = _t.App.CreateChild("area");
var first = area.Token;
Assert.That(first.IsCancellationRequested, Is.False);

area.Cancel();
var second = area.Token;

Assert.That(first.IsCancellationRequested, Is.True, "the old token stays cancelled forever");
Assert.That(second, Is.Not.EqualTo(first));
Assert.That(second.IsCancellationRequested, Is.False);

area.Cancel();

Assert.That(second.IsCancellationRequested, Is.True);
```

Step by step:

- The first read returns a live token. Reading `Token` twice within one generation returns the same
  token.
- `Cancel()` cancels that token. It stays cancelled forever; it is never reset and never disposed,
  so code that still holds it can keep checking it safely.
- The next read returns a **different** token, which belongs to the new generation and is not
  cancelled.
- While the lifetime is in the middle of a `Cancel()` or `Dispose()` (inside one of its cancel
  callbacks), and after it was disposed, `Token` returns a token that is already cancelled.
- The active lifetime of a GameObject that is inactive, and every area created under that active
  lifetime, also return an already cancelled token until the object is activated again. They accept
  no work in that state (see
  [Components and scenes](Components-and-Scenes.md#what-setactivefalse-stops)).

What follows from that:

- Do not keep `lifetime.Token` in a field. After the next `Cancel()` the field holds a dead token
  and everything started with it is cancelled before it begins. Keep the lifetime, and read `Token`
  (or use `Run`, which passes the right token to your method) each time work starts.
- There is no implicit conversion from `Lifetime` to `CancellationToken`. An API that wants a token
  gets `lifetime.Token`, written out.
- The token's source is created on first use: a generation whose token is never read allocates no
  `CancellationTokenSource`. `Run`, `After` and `Every` read it.
- A callback added with `token.Register(...)` runs synchronously inside `Cancel()`, before any
  registered item is terminated.
- `Token` must be read on the main thread.

---

## The order in which things stop

One `Cancel()` or `Dispose()` runs in passes over the lifetime and everything below it:

1. **Every token is cancelled**, parents before children, siblings in the order they were created.
   Token callbacks run here.
2. **Every item is terminated**, in the reverse order: the newest sibling's subtree first, each
   child before its parent, and inside one lifetime the newest registration first.
3. The lifetimes are finished: new generations open (`Cancel`), or the lifetimes leave the tree
   (`Dispose`).

The guarantee that matters in practice is the first one: **no item is terminated while any token in
the subtree is still live**. A cleanup action therefore never runs while other work in the same
subtree still holds a live token.

The order in pass 2 is the order of **one** operation. A `Cancel()` or `Dispose()` that is called
from inside a cancel callback, on a lifetime the running operation does not cover, is a second
operation: it runs to its end at once, before the remaining items of the first one. A callback of
a child that cancels its parent is the usual case: the parent's items are then terminated before
the child's remaining ones. The token guarantee holds in that case too. See
[Re-entrancy](#re-entrancy).

The package's tests pin the full order on this tree. `Record` registers a cleanup action that writes
its label to a log when it is terminated, and `RecordToken` adds a callback to the lifetime's token:

<!-- source: Tests/Editor/LifetimeOrderTests.cs -->
```csharp
_root = _t.App.CreateChild("root");
var a = _root.CreateChild("a");
var a1 = a.CreateChild("a1");
var b = _root.CreateChild("b");

_root.RecordToken(_t.Log, "token:root");
a.RecordToken(_t.Log, "token:a");
a1.RecordToken(_t.Log, "token:a1");
b.RecordToken(_t.Log, "token:b");

_root.Record(_t.Log, "item:root:1");
_root.Record(_t.Log, "item:root:2");
a.Record(_t.Log, "item:a");
a1.Record(_t.Log, "item:a1");
b.Record(_t.Log, "item:b");
```

After `_root.Cancel()`, and identically after `_root.Dispose()`, the log is:

<!-- source: Tests/Editor/LifetimeOrderTests.cs -->
```csharp
// Tree: root -> [a -> [a1], b]. Pre-order is root, a, a1, b; the drain walks it in reverse.
private static readonly string[] FullOrder =
{
    "token:root", "token:a", "token:a1", "token:b",
    "item:b", "item:a1", "item:a", "item:root:2", "item:root:1",
};
```

When Unity destroys an object, the same passes run for the lifetimes of that object. For the
lifetime of a component whose GameObject has been active at least once, they run at a fixed point
between two Unity messages: `OnDisable`, then the tokens, then the items, then `OnDestroy` (verified
on Unity 6000.3). By the time `OnDestroy` runs there is nothing left to clean. The details are in
[Components and scenes](Components-and-Scenes.md#when-the-object-is-destroyed).

---

## Re-entrancy

A **cancel callback** is any of your code that runs inside a `Cancel()` or `Dispose()`: an
`OnCancel` action, the `Dispose()` of a disposable added with `AddTo`, the remove lambda of a paired
`Subscribe`, a callback registered on the token, a tween callback fired by the kill. The rules below
say what happens when such code calls back into the lifetime that is ending. They hold for every
kind of lifetime.

### Registering from inside a cancel callback

A lifetime that is ending accepts no work. The registration is **terminated on the spot**: the
disposable is disposed inside the `AddTo` call, the `OnCancel` action runs inside the `OnCancel`
call, a `Run` does not start, a subscription is not added. The returned handle is inactive. Nothing
is carried over into the next generation.

This is deliberate. If a registration made during a cancel survived, "stop" would start work. Work
that should follow a stop is started after `Cancel()` returns, which is exactly the
cancel-previous-then-start pattern:

<!-- source: Samples~/BasicUsage/Countdown.cs -->
```csharp
public void StartCountdown(int seconds)
{
    _run.Cancel();                                    // a countdown already running stops here
    _run.Run(ct => TickAsync(seconds, ct));
}
```

`Token` read from inside a cancel callback is already cancelled. An area created with `CreateChild`
from inside a cancel callback joins the running operation: it is born ending, registrations on it
are terminated at once, and when the operation finishes it becomes a normal usable area (after a
`Cancel`) or a disposed one (after a `Dispose`).

### Cancelling from inside a cancel callback

- `Cancel()` on the lifetime that is ending, or on one of its ancestors that is ending in the same
  operation, does nothing. The running operation already covers it, and no second generation is
  opened.
- `Cancel()` on a lifetime that is not part of the running operation runs normally, nested inside
  the callback, and finishes before the callback returns. Lifetimes below it that are already
  ending are left to the operation that is ending them.
- `registration.Cancel()` on an item of the lifetime that is ending terminates that item now, if the
  operation has not reached it yet. Every item is terminated exactly once, by whoever reaches it
  first.

### Disposing from inside a cancel callback

- `Dispose()` on an area that the running `Cancel()` covers wins: the area is drained at that moment
  and ends disposed. The outer `Cancel()` skips it from then on and never brings it back.
- `Dispose()` on the lifetime whose own `Dispose()` is running does nothing.

### Calling Cancel from ordinary code that the lifetime owns

A task body, a timer callback or an event handler is not a cancel callback: it runs while the
lifetime is active. Such code may call `Cancel()` on its own lifetime, and the cancel runs normally.
The caller's own token is cancelled by it, and the caller keeps running until its next token check
or token-aware `await`. A repeating timer whose callback cancels its own lifetime does not fire
again. A task on an active lifetime may likewise deactivate its own GameObject (see
[Pooling](Pooling.md#a-task-that-ends-its-own-use)).

### Reference

| Situation | Result |
|---|---|
| `Cancel()` on a lifetime that is already ending or is disposed | Nothing happens |
| A registration while the lifetime is ending or disposed | The item is terminated immediately; the handle is inactive |
| A registration after `Cancel()` returned | It belongs to the new generation |
| A registration on an active lifetime, or on an area under one, while its GameObject is inactive | The item is terminated immediately, with [JANITOR108](Troubleshooting.md#janitor108); `Token` is already cancelled |
| `CreateChild` while the parent is being cancelled | The child is born ending and becomes a usable area when the operation finishes |
| `CreateChild` while the parent is being disposed | The child is born ending and ends disposed |
| `CreateChild` on a disposed parent | A detached, already disposed area is returned; it never throws |
| A child area is disposed during its parent's `Cancel()` | The child ends disposed; the parent's cancel continues without it |
| A parent is cancelled during a child's `Dispose()` | The rest of the tree is cancelled; the child finishes its own dispose |
| A parent is disposed during a child's `Cancel()` | The child ends disposed, never active again |
| A component lifetime is cancelled by hand, and the object is destroyed later | The destroy still disposes the lifetime; `Cancel()` does not detach it from its owner |
| `Dispose()` on a package-owned lifetime | Ignored, with [JANITOR107](Troubleshooting.md#janitor107) |
| A stale, repeated or default `LifetimeRegistration.Cancel()` | Nothing happens |
| A cleanup action or a token callback throws | The exception is routed to `LifetimeErrors.Handler`; the operation continues |
| The error handler itself throws | Both exceptions are logged with `Debug.LogException`; the operation continues |
| `Cancel()`, `Dispose()` or `registration.Cancel()` from another thread | Queued for the next main-thread tick, with [JANITOR111](Troubleshooting.md#janitor111) |

The rules for an `OwnedEvent` that is invoked while its subscribers are being removed are in
[Events](Events.md#what-invoke-does). What happens to the lifetime of a live object when the
category it was placed in is disposed is in
[Organizing work](Organizing-Work.md#when-a-category-is-disposed).

---

## What each kind of stop does

| Action | Tasks and timers (`Run`, `After`, `Every`) | Tweens (`AddTo`) | Coroutines (`StartCoroutine`) | Subscriptions, disposables, `OnCancel` actions | The lifetime afterwards | The GameObject |
|---|---|---|---|---|---|---|
| `lifetime.Cancel()`, on any lifetime | The token is cancelled. A task stops at its next token-aware `await`, without a log. A timer does not fire again | Killed, or completed when registered with `TweenCancelMode.Complete` | Stopped | Subscriptions removed, disposables disposed, actions run | Usable: a new generation. Tokens and handles from before are dead | Untouched |
| `area.Dispose()`, on areas only | Same | Same | Same | Same | Ended for good, with every area below it. Later registrations are terminated at once. Lifetimes of live objects placed in it are cancelled and moved under their scene lifetime | Untouched |
| `Destroy(gameObject)` | Same, for the component, GameObject and active lifetimes of the object and the areas under them | Same | Same. Unity also stops every coroutine the object hosts | Same | Disposed | Destroyed at the end of the frame |
| `SetActive(false)` | Only work on the active lifetime, and on areas created under it, stops. Work on the component lifetime keeps running | The same split | Unity stops every coroutine hosted on the deactivated object, whichever lifetime it was bound to. A coroutine bound to the active lifetime and hosted elsewhere is stopped by the lifetime | Those on the active lifetime are removed. Those on the component lifetime stay | The active lifetime opens a new generation, used by the next activation | Inactive, alive |
| `SceneLifetimes.Dispose(scene)` or `DisposeAll()` | Everything owned by the scene stops (by every loaded scene for `DisposeAll`) | Same | Same | Same | The scene lifetime and everything under it are disposed, including objects of the scene that were placed in a category elsewhere and objects that were moved into the scene after their lifetimes were created. `App` and DontDestroyOnLoad objects are untouched | Alive until the unload destroys it |
| `registration.Cancel()` | Not available: these calls return nothing. Give the task its own area | Not available: `AddTo` returns the tween. Call `tween.Kill()` | That coroutine only | That one subscription, disposable or action only | Unchanged | Untouched |
| The application quits, or Play Mode ends | Everything stops | Same | Same | Same | `App` and every lifetime are disposed | As Unity decides |

Notes on the table:

- A task "stops at its next token-aware `await`" because C# code cannot be interrupted from outside.
  An `await` that was not given the token resumes after the cancel and runs the code that follows
  it. See [Tasks and errors](Tasks-and-Errors.md#what-a-token-less-await-does).
- A scene `Cancel()` (`SceneLifetimes.Get(scene).Cancel()`) is the first row applied to a scene
  lifetime. It reaches an object that was moved into the scene. It does not reach an object of that
  scene whose lifetime was placed in a category outside the scene; `Dispose(scene)` does. See
  [Organizing work](Organizing-Work.md#scene-membership).
- A task or a timer that is still pending when its lifetime is cancelled costs one exception object
  on the following frame; see [Tasks and errors](Tasks-and-Errors.md#what-happens-to-exceptions).
- The tween column is the DOTween integration; see [DOTween](DOTween.md).

---

## See also

- [Stopping work](Stopping-Work.md): the same model as tasks, one example per kind of stop
- [Components and scenes](Components-and-Scenes.md): component, GameObject, active and scene
  lifetimes
- [Organizing work](Organizing-Work.md): areas, categories and placing objects in them
- [Tasks and errors](Tasks-and-Errors.md): `Run`, `After`, `Every` and the error handler
- [Threading](Threading.md): what is allowed off the main thread
- [Troubleshooting](Troubleshooting.md): one section per `JANITOR1xx` ID
