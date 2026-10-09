# Components and scenes

[Back to index](index.md)

This page covers the lifetimes Unity objects get from the package: the component lifetime
(`this.GetLifetime()`), the GameObject lifetime, the active lifetime (`this.GetActiveLifetime()`)
and the scene lifetime. It says what destroying, deactivating and unloading stop, what happens to
objects that were never activated or that live in DontDestroyOnLoad, and why one call belongs before
every scene load.

---

## Three lifetimes of an object

<!-- signature -->
```csharp
public static Lifetime GetLifetime(this MonoBehaviour behaviour);
public static Lifetime GetLifetime(this MonoBehaviour behaviour, Lifetime parent);
public static Lifetime GetLifetime(this GameObject gameObject);
public static Lifetime GetActiveLifetime(this Component component);
public static Lifetime GetActiveLifetime(this Component component, Lifetime parent);
```

| | Component lifetime | GameObject lifetime | Active lifetime |
|---|---|---|---|
| You get it with | `this.GetLifetime()` | `gameObject.GetLifetime()` | `this.GetActiveLifetime()` |
| It belongs to | One `MonoBehaviour` | The GameObject | The GameObject; every component on it gets the same one |
| It is cancelled | Only by a `Cancel()` call: on the lifetime itself, or on one above it (its category, its scene, `App`) | The same | By the package, every time the GameObject becomes inactive, and by the same `Cancel()` calls |
| It is disposed when | That component is destroyed | The GameObject is destroyed | The GameObject is destroyed |
| The first call adds | Nothing, when the object is active | UniTask's destroy trigger component | An `ActiveLifetimeTrigger` component, hidden in the Inspector |
| It is used implicitly by | `this.Run`, `this.After`, `this.Every`, `this.OnCancel`, `this.Subscribe(...)`, and `AddTo(this)` or `Subscribe(handler, this)` when `this` is a `MonoBehaviour` | `AddTo(gameObject)`, and an owner argument that is a component but not a `MonoBehaviour`, such as a `Transform` | Nothing. It is always asked for by name |

What the three have in common:

- Each is a child of the lifetime of the object's scene, or of `Lifetime.App` for a
  DontDestroyOnLoad object. The forms with a `parent` place the lifetime in a category instead; see
  [Organizing work](Organizing-Work.md#placing-an-object-in-a-category).
- Each is also disposed when its scene lifetime is disposed and when the session ends (the
  application quits, or the editor leaves Play Mode).
- Each is package-owned: `Cancel()` works on it, `Dispose()` is ignored
  ([JANITOR107](Troubleshooting.md#janitor107)).
- The first call creates the lifetime. Every later call returns the same one and allocates nothing.
- They exist in Play Mode only and on the main thread only. Outside Play Mode, and off the main
  thread, the calls throw `InvalidOperationException`.
- For a **destroyed** owner the calls return a lifetime that is already disposed, so a registration
  on a dead object is terminated on the spot instead of starting work. A C# `null` owner throws
  `ArgumentNullException`. An object that is not in a scene, such as a prefab asset, also gets the
  disposed lifetime, and nothing is added to the asset.
- The calls never throw when they are made from `OnDisable` or `OnDestroy` of an object that is
  being destroyed; see [When the object is destroyed](#when-the-object-is-destroyed).

---

## One class at every moment

The shop popup of the Basic Usage sample uses the active lifetime for everything it starts:

<!-- source: Samples~/BasicUsage/ShopPopup.cs -->
```csharp
/// <summary>A popup whose work stops on SetActive(false), on a Popups cancel and on destroy.</summary>
public sealed class ShopPopup : MonoBehaviour
{
    private Button _buyButton;
    private Graphic _saleBadge;
    private TMP_Text _coinsLabel;
    private GameplayLifetimes _lifetimes;
    private Wallet _wallet;
    private OfferService _offers;

    // Arrives here instead of [Inject] and the Inspector; call it before the object is enabled.
    public void Initialize(
        GameplayLifetimes lifetimes,
        Wallet wallet,
        OfferService offers,
        Button buyButton,
        Graphic saleBadge,
        TMP_Text coinsLabel)
    {
        _lifetimes = lifetimes;
        _wallet = wallet;
        _offers = offers;
        _buyButton = buyButton;
        _saleBadge = saleBadge;
        _coinsLabel = coinsLabel;
    }

    private void OnEnable()
    {
        // Cancelled by SetActive(false) or _lifetimes.Popups.Cancel(); disposed on destroy.
        var shown = this.GetActiveLifetime(_lifetimes.Popups);

        _coinsLabel.text = _wallet.Coins.ToString();
        shown.StartCoroutine(this, BlinkBadge());
        shown.Run(LoadOffersAsync);
        _buyButton.onClick.Subscribe(OnBuyClicked, shown);
        _wallet.CoinsChanged.Subscribe(OnCoinsChanged, shown);
    }

    private async UniTask LoadOffersAsync(CancellationToken ct)
    {
        var offers = await _offers.FetchAsync(ct);
        _offers.Show(offers);
    }

    private IEnumerator BlinkBadge()
    {
        while (true)
        {
            _saleBadge.enabled = !_saleBadge.enabled;
            yield return new WaitForSeconds(0.5f);
        }
    }

    private void OnBuyClicked() => _offers.BuySelected();
    private void OnCoinsChanged(int coins) => _coinsLabel.text = coins.ToString();
}
```

- **The popup is activated.** `OnEnable` runs. `this.GetActiveLifetime(_lifetimes.Popups)` returns
  the popup's active lifetime; on the very first call it is created, as a child of the `Popups`
  category, and the hidden trigger component is added. The coroutine, the task and the two
  subscriptions are registered in its current generation.
- **The popup is deactivated.** The trigger's `OnDisable` cancels the lifetime, inside the
  `SetActive(false)` call: the token given to `LoadOffersAsync` is cancelled, the coroutine is
  stopped, both subscriptions are removed. The lifetime stays, with a new, empty generation.
- **The popup is activated again.** `OnEnable` runs again and registers everything into the new
  generation. Nothing is left from the previous activation, so nothing is registered twice.
- **`Popups.Cancel()` is called while the popup is active.** The same work stops, but Unity sends no
  `OnEnable`, so the popup stays on screen without its work until it is closed and opened.
- **The popup is destroyed.** Unity sends `OnDisable` first, which cancels the lifetime as above,
  and then the lifetime is disposed.
- **The scene is disposed** with `SceneLifetimes.DisposeAll()`. The lifetime is disposed with the
  scene, before Unity destroys the popup.

The class has no `OnDisable`, no `OnDestroy` and no fields for handles.

---

## What `SetActive(false)` stops

**It stops** everything registered on the GameObject's active lifetime. The hidden trigger receives
`OnDisable` and calls `Cancel()`, synchronously, so the work has stopped when `SetActive(false)`
returns. The same happens when a parent is deactivated, because the trigger follows the object's
activation in the hierarchy. In the demo of the sample, `ModalBlocker` sits on a child object of
the shop popup and has an active lifetime of its own; closing the popup cancels it too.

**It does not stop** work on the component lifetime or the GameObject lifetime, or on areas created
under them. `CoinsLabel` in the sample listens to the same wallet event as the popup, but with the
component as the owner:

<!-- source: Samples~/BasicUsage/CoinsLabel.cs -->
```csharp
/// <summary>A label that follows the wallet for as long as the component exists.</summary>
public sealed class CoinsLabel : MonoBehaviour
{
    private Wallet _wallet;
    private TMP_Text _label;

    // Call it before the object is enabled, so Awake sees the wallet.
    public void Initialize(Wallet wallet, TMP_Text label)
    {
        _wallet = wallet;
        _label = label;
    }

    private void Awake()
    {
        OnCoinsChanged(_wallet.Coins);
        _wallet.CoinsChanged.Subscribe(OnCoinsChanged, this);   // owned by the component: it ends with it, not with SetActive
    }

    private void OnCoinsChanged(int coins) => _label.text = $"Wallet: {coins}";
}
```

When the label's GameObject is deactivated, its subscription stays. `OnCoinsChanged` keeps running
for every coin change, and the label holds the current amount when it is activated again. The
subscription ends when the component is destroyed or its scene is disposed. The popup's subscription
to the same event is on its active lifetime and ends with `SetActive(false)`.

More that follows from this:

- **The active lifetime follows the GameObject, not the `enabled` flag.** Setting `enabled = false`
  on one of your components stops nothing. A component that needs "stop when I am disabled" for
  itself keeps an area and cancels it in its own `OnDisable`.
- **Re-enabling a component runs `OnEnable` again in the same generation.** `enabled = false`
  followed by `enabled = true`, while the GameObject stays active, makes Unity call `OnEnable` a
  second time, and the active lifetime was not cancelled in between. Whatever `OnEnable` registers
  there is then registered twice in one generation: two tasks, two timers, two coroutines. A
  subscription that the package recognises as a repeat is ignored with
  [JANITOR105](Troubleshooting.md#janitor105). Nothing else is reported: JANITOR116 watches
  component and GameObject lifetimes only. A component that is switched off and on through
  `enabled` has two guards, for two different needs:
  - **Cancel at the top of `OnEnable`.** Keep that work in an area of its own under the active
    lifetime, and cancel the area before registering, the same two lines as `Refresh()` in
    `InboxPanel` (see [Organizing work](Organizing-Work.md#an-area-under-the-active-lifetime)).
    The work is never doubled, and it keeps running while only the component is disabled.
  - **Cancel in `OnDisable` as well.** If the work must also stop while the component is disabled,
    cancel that area in the component's `OnDisable`. That is one line the class has to carry,
    because no lifetime follows `enabled`.
- **The one exception is the hidden trigger itself.** A loop that disables every `Behaviour` on an
  object (`GetComponentsInChildren<Behaviour>()`) reaches the trigger too. Unity reports that
  disable to the trigger exactly as it reports a destruction, so the trigger cancels the active
  lifetime, once. It enables itself again the next time the package touches it: a
  `GetActiveLifetime()` call, a registration on the active lifetime, or the start of a
  lifetime-bound coroutine on that object. Components that are re-enabled register their work again
  in their own `OnEnable`.
- **Nothing restarts by itself.** A cancel ends work; it does not remember it. Work comes back on
  reactivation because `OnEnable` runs again and registers it again.
- **Registering on the active lifetime while the GameObject is inactive is refused.** The item is
  terminated at once and [JANITOR108](Troubleshooting.md#janitor108) is logged in the editor and in
  development builds. The same holds for an area created under the active lifetime. `Token` of
  either is an already cancelled token while the object is inactive; reading it logs nothing.
  Asking for the lifetime (`GetActiveLifetime()`) while inactive is fine.
- **Unity stops coroutines of a deactivated object on its own**, whichever lifetime they were bound
  to. See [Coroutines](Coroutines.md).

### Which lifetime for which work

| The work is started in | It should stop when | Register it on |
|---|---|---|
| `OnEnable` | The object is hidden or returned to a pool | The active lifetime |
| `OnEnable`, and again while the object is shown (a refresh, a retry) | The object is hidden, or the next run starts | An area created under the active lifetime (see [Organizing work](Organizing-Work.md#an-area-under-the-active-lifetime)) |
| `Awake`, `Start`, or a public method | The object is destroyed | The component lifetime (`this.Run`, `Subscribe(handler, this)`) |
| Anywhere | A feature is stopped as a unit | An area or a category (see [Organizing work](Organizing-Work.md)) |

The two mismatches to avoid:

- **`OnEnable` with the component as owner.** The component lifetime is not cancelled by
  deactivation, so the registration survives and the next `OnEnable` adds it again. A subscription
  that the package recognises as a repeat (`OwnedEvent`, `UnityEvent`, and most paired
  subscriptions) is ignored and reported as [JANITOR105](Troubleshooting.md#janitor105). Tasks,
  timers, tweens and `OnCancel` actions are doubled on every activation; in the editor the Janitor
  window reports the second activation as [JANITOR116](Troubleshooting.md#janitor116).
- **`Awake` with the active lifetime.** It works until the first deactivation. After that the work
  is cancelled, and `Awake` does not run again to bring it back.

---

## When the object is destroyed

Destroying an object disposes its lifetimes. For the lifetime of a component whose GameObject has
been active at least once, this happens at a fixed point between two Unity messages. The package's
tests record the order (`RecordToken` adds a callback to the lifetime's token; the probe component
writes its own Unity messages to the same log):

<!-- source: Tests/Runtime/Binding/ComponentLifetimeTests.cs -->
```csharp
var go = _s.NewObject();
var probe = go.AddComponent<BindingProbe>();
var lifetime = probe.GetLifetime();
lifetime.Record(_log, "item");
lifetime.RecordToken(_log, "token");
BindingProbe.Log = _log;

Object.Destroy(go);
yield return null;

Assert.That(_log.ToArray(), Is.EqualTo(new[] { "OnDisable", "token", "item", "OnDestroy" }));
```

So the sequence for `Destroy(gameObject)` is:

1. `OnDisable`. The trigger cancels the active lifetime here, if the object has one.
2. The component lifetime is disposed: its token is cancelled, then its items are terminated, then
   it leaves the tree with every area under it.
3. `OnDestroy`. Nothing is left to clean, which is why classes that register through a lifetime need
   no `OnDestroy`.

`DestroyImmediate` gives the same order.

- `Destroy(component)` disposes that component's lifetime only. The GameObject lifetime and the
  active lifetime belong to the GameObject and stay.
- After the destruction, `GetLifetime()` on the destroyed component returns the disposed lifetime.
  Late callers cannot start work on it.
- **A first access during the destruction never throws.** Asking for a lifetime for the first time
  from `OnDisable` or `OnDestroy` of an object that is being destroyed, or of one of its children,
  returns without an exception. `gameObject.GetLifetime()` and `GetActiveLifetime()` have to add a
  component on first use, which Unity refuses for an object that is being destroyed: called from
  the object's own `OnDestroy`, Unity logs "Can't add component to object that is being destroyed."
  and the call returns a disposed lifetime. From `OnDisable`, or for a child, nothing is logged.
- **Do not start work in `OnDestroy`.** A component lifetime that is first asked for inside the
  component's own `OnDestroy` cannot be tied to a destruction that is already under way: Unity does
  not cancel a `destroyCancellationToken` that is first read inside `OnDestroy`. Such a
  lifetime stays alive until its scene lifetime is disposed, and the Janitor window
  lists it as [JANITOR103](Troubleshooting.md#janitor103).

---

## Objects that were never activated

Unity treats a component whose GameObject was never active as if it had not started: it receives
neither `Awake` nor `OnDestroy`, and its `destroyCancellationToken` is **not** cancelled when the
object is destroyed. A lifetime bound to that token would never end.

The package therefore chooses how to watch the object at the moment the lifetime is first asked for:

- **The GameObject is active in the hierarchy:** the lifetime is bound to the component's
  `destroyCancellationToken`. No component is added.
- **The GameObject is inactive:** the lifetime is bound to two signals, and whichever comes first
  disposes it. One is the component's `destroyCancellationToken`. The other is UniTask's destroy
  trigger on the GameObject (an `AsyncDestroyTrigger` component is added), which also notices the
  destruction of an object that was never activated; in that case the lifetime is disposed within a
  frame or two of the `Destroy` call.

So a lifetime that was first asked for while its GameObject was inactive ends with the component,
like any other, once the object has been activated. One case remains: a component whose GameObject
was never active does not cancel its destroy token, so `Destroy(component)` alone does not dispose
its lifetime. Destroying the GameObject does, and so does disposing the scene.

When this matters in practice: an object is instantiated inactive, and an initialization method
called before activation uses `this.Run`, `Subscribe(handler, this)` or `GetLifetime()`. That is
supported, and the work starts; it is the inactive path. Objects created through a dependency
injection container are usually injected while inactive, so an injected lifetime takes this path
too (see [Zenject](Zenject.md)). The classes in the samples are also created inactive, but they
first touch a lifetime in `Awake` or `OnEnable`, when the object is active.

The active lifetime accepts no work while its object is inactive, as described above. The component
lifetime does: it has no connection to activation.

---

## DontDestroyOnLoad objects

- The lifetimes of a DontDestroyOnLoad object are children of `Lifetime.App`, not of a scene
  lifetime. `SceneLifetimes.Dispose(scene)` and `DisposeAll()` never touch them.
- `SceneLifetimes.Get(scene)` for the DontDestroyOnLoad scene returns `Lifetime.App`.
- Their work ends when the object is destroyed or the session ends. `Lifetime.App.Cancel()`
  reaches them.
- An object may be moved to DontDestroyOnLoad **after** its lifetime was created under a scene
  lifetime. Right before that scene lifetime is cancelled or disposed, the package checks whether
  its objects are still in the scene, and moves the lifetime of one that left under `App`. No call
  is needed.
- Move the object **before** its scene is disposed. An object that is kept with
  `DontDestroyOnLoad` after `SceneLifetimes.DisposeAll()` has already disposed its lifetimes gets a
  new component lifetime and a new GameObject lifetime under `App` on the next access, but its
  active lifetime stays disposed for the rest of the object's life. Use the component lifetime for
  the work of such an object.

A plain C# object that must outlive scenes has no GameObject to follow. It is given an area under
`Lifetime.App`; see [Organizing work](Organizing-Work.md#an-area-under-app).

### Objects that move between scenes

The same check works in both directions and for ordinary scenes. Right before a scene lifetime is
cancelled or disposed:

- an object that **left** the scene after its lifetime was created (`MoveGameObjectToScene`, or a
  new parent in another scene) is handed to the lifetime of the scene it is in now;
- an object that **moved into** the scene is taken in. The usual case is an object that is
  instantiated in the active scene, touches its lifetime in `Awake`, and is then moved or parented
  into an additive scene: `SceneLifetimes.Dispose(additiveScene)` and
  `SceneLifetimes.Get(additiveScene).Cancel()` reach it.

A lifetime that was placed in a category keeps its category in both cases; only its scene
membership moves (see [Organizing work](Organizing-Work.md#scene-membership)).

---

## The scene call

<!-- signature -->
```csharp
public static class SceneLifetimes
{
    public static Lifetime Get(Scene scene);
    public static void Dispose(Scene scene);
    public static void DisposeAll();
}
```

A scene load written the usual way:

<!-- illustrative: before -->
```csharp
public sealed class SceneFlow
{
    public async UniTask LoadAsync(string sceneName, CancellationToken ct)
    {
        await SceneManager.LoadSceneAsync(sceneName).ToUniTask(cancellationToken: ct);
    }
}
```

The line that causes the trouble is the load itself, because nothing comes before it. Unity destroys
the objects of the old scene in an order the game does not control, while their tasks, tweens and
handlers are still live. A handler of object A runs after object B was destroyed and touches it.

The version from the Basic Usage sample adds one call, and one check in front of it:

<!-- source: Samples~/BasicUsage/SceneFlow.cs -->
```csharp
/// <summary>Ends the work of the loaded scenes right before it loads the next one.</summary>
public sealed class SceneFlow
{
    private readonly Lifetime _lifetime;   // a child of Lifetime.App: it must outlive the scenes it unloads

    public SceneFlow(Lifetime lifetime) => _lifetime = lifetime;

    public void Load(string scenePath) => _lifetime.Run(ct => LoadAsync(scenePath, ct));

    // Disposal is final, so a load that can fail at runtime should use LoadingScreenFlow, which disposes right before activation.
    private static async UniTask LoadAsync(string scenePath, CancellationToken ct)
    {
        if (SceneUtility.GetBuildIndexByScenePath(scenePath) < 0)
        {
            Debug.LogError($"Scene '{scenePath}' is not in the Build Settings; nothing was disposed.");
            return;
        }

        SceneLifetimes.DisposeAll();       // everything owned by the loaded scenes stops now, before any destroy
        await SceneManager.LoadSceneAsync(scenePath).ToUniTask(cancellationToken: ct);
    }
}
```

What happens when `Load(scenePath)` is called:

1. `_lifetime.Run(...)` starts `LoadAsync`, which runs synchronously up to its first `await`.
2. The path is looked up in the Build Settings. A scene that is not there cannot be loaded, so the
   method logs an error and returns. Nothing was disposed, and the current scene keeps working.
3. `SceneLifetimes.DisposeAll()` disposes the lifetime of every loaded scene, the last loaded scene
   first. For each scene, every token in the scene is cancelled, then every registered item is
   terminated, then the lifetimes leave the tree. All objects of the scene still exist at this
   point, so every cleanup action runs against a complete scene.
4. `LoadSceneAsync` starts. When Unity later destroys the old objects, their lifetimes are already
   disposed and their destroy callbacks find nothing to do.
5. The load completes and `LoadAsync` continues in the new scene. Objects of the new scene get
   lifetimes under a new scene lifetime.

The check in step 2 comes first because disposal cannot be taken back; see
[Disposal is terminal](#disposal-is-terminal).

`Lifetime.App`, the lifetimes of DontDestroyOnLoad objects and areas under `App` are not touched.

### Why the call comes before the load

Unity raises no event before it starts destroying a scene. On a Single-mode load the order is
`OnDisable`, `OnDestroy`, `sceneUnloaded`, `activeSceneChanged`, `sceneLoaded`; on
`UnloadSceneAsync` it is `OnDisable`, `OnDestroy`, `sceneUnloaded`.
The first notification the package could listen to arrives after every object is gone. Stopping the
scene's work while the scene is still whole therefore needs a call from the code that starts the
load, placed before the load. It is one call, and it works with any loader, because it only has to
run before the loader does.

- `DisposeAll()` is for a Single-mode load: it covers every loaded scene. A scene that is still
  loading is skipped.
- `Dispose(scene)` is for unloading one scene: call it right before `UnloadSceneAsync(scene)`.
  Additive scenes have independent lifetimes, and disposing one does not touch the others.
- Both calls are idempotent and never throw for your code's failures; those are routed to
  `LifetimeErrors.Handler`. An invalid `Scene` passed to `Get` or `Dispose` throws
  `ArgumentException`.

`AdditiveSceneFlow` in the sample is the additive counterpart of `SceneFlow`:

<!-- source: Samples~/BasicUsage/AdditiveSceneFlow.cs -->
```csharp
/// <summary>Loads and unloads additive scenes; the scene's work ends before Unity destroys its objects.</summary>
public sealed class AdditiveSceneFlow
{
    private readonly Lifetime _lifetime;   // a child of Lifetime.App: it must outlive the scenes it unloads

    public AdditiveSceneFlow(Lifetime lifetime) => _lifetime = lifetime;

    public void Load(string sceneName) => _lifetime.Run(ct => LoadAsync(sceneName, ct));

    public void Unload(Scene scene) => _lifetime.Run(ct => UnloadAsync(scene, ct));

    // Stops the scene's work and keeps the scene loaded; its lifetime stays usable.
    public void StopWork(Scene scene) => SceneLifetimes.Get(scene).Cancel();

    private static async UniTask LoadAsync(string sceneName, CancellationToken ct)
    {
        await SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive).ToUniTask(cancellationToken: ct);
    }

    private static async UniTask UnloadAsync(Scene scene, CancellationToken ct)
    {
        // Unity refuses an unloaded scene and the last loaded one, and disposal is final, so check before it.
        if (!scene.isLoaded || SceneManager.sceneCount < 2)
        {
            return;
        }

        SceneLifetimes.Dispose(scene);     // this scene's work stops now, before the unload destroys anything
        await SceneManager.UnloadSceneAsync(scene).ToUniTask(cancellationToken: ct);
    }
}
```

- **`Unload(scene)` is called.** The method first checks the two cases in which Unity refuses an
  unload: the scene is not loaded, or it is the only loaded scene. In both it returns before
  anything is disposed. Otherwise `SceneLifetimes.Dispose(scene)` ends the work of that one scene
  while its objects still exist, including objects that were moved into it after they were created.
  Every other loaded scene keeps working. Then the unload destroys the objects, whose lifetimes are
  already disposed.
- **`Load(sceneName)` is called.** No call is needed: nothing is being destroyed. The new scene gets
  its own lifetime the first time one of its objects asks for one.
- **`StopWork(scene)` is called.** The scene's lifetime is cancelled, not disposed. The scene stays
  loaded and its objects can register work again.

### A scene name, and the synchronous load

`SceneFlow` takes a scene **path**, and `SceneUtility.GetBuildIndexByScenePath` is the check for a
path. For a scene **name** the check is `Application.CanStreamedLevelBeLoaded(sceneName)`.
`SceneNameFlow` in the sample uses it for an async load and for a synchronous one:

<!-- source: Samples~/BasicUsage/SceneNameFlow.cs -->
```csharp
/// <summary>The scene call for a scene name, async and synchronous; the name is checked before anything is disposed.</summary>
public sealed class SceneNameFlow
{
    private readonly Lifetime _lifetime;   // a child of Lifetime.App: it must outlive the scenes it unloads

    public SceneNameFlow(Lifetime lifetime) => _lifetime = lifetime;

    public void Load(string sceneName) => _lifetime.Run(ct => LoadAsync(sceneName, ct));

    // Synchronous: Unity replaces the scene in the next frame, so the caller's own work may be what is disposed here.
    public void LoadNow(string sceneName)
    {
        if (!CanLoad(sceneName))
        {
            return;
        }

        SceneLifetimes.DisposeAll();
        SceneManager.LoadScene(sceneName);
    }

    private static async UniTask LoadAsync(string sceneName, CancellationToken ct)
    {
        if (!CanLoad(sceneName))
        {
            return;
        }

        SceneLifetimes.DisposeAll();       // everything owned by the loaded scenes stops now, before any destroy
        await SceneManager.LoadSceneAsync(sceneName).ToUniTask(cancellationToken: ct);
    }

    // Disposal is final, so a name that is not in the Build Settings is refused before it.
    private static bool CanLoad(string sceneName)
    {
        if (Application.CanStreamedLevelBeLoaded(sceneName))
        {
            return true;
        }

        Debug.LogError($"Scene '{sceneName}' is not in the Build Settings; nothing was disposed.");
        return false;
    }
}
```

- **`Load(sceneName)` is called.** The steps are those of `SceneFlow`. The task starts on the
  flow's own lifetime, `CanLoad` refuses a name that is not in the Build Settings before anything
  is disposed, and then `DisposeAll()` runs right before `LoadSceneAsync`.
- **`LoadNow(sceneName)` is called.** No task is started. After the same check, `DisposeAll()` ends
  the work of every loaded scene inside the `LoadNow` call. `SceneManager.LoadScene` then asks
  Unity for the new scene; Unity destroys the old objects and brings in the new scene in the next
  frame.
- **The caller of `LoadNow` belongs to the old scene**, for example a button handler. Its own
  lifetimes are among those `DisposeAll()` disposes. The handler still runs to its end, because C#
  code is not interrupted, but whatever it registers after the call is terminated on the spot (see
  [Disposal is terminal](#disposal-is-terminal)). Make `LoadNow` the last thing such a caller
  does.

### Who makes the call

A flow that awaits the load must not be owned by a scene it disposes. `SceneFlow` receives a
lifetime that is a child of `Lifetime.App`. Had it been given a scene lifetime, `DisposeAll()` would
cancel the very task that is about to await the load. In the demo `SceneFlow` is built from
`Lifetime.App.CreateChild("SceneFlow")`; with Zenject it is bound in the ProjectContext (see
[Zenject](Zenject.md)). The synchronous `LoadNow` awaits nothing, so it may be called from the old
scene.

### Disposal is terminal

After `Dispose(scene)` or `DisposeAll()`, and until the scene is really unloaded:

- `SceneLifetimes.Get(scene)` returns the disposed scene lifetime;
- `GetLifetime()` and `GetActiveLifetime()` of an object in that scene return a disposed lifetime;
- every registration made through them is terminated on the spot. The editor and development builds
  log [JANITOR109](Troubleshooting.md#janitor109) when work is registered on the disposed scene
  lifetime, an area is created under it, or a component or GameObject lifetime is asked for in that
  scene, while the scene is still loaded. It is logged once per scene, and not while the session is
  ending.

The objects of the old scene are still there during that time and still receive `Update`, but they
cannot start new owned work, and their listeners are gone. That is the intended state for the few
frames a load takes. For longer loads, see the next section.

**A load that fails after the call leaves a scene that cannot work again.** There is no way to
bring a disposed scene lifetime back: if `DisposeAll()` ran and the load then does not happen, the
old scene stays on screen with every listener removed and every new registration refused. Two rules
follow, and the samples apply both:

- Check what can be checked before disposing. `SceneFlow` looks the path up in the Build Settings,
  and `AdditiveSceneFlow` tests the two conditions under which Unity refuses an unload.
- For a load that can fail at runtime (a scene from a downloaded bundle, or any load behind a
  network step), hold the activation and dispose right before it, as `LoadingScreenFlow` does in
  the next section. A failure before that point leaves the old scene fully alive.

### Stopping a scene without unloading it

`SceneLifetimes.Get(scene).Cancel()`, the `StopWork` method above, stops the scene's work and keeps
the scene usable: every object lifetime opens a new generation and accepts work again. See
[Stopping work](Stopping-Work.md#stop-a-scene).

---

## Loading screens

A load with `allowSceneActivation = false` keeps the old scene alive and visible while the new one
loads in the background. Calling `DisposeAll()` at the start of such a load would stop the old
scene's work for the whole duration: its animations stop, its buttons do nothing, and nothing in it
can start new work.

Make the call right before the switch instead. `LoadingScreenFlow` in the sample does this:

<!-- source: Samples~/BasicUsage/LoadingScreenFlow.cs -->
```csharp
/// <summary>Loads a scene behind a loading screen and ends the old scene's work right before it is replaced.</summary>
public sealed class LoadingScreenFlow
{
    private readonly Lifetime _lifetime;   // a child of Lifetime.App: it must outlive the scenes it unloads
    private readonly ILoadingScreen _screen;
    private bool _loading;

    public LoadingScreenFlow(Lifetime lifetime, ILoadingScreen screen)
    {
        _lifetime = lifetime;
        _screen = screen;
    }

    public void Load(string sceneName) => _lifetime.Run(ct => LoadAsync(sceneName, ct));

    private async UniTask LoadAsync(string sceneName, CancellationToken ct)
    {
        if (_loading)
        {
            return;                        // a second load would replace the scene this one is loading
        }

        _loading = true;
        _screen.Show();
        try
        {
            var operation = SceneManager.LoadSceneAsync(sceneName);
            if (operation == null)
            {
                return;                    // unknown scene: nothing started, nothing was disposed
            }

            operation.allowSceneActivation = false;
            try
            {
                await UniTask.WaitUntil(() => operation.progress >= 0.9f, cancellationToken: ct);   // loaded, not yet activated
            }
            finally
            {
                SceneLifetimes.DisposeAll();               // right before activation, also when the wait was cancelled
                operation.allowSceneActivation = true;     // a started load cannot be aborted, and a held one stalls later loads
            }

            await operation.ToUniTask(cancellationToken: ct);
        }
        finally
        {
            _screen.Hide();
            _loading = false;
        }
    }
}
```

What happens when `Load` is called:

1. A load that is already running makes the call return at once: a second load would replace the
   scene the first one is loading. Otherwise `_loading` is set and the loading screen is shown.
2. `LoadSceneAsync` starts the load. For a scene Unity does not know it returns `null`; the method
   returns, the outer `finally` hides the screen, and nothing was disposed.
3. Activation is held back. The old scene is untouched: its lifetimes are alive and its objects
   keep working behind the loading screen.
4. `WaitUntil` returns when the new scene is ready and only its activation is left.
5. The inner `finally` runs `SceneLifetimes.DisposeAll()`, which ends the old scene's work at the
   last moment before it is replaced. `DisposeAll()` skips a scene that has not finished loading,
   and a scene that waits for its activation has not, so the incoming scene is not affected.
6. `allowSceneActivation = true` lets Unity make the switch. The old objects are destroyed with
   their lifetimes already disposed.
7. The method continues in the new scene. The outer `finally` hides the loading screen and clears
   `_loading`.

**The flow's own lifetime is cancelled during step 4.** `WaitUntil` throws, and the inner `finally`
still disposes the scenes and releases the activation. Both lines are needed on that path too:
Unity cannot abort a load that has started, and a load that is left held back stalls every load
started after it. The scene change therefore completes, with the old scene's work ended first. The
outer `finally` hides the screen.

Like `SceneFlow`, this class is given a lifetime under `Lifetime.App`, so step 5 does not cancel the
task that is running it.

The same rule applies to any flow with a gap between "the load starts" and "the old scene goes
away", including an additive loading-screen scene: dispose the old scene (`Dispose(oldScene)`) right
before unloading it, not when the transition begins.

---

## When the scene call is skipped

Nothing leaks if the call is left out. Cleanup still happens, in three steps, but the guarantee that
all work stops before anything is destroyed is lost:

1. **Each object's lifetimes are disposed by its own destruction.** That is the same order Unity
   destroys the objects in, which is not defined between objects. Work owned by object A can still
   run after object B was destroyed.
2. **Work that is not owned by an object keeps running through the destruction.** This is work
   registered directly on the scene lifetime, and areas under it that belong to plain C# classes.
3. **After the unload, on `SceneManager.sceneUnloaded`,** the package disposes a scene lifetime that
   is still alive. This is late: every object of the scene is already gone. The editor and
   development builds log [JANITOR104](Troubleshooting.md#janitor104) once per scene.

With the Zenject integration installed in the scene's context, there is one more fallback between
steps 1 and 3. When the SceneContext is destroyed, the integration disposes the scene lifetime
before any other disposable of that scene's container runs, and logs JANITOR104 at that point.
That is earlier than step 3, but still inside the destruction: other objects of the scene may
already be gone. See [Zenject](Zenject.md).

When the application quits or Play Mode ends, `Lifetime.App` is disposed and every lifetime with it.
No call is needed for that. The package ends the session on whichever signal comes first
(`Application.exitCancellationToken`, `Application.quitting`, or the editor's return to Edit Mode)
and does it exactly once. After the editor is back in Edit Mode the tree is gone, and the calls
throw `InvalidOperationException` as described above.

A script recompile during Play Mode (editor only) discards every lifetime together with the rest of
the managed state. The first call into the package afterwards starts a new tree and logs one
warning that says so. Work that was owned before the reload is not carried over; restart Play Mode
for a clean state.

---

## See also

- [Concepts](Concepts.md): the tree, generations, and the order in which things stop
- [Stopping work](Stopping-Work.md): cancelling an object, an area or a scene by hand
- [Organizing work](Organizing-Work.md): placing an object's lifetime in a category, and scene
  membership
- [Pooling](Pooling.md): the active lifetime on recycled objects
- [Coroutines](Coroutines.md): what deactivation does to coroutines
- [Zenject](Zenject.md): injected lifetimes and the scene context
- [Troubleshooting](Troubleshooting.md): JANITOR103, JANITOR104, JANITOR108 and JANITOR109
