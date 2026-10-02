# Architecture

This is the design document for the Janitor package, written against the shipped code. It is the
page to read before reviewing the source or adding a feature. The user-facing pages explain *how to
use* the package; this one explains *why it is shaped the way it is* and *what the shape
guarantees*.

- Usage: [index](../index.md), [Concepts](../Concepts.md), [Stopping work](../Stopping-Work.md),
  [Events](../Events.md), [Components and scenes](../Components-and-Scenes.md),
  [Diagnostics](../Diagnostics.md)
- Decisions:
  [ADR-001 The Janitor brand and the `Lifetime` type](Decisions/ADR-001-Janitor-Brand-And-The-Lifetime-Type.md),
  [ADR-002 Reusable `Cancel` versus terminal `Dispose`](Decisions/ADR-002-Reusable-Cancel-Versus-Terminal-Dispose.md),
  [ADR-003 Tweens are not recyclable](Decisions/ADR-003-Tweens-Are-Not-Recyclable.md),
  [ADR-004 Opt-in scene call and no StopAll broadcast](Decisions/ADR-004-Opt-In-Scene-Call-And-No-StopAll-Broadcast.md),
  [ADR-005 No removal lines in user code](Decisions/ADR-005-No-Removal-Lines-In-User-Code.md)

---

## 1. Problem and goals

In a Unity project, work has no declared owner. A task, a tween, a coroutine or a subscription is
started in one method, and whether it stops depends on somebody remembering a matching line in
`OnDestroy`, `OnDisable` or a teardown method. The engine adds two problems of its own: it destroys
the objects of a scene in an order the game does not control and raises no event before it starts,
and it offers no general way to say "stop everything in this part of the game" without bookkeeping
in every class.

The failure classes that follow from this are the requirements list of the package. They are
described generically here, and each one is mapped to the mechanism that removes it in
[section 12](#12-failure-classes-and-the-mechanism-that-removes-each).

- **Work that outlives its owner.** An `async` continuation resumes on a destroyed object, a tween
  keeps writing to a target that is gone, a delayed callback fires after the scene changed.
- **A forgotten removal line.** A handler stays subscribed to a publisher that lives longer than the
  listener, so it fires on a dead object, or fires once per time the listener was opened.
- **A recycled handle acting on someone else's object.** A pooled tween, a pooled GameObject or a
  reused token source is referenced by a field that was valid for the previous user; cleanup through
  that field stops the next user's work.
- **Teardown order during a scene unload.** Objects are destroyed one by one while the work of the
  objects not yet destroyed still runs against the ones already gone.
- **Re-entrancy during cancel.** A cleanup action starts new work, cancels a sibling, disposes its
  own parent or raises an event whose handlers are being removed at that moment.

**Goals**

- **G1. The registered form is as short as the raw form.** A correct call (`tween.AddTo(this)`,
  `this.Run(...)`, `evt.Subscribe(handler, this)`) must not cost more code than the incorrect one,
  or it will not be used.
- **G2. Automatic cleanup.** A component's work ends when the component is destroyed, with no
  `OnDestroy` in user code.
- **G3. One general stop mechanism.** `Cancel()` on any lifetime stops everything under it and
  leaves it usable; a registration handle stops one item. The package prescribes no game structure.
- **G4. One model for every kind of work.** UniTask work, timers, DOTween tweens, coroutines,
  `OwnedEvent`, `UnityEvent`, SignalBus, C# events and `IDisposable` all register the same way.
- **G5. Scene awareness.** One line before a scene load ends all scene-owned work before Unity
  destroys anything. Without the line, cleanup degrades to the engine's order and the diagnostics
  say so.
- **G6. Safety under reuse.** Tokens and handles are stamped with a generation, so anything from
  before a cancel is harmless afterwards.
- **G7. Teardown never throws.** `Cancel`, `Dispose` and item cancellation route every failure with
  context and continue.
- **G8. Bounded memory.** In steady state the package allocates nothing per `Cancel`, per event
  invoke, or per registration of a task, a timer, a tween, an owned-event subscription or a
  disposable, and no list grows with finished work.
- **G9. Diagnostics with no player cost.** An editor window shows the tree; every warning has a
  stable ID and a documentation section.
- **G10. A core without framework dependencies.** DOTween and Zenject support are optional
  assemblies.
- **G11. No removal lines.** Every subscription API takes its owner in the same call.

**Non-goals in 0.1.0**

- Messaging. There is no event bus, no signals and no commands. `OwnedEvent` is a field on its
  publisher: no global registry, no routing by type, no asynchronous dispatch.
- Seeing work that was not started through the package.
- A broadcast "stop all" message
  ([ADR-004](Decisions/ADR-004-Opt-In-Scene-Call-And-No-StopAll-Broadcast.md)).
- Game-structure concepts. The package has areas and owners, and nothing that models a part of a
  particular game.
- Asynchronous or graceful teardown. Teardown is synchronous.
- Wrappers around `SceneManager`, Addressables or a DI scene loader. The scene call works with any
  loader because it only has to run before it.
- Lifetimes outside Play Mode, registration from worker threads, `Task` or `Awaitable` based APIs,
  ownership of Addressables handles, a diagnostics UI in players, events that serialize in the
  Inspector, and pooling of user objects.

The full list for users is in [Limitations](../Limitations.md).

---

## 2. Shape

The package is **a tree of reusable lifetimes**. Every lifetime has a current *generation*: a lazily
created token source plus the items registered since the last cancel. `Cancel()` ends the current
generation of a lifetime and of all its descendants and opens fresh ones; `Dispose()` ends a subtree
for good. Thin adapters give each kind of work one registration call.

```text
App                          Lifetime.App: disposed when the session ends
  Scene "Main"               one per loaded scene: disposed by SceneLifetimes.Dispose / DisposeAll
    Component GameplayRoot   component lifetime: disposed when the component is destroyed
      "Gameplay"             area: created by user code with CreateChild
        "Popups"             area used as a category
          Active ShopPopup   active lifetime placed in the category: cancelled on SetActive(false)
        "Combat"             area used as a category
    Component Countdown      component lifetime
      "Run"                  area for one operation that restarts
    Active CoinPickup        active lifetime with the default parent (the scene)
  Component SfxPlayer        DontDestroyOnLoad object: a child of App
  "SceneFlow"                area created under App by a service that outlives scenes
```

**Ownership rule.**

- *Package-owned* lifetimes (App, scene, component, GameObject and active lifetimes) are disposed
  only when their owner goes away. `Cancel()` works on them; `Dispose()` is ignored with
  `JANITOR107`.
- *Areas* are created by user code with `parent.CreateChild()`. Both `Cancel()` and `Dispose()` work
  on them. There is no public constructor and no default parent: a hidden default parent is how work
  silently outlives a scene.
- An object's own lifetime may be *placed* in an area (a category) by passing the area to the first
  `GetLifetime(parent)` or `GetActiveLifetime(parent)`. The area's `Cancel()` then reaches it; the
  area's `Dispose()` cancels it and moves it back under its scene, and never kills it.

```text
 game code
     |   this.Run(..)   tween.AddTo(this)   evt.Subscribe(handler, this)   area.Cancel()
     v
 adapters, one per kind of work
     tasks and timers | OwnedEvent | paired Subscribe | UnityEvent guard | IDisposable | coroutine
     DOTween (optional assembly)             SignalBus (optional assembly)
     |   each stores: the item + a static function that ends it
     v
 core model
     Lifetime (state, generation, token source, entry list, tree links)
     LifetimeTeardown (mark, signal, drain, finalize)       LifetimeRegistration (one item)
     |
 LifetimeTree (one per play session)
     App root, owner index, scene index, main-thread guard, marshal queue, error dispatch
     ^
     |   destroy tokens, OnDisable, sceneUnloaded, session start and exit
 Unity binding
     ComponentLifetimes, ActiveLifetimeTrigger, SceneBinding / SceneLifetimes, PlayModeBootstrap
```

Three rules hold this together.

**The core does not know what it is cleaning.** A lifetime stores entries of one shape: up to two
object references, a delegate, and a cached static function that ends the item. Killing a tween,
removing a listener and disposing a disposable are all "call the function with the stored
arguments". `Cancel()` never inspects a type, and no assembly reference to DOTween or Zenject exists
in the core.

**Integrations use the public API only.** The DOTween and Zenject assemblies are built on
`Lifetime.OnCancel`, `CreateChild`, `GetLifetime`, `SceneLifetimes` and the public diagnostics hook.
The core grants `InternalsVisibleTo` to the editor and test assemblies only. Anything a third
integration needs is therefore already public.

**State is per session, not per process.** The only public statics are `Lifetime.App`,
`SceneLifetimes` and `LifetimeErrors`, which exist because extension methods need ambient access and
because scenes and quitting are process-global facts. Behind them sits one `LifetimeTree` that is
recreated at the start of every play session, so nothing leaks from one session into the next when
domain reload is disabled. When the session ends the tree is shut down, and in the editor the static
is cleared, so a call outside Play Mode throws instead of reaching a dead tree
([section 7](#7-the-unity-binding)). Tests construct their own trees.

---

## 3. Assemblies

| Assembly | Folder | Namespace | May reference | Compiled when |
|---|---|---|---|---|
| `Ecanakli.Janitor` | `Runtime/` | `Ecanakli.Janitor` | `UniTask`, the engine | Always |
| `Ecanakli.Janitor.DOTween` | `DOTween/` | `Ecanakli.Janitor` | The core, `UniTask`, `DOTween.dll` | `DOTWEEN` is defined, or `com.demigiant.dotween` is installed as a package (`versionDefines` sets `ECANAKLI_JANITOR_DOTWEEN`) |
| `Ecanakli.Janitor.DependencyInjection.Zenject` | `DependencyInjection/Zenject/` | `Ecanakli.Janitor.DependencyInjection` | The core, `UniTask`, `Zenject`, `Zenject-usage.dll` | `ECANAKLI_JANITOR_DI_ZENJECT` is defined: by `versionDefines` for `com.svermeulen.extenject` 9.0.0 or newer, or by `Tools/Janitor/Zenject Integration` |
| `Ecanakli.Janitor.Editor` | `Editor/` | `Ecanakli.Janitor.EditorTools` | The core | In the editor |
| `Ecanakli.Janitor.TestUtilities` and six test assemblies | `Tests/**` | test namespaces | The assembly under test, plus the matching library | `UNITY_INCLUDE_TESTS` plus the gate of the assembly under test |
| `Ecanakli.Janitor.Samples.*` (three) | `Samples~/*` | `Ecanakli.Janitor.Samples.*` | Like the runtime assembly they demonstrate, plus `UnityEngine.UI` and `Unity.TextMeshPro` | Like the runtime assembly they demonstrate |

Dependency direction:

```text
game  ->  Ecanakli.Janitor.DOTween                        ->  Ecanakli.Janitor  ->  UniTask
                                                          ->  DOTween.dll
game  ->  Ecanakli.Janitor.DependencyInjection.Zenject    ->  Ecanakli.Janitor
                                                          ->  Zenject
          Ecanakli.Janitor.Editor                         ->  Ecanakli.Janitor
```

Rules that the layout follows:

- **The core references UniTask and nothing else.** `package.json` declares no dependency, because
  the package manager cannot resolve a git dependency of a git package; UniTask is a prerequisite
  the README lists.
- **Every assembly that compiles into a player sets `overrideReferences`** and lists its precompiled
  references, so an auto-referenced DLL in the consuming project cannot leak in. `DOTween.dll` is
  listed only by the DOTween assembly; the core can never pick it up.
- **The DOTween assembly uses only the API of `DOTween.dll`.** DOTween's module scripts and
  UniTask's DOTween support compile into assemblies that a package cannot reference in a default
  install.
- **Assembly names may name a framework; namespaces do not.** No namespace segment is `DOTween`,
  `Zenject` or `Editor`: inside `Ecanakli.Janitor`, such a child namespace would capture those
  identifiers, and `DOTween.Kill(...)` or a class deriving from `Editor` would stop resolving. The
  DOTween assembly therefore uses the root namespace `Ecanakli.Janitor`, the Zenject installer lives
  in `Ecanakli.Janitor.DependencyInjection`, and the editor namespace is `EditorTools`.
- **No type is named `Janitor`.** A type with the name of its namespace produces `CS0118`. The
  window class is `JanitorWindow` and the main type is `Lifetime`
  ([ADR-001](Decisions/ADR-001-Janitor-Brand-And-The-Lifetime-Type.md)).
- **Extension methods the user calls sit in `Ecanakli.Janitor`,** including `tween.AddTo` and the
  SignalBus `Subscribe`. One `using` line is enough for every registration call; only the Zenject
  installer needs `Ecanakli.Janitor.DependencyInjection`.
- **Every public member of the three runtime assemblies has XML documentation.** The development
  build treats a missing one (`CS1591`) as a compile error.

---

## 4. The core model

### `Lifetime`

| Data | Meaning |
|---|---|
| State | `Active`, `Cancelling`, `Disposing`, `Disposed`. A transition never moves backwards, with one exception: `Cancelling` returns to `Active` at the end of the operation that set it |
| Operation id | Which running `Cancel` or `Dispose` currently owns this node |
| Generation | An integer, incremented when a cancel or a dispose finalizes |
| Token source | One per generation, created the first time `Token` is read. It is cancelled and dropped, never disposed and never reused |
| Entries | The items registered in the current generation (`EntryList`) |
| Tree links | Parent, first and last child, previous and next sibling. Intrusive, children in creation order |
| Kind and ownership | App, scene, component, GameObject, active or area; and whether the package owns it |
| Unity owner | The owning object, its index key, and the registration on its destroy token |
| Label | An optional name, or the member and line of the `CreateChild` call |

There is no linked token source anywhere. A child does not link its token to its parent's; the
teardown walks the tree and cancels each source. That removes an allocation per child and the unlink
bookkeeping that linked sources need.

`Token` returns an already cancelled token, without creating a source, while the lifetime is not
`Active`. A token requested in the middle of a teardown can therefore never be one that nobody
cancels. It also returns a cancelled token for an active lifetime, and for an area below one, while
the GameObject is inactive (see the activity gate below).

### Entries

An entry is a struct in an array-backed doubly linked list with a free list:

<!-- signature -->
```csharp
internal struct EntrySlot
{
    internal object A;                  // the item, or the first state value
    internal object B;                  // the second state value
    internal Delegate Fn;               // the user's action, or the remove delegate
    internal Delegate Probe;            // optional "has this finished by itself"
    internal EntryTerminate Terminate;  // cached static function that ends the item
    internal EntryProbe ProbeInvoker;
    internal string Member;             // call site, for errors and the window
    internal long Aux;
    internal int Line;
    internal int Prev;
    internal int Next;
    internal int Version;
    internal int Flags;
}
```

- The head is the newest entry, so draining is last in, first out.
- Removal by `(id, version)` is O(1). Releasing a slot bumps its version, which is what makes a
  stale handle a no-op.
- The array is kept across generations and grows by doubling, so a lifetime that is cancelled and
  refilled allocates nothing.
- `Terminate` is one of a few static delegates cached per closed generic type (`EntryInvoker<T>`).
  It casts `A`, `B` and `Fn` back and calls them. No closure exists per registration.

**Keeping the list bounded.** Items that end by themselves must not pile up in a long-lived
lifetime.

- A task releases its own entry when it completes. The release carries the generation it was
  registered in, so a late completion is a no-op.
- A coroutine wrapper releases its entry when the routine ends or throws.
- Tweens and coroutines that are ended by something else carry a *probe*. When a registration pushes
  the count to a threshold, the lifetime sweeps: every entry whose probe reports "finished" is
  dropped without running its action. The threshold is 16, or twice the count that survived the last
  sweep, whichever is larger.
- `OwnedEvent` subscriber slots go when their owner's generation ends.

### `LifetimeRegistration`

A handle is `(lifetime, generation, slot, version)`. It is live only while the lifetime is not
disposed, the generation is current and the slot version matches. `Cancel()` on it ends that one
item. It deliberately does not implement `IDisposable`: disposing a `CancellationTokenRegistration`
merely unregisters a callback, while this handle *ends* the item, and the two should not look alike.

### The single registration path

Every adapter ends in `Lifetime.Register`. It checks the thread, and then:

- If the lifetime is not `Active`, the item is ended on the spot and a default handle is returned. A
  disposed scene lifetime whose scene is still loaded also raises `JANITOR109`.
- If the lifetime has an *activity gate* and the gate's GameObject is inactive, the same happens,
  with `JANITOR108`.
- Otherwise the entry is added, and the sweep runs if the threshold was reached.

**The activity gate.** An active lifetime is its own gate, and every area created below it inherits
the gate. Without that, `GetActiveLifetime().CreateChild()` would be a way around the rule that an
inactive object accepts no work. A lifetime without a gate pays one field read for the check; a
gated one pays one `activeInHierarchy` call per registration. `Run`, `After` (also with zero
seconds), `Every`, every `Subscribe` form and `StartCoroutine` go through the same check, and each
refusal is counted for the window.

"Not `Active`" includes `Cancelling`. Work registered from inside a cleanup action therefore lands
in the generation that is ending and is ended immediately, which is what keeps a cancel from being
undone by its own callbacks.

### The disposed sentinel

Each tree has one lifetime that is permanently disposed. It is returned for an owner that Unity has
destroyed or is destroying, for an object that is not in a scene (a prefab asset), and for an object
whose scene lifetime is being or has been disposed. Registrations on it end at once, so no caller
needs a null check. A C# `null` owner is different: it is a programming error and throws
`ArgumentNullException`, after the item was ended.

### `LifetimeTree`

The tree composes the App root, the sentinel, the owner index (instance id to lifetime), the scene
index (scene handle to lifetime, plus the handles known to be persistent), the main-thread guard,
the marshal queue, the teardown, a pool of snapshot buffers, the delay provider behind the timers,
and error dispatch. Production code reaches it through one internal static that the session
bootstrap assigns.

---

## 5. Teardown in passes

`Cancel` and `Dispose` are the same algorithm over a subtree. It runs in four passes because two
guarantees need it: every token is cancelled before any item is ended, and a registration made
during the operation is ended with it.

### `Cancel(L)`

`Cancel` returns immediately unless `L` is `Active`.

1. **Mark.** The subtree is walked parent first into a pooled snapshot buffer, and every `Active`
   node is marked `Cancelling` with the id of this operation. A node that another operation already
   owns (`Cancelling` elsewhere, `Disposing`, `Disposed`) is skipped together with its subtree. No
   user code runs in this pass.
2. **Signal.** Each marked node's token source is cancelled, parent first. Callbacks registered on
   the tokens run synchronously here. An exception from them is routed with the source
   `TokenCallback`; an `AggregateException` is flattened and each inner exception routed.
3. **Drain.** The snapshot is walked backwards, so the deepest and newest lifetimes go first. Each
   node pops its entries newest first and runs each terminate function in its own `try`/`catch`. A
   node stops draining as soon as another operation has taken it over.
4. **Finalize.** For every node this operation still owns: the generation is incremented, the token
   source is dropped, and the state returns to `Active`. No user code runs in this pass, and it runs
   in a `finally` block.

### `Dispose(L)`

The same passes with `Disposing`, and three differences.

- **Mark.** A node that is `Cancelling` under another operation is *upgraded* to `Disposing`:
  dispose wins. A placed object lifetime whose owner is alive, and whose own scene or App is not
  what is being disposed, is marked for *re-homing*: it is cancelled, and its subtree gets cancel
  semantics. When a scene lifetime is being disposed, the scene's membership entries (see
  [section 7](#7-the-unity-binding)) are marked as well.
- **Finalize, disposed nodes.** The state becomes `Disposed`; the node is unlinked from its parent,
  removed from the owner index and from any scene membership, and its registration on the owner's
  destroy token is released. A scene lifetime stays in the scene index until the scene is unloaded,
  so `SceneLifetimes.Get` returns the disposed lifetime and later registrations are refused.
- **Finalize, re-homed nodes.** The node finishes as a cancel and is relinked under the lifetime of
  the scene its GameObject is in now (App for DontDestroyOnLoad). `JANITOR114` records it, and the
  lifetime is marked as re-homed: it cannot be placed in a category again. If that home is itself
  going away, the node is disposed together with the areas below it.

### What the order guarantees

Within one operation, **every token in the subtree is cancelled before any item is ended, and items
are then ended deepest lifetime first and newest item first.**

- Tokens first means that any code that runs during the drain (a terminate function, an event
  handler, a continuation that DOTween invokes inline) already sees every related token cancelled.
  Nothing can resume into a subtree that is half torn down.
- Deepest and newest first mirrors construction: what was set up last, on top of the rest, is taken
  down first.
- The guarantee is per operation. A `Cancel` or `Dispose` that a cleanup action starts is an
  operation of its own: it runs to its end at once, before the remaining items of the operation that
  is running.

A token source is cancelled and never disposed. A plain source (no timer, no wait handle) holds no
unmanaged state, and a token still held by in-flight code must stay valid and cancelled forever.

### Re-entrancy

| Situation | Behaviour |
|---|---|
| `Cancel` on a lifetime that is `Cancelling`, `Disposing` or `Disposed` | Nothing happens; the running operation already covers it |
| A task or handler registered on `L` calls `L.Cancel()` from ordinary code | Runs normally. The caller's own token is cancelled and it continues until its next token check |
| A registration while the target is `Cancelling` | Ended immediately. Start new work after `Cancel()` returns |
| A registration after `Cancel()` returned | Goes into the new generation |
| `CreateChild` while the parent is in an operation | The child joins that operation: it is born `Cancelling` and becomes `Active` at finalize, or born `Disposing` and becomes `Disposed` |
| `CreateChild` on a disposed parent | The child is born disposed and detached |
| A child's `Dispose` during its parent's `Cancel` | Dispose wins. The child is drained and disposed now; the parent's later passes skip it |
| The parent's `Cancel` during a child's `Dispose` | The disposing subtree is skipped; the rest is cancelled |
| The parent's `Dispose` during a child's `Cancel` | The child is upgraded. Its own finalize no longer owns it, so `Disposed` never flips back to `Active` |
| A category disposed while a placed object lives | The object lifetime is cancelled and re-homed |
| A component lifetime cancelled by hand, then destroyed | `Cancel` keeps the destroy registration (it belongs to the lifetime, not to a generation); the destroy disposes |
| An owner cancelled during an `OwnedEvent.Invoke`, before its handler was reached | The handler is skipped in that invoke |
| A subscribe during an `OwnedEvent.Invoke` | First delivered by the next invoke |
| A terminate function or a handler throws | Routed to `LifetimeErrors.Handler`; the operation continues |
| `LifetimeErrors.Handler` itself throws | Caught; both exceptions are logged |
| `Cancel`, `Dispose` or `registration.Cancel()` on a worker thread | Queued and run on the next main-thread tick, with `JANITOR111` |
| A stale or repeated `registration.Cancel()` | Nothing happens |

**Why teardown never throws.** It runs inside callbacks of the engine and of other libraries: a
destroy-token callback, a DI container's disposal loop (where one throwing `Dispose` skips every
remaining disposable), a DOTween callback. An exception escaping there breaks cleanup that has
nothing to do with the failing item.

---

## 6. Adapters

Each adapter is small and owns one kind of work. None of them holds state between calls except what
it stores in the owner's entry.

| Work | Entry stores | Ended by | Notes |
|---|---|---|---|
| `Run` | A pooled continuation object | The token (the entry's function is a diagnostics hook only) | The work is called at once with the generation token. Completion is observed through a delegate cached on the pooled object, not through an `async` state machine, which would allocate in debug builds. A completion on another thread hops to the main thread. A fault is routed with source `Task`, also after the generation ended; cancellation is silent. The pool is not capped: it keeps the peak number of concurrent tasks |
| `After`, `Every` | One pooled timer object for the whole timer | The token | `UniTask.Delay` in scaled or unscaled time, through a provider that tests replace with a manual clock. `After` with zero or less runs in place. `Every` stops on its first exception and has a guard against a delay that keeps completing synchronously. Seconds above 9e11 are rejected like NaN and infinity, so the delay cannot overflow. The pool keeps the peak number of concurrent timers |
| `OwnedEvent.Subscribe` | The event and the subscriber's serial number | Blanking the subscriber slot | See below |
| Paired `Subscribe` | The handler, the `add` delegate and the `remove` delegate | `remove(handler)`, exactly once | `add` runs before the entry is created; if `add` ended the owner's generation, `remove` runs immediately. A live entry with an equal handler, `add` and `remove` makes the call a duplicate, found by a scan of the owner's entries |
| `UnityEvent.Subscribe` | A guard object | `RemoveListener(guard)` | See below |
| `AddTo(IDisposable)` | The disposable | `Dispose()` | A struct is boxed once |
| `OnCancel` | The action and up to two state objects | Calling the action | The extension point; an optional probe makes one-shot items sweepable |
| `StartCoroutine` | A wrapper around the routine | `host.StopCoroutine(handle)` | See below |

**What a cancelled task or timer costs.** The token does the stopping, and UniTask reports a
cancelled await through an exception: its pooled promise is released only by `GetResult`, which
throws for a cancelled source. A pending `After`, `Every` or `Run` that is cancelled therefore costs
one exception object and one or two throw and catch on the frame after the cancel. The `Cancel()`
call itself allocates nothing.

**`OwnedEvent`.** Subscribers live in an ordered slot array inside the event; each slot holds the
handler, the owner, the owner's generation and a serial number. The owner's entry holds the event
and the serial, with one shared static function, which is why a subscription allocates nothing
beyond the caller's delegate. `Invoke` iterates by index up to the count at entry. A slot is skipped
when it was removed, when its owner is not `Active`, or when the owner's generation differs. A
removal during an invoke blanks the slot in place and takes effect at once; order-preserving
compaction waits until no invoke is on the stack. This differs from a C# multicast delegate on
purpose: if an earlier handler ends a later subscriber, the later handler must not run on a dead
object. Each handler runs in its own `try`/`catch`. A nested invoke is safe; when the depth passes
64 the invoke is refused and reported (`JANITOR115`), which turns event ping-pong into an error
instead of a stack overflow.

**`UnityEvent`.** `UnityEvent` dispatches over a snapshot of its listeners: a listener removed
during an invoke still runs in that invoke (verified on Unity 6000.3). The adapter therefore adds a
small guard as the listener, not the handler. The guard calls the handler only while its
subscription is live, its owner is `Active` in the same generation, and the handler's target object
has not been destroyed. All subscription sources thus share one rule: *a subscription removed during
a dispatch is not delivered in that dispatch.* The cost is one guard and one delegate per
subscription. A `UnityEvent` cannot carry extra state, so duplicate detection scans the owner's
entries.

**Coroutines.** The package wraps the routine in its own enumerator and steps it by hand. It
therefore knows when the routine ended, can route an exception with source `Coroutine`, and never
calls `StopCoroutine` with a null handle (Unity returns null for a routine that never yields). A
pre-check refuses a missing, inactive or destroyed host before Unity can log its own error. Unity
stops a coroutine when its host is deactivated without telling the enumerator, so the host's
GameObject gets the hidden trigger component, which counts deactivations; the wrapper's probe
reports "finished" once the count has changed, and the sweep reclaims the entry even if the host is
active again.

---

## 7. The Unity binding

### Component, GameObject and active lifetimes

`this.GetLifetime()` looks the component up in the owner index by instance id and creates the
lifetime on first access.

- **The parent** is the lifetime of the GameObject's scene (App for a persistent scene), or the
  category passed in the first call.
- **The destroy signal** for a GameObject that is active in the hierarchy is the component's
  `destroyCancellationToken`, read at creation. Reading it early matters: a token first read inside
  `OnDestroy` is never cancelled. For an inactive GameObject the binding registers on two signals,
  the component's token and UniTask's destroy trigger on the GameObject, and the first one to fire
  disposes the lifetime and releases the other. The second signal is needed because Unity never
  cancels the destroy token of a component that never ran `Awake`. This is the normal path for
  objects that a DI container creates inactive, injects and then activates. One case remains: a
  component on an object that is never activated is not disposed by destroying the component alone;
  destroying the GameObject or disposing the scene ends it.
- **A first access during the owner's destruction** returns the disposed sentinel and never throws.
  Every failure while reading a token or adding a component is caught; the expected ones (a
  destroyed object, a disposed token source, a refused `AddComponent`) stay silent.
- **The registration on the destroy token belongs to the lifetime object**, not to a generation, so
  it survives any number of `Cancel()` calls.

`gameObject.GetLifetime()` always uses the UniTask trigger. An owner passed as a `Component`
resolves to its component lifetime when it is a `MonoBehaviour`, otherwise to the lifetime of its
GameObject.

`GetActiveLifetime()` adds one `ActiveLifetimeTrigger` to the GameObject, hidden in the Inspector.
Its `OnDisable` calls `Cancel()` on the active lifetime; nothing happens in `OnEnable`, because the
next registration lands in the new generation by itself. The active lifetime hangs directly under
the scene lifetime, App or its category. Registering on it, or on an area below it, while the
GameObject is inactive is refused.

The trigger has to tell a deactivation from everything else that makes Unity call `OnDisable`.
Measured on Unity 6000.3, inside `OnDisable`:

| Cause | `enabled` | `activeInHierarchy` |
|---|---|---|
| `SetActive(false)` on the object or on a parent | true | false |
| `DestroyImmediate` | true | false |
| `Destroy` of the object, of a parent or of the component | false | true |
| The component's own `enabled = false` | false | true |

So the trigger reads `enabled`. When it is true, the GameObject was deactivated, and the
deactivation is counted for the coroutine bookkeeping. When it is false, Unity is destroying the
object or something disabled the trigger itself (a loop over every `Behaviour` reaches it), and the
two cannot be told apart at that point. In every case the active lifetime is cancelled, which is
what makes the work end before any component's `OnDestroy`. A trigger that was disabled would miss
the next deactivation, so it remembers that and enables itself again the next time the package
touches it: `GetActiveLifetime()`, a registration on the active lifetime or on an area below it, or
a coroutine start.

### Categories, re-homing and scene membership

- **The first access decides the parent.** Anything that touches the object's lifetime first
  (`this.Run`, `tween.AddTo(this)`, an injected `Lifetime`) fixes the default parent. A later call
  with another parent returns the existing lifetime unchanged and raises `JANITOR113`. Reparenting
  silently would move work that was already registered under one owner to another, and change which
  `Cancel()` reaches it.
- **Disposing a category does not kill the objects in it.** An object's lifetime backs every later
  `this.Run(...)` on that object; disposing it while the object lives would leave a broken object
  with no error. The lifetime is cancelled and re-homed under its scene.
- **Scene membership.** A category can live outside the scene's subtree (for example under App). An
  object lifetime placed there gets a dispose-only membership entry in its scene's lifetime, so
  `SceneLifetimes.Dispose` still disposes it before Unity destroys the object. A scene `Cancel()`
  does not follow membership: for cancellation the object belongs to its category.

### Scenes and the session

- `SceneLifetimes.Get(scene)` creates the scene lifetime on first use, as a child of App.
- A scene handle that is loaded but not listed by the scene manager (DontDestroyOnLoad, preview
  scenes) is marked persistent and maps to App. A scene that is still loading is not mistaken for
  one.
- **Pre-pass.** Before a scene lifetime's own `Cancel` or `Dispose`, the binding brings the tree in
  line with where the objects are now. An object that has moved to another scene or to
  DontDestroyOnLoad since its first access is reparented to its current home first, so the operation
  does not touch it. An object that has moved *into* this scene (`SetParent`,
  `MoveGameObjectToScene`) is adopted: one scan of the owner index finds the object lifetimes whose
  GameObject is in the scene but which hang elsewhere. A plain object lifetime becomes a child of
  the scene; a placed one keeps its category, and only its scene membership moves. The scan is
  linear in the number of object lifetimes and allocates nothing.
- **`DisposeAll`** takes a snapshot of the loaded scenes and disposes their lifetimes, the last
  loaded first. Scenes that are still loading are skipped.
- **When the scene call is skipped,** three fallbacks remain: each object lifetime is disposed by
  its own destroy token; a Zenject SceneContext with the installer disposes the scene lifetime first
  among its disposables; and the `sceneUnloaded` handler disposes whatever is still alive. The last
  two report `JANITOR104`. `sceneUnloaded` is raised after `OnDestroy` (verified on Unity 6000.3),
  which is why it is a fallback and not the mechanism.
- **Session start** (`RuntimeInitializeOnLoadMethod`, subsystem registration): shut down the
  previous tree, create a new one, re-subscribe the engine events and reset `LifetimeErrors.Handler`
  to the default.
- **Session end.** Three signals run the same shutdown, which disposes App and everything below it:
  `Application.exitCancellationToken`, `Application.quitting`, and in the editor the return to Edit
  Mode. Whichever arrives first does the work; the shutdown is idempotent, so the others find
  nothing to do. No single signal is trusted, because an exit token read at session start can be
  stale. On the return to Edit Mode the engine handlers are removed and the default tree is cleared,
  so `Lifetime.App`, `SceneLifetimes` and the component accessors throw `InvalidOperationException`
  there, with domain reload on or off.
- **A script reload during Play Mode** (editor only) wipes the statics and does not run the session
  start again. The first call that needs a tree finds none while the application is playing and
  starts a new session on the spot, with one console warning: every lifetime of the old tree and the
  work it owned are gone. Components that register in `OnEnable` register again, because Unity
  re-enables them after the reload.
- **`JANITOR109`** (a registration, or a `CreateChild`, on a disposed scene lifetime while the scene
  is still loaded) is reported once per scene lifetime and never while the session is ending.

---

## 8. Integrations as gated assemblies

Both integrations follow the same shape: a folder with its own assembly definition, a
`defineConstraints` gate, a `versionDefines` entry that sets the define when the library is a UPM
package, and no reference from the core.

### DOTween

- `tween.AddTo(owner, mode)` calls `SetRecyclable(false)` and registers the tween through
  `OnCancel<Tween>(tween, kill-or-complete, isFinished)` with static delegates. It never reads or
  writes `onKill`, `onComplete` or any other callback
  ([ADR-003](Decisions/ADR-003-Tweens-Are-Not-Recyclable.md)).
- A tween added to an owner that is not `Active` is always killed, even in `Complete` mode: it never
  ran for that owner, and completing it would fire callbacks in the middle of a teardown.
- `AwaitCompletionAsync` is backed by a pooled UniTask source. It saves the tween's `onComplete` and
  `onKill`, installs its own, runs the originals first and restores them when the await is consumed
  (unless something else was chained on top meanwhile). A kill without a completion cancels the
  await; so does a completion after the lifetime's generation ended. Code after the `await`
  therefore never runs for a tween that was stopped.
- The lifetime overload does not depend on those callbacks to end the await. Its entry kills the
  tween and then settles every await that is still armed on it, so a cancel of the lifetime ends the
  await even when user code replaced `onKill` after the call, and the await of a tween nested in a
  Sequence (which DOTween will not kill) is cancelled at once. The contract for callers is to set
  tween callbacks before the call: an `onComplete` set afterwards replaces the await's, a normal
  completion is then seen only through the automatic kill, and the await ends as cancelled.
- The token overload costs one token registration. A cancellation raised on another thread is moved
  to the main thread before the tween is touched.
- A tween that is still active right after its kill (a tween nested in a Sequence, on which DOTween
  ignores `Kill`) is reported through the public diagnostics hook as `JANITOR102`.

### Zenject

- `LifetimeInstaller.Install(container)` finds the context behind the container through the
  context's own binding and captures the context lifetime: `Lifetime.App` for the ProjectContext,
  the scene lifetime for a SceneContext, the context component's lifetime for a GameObjectContext,
  and the nearest enclosing context (or App) for a container that is not a context.
- `Lifetime` is bound as a transient factory. A `MonoBehaviour` injectee receives its component
  lifetime. Any other injectee receives a new area under the context lifetime, labelled with the
  injectee's type. These are ordinary areas made with `CreateChild`, so they stay until the context
  ends or the service disposes them, and the growth diagnostic counts them. An object that a factory
  creates at run time therefore disposes its injected lifetime when it is done, as `OfferWatcher` in
  the Zenject Usage sample does ([Zenject](../Zenject.md)).
- A SceneContext or a GameObjectContext that never calls `Install` answers its plain services with
  the binding of the context above it, so their lifetimes outlive the context they belong to.
  `JANITOR112` reports it, once per scene and once per GameObject name.
- For a SceneContext the installer also binds a disposable with the highest execution order, so the
  scene lifetime ends before any service of that container is disposed. It never throws. Its
  constructor is called only through the container's reflection and is marked to survive managed
  code stripping.
- `bus.Subscribe<TSignal>(handler, owner)` registers on the owner first and subscribes on the bus
  second, so a refused owner never touches the bus and a failing subscribe leaves nothing behind.
  SignalBus holds one subscription per signal and handler, so the integration keeps its own table,
  held weakly per bus: the same handler with the same owner is a duplicate (`JANITOR105`), and with
  another owner it throws.
- `Tools/Janitor/Zenject Integration` writes the define to every build target for a Zenject that
  sits under `Assets`. It refuses when no assembly named `Zenject` exists, and explains itself when
  a UPM Extenject already activates the integration.

---

## 9. Errors and threading

**Errors.** Everything the package catches goes through one dispatch function. It drops
`OperationCanceledException`, calls `LifetimeErrors.Handler` with a context (source, owner object,
lifetime label, member and line of the registration), and if the handler itself throws it logs both
exceptions. The default handler logs a `LifetimeWorkException` with the owner as the log's context
object. Call sites are recorded with `[CallerMemberName]` and `[CallerLineNumber]` only;
`[CallerFilePath]` is not used because it would embed absolute source paths in player metadata.

**Threading.** The package is main-thread only and has no locks on its hot paths.

- Registration, `Token`, `CreateChild` and `Subscribe` compare the current thread with UniTask's
  main thread id and throw `InvalidOperationException` on a mismatch. The item is left untouched.
- `Cancel`, `Dispose` and `registration.Cancel()` must never throw, so off the main thread they are
  put on a queue that drains on the next player-loop update.
- `OwnedEvent.Invoke` checks the thread in the editor and in development builds only; it is a hot
  path.
- A task or timer that completes on another thread posts its bookkeeping and its error routing to
  the main thread.

See [Threading](../Threading.md).

---

## 10. The editor diagnostics path

```text
core call sites  ->  recording hooks  ->  recorded data  ->  snapshot  ->  view model  ->  window
(Runtime/)           (Runtime/Diagnostics, editor only)                    (Editor/)
```

- **Hooks.** The core calls static methods on `LifetimeDiagnostics` at a handful of points: a
  lifetime created, a child attached, a registration added or refused, a generation cancelled, a
  lifetime disposed, a task that outlived its generation, a re-home. Every hook is
  `[Conditional("UNITY_EDITOR")]`, so in a player the calls and the evaluation of their arguments do
  not exist, and the fields they write sit under `#if UNITY_EDITOR`.
- **Console warnings.** `DevWarnings` formats
  `[JANITOR1xx] message (see Troubleshooting#janitor1xx)` and is compiled into the editor and
  development builds. Each warning is also recorded for the window. `JANITOR115` is the one
  diagnostic that reports in every build.
- **IDs.** `DiagnosticIds` holds the sixteen IDs (`JANITOR101` to `JANITOR116`) with their titles
  and documentation anchors. The console, the window and the documentation links all read from it.
- **The public hook.** `LifetimeDiagnostics.Report(lifetime, id, message, context)` lets an
  integration assembly record a diagnostic without the editor assembly knowing that integration.
  Called from inside a cancel action with a null lifetime, it attributes the report to the lifetime
  being torn down and to the call site that registered the item. The DOTween assembly reports
  `JANITOR102` this way; the Zenject assembly reports `JANITOR104`, `JANITOR105` and `JANITOR112`.
- **Recorded data.** Per lifetime: an id, created and disposed frames, cancel count, total
  registered, refused registrations with the first call site (every refusal path counts: the
  registration itself and the early returns of `Run`, `After`, `Every`, the `Subscribe` forms and
  `StartCoroutine`). Per entry: the registration frame and an optional stack trace (off by default).
  A bounded warning list that merges repeats. A ring of the last 200 disposed lifetimes. A tracker
  for tasks whose generation ended, which schedules a scan only while such a task exists.
- **Three detections that are easy to get wrong.**
  - *Growth (`JANITOR106`).* The child limit counts **areas only**, under every kind of parent
    including scenes and App. Object lifetimes are children of their scene or category by design, so
    a category that holds many placed objects raises nothing, while areas that are created and never
    disposed (a factory that injects a lifetime into each object, a `CreateChild` per call) do. The
    hot path keeps an upper bound per parent and runs an exact count only when the bound passes the
    limit.
  - *Orphans (`JANITOR103`).* A lifetime whose owner is destroyed is reported only when two
    snapshots in different frames both see it. UniTask's destroy trigger ends the lifetime of a
    never-activated object one frame after Unity destroyed it, and a single snapshot in that gap
    would be a false alarm.
  - *Work that doubles on re-enable (`JANITOR116`).* When a registration arrives on a component or
    GameObject lifetime from a member named `OnEnable`, and an earlier registration from the same
    line, made in an earlier frame, is still live, the work is being added once per activation. It
    is reported once per lifetime and line. It needs nothing beyond what registration already
    records: the caller's member name and line, and the frame stamp of the entry.
- **Snapshot.** The window does not read the tree directly. `Capture` copies the live tree into
  reusable buffers. Live counts per kind are computed at that moment by classifying each entry:
  first by the identity of its cached terminate function, then by the type of the item. A tween and
  a SignalBus are recognised by type name, so the core still references neither library.
- **Editor assembly.** `JanitorViewModel` turns a snapshot into plain rows and has no UI types, so
  it is tested without a window. The window (`Window > Analysis > Janitor`, UI Toolkit) binds those
  rows and refreshes four times a second while the game is playing, tracking is on and the window is
  not paused. Its settings live in `EditorPrefs` and are pushed into the core, which cannot read
  them itself. The recorded history is carried across a domain reload by the editor assembly for the
  same reason. Choosing a warning selects its lifetime in the tree and pings its context object. The
  Docs button of a warning opens the Troubleshooting section for the installed package version.

See [Diagnostics](../Diagnostics.md).

---

## 11. Responsibility table

| Type | Owns | Does | Does not |
|---|---|---|---|
| `Lifetime` | State, generation, token source, entries, links, name | The public facade: register, `Cancel`, `Dispose`, `CreateChild`, `Token` | Know what a tween, an event or a scene is |
| `LifetimeRegistration` | `(lifetime, generation, slot, version)` | End exactly one item | Detach an item without ending it |
| `EntryList` | The slot array and its free list | O(1) add and remove, newest-first drain, sweep threshold | Invoke user code |
| `LifetimeTeardown` | The running operations | Mark, signal, drain, finalize; re-home; the re-entrancy rules | Keep state between operations |
| `LifetimeTree` | App, sentinel, indexes, guard, queue, pools | Composition, lookups, error dispatch | Business rules |
| `OwnerIndex`, `SceneIndex` | Integer-keyed maps | Owner and scene lookups | Hold an owner past its disposal |
| `MainThreadGuard`, `MarshalQueue` | The main thread id; a locked queue | Throw on a wrong-thread registration; run wrong-thread stops later | Make the rest thread safe |
| `OwnedEvent...`, `SubscriberList` | Ordered subscriber slots, invoke depth | Owner-bound subscribe, isolated invoke, skip ended owners, refuse duplicates | Route by type, be global, dispatch asynchronously |
| `LifetimeTaskRunner` and its continuations | Pooled continuation objects | Start, observe and end tasks and timers; route their errors | Create a token per task |
| `LifetimeSubscriptionExtensions`, `UnityEventGuard` | Nothing; one guard per `UnityEvent` subscription | Paired, `UnityEvent` and `IDisposable` registration | Let user code write a removal |
| `LifetimeCoroutine` | The routine, the host, the deactivation count at start | Step, catch, release its own entry | Pool itself, run without a host |
| `ComponentLifetimes` | Nothing | Find or create object lifetimes, bind the destroy token, decide the parent | Reparent an existing lifetime |
| `ActiveLifetimeTrigger` | The active lifetime, a deactivation counter | `Cancel()` in `OnDisable`; enables itself again after it was disabled | Anything in `OnEnable`; show in the Inspector |
| `SceneBinding`, `SceneLifetimes` | Nothing | Scene lookups, persistent detection, the pre-pass in both directions, the unloaded fallback; `Get`, `Dispose`, `DisposeAll` | Load or unload scenes |
| `PlayModeBootstrap` | The session tree, the exit registration | Session start, the three end signals, the restart after a script reload | Leave a default tree behind in Edit Mode |
| `LifetimeErrors` | The handler | Dispatch, default logging | Report cancellation |
| `DevWarnings`, `DiagnosticIds`, `LifetimeDiagnostics` | IDs, titles, anchors, the recorded history | Console warnings, recording, snapshots, the public `Report` hook | Exist as data or calls in a release player; dispose lifetimes |
| `LifetimeTweenExtensions`, `TweenCompletionPromise` | A pooled promise | Recycling-safe registration; an await that cancels on kill | Touch DOTween modules; touch callbacks in `AddTo` |
| `LifetimeInstaller`, `ContextLifetimeResolver` | The captured context lifetime | Bind a `Lifetime` per injectee | Install SignalBus |
| `SceneContextLifetimeDisposer` | The scene handle | Dispose the scene lifetime first among the container's disposables | Throw |
| `JanitorWindow` and its models | UI state, reusable rows | Display, filter, sort, ping, `Cancel`, open the documentation link | Dispose lifetimes |

---

## 12. Failure classes and the mechanism that removes each

| # | Failure class | Mechanism |
|---|---|---|
| 1 | Work that outlives its owner | Every registration names an owner. `Run` always passes the generation token. Object lifetimes bind to the destroy token at creation. `AwaitCompletionAsync` cancels on any kill, so code after the await does not run. A destroyed owner resolves to the disposed sentinel, and a `null` owner throws after the item was ended |
| 2 | A forgotten removal line | Every subscription API takes the owner in the same call and the package performs the removal. There is no ownerless subscribe: `Lifetime.App` is the explicit "forever". Events the game cannot change take `add` and `remove` in one call ([ADR-005](Decisions/ADR-005-No-Removal-Lines-In-User-Code.md)) |
| 3 | The same handler subscribed twice | An equal handler for the same owner returns the existing registration and raises `JANITOR105`: for `OwnedEvent`, `UnityEvent`, SignalBus and the paired form (where `add`, `remove` and handler must all be equal) |
| 4 | Work started in `OnEnable` on a lifetime that does not follow activation | The active lifetime is the owner that does follow it. For tasks, timers, coroutines and tweens registered on the component instead, the editor reports the second activation as `JANITOR116` |
| 5 | A recycled handle acting on someone else's object | Tokens and handles are stamped with the generation and the slot version. Registered tweens are taken out of DOTween's recycling. Event slots and task entries pin the generation they were created in. A pooled object uses the active lifetime, which opens a new generation per activation |
| 6 | Teardown order during a scene unload | `SceneLifetimes.DisposeAll()` runs before the loader, while every object exists. All tokens are cancelled before any item is ended. Membership entries cover objects placed in categories outside the scene. The pre-pass leaves objects that moved away alone and takes in objects that moved into the scene |
| 7 | A handler that runs on an owner that ended earlier in the same dispatch | `OwnedEvent` skips it; the `UnityEvent` guard skips it; SignalBus applies removal immediately by itself |
| 8 | Re-entrancy during cancel | Four passes, state precedence and operation ids ([section 5](#5-teardown-in-passes)). Registrations during a cancel are ended at once. Children created during an operation join it. Event removals apply immediately and additions wait for the next invoke. A depth guard stops event ping-pong |
| 9 | Cleanup that throws and takes unrelated cleanup with it | Each terminate function, token callback and handler runs in its own `try`/`catch`. Failures are routed with context; teardown never throws |
| 10 | Bookkeeping that grows with finished work | Self-removal of tasks and coroutines, the probe sweep for tweens and coroutines, the deactivation counter for coroutines on pooled hosts, and `JANITOR106` when one lifetime passes 256 entries or 64 live child areas |
| 11 | A global store with no owner | No registry and no lookup by name. An area has exactly one parent, chosen explicitly. Categories are typed properties on a class that some lifetime owns |
| 12 | Coroutine errors in teardown | The inactive-host pre-check replaces Unity's error with `JANITOR110`; the wrapper never stops a null handle |
| 13 | Work started on an object that is on its way out | An active lifetime, and every area below it, refuses registrations while its GameObject is inactive (`JANITOR108`) and hands out a cancelled token. A disposed scene lifetime refuses them while the scene is still loaded (`JANITOR109`). A first access during the owner's destruction returns the disposed sentinel |
| 14 | State that crosses a session boundary in the editor | The session start shuts down the tree it tracked, creates a new one and resets the error handler and the recorded warnings, with domain reload on or off. The return to Edit Mode clears the default tree. A script reload during Play Mode starts a new session at the next call |
| 15 | A stop request from the wrong thread | `Cancel` and `Dispose` are marshalled to the main thread and report `JANITOR111`; they do not throw and do not race |

---

## 13. Performance notes

The numbers and the tests behind them are in [Performance](../Performance.md). The design choices
that produce them:

| Risk | Choice |
|---|---|
| An allocation per registration | Entries are structs in an array that is kept across generations; terminate functions are cached static delegates; `Run`, `After` and `Every` reuse pooled continuation objects. The pools are not capped: they grow to the peak number of concurrent tasks and timers and do not shrink, so a burst allocates once |
| A cancelled pending task or timer | Not avoided: UniTask ends a cancelled await through an exception, so each one costs an exception object and one or two throw and catch on the next frame. `Cancel()` itself allocates nothing |
| A coroutine or a `UnityEvent` subscription | These two do allocate, once per registration: a wrapper that steps the routine, and a guard listener. Neither allocates per frame or per invoke |
| An allocation per `Cancel` | The subtree snapshot comes from a buffer pool; lifetimes are reused, not recreated; nothing is linked or unlinked |
| A token source per lifetime per generation | The source is created only when `Token` is read. A generation that registers only tweens and subscriptions never creates one |
| `tween.AddTo(this)` looks the owner up on every call | An instance-id dictionary lookup after the first access. `GetActiveLifetime()` finds the trigger with `TryGetComponent` on every call instead. A hot loop can hold the lifetime in a local |
| A scene `Cancel` or `Dispose` | One allocation-free scan of the owner index in the pre-pass, linear in the number of object lifetimes, on top of the walk over the subtree |
| `OwnedEvent.Invoke` cost | Per handler: a few field reads, a `try` region and a direct delegate call. No snapshot copy, no boxing |
| Duplicate checks | A scan at subscribe time only, never at invoke |
| A whole-tree `Cancel` (`Lifetime.App`, a scene) | Linear in lifetimes plus entries. Expected to be rare |
| Registered tweens leave DOTween's pool | Accepted ([ADR-003](Decisions/ADR-003-Tweens-Are-Not-Recyclable.md)) |
| Editor diagnostics | Counters and struct copies on the hot paths; strings are built only when a warning fires or the window asks; all of it is absent from players |

All allocation measurements were taken in the editor on Mono. IL2CPP players have not been measured.

---

## 14. What changed between the design and the code

Documented because the reasoning is still useful, and because a reviewer who knows an earlier shape
will otherwise trip over these.

| Area | Designed as | Shipped as | Why |
|---|---|---|---|
| Lifetimes injected by Zenject into plain classes | A package-owned kind that ignores `Dispose()` | An ordinary area labelled with the injectee's type | The integration uses the public API only, and the public API creates areas. A service may therefore dispose its own injected area; the context never has to |
| Parent of the active lifetime | The component lifetime of the hidden trigger | The scene lifetime, App or the category, directly | The intermediate lifetime was unreachable, cost one lifetime per GameObject and collided with the active lifetime in the owner index. Observable behaviour is the same |
| `Component` owner on the `IOwnedEvent` views | An interface member | Extension methods | The interface stays one method, and existing implementers do not break. The call reads the same |
| `Run` continuation | An `async` method | A pooled object with a cached completion delegate | An `async` state machine allocates per call in debug builds, which would break the allocation guarantee in the editor |
| `UnityEvent` subscription | The raw handler added as the listener | A guard listener per subscription | `UnityEvent` dispatches over a snapshot, so a removed listener still ran once on an owner that had ended. The guard also keeps Unity's rule of skipping a listener whose target was destroyed |
| Coroutines stopped by host deactivation | Found by the probe only while the host stays inactive | The host's trigger counts deactivations | Unity does not notify the enumerator. A pooled host reactivated before the next sweep left one dead entry per cycle on a long-lived lifetime |
| A C# `null` owner | Treated like a destroyed owner | `ArgumentNullException`, after the item was ended | A destroyed owner is a normal runtime state; a `null` reference is a programming error and should be loud |
| A disposed scene lifetime in the scene index | Removed when disposed | Kept until the scene is unloaded | Otherwise `SceneLifetimes.Get` would hand out a fresh lifetime for a scene that is going away, and disposal would not be terminal |
| Persistent-scene detection | A handle the scene manager does not list | The same, and the scene must be loaded | An object whose `Awake` runs while its scene is still loading must resolve to that scene, not to App |
| Reparent pre-pass | Before every scene or App operation | Before scene operations only | An App operation spans every scene subtree anyway |
| SignalBus: the same handler under two owners | Two separate subscriptions | `InvalidOperationException` | SignalBus holds one subscription per signal and handler, and its own duplicate check exists only in the editor |
| SignalBus: order of work | Subscribe, then register the removal | Register on the owner, then subscribe | A refused owner never touches the bus, and an undeclared signal leaves no entry behind |
| `JANITOR104` with the Zenject scene disposer installed | A silent fallback | The disposer reports the hint | Installing the integration would otherwise hide a skipped scene call |
| A tween added in `Complete` mode to an owner that already ended | Not specified | Killed | It never ran for that owner; completing it would fire callbacks during a teardown |
| `AwaitCompletionAsync` on an ended lifetime or a cancelled token | A synchronous throw | A cancelled task, which throws at the `await` | The same shape as every other cancellation, and no allocation on that path |
| How a lifetime cancel ends `AwaitCompletionAsync(lifetime)` | Through the chained `onKill` | The lifetime's entry settles the await itself after the kill | A callback set after the call replaced the chained one and the await never ended; a nested tween is never killed |
| `SetRecyclable(false)` | In `AddTo` | Also in both `AwaitCompletionAsync` overloads | The promise holds the tween and asks it whether it is active |
| Diagnostics from integrations | Internal to the core | A small public, editor-only `LifetimeDiagnostics.Report` | The editor assembly must not reference DOTween, and integrations stay on public API |
| `JANITOR106`, the 64 children rule | Every child counts | Only child areas count, under every parent | Every object lifetime of a scene is a child of that scene by design, and a category can hold any number of placed objects. Areas that pile up are the real signal, also directly under a scene or App |
| Diagnostic IDs | Fifteen | Sixteen: `JANITOR116` for work that `OnEnable` registers on a lifetime that does not follow activation | The duplicate check only covers subscriptions; tasks, timers, coroutines and tweens doubled silently |
| `JANITOR103` | Reported by the first snapshot that sees a destroyed owner | Reported when a second snapshot in a later frame still sees it | A never-activated object is ended one frame after its destruction; the first rule raised a false alarm in that gap |
| Paired `Subscribe` called twice | Two subscriptions | The repeat is ignored when handler, `add` and `remove` are equal | The same double-subscribe protection as the other sources. Lambdas that capture a local are new objects per call and cannot be recognised |
| The active-object rule | The active lifetime refuses work while its GameObject is inactive | Areas below it and `Token` follow the same rule | A child area was a way around the rule |
| `ActiveLifetimeTrigger` | A plain component | Hidden in the Inspector; a disable of the trigger itself cancels the work once and is undone at the next use | A loop that disables every behaviour reaches it, and a disabled trigger would never see the next deactivation |
| A component lifetime first used on an inactive GameObject | Bound to the GameObject's destruction only | Bound to the component's destroy token as well; the first signal wins | Objects created by a DI container are injected while inactive, and their lifetime should end with the component |
| Scene pre-pass | Moves out objects that left the scene | Also adopts objects that moved into it | Spawning in the active scene and moving the object into an additive scene is a standard pattern; a scene `Dispose` or `Cancel` missed those objects |
| End of the session | Bound to the exit token read at session start | The exit token, `Application.quitting` and the editor's return to Edit Mode run one idempotent shutdown; Edit Mode clears the default tree; a script reload in Play Mode starts a new session | One token read can be stale, a dead default tree answered calls in Edit Mode, and a script reload left no tree at all |
| Continuation pools behind `Run`, `After` and `Every` | Not specified | Not capped: they grow to the observed peak and do not shrink | With a fixed cap, every cycle of a burst above it would allocate again |
| Live counts per kind in the window | Maintained per registration | Computed when a snapshot is taken | Six code paths change an entry list; recomputing is exact and keeps the hot path to one counter |
| `OwnedEvent` depth limit | "At 64" | 64 nested invokes deliver; the next is refused | The limit reads as the largest depth that works |
| `After` and `Every` arguments | Not specified | NaN, infinity and values above 9e11 throw `ArgumentOutOfRangeException`; `Every` at zero or less fires once per frame | Left open by the design. The upper bound keeps the delay below what a `TimeSpan` can hold |
| Window layout | A details pane and a warnings tab | The tree always on top; Details, Warnings and Recently disposed as tabs below | Clicking a warning selects its lifetime in the tree while the warning list stays visible |
| Sample classes | One popup using a tween and a signal; serialized view fields | Split by dependency into three samples; views passed through `Initialize(...)` | A user of one integration must get a sample that compiles without the other, and the samples ship without scenes or prefabs |

### Known limitations in 0.1.0

- Every test and every allocation measurement ran in the editor, on Unity 6000.3 with Mono. IL2CPP
  players have not been measured.
- The order of `Application.exitCancellationToken` relative to `OnDestroy` at application exit is
  not pinned by an automated test. It no longer decides whether App is disposed: three signals run
  the same shutdown, and object lifetimes are also disposed by their own destroy tokens.
- The managed-stripping protection of the Zenject scene disposer has not been confirmed on an IL2CPP
  build.
- A script reload during Play Mode discards the tree; work started before it is not restored.
- A cancelled pending task or timer costs an exception on the next frame.
- A task has no handle of its own; cancelling one task means giving it an area.
- A lifetime-bound coroutine adds one hidden component to its host's GameObject.
- A component lifetime first accessed on a GameObject that is never activated is not disposed by
  destroying the component alone.
- A disposed scene lifetime cannot be revived. A load that fails after `SceneLifetimes.DisposeAll()`
  leaves a scene that accepts no more work
  ([ADR-004](Decisions/ADR-004-Opt-In-Scene-Call-And-No-StopAll-Broadcast.md)).

See [Limitations](../Limitations.md) for the user-facing list.

## See also

- [Concepts](../Concepts.md)
- [Performance](../Performance.md)
- [Threading](../Threading.md)
- [Diagnostics](../Diagnostics.md)
- [Troubleshooting](../Troubleshooting.md)
