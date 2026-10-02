# API reference

Every public type of the package, with its namespace, its assembly and its complete public member
list. The guides explain what the calls do and when to use them; this page is the list of names and
shapes, written as they are declared in the source.

---

## How to read this page

- Each type has one block with its public declarations and no bodies. The `partial` modifier and
  interfaces that are internal to the package are left out.
- **Caller-information parameters are left out of every block.** The methods that register work,
  subscribe or create an area end with two optional parameters that the compiler fills in:
  `[CallerMemberName] string member = null, [CallerLineNumber] int line = 0`. Never pass them. They
  are the call site that the Janitor window and `LifetimeErrorContext` show. The methods that have
  them are `CreateChild`, `OnCancel`, `Run`, `After`, `Every`, every `Subscribe`, every `AddTo`,
  `StartCoroutine` and the `AwaitCompletionAsync` overload that takes a lifetime.
- Unless a line says otherwise, a member must be called on the main thread. Lifetimes exist in Play
  Mode only. See [Threading](Threading.md).
- A method that returns `LifetimeRegistration` returns `default` when nothing stays registered, for
  example because the owner had already ended.

### Assemblies and namespaces

| Assembly | Compiled | Namespace of its public types |
|---|---|---|
| `Ecanakli.Janitor` | Always | `Ecanakli.Janitor` |
| `Ecanakli.Janitor.DOTween` | When DOTween is installed (`DOTWEEN` define or a UPM DOTween) | `Ecanakli.Janitor` |
| `Ecanakli.Janitor.DependencyInjection.Zenject` | When Zenject or Extenject is installed (`ECANAKLI_JANITOR_DI_ZENJECT`) | `Ecanakli.Janitor.DependencyInjection` for `LifetimeInstaller`, `Ecanakli.Janitor` for the SignalBus extensions |

One `using Ecanakli.Janitor;` reaches everything user code calls. An installer adds
`using Ecanakli.Janitor.DependencyInjection;`.

### Types from other libraries in the signatures

| Type | Namespace |
|---|---|
| `CancellationToken` | `System.Threading` |
| `UniTask` | `Cysharp.Threading.Tasks` |
| `IEnumerator` | `System.Collections` |
| `MonoBehaviour`, `Component`, `GameObject` | `UnityEngine` |
| `UnityEvent`, `UnityAction` | `UnityEngine.Events` |
| `Scene` | `UnityEngine.SceneManagement` |
| `Tween` | `DG.Tweening` |
| `DiContainer`, `SignalBus` | `Zenject` |

### Types on this page

| Group | Types |
|---|---|
| [Lifetimes](#lifetimes) | `Lifetime`, `LifetimeRegistration` |
| [Lifetimes of Unity objects and scenes](#lifetimes-of-unity-objects-and-scenes) | `LifetimeComponentExtensions`, `SceneLifetimes`, `ActiveLifetimeTrigger` |
| [Tasks and timers](#tasks-and-timers) | `LifetimeTaskExtensions` |
| [Events](#events) | `OwnedEvent`, `IOwnedEvent` (four arities each), `OwnedEventViewExtensions` |
| [Subscriptions, disposables and cleanup actions](#subscriptions-disposables-and-cleanup-actions) | `LifetimeSubscriptionExtensions` |
| [Coroutines](#coroutines) | `LifetimeCoroutineExtensions` |
| [Errors](#errors) | `LifetimeErrors`, `LifetimeErrorHandler`, `LifetimeErrorContext`, `LifetimeErrorSource`, `LifetimeWorkException` |
| [Diagnostics](#diagnostics) | `DiagnosticIds`, `LifetimeDiagnostics` |
| [DOTween](#dotween) | `TweenCancelMode`, `LifetimeTweenExtensions` |
| [Zenject](#zenject) | `LifetimeInstaller`, `LifetimeSignalBusExtensions` |

---

## Lifetimes

### `Lifetime`

Namespace `Ecanakli.Janitor`, assembly `Ecanakli.Janitor`. The owner of registered work. Explained
in [Concepts](Concepts.md), [Stopping work](Stopping-Work.md) and
[Organizing work](Organizing-Work.md).

<!-- signature -->
```csharp
public sealed class Lifetime : IDisposable
{
    public static Lifetime App { get; }

    public string Name { get; }
    public bool IsDisposed { get; }
    public CancellationToken Token { get; }

    public Lifetime CreateChild(string name = null);

    public void Cancel();
    public void Dispose();

    public LifetimeRegistration OnCancel(Action action);
    public LifetimeRegistration OnCancel<TState>(TState state, Action<TState> action) where TState : class;
    public LifetimeRegistration OnCancel<TState>(TState state, Action<TState> action, Func<TState, bool> isFinished) where TState : class;
    public LifetimeRegistration OnCancel<T1, T2>(T1 first, T2 second, Action<T1, T2> action) where T1 : class where T2 : class;
}
```

- `App`: the app-wide root lifetime. It throws `InvalidOperationException` outside Play Mode, and
  `Dispose()` is ignored on it.
- `Name`: a display name for diagnostics. It may be `null` and is never used for lookup.
- `IsDisposed`: true once the lifetime has been disposed. Off the main thread the value may be
  stale.
- `Token`: the token of the current generation. After a `Cancel()` the property returns a different
  token, so read it where it is used and do not keep it in a field. It is already cancelled while
  the lifetime is ending or disposed, and for an active lifetime (or an area below one) while its
  GameObject is inactive.
- `CreateChild`: creates an area under this lifetime. Cancelling this lifetime cancels the area and
  disposing it disposes the area. On a disposed lifetime the new area is born disposed.
- `Cancel`: ends the current generation of this lifetime and of every descendant, then opens fresh
  ones. The lifetime stays usable. It never throws, and off the main thread it is deferred to the
  main thread.
- `Dispose`: ends this lifetime and every descendant area for good. It is for areas made with
  `CreateChild`; on a lifetime the package created for an owner (the app, a scene, a component, a
  GameObject, an active GameObject) it is ignored with [JANITOR107](Troubleshooting.md#janitor107).
  It never throws, and off the main thread it is deferred to the main thread.
- `OnCancel`, four forms: runs the action when the current generation ends, or at once when the
  lifetime is already ending or its GameObject is inactive. It is for custom cleanup, never for
  unsubscribing. The forms with state pass the state to the action, so the action can be static and
  capture nothing. The form with `isFinished` drops the item without running the action once a sweep
  finds the probe true.

What is not public: there is no constructor (a lifetime comes from `Lifetime.App`, `CreateChild`,
the `GetLifetime` family, `SceneLifetimes.Get` or injection), and the generation counter that test
excerpts on other pages read is internal. User code sees a new generation through `Token`.

### `LifetimeRegistration`

Namespace `Ecanakli.Janitor`, assembly `Ecanakli.Janitor`. A handle to one registered item.
Explained in [Stopping work](Stopping-Work.md).

<!-- signature -->
```csharp
public readonly struct LifetimeRegistration
{
    public bool IsActive { get; }
    public void Cancel();
}
```

- `IsActive`: true while the item is registered and not yet ended. It is false for a `default`
  handle, for an item that was refused, and after the item ended.
- `Cancel`: ends exactly that item now. A stale, `default` or repeated call does nothing. It never
  throws, and off the main thread it is deferred to the next main-thread tick.

The struct does not implement `IDisposable`: cancelling ends the item, it does not only forget it.

---

## Lifetimes of Unity objects and scenes

### `LifetimeComponentExtensions`

Namespace `Ecanakli.Janitor`, assembly `Ecanakli.Janitor`. Finds the lifetime of a component or a
GameObject. Explained in [Components and scenes](Components-and-Scenes.md); the forms with `parent`
in [Organizing work](Organizing-Work.md).

<!-- signature -->
```csharp
public static class LifetimeComponentExtensions
{
    public static Lifetime GetLifetime(this MonoBehaviour behaviour);
    public static Lifetime GetLifetime(this MonoBehaviour behaviour, Lifetime parent);
    public static Lifetime GetLifetime(this GameObject gameObject);

    public static Lifetime GetActiveLifetime(this Component component);
    public static Lifetime GetActiveLifetime(this Component component, Lifetime parent);
}
```

- `GetLifetime(this MonoBehaviour)`: the component lifetime, disposed when the component is
  destroyed. It is a child of the component's scene lifetime, or of `Lifetime.App` for a
  DontDestroyOnLoad object.
- `GetLifetime(this MonoBehaviour, Lifetime parent)`: the same lifetime, placed in a category so
  that `parent.Cancel()` reaches it. The first access decides the parent; a later call with another
  parent returns the existing lifetime unchanged, with [JANITOR113](Troubleshooting.md#janitor113).
- `GetLifetime(this GameObject)`: the GameObject lifetime, disposed when the GameObject is
  destroyed. There is no form with a parent for a GameObject.
- `GetActiveLifetime`, two forms: the active lifetime of the component's GameObject. It is cancelled
  every time the GameObject is deactivated and disposed when it is destroyed. It follows GameObject
  activation, not the `enabled` flag of a component. Work registered while the GameObject is
  inactive is ended at once, with [JANITOR108](Troubleshooting.md#janitor108). The form with
  `parent` follows the same rule as `GetLifetime(parent)`.

All five return a lifetime that is already disposed when the owner was destroyed, and throw
`InvalidOperationException` outside Play Mode.

### `SceneLifetimes`

Namespace `Ecanakli.Janitor`, assembly `Ecanakli.Janitor`. The lifetime of each loaded scene and the
call that ends a scene's work before Unity destroys it. Explained in
[Components and scenes](Components-and-Scenes.md) and [Stopping work](Stopping-Work.md).

<!-- signature -->
```csharp
public static class SceneLifetimes
{
    public static Lifetime Get(Scene scene);
    public static void Dispose(Scene scene);
    public static void DisposeAll();
}
```

- `Get`: the lifetime of the scene, created on first use. A DontDestroyOnLoad scene returns
  `Lifetime.App`. After `Dispose` it returns the disposed lifetime until the scene is unloaded.
- `Dispose`: disposes the lifetime of one scene. Call it right before unloading that scene. A
  DontDestroyOnLoad scene is left alone.
- `DisposeAll`: disposes the lifetime of every loaded scene. Call it right before a Single-mode
  load. `Lifetime.App` and DontDestroyOnLoad lifetimes are never touched.

`Get` and `Dispose` throw `ArgumentException` for a scene that is not valid. All three throw
`InvalidOperationException` outside Play Mode. Neither `Dispose` nor `DisposeAll` loads or unloads
anything; the load call stays in user code.

### `ActiveLifetimeTrigger`

Namespace `Ecanakli.Janitor`, assembly `Ecanakli.Janitor`. The hidden component that
`GetActiveLifetime()` and a lifetime-bound coroutine add to a GameObject, once per GameObject. It
has no public members of its own, and there is no reason to add or use it by hand. Explained in
[Components and scenes](Components-and-Scenes.md).

<!-- signature -->
```csharp
public sealed class ActiveLifetimeTrigger : MonoBehaviour
{
}
```

---

## Tasks and timers

### `LifetimeTaskExtensions`

Namespace `Ecanakli.Janitor`, assembly `Ecanakli.Janitor`. Starts tasks and timers that end with a
lifetime. Explained in [Tasks and errors](Tasks-and-Errors.md).

<!-- signature -->
```csharp
public static class LifetimeTaskExtensions
{
    // On a lifetime.
    public static void Run(this Lifetime lifetime, Func<CancellationToken, UniTask> work);
    public static void Run<TState>(this Lifetime lifetime, TState state, Func<TState, CancellationToken, UniTask> work);
    public static void After(this Lifetime lifetime, float seconds, Action action, bool ignoreTimeScale = false);
    public static void After<TState>(this Lifetime lifetime, float seconds, TState state, Action<TState> action, bool ignoreTimeScale = false);
    public static void Every(this Lifetime lifetime, float seconds, Action action, bool ignoreTimeScale = false);
    public static void Every<TState>(this Lifetime lifetime, float seconds, TState state, Action<TState> action, bool ignoreTimeScale = false);

    // On a component: the same six shapes, registered on the component lifetime.
    public static void Run(this MonoBehaviour behaviour, Func<CancellationToken, UniTask> work);
    public static void Run<TState>(this MonoBehaviour behaviour, TState state, Func<TState, CancellationToken, UniTask> work);
    public static void After(this MonoBehaviour behaviour, float seconds, Action action, bool ignoreTimeScale = false);
    public static void After<TState>(this MonoBehaviour behaviour, float seconds, TState state, Action<TState> action, bool ignoreTimeScale = false);
    public static void Every(this MonoBehaviour behaviour, float seconds, Action action, bool ignoreTimeScale = false);
    public static void Every<TState>(this MonoBehaviour behaviour, float seconds, TState state, Action<TState> action, bool ignoreTimeScale = false);
}
```

- `Run`: runs the work with the token of the current generation. The work is not started when the
  lifetime is not active. A fault is routed to `LifetimeErrors.Handler` with source `Task`; a
  cancellation is silent.
- `After`: runs the action once after `seconds`, and never when the generation ends first. With
  `seconds` at or below zero it runs inside the call, when the lifetime accepts work.
- `Every`: runs the action once per `seconds` until the generation ends. With `seconds` at or below
  zero it runs once per frame. An exception stops that timer and is routed once with source `Timer`.
- The forms with `TState` pass a state value to the delegate, so the delegate can be static and
  capture nothing.
- `ignoreTimeScale`: true measures the time in unscaled time.
- `seconds` must be finite and at most 900000000000; anything else throws
  `ArgumentOutOfRangeException`.

All twelve return nothing: there is no handle for one task or one timer. To stop one of them alone,
register it on its own area and cancel the area.

---

## Events

Explained in [Events](Events.md).

### `OwnedEvent`

Namespace `Ecanakli.Janitor`, assembly `Ecanakli.Janitor`. An event the game declares, with zero to
three arguments. Four classes share the name and differ in their type arguments.

No argument:

<!-- signature -->
```csharp
public sealed class OwnedEvent : IOwnedEvent
{
    public OwnedEvent(string name = null);
    public int SubscriberCount { get; }
    public LifetimeRegistration Subscribe(Action handler, Lifetime owner);
    public LifetimeRegistration Subscribe(Action handler, Component owner);
    public void Invoke();
}
```

One argument:

<!-- signature -->
```csharp
public sealed class OwnedEvent<T> : IOwnedEvent<T>
{
    public OwnedEvent(string name = null);
    public int SubscriberCount { get; }
    public LifetimeRegistration Subscribe(Action<T> handler, Lifetime owner);
    public LifetimeRegistration Subscribe(Action<T> handler, Component owner);
    public void Invoke(T arg);
}
```

Two arguments:

<!-- signature -->
```csharp
public sealed class OwnedEvent<T1, T2> : IOwnedEvent<T1, T2>
{
    public OwnedEvent(string name = null);
    public int SubscriberCount { get; }
    public LifetimeRegistration Subscribe(Action<T1, T2> handler, Lifetime owner);
    public LifetimeRegistration Subscribe(Action<T1, T2> handler, Component owner);
    public void Invoke(T1 arg1, T2 arg2);
}
```

Three arguments:

<!-- signature -->
```csharp
public sealed class OwnedEvent<T1, T2, T3> : IOwnedEvent<T1, T2, T3>
{
    public OwnedEvent(string name = null);
    public int SubscriberCount { get; }
    public LifetimeRegistration Subscribe(Action<T1, T2, T3> handler, Lifetime owner);
    public LifetimeRegistration Subscribe(Action<T1, T2, T3> handler, Component owner);
    public void Invoke(T1 arg1, T2 arg2, T3 arg3);
}
```

- Constructor: `name` is an optional display name for diagnostics.
- `SubscriberCount`: the number of live subscriptions.
- `Subscribe(handler, Lifetime owner)`: subscribes for as long as the current generation of the
  owner lasts. Nothing is added when the owner is not active. The same handler for the same owner
  again returns the existing registration, with [JANITOR105](Troubleshooting.md#janitor105).
- `Subscribe(handler, Component owner)`: the same, with the owner's lifetime looked up. A
  `MonoBehaviour` owner uses its component lifetime, any other component the lifetime of its
  GameObject.
- `Invoke`: calls every live subscriber in subscription order. A handler exception is routed to
  `LifetimeErrors.Handler` with source `EventHandler`, and the remaining handlers still run. At 64
  nested calls it routes [JANITOR115](Troubleshooting.md#janitor115) and returns without delivering.

### `IOwnedEvent`

Namespace `Ecanakli.Janitor`, assembly `Ecanakli.Janitor`. The subscribe-only view of an
`OwnedEvent`: a publisher keeps the event private and exposes the view, so that outsiders can
subscribe and only the publisher can invoke. Four interfaces, one per arity, each with one method.

<!-- signature -->
```csharp
public interface IOwnedEvent
{
    LifetimeRegistration Subscribe(Action handler, Lifetime owner);
}
```

<!-- signature -->
```csharp
public interface IOwnedEvent<T>
{
    LifetimeRegistration Subscribe(Action<T> handler, Lifetime owner);
}
```

<!-- signature -->
```csharp
public interface IOwnedEvent<T1, T2>
{
    LifetimeRegistration Subscribe(Action<T1, T2> handler, Lifetime owner);
}
```

<!-- signature -->
```csharp
public interface IOwnedEvent<T1, T2, T3>
{
    LifetimeRegistration Subscribe(Action<T1, T2, T3> handler, Lifetime owner);
}
```

- `Subscribe`: the same rules as `OwnedEvent.Subscribe(handler, Lifetime owner)`.

### `OwnedEventViewExtensions`

Namespace `Ecanakli.Janitor`, assembly `Ecanakli.Janitor`. Component owners for the four views, so
that `view.Subscribe(handler, this)` works on an `IOwnedEvent` as it does on the event class.

<!-- signature -->
```csharp
public static class OwnedEventViewExtensions
{
    public static LifetimeRegistration Subscribe(this IOwnedEvent view, Action handler, Component owner);
    public static LifetimeRegistration Subscribe<T>(this IOwnedEvent<T> view, Action<T> handler, Component owner);
    public static LifetimeRegistration Subscribe<T1, T2>(this IOwnedEvent<T1, T2> view, Action<T1, T2> handler, Component owner);
    public static LifetimeRegistration Subscribe<T1, T2, T3>(this IOwnedEvent<T1, T2, T3> view, Action<T1, T2, T3> handler, Component owner);
}
```

- `Subscribe`, four arities: subscribes to the event behind the view. A `MonoBehaviour` owner uses
  its component lifetime, any other component the lifetime of its GameObject.

---

## Subscriptions, disposables and cleanup actions

### `LifetimeSubscriptionExtensions`

Namespace `Ecanakli.Janitor`, assembly `Ecanakli.Janitor`. Binds disposables, events the game does
not own and `UnityEvent` listeners to a lifetime. Explained in [Events](Events.md).

<!-- signature -->
```csharp
public static class LifetimeSubscriptionExtensions
{
    // A disposable.
    public static LifetimeRegistration AddTo<T>(this T disposable, Lifetime lifetime) where T : IDisposable;
    public static LifetimeRegistration AddTo<T>(this T disposable, Component owner) where T : IDisposable;
    public static LifetimeRegistration AddTo<T>(this T disposable, GameObject owner) where T : IDisposable;

    // A cleanup action on the component lifetime.
    public static LifetimeRegistration OnCancel(this MonoBehaviour behaviour, Action action);
    public static LifetimeRegistration OnCancel<TState>(this MonoBehaviour behaviour, TState state, Action<TState> action) where TState : class;
    public static LifetimeRegistration OnCancel<TState>(this MonoBehaviour behaviour, TState state, Action<TState> action, Func<TState, bool> isFinished) where TState : class;
    public static LifetimeRegistration OnCancel<T1, T2>(this MonoBehaviour behaviour, T1 first, T2 second, Action<T1, T2> action) where T1 : class where T2 : class;

    // The paired form for an event the game does not own, on a lifetime.
    public static LifetimeRegistration Subscribe(this Lifetime owner, Action<Action> add, Action<Action> remove, Action handler);
    public static LifetimeRegistration Subscribe<T>(this Lifetime owner, Action<Action<T>> add, Action<Action<T>> remove, Action<T> handler);
    public static LifetimeRegistration Subscribe<T1, T2>(this Lifetime owner, Action<Action<T1, T2>> add, Action<Action<T1, T2>> remove, Action<T1, T2> handler);
    public static LifetimeRegistration Subscribe<T1, T2, T3>(this Lifetime owner, Action<Action<T1, T2, T3>> add, Action<Action<T1, T2, T3>> remove, Action<T1, T2, T3> handler);
    public static LifetimeRegistration Subscribe<TDelegate>(this Lifetime owner, Action<TDelegate> add, Action<TDelegate> remove, TDelegate handler) where TDelegate : Delegate;

    // The paired form on the component lifetime.
    public static LifetimeRegistration Subscribe(this MonoBehaviour owner, Action<Action> add, Action<Action> remove, Action handler);
    public static LifetimeRegistration Subscribe<T>(this MonoBehaviour owner, Action<Action<T>> add, Action<Action<T>> remove, Action<T> handler);
    public static LifetimeRegistration Subscribe<T1, T2>(this MonoBehaviour owner, Action<Action<T1, T2>> add, Action<Action<T1, T2>> remove, Action<T1, T2> handler);
    public static LifetimeRegistration Subscribe<T1, T2, T3>(this MonoBehaviour owner, Action<Action<T1, T2, T3>> add, Action<Action<T1, T2, T3>> remove, Action<T1, T2, T3> handler);
    public static LifetimeRegistration Subscribe<TDelegate>(this MonoBehaviour owner, Action<TDelegate> add, Action<TDelegate> remove, TDelegate handler) where TDelegate : Delegate;

    // A UnityEvent listener owned by a lifetime.
    public static LifetimeRegistration Subscribe(this UnityEvent evt, UnityAction handler, Lifetime owner);
    public static LifetimeRegistration Subscribe<T0>(this UnityEvent<T0> evt, UnityAction<T0> handler, Lifetime owner);
    public static LifetimeRegistration Subscribe<T0, T1>(this UnityEvent<T0, T1> evt, UnityAction<T0, T1> handler, Lifetime owner);
    public static LifetimeRegistration Subscribe<T0, T1, T2>(this UnityEvent<T0, T1, T2> evt, UnityAction<T0, T1, T2> handler, Lifetime owner);
    public static LifetimeRegistration Subscribe<T0, T1, T2, T3>(this UnityEvent<T0, T1, T2, T3> evt, UnityAction<T0, T1, T2, T3> handler, Lifetime owner);

    // A UnityEvent listener owned by a component.
    public static LifetimeRegistration Subscribe(this UnityEvent evt, UnityAction handler, Component owner);
    public static LifetimeRegistration Subscribe<T0>(this UnityEvent<T0> evt, UnityAction<T0> handler, Component owner);
    public static LifetimeRegistration Subscribe<T0, T1>(this UnityEvent<T0, T1> evt, UnityAction<T0, T1> handler, Component owner);
    public static LifetimeRegistration Subscribe<T0, T1, T2>(this UnityEvent<T0, T1, T2> evt, UnityAction<T0, T1, T2> handler, Component owner);
    public static LifetimeRegistration Subscribe<T0, T1, T2, T3>(this UnityEvent<T0, T1, T2, T3> evt, UnityAction<T0, T1, T2, T3> handler, Component owner);
}
```

- `AddTo`, three forms: disposes the item when the current generation of the owner ends, or at once
  when the owner has already ended. A `MonoBehaviour` owner uses its component lifetime, any other
  component and a GameObject the GameObject lifetime.
- `OnCancel` on a `MonoBehaviour`, four forms: the same as the four `Lifetime.OnCancel` forms, on
  the component lifetime.
- Paired `Subscribe`, five forms on each receiver: `add` runs inside the call when the owner is
  active, and the package calls `remove` with the same handler exactly once when the current
  generation of the owner ends. The type arguments must be written out for every form except the
  plain `Action` one. The same add, remove and handler on the same owner again is ignored with
  [JANITOR105](Troubleshooting.md#janitor105). The handler is called by the source of the event, not
  by the package.
- `Subscribe` on a `UnityEvent`, five arities with each kind of owner: adds a guard listener and
  removes it when the current generation of the owner ends. A component owner resolves as for
  `AddTo`. The same handler on the same event with the same owner again returns the existing
  registration, with [JANITOR105](Troubleshooting.md#janitor105).

---

## Coroutines

### `LifetimeCoroutineExtensions`

Namespace `Ecanakli.Janitor`, assembly `Ecanakli.Janitor`. Binds a Unity coroutine to a lifetime.
Explained in [Coroutines](Coroutines.md).

<!-- signature -->
```csharp
public static class LifetimeCoroutineExtensions
{
    public static LifetimeRegistration StartCoroutine(this Lifetime lifetime, MonoBehaviour host, IEnumerator routine);
}
```

- `StartCoroutine`: starts the routine on `host` and stops it when the current generation of the
  lifetime ends, or when the returned registration is cancelled. The receiver is always a lifetime
  and the host is always passed, usually `this`. The routine is not started when the lifetime is not
  active, or when the host is null, destroyed or inactive (see
  [JANITOR110](Troubleshooting.md#janitor110)). An exception inside the routine is routed with
  source `Coroutine`.

---

## Errors

Explained in [Tasks and errors](Tasks-and-Errors.md).

### `LifetimeErrors`

Namespace `Ecanakli.Janitor`, assembly `Ecanakli.Janitor`. The one place where routed errors arrive.

<!-- signature -->
```csharp
public static class LifetimeErrors
{
    public static LifetimeErrorHandler Handler { get; set; }
}
```

- `Handler`: the handler that receives routed errors. The getter never returns `null`: with no
  handler set it returns the default one, which logs a `LifetimeWorkException` to the console with
  the owner object as context. Assigning `null` restores the default. The default has no public name
  of its own, so read `Handler` before assigning to keep it. The handler is reset to the default at
  the start of every Play Mode session.

### `LifetimeErrorHandler`

Namespace `Ecanakli.Janitor`, assembly `Ecanakli.Janitor`. The delegate type of
`LifetimeErrors.Handler`. The exception it receives is never an `OperationCanceledException`.

<!-- signature -->
```csharp
public delegate void LifetimeErrorHandler(Exception exception, in LifetimeErrorContext context);
```

### `LifetimeErrorContext`

Namespace `Ecanakli.Janitor`, assembly `Ecanakli.Janitor`. Says where a routed error came from.

<!-- signature -->
```csharp
public readonly struct LifetimeErrorContext
{
    public LifetimeErrorContext(LifetimeErrorSource source, UnityEngine.Object owner, string lifetimeName, string member, int line);

    public LifetimeErrorSource Source { get; }
    public UnityEngine.Object Owner { get; }
    public string LifetimeName { get; }
    public string Member { get; }
    public int Line { get; }
}
```

- `Source`: the kind of work that failed.
- `Owner`: the Unity object that owns the lifetime, or `null` when the lifetime has none.
- `LifetimeName`: the display name or call-site label of the lifetime, or `null`.
- `Member` and `Line`: the member and the line of the registration call, or `null` and 0.

### `LifetimeErrorSource`

Namespace `Ecanakli.Janitor`, assembly `Ecanakli.Janitor`. The kind of work that raised a routed
error.

<!-- signature -->
```csharp
public enum LifetimeErrorSource
{
    Task,
    Timer,
    CancelAction,
    Coroutine,
    TokenCallback,
    EventHandler,
}
```

- `Task`: a task started with `Run`.
- `Timer`: an `After` or `Every` callback.
- `CancelAction`: an action that ends an item while a lifetime is cancelled or disposed: an
  `OnCancel` action, a disposable, the remove action of a paired `Subscribe`, or an `isFinished`
  probe. An internal fault of `Cancel` or `Dispose` is routed with this source too.
- `Coroutine`: a lifetime-bound coroutine.
- `TokenCallback`: a callback registered on a lifetime token that failed while the token was
  cancelled.
- `EventHandler`: a handler invoked through an `OwnedEvent`, the add action of a paired `Subscribe`,
  or an `OwnedEvent` invoked more than 64 nested calls deep.

### `LifetimeWorkException`

Namespace `Ecanakli.Janitor`, assembly `Ecanakli.Janitor`. The wrapper the default handler logs: the
original exception as `InnerException`, together with its context.

<!-- signature -->
```csharp
public sealed class LifetimeWorkException : Exception
{
    public LifetimeWorkException(LifetimeErrorContext context, Exception innerException);
    public LifetimeErrorContext Context { get; }
}
```

- `Context`: where the failed work came from.

---

## Diagnostics

Explained in [Diagnostics](Diagnostics.md); each ID has a section in
[Troubleshooting](Troubleshooting.md).

### `DiagnosticIds`

Namespace `Ecanakli.Janitor`, assembly `Ecanakli.Janitor`. The sixteen stable IDs as constants, with
their titles and documentation anchors.

<!-- signature -->
```csharp
public static class DiagnosticIds
{
    public const string TaskOverrun = "JANITOR101";
    public const string UnkillableTween = "JANITOR102";
    public const string OrphanLifetime = "JANITOR103";
    public const string SceneDisposedLate = "JANITOR104";
    public const string DuplicateSubscription = "JANITOR105";
    public const string Growth = "JANITOR106";
    public const string DisposeIgnored = "JANITOR107";
    public const string RegistrationOnInactiveObject = "JANITOR108";
    public const string RegistrationOnDisposedScene = "JANITOR109";
    public const string CoroutineNotStarted = "JANITOR110";
    public const string Marshalled = "JANITOR111";
    public const string MissingSceneInstall = "JANITOR112";
    public const string ParentMismatch = "JANITOR113";
    public const string Rehomed = "JANITOR114";
    public const string DepthExceeded = "JANITOR115";
    public const string RepeatedOnEnable = "JANITOR116";

    public static string GetTitle(string diagnosticId);
    public static string GetAnchor(string diagnosticId);
}
```

- The sixteen constants: one per diagnostic. What each one means is in the table of
  [Diagnostics](Diagnostics.md).
- `GetTitle`: the one-line title of a diagnostic, or `null` for a string that is not one of the
  sixteen IDs.
- `GetAnchor`: the package-relative path of its section, for example
  `Documentation~/Troubleshooting.md#janitor101`, or `null` for a string that is not one of the
  sixteen IDs.

### `LifetimeDiagnostics`

Namespace `Ecanakli.Janitor`, assembly `Ecanakli.Janitor`. Lets another assembly record a diagnostic
of its own in the Janitor window.

<!-- signature -->
```csharp
public static class LifetimeDiagnostics
{
    public static void Report(Lifetime lifetime, string diagnosticId, string message, UnityEngine.Object context = null);
}
```

- `Report`: records a diagnostic in the Janitor window. The method is marked
  `[Conditional("UNITY_EDITOR")]`, so the call and the evaluation of its arguments are compiled out
  of player builds. It does not write to the console. It may be called from any thread, never
  throws, and does nothing while the Tracking switch of the window is off. `diagnosticId` is
  normally a `DiagnosticIds` constant; another non-empty string is shown without a documentation
  link.

---

## DOTween

Assembly `Ecanakli.Janitor.DOTween`, compiled only when DOTween is installed. Explained in
[DOTween](DOTween.md).

### `TweenCancelMode`

Namespace `Ecanakli.Janitor`. What happens to a registered tween when the generation of its owner
ends.

<!-- signature -->
```csharp
public enum TweenCancelMode
{
    Kill,
    Complete,
}
```

- `Kill`: the tween is killed. No completion callback runs and the target keeps the value it has
  reached. This is the default.
- `Complete`: the tween is completed and then killed, so the target lands on its end value and the
  completion callbacks run.

### `LifetimeTweenExtensions`

Namespace `Ecanakli.Janitor`. Binds a tween to a lifetime and awaits a tween.

<!-- signature -->
```csharp
public static class LifetimeTweenExtensions
{
    public static T AddTo<T>(this T tween, Lifetime owner, TweenCancelMode onCancel = TweenCancelMode.Kill) where T : Tween;
    public static T AddTo<T>(this T tween, Component owner, TweenCancelMode onCancel = TweenCancelMode.Kill) where T : Tween;
    public static T AddTo<T>(this T tween, GameObject owner, TweenCancelMode onCancel = TweenCancelMode.Kill) where T : Tween;

    public static UniTask AwaitCompletionAsync(this Tween tween, Lifetime lifetime);
    public static UniTask AwaitCompletionAsync(this Tween tween, CancellationToken token);
}
```

- `AddTo`, three forms: kills, or completes, the tween when the current generation of the owner
  ends, and returns the same tween for chaining. The tween is its own handle. A `MonoBehaviour`
  owner uses its component lifetime, any other component and a GameObject the GameObject lifetime.
  An owner that has already ended kills the tween at once, whatever the mode. Register the root
  Sequence only.
- `AwaitCompletionAsync(this Tween, Lifetime)`: registers the tween on the lifetime and awaits its
  completion. The await ends in an `OperationCanceledException` when the lifetime is cancelled or
  disposed and when anything else kills the tween.
- `AwaitCompletionAsync(this Tween, CancellationToken)`: awaits the tween and kills it when the
  token is cancelled. This overload has no caller-information parameters.
- Both overloads chain `onComplete` and `onKill`, so set your own callbacks before the call. Both
  make the tween non-recyclable, as `AddTo` does.

---

## Zenject

Assembly `Ecanakli.Janitor.DependencyInjection.Zenject`, compiled only when Zenject or Extenject is
installed. Explained in [Zenject](Zenject.md).

### `LifetimeInstaller`

Namespace `Ecanakli.Janitor.DependencyInjection`. Binds `Lifetime` in a container.

<!-- signature -->
```csharp
public static class LifetimeInstaller
{
    public static void Install(DiContainer container);
}
```

- `Install`: binds `Lifetime` in the container of the context being installed. Call it from an
  installer of the ProjectContext, of every SceneContext and of every GameObjectContext whose plain
  services inject a `Lifetime`. A `MonoBehaviour` injectee receives its component lifetime; any
  other injectee receives a new area under the context lifetime. Installing the same container twice
  does nothing. No SignalBus binding is added.

### `LifetimeSignalBusExtensions`

Namespace `Ecanakli.Janitor`. SignalBus subscriptions with an owner.

<!-- signature -->
```csharp
public static class LifetimeSignalBusExtensions
{
    public static LifetimeRegistration Subscribe<TSignal>(this SignalBus bus, Action<TSignal> handler, Lifetime owner);
    public static LifetimeRegistration Subscribe<TSignal>(this SignalBus bus, Action handler, Lifetime owner);
    public static LifetimeRegistration Subscribe<TSignal>(this SignalBus bus, Action<TSignal> handler, Component owner);
    public static LifetimeRegistration Subscribe<TSignal>(this SignalBus bus, Action handler, Component owner);
}
```

- `Subscribe<TSignal>`, four forms: subscribes now, and the package unsubscribes when the current
  generation of the owner ends or when the returned registration is cancelled. The signal type
  argument must be written out, and the signal has to be declared in the container as for
  `SignalBus.Subscribe`. A `MonoBehaviour` owner uses its component lifetime, any other component
  the lifetime of its GameObject. The same handler for the same signal and owner again returns the
  existing registration, with [JANITOR105](Troubleshooting.md#janitor105); the same handler under
  another owner throws `InvalidOperationException`.

---

## What is not in the API

- The editor window (`Window > Analysis > Janitor`) lives in the editor assembly
  `Ecanakli.Janitor.Editor`, which is not part of a player build and is not covered here.
- `Run`, `After` and `Every` return nothing, and there is no call that cancels and starts again in
  one step. See [Stopping work](Stopping-Work.md).
- Names that are often looked for and do not exist are listed in the
  [FAQ](FAQ.md#names-that-do-not-exist).

---

## See also

- [README](../README.md): the same API as a short overview, next to the main example
- [Concepts](Concepts.md): the model behind `Lifetime`, `Cancel`, `Dispose` and `Token`
- [Tasks and errors](Tasks-and-Errors.md): `Run`, `After`, `Every` and the error handler
- [Events](Events.md): every `Subscribe` form, with examples
- [Troubleshooting](Troubleshooting.md): one section per diagnostic ID
