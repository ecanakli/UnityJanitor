# ADR-002: Reusable `Cancel` versus terminal `Dispose`

- **Status:** Accepted, implemented in 0.1.0
- **Related:** [Architecture section 5](../Architecture.md#5-teardown-in-passes),
  [ADR-005](ADR-005-No-Removal-Lines-In-User-Code.md), [Concepts](../../Concepts.md),
  [Stopping work](../../Stopping-Work.md)

## Context

A game stops work for two different reasons.

- **Stop and continue.** Gameplay is reset, a popup's work is cancelled while the popup stays, a
  countdown is restarted, a pooled object is returned and rented again. The thing that owned the
  work is still there and will own new work in a moment. This is by far the more frequent case.
- **End for good.** The owner is going away: the object is destroyed, the scene unloads, the
  application quits.

Cancellation primitives in .NET are one-shot. A `CancellationTokenSource` that was cancelled cannot
be used again, so "stop and continue" becomes three lines that every restartable operation repeats
by hand: cancel the old source, dispose it, create a new one. That swap is where the bugs are. The
field is replaced while other code still holds the old token; the dispose is forgotten; a call
arrives after the dispose and throws; nothing cancels the last source when the owner dies.

A scope object that is itself one-shot has the same problem one step up. Anything that holds a
reference to it (a field, an injected service, a category shared between classes, a row in a
diagnostics window) holds a dead object after the first stop, and somebody has to hand out the
replacement.

There is a second constraint. Some lifetimes are created by the package on behalf of an owner: the
lifetime behind `this.GetLifetime()` backs every `this.Run(...)`, `tween.AddTo(this)` and
`Subscribe(handler, this)` that the component will ever make. If user code could end it while the
component is alive, every later registration on that component would be ended immediately, with no
error at the call site.

## Decision

**Two operations with different meanings. `Cancel()` is repeatable and leaves the lifetime usable.
`Dispose()` is terminal and is for areas.**

| | `Cancel()` | `Dispose()` |
|---|---|---|
| Allowed on | Any lifetime | Areas (lifetimes created with `CreateChild`). On a package-owned lifetime it is ignored, with `JANITOR107` in the editor and in development builds |
| Effect on this lifetime | The current generation ends: its token is cancelled and its items are ended. A fresh generation opens | The same things end, and then the lifetime is detached from the tree for good |
| Effect on descendants | Each one is cancelled and stays attached and usable | Each one is disposed. Object lifetimes that were placed in the area are the exception: they are cancelled and moved back under their scene |
| Afterwards | New registrations go into the new generation; `Token` returns a new token | Every registration is ended immediately, forever; `Token` returns a cancelled token |
| Repeatable | Any number of times | Idempotent |

There is no `End()`, `Stop()` or `Restart()`. Stopping is `Cancel()`. Restarting is `Cancel()`
followed by a registration. Ending for good is `Dispose()` on an area, or the owner going away.

### Generations

- A lifetime has an integer generation. It is incremented when a cancel or a dispose finalizes.
- A registration handle is `(lifetime, generation, slot, slot version)`. It is valid only while the
  lifetime is not disposed and all three numbers match.
- A token belongs to exactly one generation. Its source is cancelled when the generation ends, and
  it is never reused and never disposed, so a token held by code that is still running stays valid
  and cancelled.
- Tasks, awaited tweens and event subscriber slots record the generation they were created in.
- The `Lifetime` *object* deliberately follows the current generation. That is what makes it
  reusable, and it is why `Token` returns a different token after `Cancel()`.

### What follows from the two operations

- **`Cancel()` ends everything in the generation, subscriptions included.** There is one rule for
  every kind of work. Long-lived listening therefore lives on the owner's lifetime, and activity
  that starts and stops lives in a child area that is cancelled on its own.
- **Tasks have no per-item handle.** `Run`, `After` and `Every` share the generation's token and
  return nothing. A task that has to be stopped alone gets its own child area.
- **Lifetimes are not pooled.** They are long-lived objects that user code holds.
- **Disposing a category does not dispose the objects placed in it.** Their lifetimes are cancelled
  and re-homed under their scene, because a category groups cancellation and does not own the
  objects.

The granularity that results:

| Stop | Call |
|---|---|
| Everything | `Lifetime.App.Cancel()` |
| One scene | `SceneLifetimes.Get(scene).Cancel()` |
| One object | `GetLifetime().Cancel()` on it |
| One area or category | `area.Cancel()` |
| One item | `registration.Cancel()`, or `tween.Kill()` |
| One task | `Cancel()` on the area it was given |

The restart pattern, as it ships in the Basic Usage sample:

<!-- source: Samples~/BasicUsage/Countdown.cs -->
```csharp
private void Awake() => _run = this.GetLifetime().CreateChild("Run");

public void StartCountdown(int seconds)
{
    _run.Cancel();                                    // a countdown already running stops here
    _run.Run(ct => TickAsync(seconds, ct));
}

public void StopCountdown() => _run.Cancel();
```

## Alternatives considered

### One-shot scopes: cancelling ends the scope, a restart creates a new one

Rejected.

**Concrete failure mode.** It moves the hand-written swap from a token source to a scope without
removing it. The class still has a field that is replaced on every restart, and everything that
captured the old scope is now stale: a category that three classes received by injection cannot be
replaced for all of them at once, and an object's own lifetime would be dead after a manual cancel
while the object is alive and still calling `this.Run(...)`.

### A serial holder type, plus separate owner and view types

Rejected. A "serial" holder keeps one current child and replaces it on assignment, which gives
cancel-previous-start-new in one line. It is the same swap, hidden inside a type, and it adds public
types for something that `Cancel()` followed by a registration already expresses. Fewer public types
won.

### `Dispose()` honoured on package-owned lifetimes

Rejected.

**Concrete failure mode.** A `using` statement, a container that disposes what it resolves, or a
well-meant `GetLifetime().Dispose()` in a teardown method ends the lifetime of a component that is
still alive. From then on every `this.Run(...)` on it does not start and every subscription is not
added, silently. The object looks healthy and does nothing. Ignoring the call and warning in
development is the smaller harm; the owner (destroy, scene disposal, exit) remains the only thing
that can end such a lifetime.

### Subscriptions survive `Cancel()` and are removed only by `Dispose()`

Rejected. It would match the intuition "cancel stops work, listeners stay", but it needs a second
class of registration, an "on dispose" primitive next to `OnCancel`, a rule that classifies every
`IDisposable` as activity or as listening, and special handling in the active lifetime, where a
deactivation must remove listeners. One rule that covers everything is easier to predict than two
rules with a classification in between. The cost is listed below.

### A handle per task

Rejected. A handle that can cancel one `Run` needs a token source for that task, which is one
allocation per call for a capability most tasks never use. A child area gives the same control where
it is needed and costs nothing where it is not.

### Linked token sources for the hierarchy

Rejected. A child source linked to its parent's token allocates per child and per generation, and a
linked source keeps a registration inside the parent's token until it is disposed, which is the
variant of token source that does leak when it is forgotten. The tree walk in `Cancel()` does the
propagation without any link.

### Disposing or recycling a generation's token source

Rejected. Code that is still running may hold the token after the cancel, and a disposed source is
not safe to keep using. A source also cannot be made usable again once cancellation was requested. A
plain source holds no unmanaged state, so dropping the reference is enough.

### Pooling lifetime objects

Rejected. User code holds them in fields. Handing a pooled lifetime to a new owner while an old
field still points at it is the "recycled handle acts on someone else's object" failure that the
generations exist to prevent.

## Consequences

**Positive**

- Restarting is two lines with no field swap, and it is safe to call at any moment, any number of
  times, including after the owner is gone.
- A lifetime has a stable identity. It can be held in a field, injected, shared as a category and
  shown as one row in the window for as long as its owner lives.
- Everything from before a cancel is harmless afterwards: a stale token is cancelled, a stale handle
  does nothing, a stale event slot is skipped. This is what makes `SetActive` pooling safe.
- One rule: "`Cancel()` ends what was registered since the last cancel."

**Negative / accepted costs**

- **`Token` is not stable.** Code that stores `lifetime.Token` in a field holds a cancelled token
  after the next `Cancel()`. This differs from every one-shot token source and has to be taught.
- **One hole remains.** Code that ignores its token (an await that takes none) can resume after a
  `Cancel()` and register through the lifetime *object*; that registration lands in the new
  generation. The editor window flags the task as an overrun (`JANITOR101`); nothing catches it at
  compile time.
- **Cancelling an owner's lifetime also stops its listening.** `GetLifetime().Cancel()` on a
  controller removes the button listener it subscribed in `Awake`, and nothing subscribes it again.
  The pattern (listening on the owner, activity in a child area) has to be learned.
- **`Dispose()` on a package-owned lifetime does nothing,** and in a release build it does so
  without a warning.
- **Two verbs to learn,** where most libraries have one.
- **A token source per generation that reads its token.** The source is created lazily, so a
  generation that only holds tweens and subscriptions creates none.
- **Restart costs a field and an extra line** compared with a helper that would do both.

**Enforced by tests**

- `LifetimeCancelReuseTests`: `Cancel_RegistrationAfterCancel_LandsInNewGeneration`,
  `Cancel_TokenReadAfterCancel_DiffersAndOldTokenStaysCancelled`,
  `Cancel_Repeated1000Times_KeepsCountsBounded`, `Cancel_LeavesLifetimeActiveAndAttached`.
- `LifetimeGenerationTests`: stale, repeated and default handles are no-ops, including a slot that
  was reused in the same generation.
- `LifetimePropagationTests`: `Cancel_Parent_KeepsChildrenUsableAndAttached`,
  `Dispose_Parent_DisposesEveryDescendantAndRunsTheirItems`,
  `Registration_OnDisposedLifetime_EveryLaterRegistrationIsTerminated`.
- `LifetimeReentrancyTests`: the full matrix of `Cancel` and `Dispose` called from inside each
  other.
- `ComponentLifetimeTests.Dispose_OnAComponentLifetime_IsIgnoredWithJanitor107` and
  `LifetimeAppTests.Dispose_OnApp_IsIgnoredWithAJanitor107Warning`.
- `CategoryLifetimeTests.Dispose_OnTheCategory_CancelsAndRehomesPlacedLifetimes_AndTheObjectKeepsWorking`.
- `ActiveLifetimeTests.Deactivate_CancelsTheActiveLifetime_AndReactivationOpensANewGeneration`.

## See also

- [Concepts](../../Concepts.md)
- [Stopping work](../../Stopping-Work.md)
- [Pooling](../../Pooling.md)
- [Architecture](../Architecture.md)
