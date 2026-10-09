# FAQ

[Back to index](index.md)

Short answers. Each one links to the page that explains it in full.

---

## Choosing the package

### What does this give me that `OnDestroy` cleanup does not?

Hand-written cleanup depends on a second line that mirrors the first: a `Kill` for every tween, a
`-=` for every `+=`, a `Cancel` for every token source. Forgetting one compiles and usually works
until a scene changes. With a lifetime the registration is the cleanup, so there is no second line.
A lifetime can also be stopped on demand without destroying anything, at any size: everything, one
scene, one object, one area, one item. See the walk-through on the [index page](index.md) and the
before and after classes in [Migration](Migration.md).

### Why not a `CancellationToken` plus `GetCancellationTokenOnDestroy()`?

That covers one trigger (the object is destroyed) for one kind of work (awaits that accept a token).

- A tween, a coroutine or an event subscription does not react to a token. You still write the
  `Kill`, the `StopCoroutine` and the `-=`.
- Deactivation, returning to a pool, "stop this group now" and "restart this" each need another
  token source, a field and the cancel, dispose and recreate lines.
- During a scene unload the token is cancelled as each object is destroyed, in Unity's order. There
  is no moment before the destruction starts.
- A plain C# class has no destroy token at all.
- Unity's own `destroyCancellationToken` is never cancelled for an object that was never activated,
  and a token first read inside `OnDestroy` is never cancelled either.

The package uses these tokens internally to notice destruction, and token-based code keeps working:
`Run` hands your method a token, and `lifetime.Token` gives you one directly. See
[Tasks and errors](Tasks-and-Errors.md) and [Components and scenes](Components-and-Scenes.md).

### Why not R3 or UniRx `AddTo`?

Their `AddTo` disposes an `IDisposable` when a component is destroyed, or when you dispose a bag by
hand. That covers subscriptions that are disposables, on one trigger. It does not cover tasks,
tweens or coroutines, and it has no reusable cancel and no hierarchy.

The two work together. Any `IDisposable`, including a reactive subscription, can be bound to a
lifetime. `AddTo` disposes it when the lifetime's generation ends and returns a
`LifetimeRegistration`:

<!-- source: Samples~/BasicUsage/ModalBlocker.cs -->
```csharp
private void OnEnable()
{
    var shown = this.GetActiveLifetime();
    _gate.Block().AddTo(shown);         // an IDisposable owned by the lifetime: disposed when it ends
}
```

In a file that imports both namespaces, `AddTo(this)` on a disposable usually binds to the other
library without any error, because its extension has no optional parameters; the disposable is then
not owned by a lifetime. Pass a lifetime, as above. See [Events](Events.md) and
[Troubleshooting](Troubleshooting.md).

### Does it work without DOTween or Zenject?

Yes. The core assembly needs only UniTask. The DOTween and Zenject integrations are separate
assemblies inside the package that compile only when the library is present: DOTween through its
`DOTWEEN` define or a UPM install, Zenject through `ECANAKLI_JANITOR_DI_ZENJECT`, which a UPM
Extenject 9.0.0 or newer sets automatically and `Tools/Janitor/Zenject Integration` sets for an
`Assets` install. See [DOTween](DOTween.md) and [Zenject](Zenject.md).

### Does it cost anything in a build?

- **Diagnostics: no.** The Janitor window, the recording behind it, the optional stack traces and
  fifteen of the sixteen diagnostics (`JANITOR101` to `JANITOR116`) exist only in the editor. The
  recording calls are compiled out of players together with their arguments.
- **Console warnings:** they are compiled into the editor and development builds only. The one
  exception is `JANITOR115` (event re-entrancy deeper than 64), which is routed to the error handler
  in every build because it replaces a stack overflow.
- **Runtime:** what remains is the work itself: one object per lifetime, one entry per registered
  item, and a walk over the subtree on `Cancel()`. Once warm, the package allocates nothing for
  `Cancel()`, for `OwnedEvent.Invoke`, or for registering a task, a timer, a tween, an owned-event
  subscription or a disposable. A coroutine and a `UnityEvent` subscription cost one small object
  each.
- **Cancelling work that is waiting is not free.** The `Cancel()` call itself allocates nothing, but
  every pending `After`, `Every` or `Run` that it cancels ends through an
  `OperationCanceledException` on the following frame: one exception object each, and one or two
  throw and catch. Returning two hundred pooled objects in one frame, each with a pending timer,
  costs two hundred exceptions in the next one.

See [Performance](Performance.md) and [Diagnostics](Diagnostics.md).

### Do I need a base class or a component on every object?

No. The API is extension methods on `MonoBehaviour`, `Component`, `GameObject` and `Lifetime`, so it
fits any class hierarchy. A hidden component is added in two cases only: `GetActiveLifetime()` and a
lifetime-bound coroutine each add one `ActiveLifetimeTrigger` to the GameObject, once. The first
`gameObject.GetLifetime()` adds UniTask's destroy trigger. See
[Components and scenes](Components-and-Scenes.md).

### Does it see work I started without it?

No. A raw `UniTask.Delay` without a token, a `+=`, a tween without `AddTo`: none of them is known to
the package, so none of them is stopped or shown in the window. That is why the registered form is
kept as short as the raw one. See [Limitations](Limitations.md).

---

## Names

### Why is the package called Janitor while the type is `Lifetime`?

The brand says what the package does (it cleans up); the type says what the object is (a span that
ends). A type named `Janitor` inside the namespace `Ecanakli.Janitor` would also make the simple
name resolve to the namespace, not the type, in every file that sits under that namespace
(`CS0118`). See [ADR-001](Design/Decisions/ADR-001-Janitor-Brand-And-The-Lifetime-Type.md).

### `Lifetime` is ambiguous in a file that also uses VContainer. What do I do?

VContainer has an enum named `Lifetime`. A file that imports both namespaces and writes the simple
name gets `CS0104`. Add `using Lifetime = Ecanakli.Janitor.Lifetime;` to that file, or write
VContainer's name in full (`VContainer.Lifetime.Singleton`). See
[Troubleshooting](Troubleshooting.md) and
[ADR-001](Design/Decisions/ADR-001-Janitor-Brand-And-The-Lifetime-Type.md).

### Is `this.Run(...)` related to `UniTask.Run`?

No. `UniTask.Run` moves work to the thread pool. The `Run` extension calls your delegate at once on
the calling thread, which must be the main thread, hands it the lifetime's token and observes the
result. See [Tasks and errors](Tasks-and-Errors.md).

## Names that do not exist

### I looked for a method and it is not there. What is it called?

None of the names in the left column exists in the package. The middle column is what to use.

| Looked for | Use instead | Explained in |
|---|---|---|
| `Lifetime.Stop()`, `Lifetime.End()` | `Cancel()` stops everything in the lifetime and below it and leaves it usable; `Dispose()` ends an area for good | [Stopping work](Stopping-Work.md) |
| `OwnedEvent.Raise`, `OwnedEvent.Publish` | `Invoke`, with the arguments of the event | [Events](Events.md) |
| A handle returned by `Run`, `After` or `Every` | They return nothing. Register the task on an area of its own and cancel the area | [How do I cancel a single `Run` task?](#how-do-i-cancel-a-single-run-task) |
| One call that cancels and runs again | `area.Cancel()` followed by the registration | [Is there a shorter way to restart an operation?](#is-there-a-shorter-way-to-restart-an-operation) |
| `LifetimeErrors.DefaultHandler` | Read `LifetimeErrors.Handler` before you assign it. The getter never returns `null`: with no handler set it returns the default one, which logs to the console | [Tasks and errors](Tasks-and-Errors.md) |
| `this.StartCoroutine(routine)` as a lifetime-bound form | `lifetime.StartCoroutine(this, routine)`: the receiver is a lifetime and the host is passed. `this.StartCoroutine(routine)` is Unity's own method and binds nothing | [Coroutines](Coroutines.md) |
| `new Lifetime()` | `Lifetime.App`, `CreateChild`, `GetLifetime()`, `GetActiveLifetime()`, `SceneLifetimes.Get(scene)` or an injected `Lifetime` | [Concepts](Concepts.md) |
| `gameObject.GetLifetime(parent)` | Only `GetLifetime(parent)` on a `MonoBehaviour` and `GetActiveLifetime(parent)` on a component take a parent | [Organizing work](Organizing-Work.md) |
| A method on `SceneLifetimes` that loads a scene | `SceneLifetimes.DisposeAll()` and then your own `SceneManager` call | [Components and scenes](Components-and-Scenes.md) |

Every name that does exist is on the [API reference](API-Reference.md) page.

---

## Cancel, Dispose and tokens

### What is the difference between `Cancel()` and `Dispose()`?

`Cancel()` stops everything registered in a lifetime and in its descendants, and leaves all of them
usable: a new generation opens and you can register again at once. It works on every lifetime and
can be called any number of times. `Dispose()` ends an area for good: the same things stop, the area
leaves the tree, and anything registered on it later is ended immediately. See
[Concepts](Concepts.md) and
[ADR-002](Design/Decisions/ADR-002-Reusable-Cancel-Versus-Terminal-Dispose.md).

### Why does `Dispose()` do nothing on `this.GetLifetime()`?

Lifetimes the package creates for an owner (the app, a scene, a component, a GameObject, an active
GameObject) are disposed only when that owner goes away. `Dispose()` on them is ignored, with
[JANITOR107](Troubleshooting.md#janitor107) in the editor and in development builds. An object's
lifetime backs every `this.Run(...)` and `Subscribe(handler, this)` on that object; disposing it
while the object lives would silently end everything registered afterwards. Use `Cancel()`.
`Dispose()` is for areas you created with `CreateChild`.

### Why is `Token` different after `Cancel()`?

A token belongs to one generation. `Cancel()` cancels that generation's token, and a cancelled token
stays cancelled forever, so the next generation needs a new one. Read `lifetime.Token` at the moment
you start the work and do not keep it in a field; `Run` already hands the right token to your
method. While a lifetime is being cancelled, or after it is disposed, `Token` returns a token that
is already cancelled. See [Concepts](Concepts.md).

### `Cancel()` also removed my listeners. Is that intended?

Yes. `Cancel()` ends everything registered in the current generation, subscriptions included. There
is one rule for every kind of work. Keep long-lived listening on the object's own lifetime, put
activity that starts and stops into a child area, and cancel the area. See
[Stopping work](Stopping-Work.md) and
[ADR-005](Design/Decisions/ADR-005-No-Removal-Lines-In-User-Code.md).

### Why is there no `StopAll` message that everything listens to?

A broadcast only reaches the classes that remembered to subscribe and to react correctly, which is
the same failure as forgetting an unsubscribe, and it gives no order. Stopping is a call on the
tree: `Cancel()` on a lifetime reaches every descendant, cancels all tokens first and then ends the
items deepest first. A game event can trigger it with one handler that calls `Cancel()`. See
[ADR-004](Design/Decisions/ADR-004-Opt-In-Scene-Call-And-No-StopAll-Broadcast.md).

### How do I stop everything, then?

Create one root area for the part of the game that should stop together, hand out its children as
categories, and cancel the root. `GameplayRoot` in the Basic Usage sample does this. The literal
"everything" is `Lifetime.App.Cancel()`, which also removes every listener registered on app, scene
and object lifetimes until something subscribes again; it is rarely what you want. See
[Stopping work](Stopping-Work.md).

### How do I cancel a single `Run` task?

Give it its own child area and cancel that area. `Run`, `After` and `Every` share the token of the
lifetime's generation and return nothing. `Countdown` in
[Migration, recipe 1](Migration.md#1-a-cancellationtokensource-field-cancelled-in-ondestroy) is the
pattern: an area in a field, `Cancel()` to stop, `Cancel()` and then `Run` to restart. The task
notices at its next await that checks the token; work on the thread pool has to check the token
itself. See [Stopping work](Stopping-Work.md) and [Tasks and errors](Tasks-and-Errors.md).

### Why does `Run` return nothing, when every other registration returns a handle?

A handle per task would need a token source per task, which is one allocation per `Run` for a case
most tasks never use. Tasks therefore share the generation's token, and the rare task that must be
stopped alone gets an area. Coroutines, subscriptions, disposables and `OnCancel` actions return a
`LifetimeRegistration`; a tween is its own handle (`tween.Kill()`).

### Is there a shorter way to restart an operation?

No. The pattern is an area in a field, then `Cancel()` followed by `Run` (or `AddTo`, or
`StartCoroutine`). The `Cancel()` is what makes a second call safe, so it stays visible at the call
site. When the work also has to stop on `SetActive(false)`, create the area under the active
lifetime instead of the component lifetime: deactivation cancels it without disposing it, so it can
be kept in a field and used again. See [Organizing work](Organizing-Work.md).

### What happens when I register on a lifetime that has already ended?

The item is ended on the spot: a tween is killed, a disposable is disposed, an `OnCancel` action
runs, a task or coroutine is not started, a handler is not added. The call returns a default
`LifetimeRegistration` whose `IsActive` is false. The same happens while the lifetime is in the
middle of a `Cancel()`, so start new work after `Cancel()` returns, and on an active lifetime (or an
area created under one) while its GameObject is inactive. A `null` owner is a programming error: the
item is ended and `ArgumentNullException` is thrown. See [Concepts](Concepts.md).

### Can `Cancel()` or `Dispose()` throw?

No. Every cleanup action runs in its own `try`/`catch`, and a failure is routed to
`LifetimeErrors.Handler` with the owner and the line that registered the work; the rest of the
teardown continues. `OperationCanceledException` is never routed. See
[Tasks and errors](Tasks-and-Errors.md).

### Is teardown asynchronous? Can I await a graceful stop?

No. `Cancel()` and `Dispose()` are synchronous: when they return, every token is cancelled and every
item has been ended. A task notices at its next await that checks the token, which can be a frame
later. A task that is still running more than three frames after its generation ended (the default
threshold) is shown in the window as [JANITOR101](Troubleshooting.md#janitor101).

---

## Organizing

### Why can I not look a lifetime up by a string key?

A key is a reference the compiler cannot check: a typo cancels nothing and reports nothing. A keyed
store also has no owner, so entries outlive the objects that made them, and any class can cancel any
key. The package has no registry of lifetimes and names are display labels only. A category is an
area held by a typed class and passed or injected where it is needed. See
[Migration, recipe 9](Migration.md#9-a-string-keyed-global-registry) and
[Organizing work](Organizing-Work.md).

### How do I keep a service alive across scenes?

Give it a lifetime that is not under a scene: a child of `Lifetime.App`. The Basic Usage demo
creates its scene loader that way:

<!-- source: Samples~/BasicUsage/Demo/BasicUsageDemo.cs -->
```csharp
private static SceneFlow GetSceneFlow()
{
    // A new Play Mode session disposes the old App tree, so a stale area is replaced here.
    if (_flowArea == null || _flowArea.IsDisposed)
    {
        _flowArea = Lifetime.App.CreateChild("SceneFlow");
        _flow = new SceneFlow(_flowArea);
    }

    return _flow;
}
```

`Lifetime.App` lasts for one session. It is disposed when the application quits or the editor leaves
Play Mode, and the next session gets a new one. A static field can outlive it: with domain reload
disabled it survives from the previous session, and in the editor a script recompile during Play
Mode also ends the session and starts a new one. That is why the demo checks `IsDisposed` before it
reuses its area. With Zenject, bind the service in the ProjectContext, where the injected lifetime
is a child of `Lifetime.App`:

<!-- source: Samples~/ZenjectUsage/ProjectInstaller.cs -->
```csharp
public override void InstallBindings()
{
    LifetimeInstaller.Install(Container);    // here the context lifetime is Lifetime.App
    Container.Bind<SceneFlow>().AsSingle();  // so its injected lifetime outlives every scene
}
```

A `MonoBehaviour` on a DontDestroyOnLoad object needs nothing: its lifetime is a child of
`Lifetime.App` already. See [Components and scenes](Components-and-Scenes.md) and
[Zenject](Zenject.md).

### Should a class expose its area or a method?

Expose a method that states the intent (`CancelApply()`) and keep the area private. Expose the area
itself only when other classes are meant to register work into it, as the `Popups` and `Combat`
categories are. See [Organizing work](Organizing-Work.md).

### My service received the lifetime of an outer context with Zenject. Why?

`LifetimeInstaller.Install(Container)` was not called in the context the service belongs to, so the
service resolved the binding of the context above it: a scene service gets a lifetime under
`Lifetime.App`, and a plain service of a GameObjectContext gets one that lasts as long as the scene.
Install it in the ProjectContext, in every SceneContext and in every GameObjectContext whose plain
services inject a `Lifetime`. The editor and development builds report it as
[JANITOR112](Troubleshooting.md#janitor112). See [Zenject](Zenject.md).

---

## Events

### Why a new event type instead of C# `event`?

A C# `event` can only be touched with `+=` and `-=` from outside its class, and it cannot be passed
to a method. A library therefore cannot subscribe on your behalf or remove the handler for you; the
`-=` stays in your code, where it can be forgotten. `OwnedEvent` takes the owner in the `Subscribe`
call and removes the handler when that owner ends. For events you cannot change (Unity's, an SDK's)
the paired `Subscribe` takes the add and the remove in one call. See [Events](Events.md) and
[ADR-005](Design/Decisions/ADR-005-No-Removal-Lines-In-User-Code.md).

### Which side should own a subscription?

Whichever side ends first. A popup that listens to a wallet ends before the wallet, so the popup is
the owner. When the publisher is the short-lived side, pass the publisher as the owner. The Basic
Usage sample has a tracker that lives as long as the scene and listens to countdowns that come and
go:

<!-- source: Samples~/BasicUsage/CountdownTracker.cs -->
```csharp
public void Track(Countdown countdown)
{
    // The owner is the countdown, not this tracker: it ends first, so the entry leaves with it instead of piling up here.
    countdown.Finished.Subscribe(OnFinished, countdown);
}
```

With the tracker as the owner, each subscription would stay in the tracker's lifetime until the
tracker's generation ends, one entry per countdown that is long gone. See [Events](Events.md).

### Is `OwnedEvent` an event bus? Does it show in the Inspector?

Neither. It is a field on the class that publishes it, like a C# event: no global registry, no
routing by type, no asynchronous dispatch. It is not serialized, so Inspector wiring stays on
`UnityEvent`, which the package can subscribe to as well. See [Events](Events.md).

### Why do I have to write `<string>` on the paired `Subscribe`?

C# 9 cannot infer a generic type argument from a lambda or a method group, and all three arguments
of the paired form are one or the other. Without the type argument the compiler binds the plain
`Action` form and reports `CS0029` (cannot convert `System.Action` to `System.Action<string>`) on
the two lambdas. The plain `Action` form needs none. See [Events](Events.md).

### Why not weak events that unsubscribe when the listener is collected?

The timing is not deterministic. A destroyed Unity object keeps its managed part alive until the
garbage collector runs, so a weak event keeps calling handlers on destroyed objects for an unknown
time. Owner-bound removal happens at a known moment: when the owner's generation ends.

### How does one lifetime handle tweens, tasks, coroutines and events at once?

It does not know what they are. Each registration method stores the item together with a static
function that ends it (kill, stop, remove listener, dispose). `Cancel()` cancels the tokens and then
runs those functions; it never inspects a type. That is also how the DOTween and Zenject assemblies
plug in through public API only. See [Architecture](Design/Architecture.md).

---

## Unity objects, scenes and tweens

### `GetLifetime()` or `GetActiveLifetime()`?

`GetLifetime()` (and every call that takes `this` as the owner) lasts until the component is
destroyed; use it for work set up in `Awake` or `Start`. `GetActiveLifetime()` is cancelled every
time the GameObject is deactivated and opens a new generation on the next activation; use it for
work set up in `OnEnable`, and for pooled objects. A task, timer, coroutine or tween that `OnEnable`
registers on the component's own lifetime is added again by every activation; the window reports the
second one as [JANITOR116](Troubleshooting.md#janitor116). See
[Components and scenes](Components-and-Scenes.md) and [Pooling](Pooling.md).

### Does `enabled = false` cancel the active lifetime?

Not for your components. The active lifetime follows the activation of the GameObject, not the
`enabled` flag of a component. For work that should follow one component's `enabled` state, keep a
child area and cancel it in that component's `OnDisable`.

Because nothing is cancelled, enabling the component again while the GameObject stays active runs
`OnEnable` a second time in the same generation: tasks, timers and coroutines registered there on
the active lifetime are registered again. [JANITOR116](Troubleshooting.md#janitor116) does not
report this case, because it watches component and GameObject lifetimes only. Subscriptions that the
duplicate check recognises are ignored with [JANITOR105](Troubleshooting.md#janitor105). A component
that is switched off and on through `enabled` keeps that work in an area of its own and cancels the
area before it registers.

The one exception is the package's own trigger, the component that `GetActiveLifetime()` adds and
hides in the Inspector. A loop that disables every `Behaviour` on the object reaches it, and Unity
reports that exactly like a destruction, so the work on the active lifetime is cancelled once. The
trigger enables itself again the next time the lifetime is used, and components that register in
`OnEnable` register again when they are enabled. See
[Components and scenes](Components-and-Scenes.md).

### Why does `StartCoroutine` take `this` as an argument?

`lifetime.StartCoroutine(host, routine)` separates two roles: the host is the `MonoBehaviour` that
Unity runs the coroutine on, and the lifetime decides when it stops. They are often different
objects (a popup hosts the coroutine, a category lifetime stops it). The package has no hidden
global runner, because a runner that outlives everything is the kind of unowned work the package
exists to remove. See [Coroutines](Coroutines.md).

### What happens if I forget `SceneLifetimes.DisposeAll()`?

Cleanup still happens, later. Each object's lifetime is disposed when Unity destroys the object, in
Unity's order, and the scene's own lifetime is disposed when the scene has been unloaded. Work can
therefore run against a scene that is half destroyed. The editor and development builds report it
once per scene as [JANITOR104](Troubleshooting.md#janitor104). See
[Components and scenes](Components-and-Scenes.md) and
[ADR-004](Design/Decisions/ADR-004-Opt-In-Scene-Call-And-No-StopAll-Broadcast.md).

### What if the load fails after `SceneLifetimes.DisposeAll()`?

The old scene stays loaded, and its lifetime stays disposed: disposal is final, so its objects can
register no more work. Check what can be checked before the call (the `SceneFlow` sample checks that
the scene is in the Build Settings). For a load that can still fail at run time, start the load with
the scene activation held and dispose right before the activation, as `LoadingScreenFlow` in the
Basic Usage sample does. See [Components and scenes](Components-and-Scenes.md).

### Does it work outside Play Mode?

No. `Lifetime.App`, `SceneLifetimes` and the component accessors throw `InvalidOperationException`
in the editor outside Play Mode, because the lifetime tree is created when a play session starts and
removed when it ends. See [Limitations](Limitations.md).

### My scripts recompiled during Play Mode. What happened to the work?

It is gone. A script reload discards every lifetime together with the work it owned. The first call
into the package afterwards starts a new session and logs one warning that says so. Objects that
register in `OnEnable` register again after the reload; everything else stays stopped. Restart Play
Mode for a clean state. This happens in the editor only. See [Troubleshooting](Troubleshooting.md).

### Do registered tweens still use DOTween's recycling?

No. `AddTo` and `AwaitCompletionAsync` call `SetRecyclable(false)` on the tween. With recycling on,
a killed tween's object is reused for an unrelated tween, and a held reference would then kill the
wrong one. Recycling is off by default in DOTween; if your project turned it on, registered tweens
are created and collected instead of pooled. See
[ADR-003](Design/Decisions/ADR-003-Tweens-Are-Not-Recyclable.md).

### My tween keeps running after `Cancel()`. Why?

Most likely it is nested in a `Sequence` and was registered on its own. DOTween ignores `Kill` on a
nested tween and logs nothing. Register the root `Sequence` only. The window shows the case as
[JANITOR102](Troubleshooting.md#janitor102). See [DOTween](DOTween.md).

### Should I still use `SetLink`?

Only for its pause and restart behaviours. For killing, `AddTo` is the stronger tool: it kills the
tween synchronously during the cancel, while a `SetLink` kill happens on DOTween's next update after
the target is destroyed. Using both on one tween is safe. See [DOTween](DOTween.md).

---

## Threads and diagnostics

### Can I use a lifetime from a worker thread?

Registering cannot: every registration method, `Token`, `CreateChild` and `Subscribe` throw
`InvalidOperationException` off the main thread. Stopping can: `Cancel()`, `Dispose()` and
`registration.Cancel()` called from another thread are queued and run on the next main-thread tick,
with [JANITOR111](Troubleshooting.md#janitor111) in the editor and in development builds. See
[Threading](Threading.md).

### Every row in the Janitor window shows the same call site. Why?

An item is labelled with the member and line of the call that registered it. If all your
subscriptions go through one helper method, that helper is the call site for all of them. Subscribe
at the place that owns the handler, as the samples do. See [Diagnostics](Diagnostics.md).

### Where do exceptions from my tasks and handlers go?

To `LifetimeErrors.Handler`. The default logs the exception to the console wrapped in a
`LifetimeWorkException` that names the kind of work, the lifetime, and the member and line that
registered it, with the owner object as the log context. Assign your own handler to route errors
elsewhere; assigning `null` restores the default. The getter never returns `null`, so the default
can be kept by reading `Handler` before assigning. The handler is reset at the start of every Play
Mode session. Cancellation is never reported. See [Tasks and errors](Tasks-and-Errors.md).

## See also

- [Concepts](Concepts.md)
- [Migration](Migration.md)
- [Troubleshooting](Troubleshooting.md)
- [API reference](API-Reference.md)
- [Limitations](Limitations.md)
- [Architecture](Design/Architecture.md)
