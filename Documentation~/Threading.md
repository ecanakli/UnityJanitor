# Threading

[Back to index](index.md)

The package is for Unity's main thread. This page says exactly what that means: which calls throw
on another thread, which calls are deferred to the main thread instead, what "deferred" changes for
the code that made the call, and how much of this is checked in a player build.

---

## The contract in one paragraph

Everything that **adds** something to a lifetime, or reads its token, must be called on the main
thread and throws elsewhere. The three calls that **stop** things (`Lifetime.Cancel()`,
`Lifetime.Dispose()` and `LifetimeRegistration.Cancel()`) may be called from any thread: off the
main thread they do nothing immediately, they are queued, and they run on the main thread at its
next update. Nothing in the package ever runs your callbacks, kills a tween or touches a Unity
object on a thread other than the main one.

The reason is not caution. Lifetimes end tweens, coroutines, UnityEvent listeners and Unity objects,
none of which may be touched from another thread, and the lifetime tree itself is not locked: the
registration path is kept free of locks and allocations on purpose.

---

## A task that leaves the main thread

A task started with `Run` may leave the main thread, do its work elsewhere, and come back:

<!-- source: Samples~/BasicUsage/BackgroundWork.cs -->
```csharp
public sealed class BackgroundWork : MonoBehaviour
{
    private TMP_Text _label;
    private Lifetime _job;

    // The view arrives here because the demo builds its UI in code.
    public void Initialize(TMP_Text label) => _label = label;

    private void Awake() => _job = this.GetLifetime().CreateChild("Job");

    public void CountPrimes(int limit)
    {
        _job.Cancel();                                 // a job already running stops here, so only the latest answer lands
        _job.Run(ct => CountPrimesAsync(limit, ct));
    }

    private async UniTask CountPrimesAsync(int limit, CancellationToken ct)
    {
        await UniTask.SwitchToThreadPool();
        var count = 0;
        for (var n = 2; n < limit; n++)
        {
            ct.ThrowIfCancellationRequested();     // a thread-pool loop does not stop unless it checks the token
            if (IsPrime(n))
            {
                count++;
            }
        }

        // The token form throws after a cancel; the token-less form would resume here and touch a dead label.
        await UniTask.SwitchToMainThread(ct);
        _label.text = $"{count} primes below {limit}";
    }

    private static bool IsPrime(int n)
    {
        for (var d = 2; d * d <= n; d++)
        {
            if (n % d == 0)
            {
                return false;
            }
        }

        return true;
    }
}
```

1. **`Awake` creates the `Job` area** under the component lifetime. It holds one job at a time.
2. **`CountPrimes` is called on the main thread.** `_job.Cancel()` cancels the token of a job that
   is still running, and `_job.Run` reads the token of the new generation and starts the method
   with it. Both calls happen on the main thread.
3. **`SwitchToThreadPool()`.** The loop runs on a thread-pool thread. It may read `ct`: the token
   is an ordinary `CancellationToken`. It may not register anything on a lifetime or touch a Unity
   object from there.
4. **The job is cancelled while the loop runs:** `CountPrimes` is called again, the component is
   destroyed, or its lifetime is cancelled. The token is cancelled on the main thread. The loop
   sees that at its next iteration and throws the cancellation on the pool thread. Nothing after
   the loop runs, and `Run` treats the cancellation as a normal end. After a second `CountPrimes`
   only the newer job is left, so an older answer can never land after a newer one.
5. **The loop finishes first.** `SwitchToMainThread(ct)` brings the method back. If the job was
   cancelled in the meantime, this await throws instead of letting the method continue, so the
   label of a destroyed object is never touched.
6. **Back on the main thread** the label is set.

Two things in this class carry the weight. Without `ct.ThrowIfCancellationRequested()` the loop
would run to its end after a cancel, because nothing else can interrupt a thread-pool loop. Without
the token in `SwitchToMainThread(ct)` the method would resume on the main thread after its owner
ended.

When a task completes on another thread, the package's own bookkeeping for it (removing its entry,
routing an exception to `LifetimeErrors.Handler`) is moved back to the main thread before it runs.
For a task, your error handler is therefore called on the main thread.

---

## What throws off the main thread

These calls throw `InvalidOperationException` when they are made on another thread. The message
names the operation, for example `Janitor: Lifetime.Token must be called on the main thread.`

| Call | Notes |
|---|---|
| `lifetime.OnCancel(...)`, `disposable.AddTo(...)` | Nothing is registered |
| `lifetime.Run(...)`, `After(...)`, `Every(...)` | Nothing is started |
| `lifetime.StartCoroutine(host, routine)` | Nothing is started |
| `ownedEvent.Subscribe(...)`, `unityEvent.Subscribe(...)`, the paired `Subscribe(add, remove, handler)` | Nothing is subscribed; `add` is not called |
| `lifetime.Token` | |
| `lifetime.CreateChild(...)` | No area is created |
| `GetLifetime()`, `GetActiveLifetime()`, and every `this.X(...)` shortcut on a `MonoBehaviour` | Checked before any Unity object is touched |
| `SceneLifetimes.Get`, `Dispose`, `DisposeAll` | |
| DOTween: `tween.AddTo(...)`, `tween.AwaitCompletionAsync(...)` | The tween is left untouched |
| Zenject: `signalBus.Subscribe<TSignal>(handler, owner)` | Nothing is subscribed |

The check comes first in each of these, so a call that throws has changed nothing: nothing was
registered, started or subscribed, and no Unity object was touched.

One editor-only case throws a different exception. After scripts were recompiled during Play Mode,
the first call into the package starts a new session, and it can do that on the main thread only. A
first call from another thread throws the "available in Play Mode only" exception instead; see
[Troubleshooting](Troubleshooting.md#scripts-were-recompiled-during-play-mode).

One call is checked less strictly. **`OwnedEvent.Invoke` off the main thread** throws
`InvalidOperationException` in the editor and in development builds, once the event has had a
subscriber. In a release build the check is removed, because `Invoke` is a hot path. Invoking from
another thread is still wrong there; it is not detected.

---

## What is deferred to the main thread

`Lifetime.Cancel()`, `Lifetime.Dispose()` and `LifetimeRegistration.Cancel()` never throw, on any
thread. Called off the main thread they are **marshalled**: put in a queue and executed on the main
thread at the next update.

Take a `Cancel()` made from a worker thread. Moment by moment:

1. **A worker thread calls `area.Cancel()`.** The call notices it is not on the main thread. It
   adds "cancel this lifetime" to a queue, asks for one drain of that queue on the main thread, and
   returns. In the editor and in development builds it also logs
   [JANITOR111](Troubleshooting.md#janitor111).
2. **Right after the call returns, nothing has stopped.** The token is not cancelled, no item was
   ended, the generation is the same, `IsDisposed` and `IsActive` read as before. Work on the
   lifetime keeps running, and registrations made on the main thread in the meantime still land in
   the current generation.
3. **At the next update of the main thread the queue is drained.** The `Cancel` now runs exactly as
   if it had been called there: tokens first, then items, deepest and newest first. Everything from
   step 2 is stopped with the rest.

The details that follow from this:

- **Order is kept.** Several deferred calls run in the order they were made, in one drain. A
  `Cancel()` followed by a `Dispose()` from the same worker thread runs as a cancel, then a dispose.
- **A stale request is harmless.** If the lifetime was disposed on the main thread before the drain,
  the queued `Cancel` finds nothing to do.
- **Failures go to the error handler.** An exception from an item that is ended during the drain is
  routed to `LifetimeErrors.Handler`, like in any other cancel.
- **"The next update"** is the next `Update` step of the player loop that the main thread reaches,
  through UniTask's player loop runner. That is within one frame.

What it means for your code: after an off-thread `Cancel()` or `Dispose()`, do not write code on
that thread that assumes the work has ended. If the stop has to be in effect before the next line,
switch to the main thread first (with UniTask, `await UniTask.SwitchToMainThread()`) and call it
there.

---

## Values you may read from another thread

| Member | Off the main thread |
|---|---|
| `lifetime.IsDisposed`, `registration.IsActive` | Readable; the value may be stale |
| `lifetime.Name` | Readable; it never changes |
| A `CancellationToken` you were handed | A normal token. It can be passed to a background computation and checked there |
| `lifetime.Token` (the property) | Throws. Read it on the main thread; `Run` does that for you |
| `LifetimeDiagnostics.Report(...)` | Safe from any thread (editor only) |

The token deserves a sentence. Reading the **property** is main thread only, because it may create
the token source for the current generation. The **token** it returns is an ordinary
`CancellationToken` and can travel anywhere. When the lifetime is cancelled, the cancellation
happens on the main thread, so callbacks registered on the token run on the main thread, during the
cancel.

---

## Crossing threads in the integrations

- **DOTween.** If the token given to `tween.AwaitCompletionAsync(token)` is cancelled from another
  thread, the kill of the tween is moved to the next main-thread update. DOTween is never called
  from the other thread.
- **Diagnostics.** A diagnostic raised on a worker thread (JANITOR111 is the usual one) is recorded
  safely; the frame number shown for it is the last frame the main thread saw.

---

## The guard in the editor and in players

The checks on this page are not editor-only helpers. They are part of the runtime:

| Behaviour | Editor | Development build | Release build |
|---|---|---|---|
| Registration, `Token`, `CreateChild`, the lookups: throw off the main thread | Yes | Yes | Yes |
| `Cancel`, `Dispose`, `registration.Cancel()`: deferred off the main thread | Yes | Yes | Yes |
| `OwnedEvent.Invoke`: throws off the main thread | Yes | Yes | No check |
| JANITOR111 console warning for a deferred call | Yes | Yes | No |
| JANITOR111 row in the Janitor window | Yes | No window | No window |

Each check reads the id of the current thread and compares it with the id of the main thread, which
is captured when the play session starts. One call can pass the check more than once: `Run`, `After`
and `Every` check three times, at the start of the call, when they read the token and when they
register their entry.

## See also

- [Concepts](Concepts.md)
- [Tasks and errors](Tasks-and-Errors.md)
- [Troubleshooting: JANITOR111](Troubleshooting.md#janitor111)
- [Performance](Performance.md)
- [Limitations](Limitations.md)
