# ADR-001: The Janitor brand and the `Lifetime` type

- **Status:** Accepted, implemented in 0.1.0
- **Related:** [Architecture section 3](../Architecture.md#3-assemblies),
  [ADR-002](ADR-002-Reusable-Cancel-Versus-Terminal-Dispose.md), [Concepts](../../Concepts.md),
  [Troubleshooting](../../Troubleshooting.md)

## Context

The package needs two names, and they do different jobs.

- **The product name** is what a developer searches for, installs and says out loud. It should say
  what the package is for.
- **The main type name** appears in every signature the developer writes: constructor parameters,
  fields, extension methods. It should say what the object *is*, read well in a sentence
  (`this.GetLifetime()`), and collide with as little as possible.

Three things constrain the choice.

1. **What is managed has to be visible.** The package stops tasks, tweens, coroutines and event
   subscriptions. A name that says only "an object that ends" or only "cancellation" does not tell a
   reader that a tween or a listener is covered.
2. **C# name lookup inside a namespace.** A type that has the same name as a segment of its own
   namespace makes the simple name ambiguous: in any file whose namespace sits under that segment,
   the identifier binds to the namespace and the compiler reports `CS0118`. The package's own
   samples and tests live in such namespaces, and so does any user code that nests itself under the
   package's root.
3. **Other libraries already use the obvious words.** `Lifetime`, `Scope`, `AddTo`, `Subscribe`,
   `Event` and `Run` all exist in libraries a Unity project is likely to reference.

## Decision

### The product is "Janitor"

- Package id `com.ecanakli.janitor`, display name "Janitor", namespace `Ecanakli.Janitor`.
- The editor window is "Janitor" (`Window > Analysis > Janitor`), the menu root is `Tools/Janitor`.
- Diagnostic IDs use the prefix `JANITOR`: `JANITOR101` to `JANITOR199` are reserved for runtime and
  editor diagnostics, and the range below 100 is left free for static analysis rules.
- Scripting defines use the prefix `ECANAKLI_JANITOR_`.
- The tagline carries the list that the single word cannot: *cleans up Tasks, Tweens, Coroutines and
  Events; nothing outlives its owner.*

"Janitor" is an established name in game development for this exact job. Cleanup helpers for the
Luau language known as Janitor, Maid and Trove collect event connections, tweens and threads and
release them in one call. This package brings that pattern to Unity and adds a hierarchy, reuse
after a cancel, scene and pooling awareness, and diagnostics.

### The main type is `Lifetime`

`Lifetime` is a sealed class that implements `IDisposable`. The prose vocabulary follows from it:

| Word | Meaning |
|---|---|
| lifetime | Any `Lifetime` object |
| area | A lifetime that user code created with `CreateChild` |
| category | An area that objects are placed in, or that several classes register work into |
| generation | Everything registered in a lifetime between two cancels |

The rest of the family is named after the type, so IntelliSense groups it:

| Role | Name |
|---|---|
| Handle to one registered item | `LifetimeRegistration` |
| App root | `Lifetime.App` |
| Scene facade | `SceneLifetimes` |
| Component accessors | `GetLifetime()`, `GetActiveLifetime()` |
| Errors | `LifetimeErrors`, `LifetimeErrorHandler`, `LifetimeErrorContext`, `LifetimeErrorSource`, `LifetimeWorkException` |
| Extension classes | `LifetimeTaskExtensions`, `LifetimeSubscriptionExtensions`, `LifetimeCoroutineExtensions`, `LifetimeComponentExtensions`, `LifetimeTweenExtensions`, `LifetimeSignalBusExtensions` |
| Events | `OwnedEvent`, `OwnedEvent<T>`, `OwnedEvent<T1, T2>`, `OwnedEvent<T1, T2, T3>` and the views `IOwnedEvent...` |
| Tween option | `TweenCancelMode` |
| Zenject installer | `LifetimeInstaller` |
| Hidden component | `ActiveLifetimeTrigger` |
| Editor window class | `JanitorWindow` |

### Smaller naming rules that follow from the collision check

- **No type is named `Janitor`.** See constraint 2. The window class is `JanitorWindow`.
- **No child namespace is named after a framework or after `Editor`.** The DOTween assembly uses the
  root namespace, the Zenject installer lives in `Ecanakli.Janitor.DependencyInjection`, and the
  editor code in `Ecanakli.Janitor.EditorTools`.
- **The event type is `OwnedEvent`,** not `Event` (`UnityEngine.Event` exists) and not a name built
  on "safe", which does not say safe against what. "Owned" states the rule the type enforces: every
  subscription has an owner.
- **The tween option is `TweenCancelMode`,** because UniTask already has `TweenCancelBehaviour`.
- **`LifetimeRegistration` has `Cancel()`, not `Dispose()`.** It ends the item. A
  `CancellationTokenRegistration` only unregisters a callback when disposed, and the two should not
  be confused.
- **There is no implicit conversion from `Lifetime` to `CancellationToken`.** `Token` is read
  explicitly, which keeps every overload unambiguous and makes the moment of reading visible (the
  token changes after a cancel, see [ADR-002](ADR-002-Reusable-Cancel-Versus-Terminal-Dispose.md)).

### Collision analysis

There is no hard collision. The soft ones have a one-line fix each.

| Against | Result | What to do |
|---|---|---|
| The package's own namespace | No type named `Janitor`; no child namespace named `DOTween`, `Zenject` or `Editor` | Nothing |
| `System`, `System.Threading` | No type named `Lifetime` or `OwnedEvent` | Nothing |
| `UnityEngine` | No type named `Lifetime` (only members such as `startLifetime`). `UnityEngine.Event` is why the event type is not called `Event` | Nothing |
| uGUI | `this.OnCancel(...)` shares its name with `ICancelHandler.OnCancel(BaseEventData)` on the UI types that implement it. The interface method does not accept a delegate, so the extension method is chosen | Nothing |
| UniTask | Its `AddTo` takes a `CancellationToken` as the second argument, not a lifetime. `UniTask.Run` is a static method that moves work to the thread pool; the `Run` extension here runs on the calling thread | Nothing |
| DOTween | None. `AddTo` on a tween and `AddTo` on a disposable are told apart by their generic constraints | Nothing |
| Zenject | `SignalBus.Subscribe<TSignal>(handler)` and the owner-taking extension differ in arity | Nothing |
| VContainer (soft) | Its enum `Lifetime` makes the simple name ambiguous (`CS0104`) in a file that imports both namespaces | `using Lifetime = Ecanakli.Janitor.Lifetime;` in that file, or write `VContainer.Lifetime.Singleton` in full |
| R3 (soft) | It also has an `AddTo` for a disposable and a component, so in a file that imports both `disposable.AddTo(this)` binds to R3 without an error (an extension without optional parameters is the better match; `CS0121` only if the other one has the same optional tail) | Pass the lifetime: `disposable.AddTo(this.GetLifetime())` |
| JetBrains.Lifetimes (soft) | Also defines a type named `Lifetime` | It is not normally referenced by game assemblies; an alias solves it where it is |

The `AddTo` that takes a lifetime, as it ships in the Basic Usage sample:

<!-- source: Samples~/BasicUsage/ModalBlocker.cs -->
```csharp
private void OnEnable()
{
    var shown = this.GetActiveLifetime();
    _gate.Block().AddTo(shown);         // an IDisposable owned by the lifetime: disposed when it ends
}
```

The rows for UniTask, DOTween, Zenject and the engine are proven by compilation: the package, its
tests and its samples build against them. The rows for VContainer, R3 and JetBrains.Lifetimes come
from reading those libraries' public API; the package is not compiled against them.

## Alternatives considered

### One word for both: package "Lifetime", type `Lifetime`

Rejected.

**Concrete failure modes.** The word alone does not say that tasks, tweens, coroutines and events
are what is being managed; it describes the mechanism, not the job. It is also too generic to search
for. And a namespace segment `Lifetime` with a type `Lifetime` inside it is exactly the `CS0118`
case of constraint 2, which would have forced an awkward plural namespace.

### "Cancellation Scopes", with a type `CancellationScope`

Rejected. It was the working name before the current one.

It names the action (cancel) and the unit (a scope), and "cancellation scope" is an established term
in structured concurrency. But it still does not say *what* is cleaned up: a reader does not expect
a "cancellation scope" to remove an event listener or kill a tween. The identifiers were long at
every call site (`GetCancellationScope()`, `CancellationScopeRegistration`). And "scope" is already
taken in Unity projects by dependency injection, where it means a container scope.

### A type named `Janitor`

Rejected.

**Concrete failure mode.** `Ecanakli.Janitor.Janitor` is the `CS0118` case: inside the package, in
the samples and in any user namespace under the root, `Janitor` would bind to the namespace. Every
such file would need the fully qualified name or an alias.

It also reads wrong. A component does not have "a janitor" that is destroyed with it; it has a
lifetime. `this.GetLifetime()`, "the lifetime of this component", "the scene's lifetime" and "an
active lifetime" are all natural sentences, and they are the sentences the documentation needs.

### An implicit conversion to `CancellationToken`

Rejected. It would let a lifetime be passed wherever a token is expected, which is convenient, but
it makes overloads that take a lifetime and overloads that take a token ambiguous, and it hides the
moment at which the token is read. That moment matters because the token of a lifetime changes after
`Cancel()`.

## Consequences

**Positive**

- The package name says what it does, and the type name says what the object is. Neither is
  stretched to do the other's job.
- `Lifetime` reads naturally in the API and in prose, and it has precedent as a type name for nested
  termination in other ecosystems.
- One namespace, `Ecanakli.Janitor`, covers every registration call, including the DOTween and
  SignalBus extensions.
- No hard collision with the engine or with the three libraries the package integrates.

**Negative / accepted costs**

- The brand and the type differ. A newcomer looking for a class called `Janitor` finds none, so the
  index page and the README have to say early that the main type is `Lifetime`.
- A project that uses VContainer needs a `using` alias in files that touch both types, typically
  composition roots and injected components.
- A project that uses R3 has to pass the lifetime explicitly in files that import both namespaces.
- "Lifetime" suggests a one-shot span to people who know a `CancellationTokenSource`. This lifetime
  is reusable and its `Token` changes after `Cancel()`. The XML documentation, the concepts page and
  the FAQ state this prominently.
- No automated test guards the naming rules (no type named like a namespace segment, no framework
  name as a namespace segment). They hold because the samples and tests, which live in namespaces
  under the package root, compile.

## See also

- [Architecture](../Architecture.md)
- [FAQ](../../FAQ.md)
- [Troubleshooting](../../Troubleshooting.md)
