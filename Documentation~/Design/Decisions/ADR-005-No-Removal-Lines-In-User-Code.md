# ADR-005: No removal lines in user code

- **Status:** Accepted, implemented in 0.1.0
- **Related:** [Architecture section 6](../Architecture.md#6-adapters),
  [ADR-002](ADR-002-Reusable-Cancel-Versus-Terminal-Dispose.md), [Events](../../Events.md),
  [Migration](../../Migration.md)

## Context

The most common leak in event-driven Unity code is a subscription whose removal lives in a different
method from its creation: `+=` in `OnEnable` and `-=` in `OnDisable`, `AddListener` and
`RemoveListener`, `Subscribe` and `Unsubscribe`. The two halves have to stay mirrored by hand. When
the second half is missing, or in the wrong method, a publisher that lives longer than the listener
keeps calling it: on a destroyed object, or once per time the listener was opened.

An earlier shape of this package bound the removal to a lifetime but still had the developer write
it: subscribe with `+=`, then register `OnCancel(() => source.Changed -= handler)`. That puts the
two halves next to each other, which is better, but the removal is still a line of user code. It can
be left out and nothing complains. The requirement that came out of this is strict: **cleanup must
not depend on a line the developer has to remember.**

Three facts make that harder than it sounds.

1. **A C# `event` cannot be handed to a library.** Outside its declaring class it supports only `+=`
   and `-=`; it is not a value that can be passed as an argument. A library cannot subscribe to it,
   or unsubscribe from it, on the caller's behalf.
2. **Some events belong to code the game cannot change:** `Application.lowMemory`, an SDK's
   callbacks, a `UnityEvent` on a `Button`, a DI framework's signal bus.
3. **Removal is not enough if dispatch works on a snapshot.** A C# multicast delegate and a
   `UnityEvent` both call the handlers that were subscribed when the invoke started. A handler that
   is removed during the invoke, because an earlier handler destroyed or closed its owner, still
   runs once more in that invoke, on an owner that has ended.

## Decision

**Every subscription API takes its owner in the same call, and the package performs the removal.**
The removal happens when the owner's current generation ends (a cancel, a dispose, the object's
destruction, a deactivation for an active lifetime) or when the returned registration is cancelled.
There is no subscribe without an owner; `Lifetime.App` is the explicit "for the whole session".

| Source of the event | Call | The package removes it with |
|---|---|---|
| An event the game declares | `OwnedEvent`: `wallet.CoinsChanged.Subscribe(handler, owner)` | Removing the subscriber slot |
| `UnityEvent` (zero to four arguments) | `button.onClick.Subscribe(handler, owner)` | `RemoveListener` |
| Zenject `SignalBus` | `bus.Subscribe<TSignal>(handler, owner)` | `TryUnsubscribe` |
| An event the game does not own | `owner.Subscribe(add, remove, handler)` | The `remove` delegate given in the same call |
| Any `IDisposable` subscription | `subscription.AddTo(owner)` | `Dispose` |

Every form returns a `LifetimeRegistration` that ends that one subscription early. The owner is a
`Lifetime` or a component. A `MonoBehaviour` owner means its component lifetime; any other component
means the lifetime of its GameObject.

### `OwnedEvent`: events the game declares

Because a C# `event` cannot be passed to a library, the package provides the event type. It is a
field on the publisher, exactly where the `event` was:

<!-- source: Samples~/BasicUsage/Wallet.cs -->
```csharp
/// <summary>Holds the coin count and publishes changes through a subscribe-only event.</summary>
public sealed class Wallet
{
    private readonly OwnedEvent<int> _coinsChanged = new("CoinsChanged");
    private int _coins;

    public int Coins => _coins;

    public IOwnedEvent<int> CoinsChanged => _coinsChanged;   // outsiders subscribe; only Wallet invokes

    public void Add(int amount)
    {
        _coins += amount;
        _coinsChanged.Invoke(_coins);
    }
}
```

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

public interface IOwnedEvent<T>
{
    LifetimeRegistration Subscribe(Action<T> handler, Lifetime owner);
}
```

- It exists with zero, one, two and three arguments, mirroring `Action`.
- `IOwnedEvent...` is the subscribe-only view. A publisher keeps the event private and exposes the
  view, which gives the encapsulation of the `event` keyword: outsiders subscribe, only the owner
  invokes. The `Component` owner on a view is an extension method, so the interface stays one
  method.
- Handlers run in subscription order, each in its own `try`/`catch`. An exception is routed to
  `LifetimeErrors.Handler` with the subscriber's owner and call site, and the remaining handlers
  still run.
- A handler whose owner is not active, or is in a different generation, is skipped.
- A removal during an invoke takes effect at once; a subscription made during an invoke is first
  delivered by the next one.
- A nested invoke is safe. Beyond 64 nested invokes the invoke is refused and reported as
  `JANITOR115`, in every build.
- It is not a bus: no global registry, no routing by type, no asynchronous dispatch. It is also not
  serialized; Inspector wiring stays on `UnityEvent`.

### Paired `Subscribe`: events the game does not own

For an event that cannot be changed, the subscription and its removal travel in one call:

<!-- source: Samples~/BasicUsage/PlatformHooks.cs -->
```csharp
private void Awake()
{
    // Custom delegate type: explicit type argument; static lambdas allocate nothing.
    this.Subscribe<Application.LowMemoryCallback>(
        static h => Application.lowMemory += h,
        static h => Application.lowMemory -= h,
        OnLowMemory);

    // Action<T> event: C# 9 cannot infer T from lambdas or method groups, so write <string>.
    this.Subscribe<string>(h => _ads.RewardGranted += h, h => _ads.RewardGranted -= h, OnRewardGranted);

    // Plain Action event: no type argument needed.
    this.Subscribe(h => _ads.Closed += h, h => _ads.Closed -= h, OnAdClosed);
}
```

`add(handler)` runs immediately. The package stores the handler with `add` and `remove`, and calls
`remove(handler)` exactly once. There are five forms: `Action`, `Action<T>`, `Action<T1, T2>`,
`Action<T1, T2, T3>` and any delegate type. If `add` throws, the error is routed and nothing is
registered. This is the only place where `-=` appears in code written against the package, and it is
in the same statement as its `+=`.

### `UnityEvent`: a guard, because of the snapshot

`evt.Subscribe(handler, owner)` does not add the handler as the listener. It adds a small guard that
calls the handler only while the subscription is live, the owner is active in the same generation,
and the handler's target object has not been destroyed. When the subscription ends, the package
removes that guard with `RemoveListener`.

### One dispatch rule for every source

> A subscription removed during a dispatch is not delivered in that dispatch.

`OwnedEvent` implements it by checking each slot as it reaches it. `UnityEvent` gets it from the
guard. Zenject's `SignalBus` applies a removal immediately by itself (verified by a test), so it
needs nothing. For the paired form the dispatch belongs to the foreign event and the rule cannot be
enforced; the removal itself is still guaranteed.

### Duplicates are ignored

Subscribing an equal handler (same target, same method) for the same owner again returns the
existing registration and raises `JANITOR105` in the editor and in development builds. The same
handler under a different owner is a separate subscription. For `SignalBus`, which can hold only one
subscription per signal and handler, a different owner throws `InvalidOperationException`.

The paired form is covered too: a call with an equal handler, an equal `add` and an equal `remove`
for the same owner is a repeat. Delegates are equal when they have the same target and the same
method, which holds for static lambdas, for lambdas that capture only `this` or fields, for method
groups and for cached delegates. A lambda that captures a local variable or a parameter is a new
object on every call, so such a repeat is not recognised and subscribes twice.

### The owner is whichever side ends first

The rule "name the owner" has a direction. A subscription is released when its owner's generation
ends, not when the publisher goes away. A short-lived listener on a long-lived publisher (a popup on
a wallet) owns the subscription itself. A long-lived listener on short-lived publishers (a manager
that subscribes to every object it spawns) should pass the *publisher's* lifetime as the owner, so
that each subscription goes when its publisher does. `CountdownTracker` in the Basic Usage sample is
that case; [Events](../../Events.md) shows it.

### `OnCancel` is not an unsubscribe

`OnCancel` stays public as the primitive for custom cleanup (release a handle, reset a flag) and as
the extension point that integrations build on. It is documented as never being the way to remove a
subscription.

### `Cancel()` removes subscriptions

A subscription is an item like a task or a tween. `Cancel()` on its owner ends it. The reasoning,
and the alternative, are in [ADR-002](ADR-002-Reusable-Cancel-Versus-Terminal-Dispose.md).

## Alternatives considered

### `+=`, then `OnCancel(() => ... -= handler)`

Rejected. It was the previous shape.

**Concrete failure mode.** The removal is still written by hand. A developer who writes the `+=` and
forgets the `OnCancel` line has reproduced the original bug with the package installed, and no tool
notices. Putting two lines next to each other is a convention; taking the owner as a parameter is a
signature.

### A subscribe without an owner that returns a handle

Rejected. `Subscribe(handler)` returning something to dispose later is the familiar shape of many
libraries, and it brings back the forgotten second step under a new name ("forgot to dispose",
"forgot to add it to a bag"). When the owner is a required parameter, the code that leaks does not
compile.

### Weak-reference events

Rejected.

**Concrete failure mode.** A weak event removes a listener when the garbage collector collects it. A
destroyed Unity object is not collected at the moment it is destroyed; its managed part stays
reachable until the next collection, and can be kept alive indefinitely by any other reference. For
that whole time the event keeps calling handlers on destroyed objects. The timing is not
deterministic, it differs between the editor and a player, and weak references allocate.

### Keep the `event` keyword and only document the snapshot behaviour of `UnityEvent`

Rejected. A documented caveat still leaves "a handler runs on an object that has ended" reachable
through ordinary code: handler A closes a popup, handler B of that popup runs anyway. The package's
purpose is that forgetting and ordering cannot produce this class of bug, so the adapter has to
close it.

### Put a bus inside `OwnedEvent`

Rejected as out of scope. How classes talk to each other (a typed bus, signals, commands) is an
architecture choice of the game. Where a subscription ends is a lifetime concern. `OwnedEvent`
solves the second without prescribing the first; a messaging package can be built on the same
`OnCancel` primitive.

### Subscriptions survive `Cancel()`

Rejected; see [ADR-002](ADR-002-Reusable-Cancel-Versus-Terminal-Dispose.md).

## Consequences

**Positive**

- The removal is structural. There is no second line, so there is nothing to forget, misplace or
  leave unmirrored.
- A handler does not run on an owner that ended earlier in the same dispatch, for `OwnedEvent`,
  `UnityEvent` and `SignalBus` alike.
- The double-subscribe bug of `OnEnable` is neutralised and reported.
- One failing handler no longer silences the handlers after it.
- Subscriptions are visible: each one is an entry in its owner's row in the Janitor window, with the
  call site that made it.
- Publishers keep the encapsulation they had with the `event` keyword.

**Negative / accepted costs**

- **A new type in the game's own API.** Classes declare `OwnedEvent<T>` where they declared
  `event Action<T>`. Converting a publisher breaks every `+=` on it at compile time. It is not
  serialized, and it stops at three arguments.
- **Dispatch differs from a C# event.** Code that relied on snapshot semantics (a handler removed
  during an invoke still runs) behaves differently.
- **The paired form is verbose.** Two lambdas and, for every form except plain `Action`, an explicit
  type argument, because C# 9 cannot infer it from a lambda or a method group (`CS0029` on the two
  lambdas when it is missing). Lambdas that capture allocate a closure and two delegates when the subscription is made.
- **A `UnityEvent` subscription allocates** one guard object and one delegate, where the raw
  `AddListener` allocated only what Unity allocates.
- **Duplicate detection is a scan at subscribe time:** over the event's subscribers for
  `OwnedEvent`, over the owner's entries for `UnityEvent` and for the paired form. For the paired
  form it is also incomplete: lambdas that capture a local are never recognised as a repeat.
- **A long-lived owner collects the subscriptions it never ends.** Nothing releases a subscription
  when only the publisher dies. With the long-lived side as the owner, each dead publisher leaves
  one entry (and stays reachable through it) until that owner's generation ends, and every
  `UnityEvent` subscribe on that owner scans a longer list. Choosing the owner is the developer's
  job; the package cannot detect the wrong choice, except through the entry-count warning
  (`JANITOR106`) in the editor.
- **Cancelling an owner removes its listeners.** See ADR-002.
- **Nothing stops user code from writing a raw `-=` or `RemoveListener` elsewhere.** The package
  makes the removal unnecessary; it cannot make it impossible.
- **`OwnedEvent` is main-thread only.** Subscribing off the main thread throws; invoking off the
  main thread is checked in the editor and in development builds only.

**Enforced by tests**

- `OwnedEventTests`:
  `Invoke_OwnerCancelledMidInvokeBeforeItsHandlerIsReached_IsSkippedInTheSameInvoke`,
  `Invoke_SubscribeDuringInvoke_IsDeliveredByTheNextInvokeOnly`,
  `Invoke_HandlerThrows_IsRoutedWithTheOwnerAndTheCallSiteAndTheOthersStillRun`,
  `Subscribe_EqualHandlerForTheSameOwner_ReturnsTheExistingRegistrationAndRaisesJanitor105`,
  `Invoke_DepthLimit_Delivers64AndRefusesThe65thWithJanitor115`,
  `IOwnedEventView_DoesNotExposeInvoke`.
- `PairedSubscribeTests`: `Subscribe_Action_RemoveRunsOnceOnCancelWithTheSameHandler`,
  `Subscribe_AddEndsTheOwner_RemoveRunsImmediatelyExactlyOnce`,
  `Subscribe_ThrowingAdd_IsRoutedWithNoEntryAndRemoveIsNeverCalled`.
- `PairedSubscribeDuplicateTests`:
  `Subscribe_TheSameAddRemoveAndHandlerTwice_AddsOnceAndReturnsTheFirstRegistration`,
  `Subscribe_LambdasCapturingAPerCallVariable_AreNotRecognisedAsRepeats`.
- `UnityEventDispatchTests`:
  `Invoke_ListenerACancelsTheOwnerOfListenerB_BIsNotCalledInThatInvokeAndNothingIsLogged`,
  `Invoke_ListenerADestroysTheObjectThatOwnsListenerB_BIsNotCalledInThatInvoke`,
  `Invoke_HandlerWhoseTargetWasDestroyed_IsSkippedWithoutAnError`.
- `UnityEventDuplicateTests.Subscribe_SameHandlerSameOwner_ReturnsTheExistingRegistrationAndLogsJanitor105`.
- `ActiveLifetimeTests.OwnedEventSubscribedInOnEnable_IsRemovedOnDisable_AndAReEnableDoesNotDoubleIt`.

## See also

- [Events](../../Events.md)
- [Migration](../../Migration.md)
- [Zenject](../../Zenject.md)
- [Troubleshooting: JANITOR105](../../Troubleshooting.md#janitor105)
