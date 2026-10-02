# ADR-003: Tweens are not recyclable

- **Status:** Accepted, implemented in 0.1.0
- **Related:** [Architecture section 8](../Architecture.md#8-integrations-as-gated-assemblies),
  [DOTween](../../DOTween.md), [Pooling](../../Pooling.md)

## Context

DOTween can recycle tween objects. With recycling on (`DOTween.defaultRecyclable`, or
`SetRecyclable(true)` on one tween), a tween that is killed goes back to a pool, and its object is
handed out again for the next tween that anyone creates. Recycling is off by default.

A reference to a recycled tween is a trap. After the kill, the reference does not point at a dead
tween; it points at whatever the object was reused for. The package's tests reproduce this on the
DOTween version they run against (`_s` is the test session; `EnableRecycling()` sets
`DOTween.defaultRecyclable` to true and `NewTween` creates a float tween):

<!-- source: Tests/DOTween/TweenRecyclingTests.cs -->
```csharp
[UnityTest]
public IEnumerator Control_AnUnregisteredRecyclableTween_IsReusedAfterItsKill()
{
    _s.EnableRecycling();
    var first = _s.NewTween(_s.Box());
    first.Kill();
    yield return null;

    var second = _s.NewTween(_s.Box());

    Assert.That(ReferenceEquals(first, second), Is.True, "premise: DOTween hands the killed object to the next tween");
    Assert.That(first.IsActive(), Is.True, "the stale reference reports the stranger's state as its own");
}
```

The two references are the same object, so `Kill()` through the first one kills the second tween.

A lifetime holds exactly such references. Every registered tween stays in the lifetime's entry list
until the generation ends, so that the lifetime can kill it. With recycling this goes wrong in three
places:

1. **The cancel.** A tween that finished early and was reused is still in the list. `Cancel()` kills
   it, and thereby kills an unrelated tween that belongs to another owner.
2. **The bookkeeping.** Finished tweens are swept out of the list by asking `IsActive()`. A stale
   reference answers for a stranger, so the list is no longer bounded.
3. **The user's own handle.** `tween.AddTo(owner)` returns the tween so that it can be killed
   individually with `tween.Kill()`. That is only safe if the reference still means the same tween.

This is the "recycled handle acts on someone else's object" failure in its purest form, and it is
silent: nothing throws, a tween somewhere else in the game stops.

## Decision

**A tween that is registered with a lifetime is taken out of recycling.**

- All three `AddTo` overloads and both `AwaitCompletionAsync` overloads call
  `tween.SetRecyclable(false)` before they do anything else with the tween. This also applies on the
  paths that end the tween immediately (a `null` owner, an owner that already ended).
- **Registration never touches the tween's callbacks.** `AddTo` does not read or write `onKill`,
  `onComplete` or any `OnX` setter. The entry stores the tween and a static kill (or complete)
  function, and the bookkeeping relies on `IsActive()`, which is reliable once the tween cannot be
  reused.
- **The tween is its own handle.** `AddTo` returns the tween with its static type, so it chains like
  any DOTween call, and a later `tween.Kill()` by user code is safe.
- **Only `AwaitCompletionAsync` chains callbacks,** because an await has to learn about completion
  and kill. It saves the existing `onComplete` and `onKill`, runs them first, and restores them when
  the await is consumed. The contract that follows: set your own callbacks *before* the call. A
  callback set afterwards replaces the await's.
- **A cancel of the lifetime does not depend on those callbacks.** The entry that
  `AwaitCompletionAsync(lifetime)` registers kills the tween and then ends the await itself, so the
  await is cancelled even when `onKill` was replaced after the call, and also for a tween nested in
  a Sequence, which DOTween will not kill.

The proof that a registered tween is never handed out again, from the test suite:

<!-- source: Tests/DOTween/TweenRecyclingTests.cs -->
```csharp
[UnityTest]
public IEnumerator AddTo_AKilledRegisteredTween_IsNeverReusedByANewTween()
{
    _s.EnableRecycling();
    var area = _s.Area();
    var registered = _s.NewTween(_s.Box());
    registered.AddTo(area);
    registered.Kill();
    yield return null;

    var next = _s.NewTween(_s.Box());
    var afterThat = _s.NewTween(_s.Box());

    Assert.That(ReferenceEquals(registered, next), Is.False);
    Assert.That(ReferenceEquals(registered, afterThat), Is.False);
    Assert.That(registered.IsActive(), Is.False, "the held reference stays dead");
    Assert.That(next.IsActive(), Is.True);
}
```

Two related rules were decided with this one:

- **An owner that is not active always kills,** even in `TweenCancelMode.Complete`. A tween added to
  a lifetime that is cancelling or disposed never ran for that owner; completing it would write end
  values and fire completion callbacks in the middle of a teardown.
- **Register the root `Sequence` only.** DOTween ignores `Kill` on a tween that is nested in a
  Sequence and logs nothing. The package cannot change that; it reports a tween that is still active
  right after its kill as `JANITOR102` in the editor window.

## Alternatives considered

### Hook `onKill` to drop the entry when the tween dies

Rejected, and it is the obvious design, so the reason matters.

**Concrete failure mode.** `onKill` is one delegate field on the tween. The fluent setter
`OnKill(...)` *replaces* it. If the package installs a hook in `AddTo` and user code calls
`.OnKill(Cleanup)` afterwards, the hook is gone and the stale entry is back, silently. If the
package installs its hook last, it has replaced the user's callback instead. Chaining instead of
replacing does not help either: UniTask's own tween await chains the same field and restores what it
found, so two libraries that both chain can lose each other's callback, depending on the order of
calls. Bookkeeping must not depend on a field that three parties write.

### Keep recycling and trust `IsActive()`

Rejected. This is the bug described in the context: the stale reference reports the stranger's state
as its own.

### Keep recycling and compare an identity

Rejected. It would need a value that changes every time the object is reused, to store next to the
reference and compare before every kill. The ids on a tween are set by user code and are not such a
value, and the package found no other public per-use identity to compare against.

### Document "do not turn recycling on"

Rejected. Recycling is a global setting that another package or another team member can change far
away from the code that breaks, and the breakage is silent. A rule that is this easy to violate by
accident has to be enforced by the code that depends on it.

### Rely on `SetLink`

Rejected as the mechanism. `SetLink(gameObject)` binds a tween to one trigger, the destruction (or
activation state) of a GameObject, and it acts on DOTween's next update: a linked tween is still
active in the frame its target is destroyed and dead one frame later (pinned by a test). It cannot
express an area, a manual cancel or a plain C# owner, and it does not kill synchronously during a
teardown. It stays useful for its pause and restart behaviours, and using it together with `AddTo`
is safe.

### Wrap the tween in a handle type

Rejected. A wrapper would let the package control identity, but `AddTo` would no longer return the
tween, the DOTween fluent chain would end at the call, and user code would hold two things for one
tween.

## Consequences

**Positive**

- A reference to a registered tween always means that tween. The lifetime's kill, the sweep and the
  user's own `Kill()` are all safe.
- The user's callbacks and UniTask's awaits are untouched by registration.
- `AddTo` is a pure trailing call: it returns the tween and allocates nothing once warm.

**Negative / accepted costs**

- **Registered tweens skip DOTween's pool.** In a project that turned recycling on, every registered
  tween is allocated when it is created and collected after it dies. The package does not measure
  that cost; its allocation tests cover the package's own calls. Projects that leave recycling at
  its default pay nothing.
- **`SetRecyclable(false)` is a visible side effect** on a tween the user created. A project that
  relies on recycling for one hot tween path should not register those tweens, and must then end
  them itself.
- **The nested-tween case is only detected, not prevented,** and only in the editor window.
- **`AwaitCompletionAsync` does touch `onComplete` and `onKill`** for the duration of the await. It
  restores them, but a tween should be awaited once, and callbacks have to be set before the call.
  An `onComplete` set afterwards replaces the await's: a normal completion is then seen only through
  the automatic kill, and the await ends as cancelled. An `onKill` set afterwards does not stop a
  lifetime cancel from ending the await, but an external kill of such a tween leaves the await
  pending until the lifetime ends.
- **Each registration is an entry.** Registering or awaiting the same live tween again on one
  lifetime adds one entry per call until the generation ends; entries are dropped only when their
  tween is inactive.

**Enforced by tests** (`Tests/DOTween/`, run with recycling on)

- `TweenRecyclingTests.Control_AnUnregisteredRecyclableTween_IsReusedAfterItsKill`: the premise.
- `TweenRecyclingTests.AddTo_AKilledRegisteredTween_IsNeverReusedByANewTween`.
- `TweenRecyclingTests.Cancel_OverAKilledRegisteredTween_DoesNotKillATweenCreatedAfterwards` and
  `Cancel_OverARegisteredTweenThatCompleted_DoesNotKillATweenCreatedAfterwards`.
- `TweenRecyclingTests.AwaitCompletionAsync_Lifetime_MakesTheTweenNonRecyclable` and
  `AwaitCompletionAsync_Token_MakesTheTweenNonRecyclable`.
- `TweenBookkeepingTests.AddTo_DoesNotReplaceTheUsersCallbacks_AndTheyRunOnceEach`.
- `TweenCancelTests.AddTo_AnAlreadyCancellingLifetime_KillsEvenInCompleteMode`.
- `TweenSequenceTests.Cancel_ANestedTweenRegisteredOnItsOwn_IsNotKilledBecauseDOTweenIgnoresIt`.
- `AwaitCompletionReplacedCallbackTests`:
  `Await_OnKillReplacedAfterTheCall_ALifetimeCancelStillCancelsTheTask`,
  `Await_OnKillReplacedAfterTheCall_AnExternalKillLeavesTheTaskPendingUntilTheLifetimeEnds` and
  `Await_OnCompleteReplacedAfterTheCall_ANormalCompletionEndsTheTaskAsCancelled`.

## See also

- [DOTween](../../DOTween.md)
- [Performance](../../Performance.md)
- [Troubleshooting: JANITOR102](../../Troubleshooting.md#janitor102)
