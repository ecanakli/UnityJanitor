# Pooling

[Back to index](index.md)

A pooled object is not destroyed between uses, so nothing that depends on destruction cleans up
after it. This page shows how the active lifetime gives a `SetActive` pool the same automatic
cleanup, why a recycled object must not carry handles from its previous use, how generations make
such handles harmless, and the one case that still needs care.

---

## A pooled coin

The Basic Usage sample has a pool that deactivates an object on release and hands the same object
out again later:

<!-- source: Samples~/BasicUsage/Support/CoinPool.cs -->
```csharp
/// <summary>Stand-in for the game's SetActive pool: it deactivates a coin on release and reuses it on rent.</summary>
public sealed class CoinPool
{
    private readonly Stack<CoinPickup> _free = new();
    private readonly HashSet<CoinPickup> _held = new();
    private readonly Func<CoinPickup> _create;

    public CoinPool(Func<CoinPickup> create) => _create = create;

    // A new coin comes back inactive; Launch activates it.
    public CoinPickup Rent()
    {
        while (_free.Count > 0)
        {
            var coin = _free.Pop();
            _held.Remove(coin);
            if (coin != null)
            {
                return coin;           // a coin that was destroyed while pooled is skipped
            }
        }

        return _create();
    }

    // Safe to call twice for one coin: the pool ignores a coin it already holds.
    public void Release(CoinPickup coin)
    {
        if (!_held.Add(coin))
        {
            return;
        }

        if (coin.gameObject.activeSelf)    // not activeInHierarchy: a coin hidden by its parent must be switched off too
        {
            coin.gameObject.SetActive(false);
        }

        _free.Push(coin);
    }
}
```

`Release` may be called twice for one coin, `Rent` skips a coin that was destroyed while it was
pooled, and the test in front of `SetActive(false)` is `activeSelf`, not `activeInHierarchy`. All
three matter further down.

A pooled coin that flies to a target and then returns itself, with hand-written cleanup:

<!-- illustrative: before -->
```csharp
public sealed class CoinPickup : MonoBehaviour
{
    [SerializeField] private float _flyDuration = 0.5f;
    private Transform _target;
    private CoinPool _pool;
    private CancellationTokenSource _cts;

    public void Launch(Transform target, CoinPool pool)
    {
        _target = target;
        _pool = pool;
        gameObject.SetActive(true);
    }

    private void OnEnable()
    {
        _cts = new CancellationTokenSource();
        FlyAsync(_cts.Token).Forget();
        ReturnLaterAsync(_cts.Token).Forget();
    }

    private void OnDisable()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async UniTask FlyAsync(CancellationToken ct)
    {
        var start = transform.position;
        for (var elapsed = 0f; elapsed < _flyDuration; elapsed += Time.deltaTime)
        {
            var t = elapsed / _flyDuration;
            transform.position = Vector3.Lerp(start, _target.position, t * t);
            await UniTask.Yield(ct);
        }
    }

    private async UniTask ReturnLaterAsync(CancellationToken ct)
    {
        await UniTask.Delay(TimeSpan.FromSeconds(_flyDuration), cancellationToken: ct);
        _pool.Release(this);
    }
}
```

The line everything depends on is `_cts?.Cancel();` in `OnDisable`. Suppose it is missing, and the
pool takes a flying coin back early and launches it again a moment later. The `ReturnLaterAsync` of
the **first** use is still waiting. When its delay ends it calls `_pool.Release(this)` on a coin
that is in the middle of its **second** use: the coin disappears mid-flight and goes onto the free
stack. Then the second use's own `ReturnLaterAsync` releases it again. This pool ignores that second
release. A pool without that guard would hold the same coin twice and later hand one object to two
callers.

Nothing here leaked in the usual sense. Work from one use acted on the next use, because the object
is the same object. That is the bug class of pooled objects, and `OnDestroy`-based cleanup cannot
help, because nothing is destroyed.

The first version has a second gap: the only code that returns the coin is the delay. Anything
else that ends the use (a "clear the screen" call, a scene that is stopped) has no way to send the
coin back.

The coin from the sample:

<!-- source: Samples~/BasicUsage/CoinPickup.cs -->
```csharp
/// <summary>A pooled coin: however its flight ends, the coin goes back to the pool.</summary>
public sealed class CoinPickup : MonoBehaviour
{
    [SerializeField] private float _flyDuration = 0.5f;
    private Transform _target;
    private CoinPool _pool;

    public void Launch(Transform target, CoinPool pool)
    {
        _target = target;
        _pool = pool;
        gameObject.SetActive(true);
    }

    private void OnEnable()
    {
        var spawn = this.GetActiveLifetime();                                       // ends on SetActive(false) and on any outside Cancel
        spawn.OnCancel(this, static coin => coin._pool.Release(coin));              // the one place that returns the coin
        spawn.Run(FlyAsync);
        spawn.After(_flyDuration, gameObject, static go => go.SetActive(false));    // the timer only ends the flight
    }

    private async UniTask FlyAsync(CancellationToken ct)
    {
        var start = transform.position;
        for (var elapsed = 0f; elapsed < _flyDuration; elapsed += Time.deltaTime)
        {
            var t = elapsed / _flyDuration;
            transform.position = Vector3.Lerp(start, _target.position, t * t);
            await UniTask.Yield(ct);
        }
    }
}
```

The demo rents and launches coins like this. A new coin is created on an inactive GameObject, so its
first `OnEnable` runs inside `Launch`, after the target and the pool were set:

<!-- source: Samples~/BasicUsage/Demo/BasicUsageDemo.cs -->
```csharp
private async UniTask LaunchCoinsAsync(CancellationToken ct)
{
    for (var i = 0; i < 5; i++)
    {
        var coin = _pool.Rent();
        coin.transform.position = _launchOrigin.position;
        coin.Launch(_coinTarget, _pool);
        await UniTask.Delay(TimeSpan.FromMilliseconds(120), cancellationToken: ct);
    }
}

private CoinPickup CreateCoin()
{
    var image = DemoUi.CreateImage(_canvas, "Coin", new Color(1f, 0.85f, 0.2f, 1f), DemoUi.Center, Vector2.zero, new Vector2(36f, 36f));
    image.gameObject.SetActive(false);
    return image.gameObject.AddComponent<CoinPickup>();
}
```

What happens at each moment:

1. **`Launch` is called.** The fields are set, then `SetActive(true)` makes Unity call `OnEnable`.
2. **`OnEnable` runs.** `this.GetActiveLifetime()` returns the coin's active lifetime. On the coin's
   first use this creates it and adds the hidden trigger component; on every later use it returns
   the same lifetime, which is in a new generation. Three items are registered. The `OnCancel`
   action is the only code that returns the coin to the pool; it is registered first, so it is
   terminated last, after the flight and the timer. `Run` starts the flight with the generation's
   token. `After` schedules the end of the flight.
3. **The coin flies.** `FlyAsync` moves it once per frame.
4. **The timer fires.** It deactivates the GameObject and does nothing else.
5. **The coin is deactivated.** The trigger's `OnDisable` cancels the active lifetime, inside the
   `SetActive(false)` call. The token is cancelled, so a flight loop that is still running ends at
   its next `UniTask.Yield(ct)`. Then the `OnCancel` action runs and calls `_pool.Release(coin)`.
   The pool puts the coin on the free stack. The generation is over.
6. **The coin is rented again.** `Launch` activates it, and `OnEnable` registers the three items in
   the next generation. Nothing from the previous use exists any more.

Every other way a use can end goes through the same action:

- **The pool's user releases the coin early**, with `_pool.Release(coin)`. The pool deactivates the
  coin, which cancels the generation: the flight stops and the timer is cancelled, so it can never
  end a later use. The action then calls `Release` a second time, from inside the first call, and
  the pool ignores it. If the coin is launched again at once, the new use starts with an empty
  generation.
- **The lifetime is cancelled from outside**: `Lifetime.App.Cancel()`, a cancel of the scene
  lifetime, or of a category the coin was placed in. The flight and the timer stop, and the action
  releases the coin, so the pool deactivates it. With a timer that called the pool directly, this
  cancel would stop the timer and leave an active coin on screen that nothing returns.
- **A parent of the coin is deactivated.** The coin becomes inactive in the hierarchy, its trigger
  cancels the active lifetime, and the action releases the coin. The coin itself was never switched
  off: its `activeSelf` is still `true`, so the pool calls `SetActive(false)` on it. Unity accepts
  that call from inside the parent's deactivation without a log, and the coin stays off when the
  parent is shown again. A pool that tests `activeInHierarchy` at this point skips the call and
  puts a coin on the free stack that is still switched on; when the parent is shown again, that
  coin runs `OnEnable` and starts a flight while it sits in the pool.
- **The coin is destroyed while it flies.** The destruction ends the active lifetime, and the
  action runs as it does for a deactivation. The pool takes the coin, and `Rent` skips it later
  because it is destroyed. The `SetActive(false)`
  the pool calls on an object that is being destroyed is accepted by Unity without an error.

The class has no `OnDisable`, no `CancellationTokenSource` and no field that has to be reset between
uses. In the Janitor window an idle coin stays in the tree as a row with no entries.

With the DOTween integration the flight is a tween on the same lifetime, and nothing else changes:

<!-- source: Samples~/DOTweenUsage/CoinPickup.cs -->
```csharp
private void OnEnable()
{
    var spawn = this.GetActiveLifetime();                                       // ends on SetActive(false) and on any outside Cancel
    spawn.OnCancel(this, static coin => coin._pool.Release(coin));              // the one place that returns the coin
    transform.DOMove(_target.position, _flyDuration).SetEase(Ease.InQuad).AddTo(spawn);
    spawn.After(_flyDuration, gameObject, static go => go.SetActive(false));    // the timer only ends the flight
}
```

See [DOTween](DOTween.md) for what `AddTo` does to a tween, including why a registered tween is
taken out of DOTween's own recycling.

---

## A task that ends its own use

The coin ends its flight with a timer. A use can also be ended by its own task, when the task has
something to do at the end. `GemPickup` in the sample writes the end position and then deactivates
its own GameObject:

<!-- source: Samples~/BasicUsage/GemPickup.cs -->
```csharp
/// <summary>A pooled gem whose flight task ends its own use; one cleanup returns it to the pool.</summary>
public sealed class GemPickup : MonoBehaviour
{
    [SerializeField] private float _flyDuration = 0.6f;
    private Vector3 _end;
    private GemPool _pool;

    // The per-use data arrives here, after the object exists.
    public void Launch(Vector3 end, GemPool pool)
    {
        _end = end;
        _pool = pool;
        gameObject.SetActive(true);
    }

    private void OnEnable()
    {
        // An object instantiated from an active prefab runs OnEnable before the pool can hand it anything.
        if (_pool == null)
        {
            return;
        }

        var use = this.GetActiveLifetime();                                // ends on SetActive(false) and on any outside Cancel
        use.OnCancel(this, static gem => gem._pool.Release(gem));          // returns the gem once, however the flight ends
        use.Run(FlyAsync);
    }

    private async UniTask FlyAsync(CancellationToken ct)
    {
        var start = transform.position;
        for (var elapsed = 0f; elapsed < _flyDuration; elapsed += Time.deltaTime)
        {
            transform.position = Vector3.Lerp(start, _end, elapsed / _flyDuration);
            await UniTask.Yield(ct);
        }

        transform.position = _end;
        gameObject.SetActive(false);       // the flight ends its own use; the cleanup above runs inside this call
    }
}
```

Its pool is `CoinPool` with one more line: `Rent` switches a newly created gem off before it hands
it out.

<!-- source: Samples~/BasicUsage/Support/GemPool.cs -->
```csharp
public GemPickup Rent()
{
    while (_free.Count > 0)
    {
        var gem = _free.Pop();
        _held.Remove(gem);
        if (gem != null)
        {
            return gem;            // a gem that was destroyed while pooled is skipped
        }
    }

    var created = _create();
    created.gameObject.SetActive(false);   // a prefab saved active arrives active; Launch switches it on
    return created;
}
```

What happens at each moment:

1. **The pool creates a gem.** When the prefab is saved active, `OnEnable` runs inside
   `Instantiate`, before the pool has the object in its hands. `_pool` is still `null`, so
   `OnEnable` returns without registering anything. `Rent` then switches the gem off.
2. **`Launch` is called.** The fields are set, and `SetActive(true)` runs `OnEnable` again, this
   time with `_pool` set. The `OnCancel` action and the flight are registered.
3. **The flight reaches its end.** The loop is left, the end position is written, and the task
   calls `gameObject.SetActive(false)` on the object whose active lifetime owns the task.
4. **Inside that call** the trigger cancels the active lifetime. The `OnCancel` action runs, once,
   and releases the gem to the pool.
5. **The call returns into the task.** The task's token is cancelled now, but the task is not
   interrupted: a statement after `SetActive(false)` would still run. Here the method ends.
   Nothing is routed to the error handler and nothing is logged.

A task may end its own generation this way, as it may call `Cancel()` on its own lifetime (see
[Concepts](Concepts.md#calling-cancel-from-ordinary-code-that-the-lifetime-owns)). Put the call
last: code after it runs against an object that is already back in the pool.

---

## Why a recycled object must not keep handles

A handle is anything that refers to one piece of running work: a `CancellationTokenSource` or a
token, a `Coroutine`, a tween reference, a `LifetimeRegistration`, a subscription.

On an object that is destroyed after use, a forgotten handle is harmless clutter: the object is gone
and the handle with it. On a pooled object the handle outlives the use it was made for, and the
object it points at is now doing something else. Using it does the wrong thing to the right object:

- a stale `CancellationTokenSource` field that is cancelled late stops the current use's work;
- a stale continuation that was not cancelled runs its remaining lines against the current use, as
  in the double release above;
- a stale subscription that was not removed makes the object react twice, or react while it sits in
  the pool.

Hand-written code prevents this by resetting every handle in `OnDisable`, one line per handle. The
active lifetime prevents it by not having the handles: `CoinPickup` stores nothing. What it started
is tied to the generation, and the generation ends at deactivation.

---

## Generations guard against stale handles

Each activation of a pooled object is one generation of its active lifetime. Everything created in
a generation is stamped with it and goes dead when it ends (see
[Concepts](Concepts.md#generations)). This excerpt from the package's tests walks an object through
two uses. `probe` is a component on an active GameObject, and `Record` registers a cleanup action
that writes its label to a log when it is terminated:

<!-- source: Tests/Runtime/Binding/ActiveLifetimeTests.cs -->
```csharp
var probe = _s.NewProbe();
var lifetime = probe.GetActiveLifetime();
var firstToken = lifetime.Token;
lifetime.Record(_log, "gen0");
Assert.That(lifetime.Generation, Is.Zero);

probe.gameObject.SetActive(false);

Assert.That(_log.ToArray(), Is.EqualTo(new[] { "gen0" }));
Assert.That(firstToken.IsCancellationRequested, Is.True);
Assert.That(lifetime.Generation, Is.EqualTo(1));
Assert.That(lifetime.IsDisposed, Is.False, "deactivation cancels, it does not dispose");

probe.gameObject.SetActive(true);

var secondToken = lifetime.Token;
Assert.That(secondToken.IsCancellationRequested, Is.False);
Assert.That(secondToken, Is.Not.EqualTo(firstToken), "a generation has its own token");
Assert.That(lifetime.Record(_log, "gen1").IsActive, Is.True);

probe.gameObject.SetActive(false);

Assert.That(_log.ToArray(), Is.EqualTo(new[] { "gen0", "gen1" }));
Assert.That(lifetime.Generation, Is.EqualTo(2));
Assert.That(secondToken.IsCancellationRequested, Is.True);
```

Applied to a pooled object, use by use:

- **Tokens.** The token of the first use is cancelled at deactivation and stays cancelled. The
  second use gets a different token. An async method of the first use that is still suspended at a
  token-aware `await` ends there; it can never continue into the second use.
- **Timers.** An `After` or `Every` of the first use never fires in the second, even if its delay
  elapses after the reactivation.
- **Registrations.** A `LifetimeRegistration` that was kept from the first use reports
  `IsActive == false`, and its `Cancel()` does nothing. It cannot stop an item of the second use.
- **Subscriptions.** A subscription made in the first use is removed at deactivation. The `OnEnable`
  of the second use subscribes once; there is no duplicate.
- **Tweens.** A tween registered with `AddTo` is killed at deactivation, and because it was taken
  out of DOTween's recycling, a reference to it can never come to mean another object's tween.
- **Coroutines.** Unity stops the coroutines of a deactivated object itself. One that was bound to
  the active lifetime is removed from it at the same moment.

The lifetime does not have to be asked for again to get this. A `LifetimeRegistration` in a field is
safe to keep across uses, because a stale one is inert.

---

## The one hole that remains

The stamp is on tokens and handles. It is not on the lifetime object itself, which always means
"the current generation"; that is what makes the lifetime reusable. So one path into the next use is
left open: **code that ignored its token and is therefore still running, and that then registers
through the lifetime object.**

Take the coin's flight loop and remove the token from its `await`, so that it reads
`await UniTask.Yield();`. When the coin is released, the cancel has nothing to stop the loop with.
The loop keeps moving the coin while it sits in the pool, and if the coin is launched again before
the loop has ended, two loops move it. And if that surviving code calls `spawn.After(...)` or
`this.GetActiveLifetime()` after the reactivation, the registration is accepted: the lifetime is
active and in a new generation, and it cannot tell that the caller belongs to the previous one.

What closes the hole in practice:

- **Give every `await` the token.** `Run` hands your method `ct` for this purpose. How to do that
  for each kind of await is listed in
  [Tasks and errors](Tasks-and-Errors.md#what-a-token-less-await-does).
- **Do not store `lifetime.Token` in a field.** It is the token of the generation it was read in.
- **Watch for [JANITOR101](Troubleshooting.md#janitor101).** In the editor, the Janitor window
  reports a task that is still running a few frames after its generation ended. On a pooled object
  that warning means a token-less await.

A registration made by such code **while the object is still inactive** is refused: work registered
on an active lifetime whose GameObject is inactive is terminated at once, with
[JANITOR108](Troubleshooting.md#janitor108). An area created under the active lifetime follows the
same rule, and `Token` of both is an already cancelled token for as long as the object is inactive.
The hole only opens once the object is active again.

---

## Rules for pooled objects

- **Start per-use work in `OnEnable`, on the active lifetime.** Not in `Awake`, which runs once, and
  not from a method that the pool calls while the object is still inactive.
- **Set per-use data before activating.** `Launch` assigns `_target` and `_pool` and then calls
  `SetActive(true)`, so `OnEnable` sees them.
- **A prefab that is saved active runs `OnEnable` inside `Instantiate`**, before the pool can hand
  it any per-use data. Either let the pool switch the new object off and return early from an
  `OnEnable` that has no data yet, as `GemPool` and `GemPickup` do, or create the object on a
  GameObject that is already inactive, as the demo's `CreateCoin` does.
- **Call the launch method only on an object that came from `Rent`.** `SetActive(true)` on an
  object that is already active does not run `OnEnable`. The fields are overwritten, no new use
  starts, and the use that is running continues with the new values.
- **Test `activeSelf` in the pool's release**, not `activeInHierarchy`, so that an object hidden
  through its parent is switched off as well.
- **Return the object from an `OnCancel` action, registered first.** It runs for every end of a use:
  the timer, an early release, a `Cancel()` from anywhere above, a destroy. A timer or a task that
  calls the pool directly covers the normal end only. The pool's release method is then called a
  second time from inside itself when the pool's user releases an object, so it has to ignore an
  object it already holds, as `CoinPool` does.
- **Keep per-use work off the component lifetime.** `this.Run(...)`, `this.After(...)` and
  `Subscribe(handler, this)` use the component lifetime, which deactivation does not cancel. Work
  started that way keeps running while the object is in the pool, and a task, a timer or a tween
  is started again on the next use. In the editor the Janitor window reports the second
  activation as [JANITOR116](Troubleshooting.md#janitor116). A subscription that the package
  recognises as a repeat is not doubled: it is ignored with
  [JANITOR105](Troubleshooting.md#janitor105), but it also stays live while the object is pooled.
  Use the component lifetime only for what should last as long as the object exists.
- **Write no `OnDisable` for cleanup.** There is nothing left for it to do.
- **One active lifetime per GameObject.** Every component on the object gets the same one, and one
  deactivation ends the work of all of them. A child GameObject that asks for an active lifetime
  gets its own; it is cancelled as well when the pooled root is deactivated, because the child
  becomes inactive in the hierarchy. `ModalBlocker` in the Basic Usage demo is such a child, under
  the shop popup.
- **Coroutines.** A coroutine hosted on a pooled object is stopped by Unity at deactivation whatever
  it was bound to. If it was bound to a lifetime that lives longer than one use, its entry is
  reclaimed later without growing that lifetime; see
  [Coroutines](Coroutines.md#how-stopped-coroutines-are-reclaimed).

### Pools that do not use SetActive

The active lifetime follows GameObject activation and nothing else. A pool that keeps its objects
active (it moves them away, or disables a renderer) gives the package no signal. For such a pool,
keep an area per object, created once from the component lifetime, register the per-use work into
it, and call `Cancel()` on it from the method the pool invokes on release. That is one line the pool
has to call, and the generation rules above apply to the area in the same way.

A pool that disables every `Behaviour` on the object is a special case, because the loop reaches
the hidden trigger component as well. Unity reports that disable to the trigger exactly as it
reports a destruction, so the work on the active lifetime is cancelled once. The trigger enables
itself again at the next `GetActiveLifetime()` call or registration on the active lifetime, so an
`OnEnable` that registers its work there keeps working when the components are enabled again.

A pool that destroys and instantiates needs nothing: destruction disposes the object's lifetimes.

### Cost

- The first `GetActiveLifetime()` on a GameObject adds one hidden component and creates one
  lifetime. Later calls find the same lifetime and allocate nothing.
- The `Cancel()` call at deactivation allocates nothing itself. Each `Run` task or timer that is
  still pending at that moment costs one exception object on the following frame, with one throw
  and catch for a timer and two for a task, because UniTask ends a cancelled await by throwing. A
  pool that takes back hundreds of flying objects in one frame sees that as a spike in the next
  one. See [Tasks and errors](Tasks-and-Errors.md#what-happens-to-exceptions).
- Each use whose token is read creates one `CancellationTokenSource`. `Run`, `After` and `Every`
  read the token. This is the same cost as the hand-written version.
- `Run(FlyAsync)` converts a method group to a delegate, which allocates once per use. The
  `OnCancel` and `After` lines of the sample pass their state and a `static` lambda, which creates
  no delegate per use. `Run` has the same form for pools that spawn many objects per second; see
  [Tasks and errors](Tasks-and-Errors.md#the-state-overload).
- Each registration on an active lifetime checks once whether the GameObject is active.

---

## See also

- [Components and scenes](Components-and-Scenes.md#what-setactivefalse-stops): what deactivation
  stops and what it leaves running
- [Concepts](Concepts.md#generations): generations in general
- [Tasks and errors](Tasks-and-Errors.md): giving every await the token
- [Coroutines](Coroutines.md): coroutines on hosts that are switched off and on
- [DOTween](DOTween.md): tweens on pooled objects
- [Troubleshooting](Troubleshooting.md): JANITOR101, JANITOR108 and JANITOR116
