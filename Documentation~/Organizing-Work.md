# Organizing work

[Back to index](index.md)

Where a piece of work is registered decides what it stops together with. This page is about laying
that out: areas for work that stops as a unit, a typed service for categories that several classes
share, placing an object's own lifetime in a category, and the choice between exposing an area and
exposing a method. It ends with why the package has no registry keyed by strings.

---

## Areas

An **area** is a lifetime you create yourself, under a parent you choose:

<!-- signature -->
```csharp
public Lifetime CreateChild(string name = null);
```

`Countdown` in the Basic Usage sample creates one for its running count:

<!-- source: Samples~/BasicUsage/Countdown.cs -->
```csharp
private void Awake() => _run = this.GetLifetime().CreateChild("Run");
```

The area is created once, kept in a field and reused. Everything registered into `_run` stops
together when `_run.Cancel()` is called, and the area is ready for the next registration at once.
Work registered on the component's own lifetime is not affected by that cancel.

The parent decides how long the area lives and which wider cancel reaches it:

| Created from | The area ends with | A cancel reaches it from |
|---|---|---|
| `this.GetLifetime()` | The component | The component lifetime, its scene, `App` |
| Another area | That area | That area and everything above it |
| A lifetime handed to a plain C# class (constructor argument, injection) | Whatever that lifetime ends with | That lifetime and everything above it |
| `SceneLifetimes.Get(scene)` | The scene's disposal | The scene lifetime, `App` |
| `Lifetime.App` | The application, or Play Mode | `App` only |

Rules:

- **There is no parent-less area.** `Lifetime` has no public constructor, so every area is attached
  to something that ends.
- **Reuse, do not re-create.** `Cancel()` empties an area and keeps it. Creating a new area per
  operation and never disposing it makes the parent grow; the Janitor window reports a lifetime with
  more than 64 live child areas, or more than 256 registered items, as
  [JANITOR106](Troubleshooting.md#janitor106). Only areas are counted, under every kind of parent.
  Object lifetimes are not, so a category that holds hundreds of placed objects raises nothing.
- **`Dispose()` is for an area that will not be used again**, for example one created for a match
  that is over. A disposed area terminates every later registration on the spot.
- **Areas nest.** A child area is cancelled when its parent is cancelled and disposed when its
  parent is disposed. `CreateChild` on a parent that is already disposed returns an area that is
  already disposed; it does not throw. Under a disposed scene lifetime the editor and development
  builds also log [JANITOR109](Troubleshooting.md#janitor109), once per scene.
- **An area under an active lifetime follows the GameObject.** It is cancelled by `SetActive(false)`
  with its parent, and while the GameObject is inactive it refuses registrations
  ([JANITOR108](Troubleshooting.md#janitor108)) and its `Token` is an already cancelled token, the
  same as the active lifetime itself. The next section shows one.
- `CreateChild` must be called on the main thread.

### An area under the active lifetime

`Countdown` hangs its area under the component lifetime, so the countdown keeps running while the
object is inactive. Work that is restarted while an object is shown, and that must also stop when
the object is hidden, gets an area under the **active** lifetime. `InboxPanel` in the sample shows a
spinner, listens to a button and to an unread counter, and loads its messages; the Refresh button
restarts the load and nothing else. (`InboxService` is a plain class with an `OwnedEvent<int>` named
`UnreadChanged` and a `FetchAsync` that takes a second and a half.)

<!-- source: Samples~/BasicUsage/InboxPanel.cs -->
```csharp
/// <summary>An inbox panel: hiding it stops everything, and Refresh restarts only the load.</summary>
public sealed class InboxPanel : MonoBehaviour
{
    private InboxService _inbox;
    private Button _refreshButton;
    private Graphic _spinner;
    private TMP_Text _unreadLabel;
    private TMP_Text _statusLabel;
    private Lifetime _load;

    // Arrives here instead of [Inject] and the Inspector; call it before the object is enabled.
    public void Initialize(InboxService inbox, Button refreshButton, Graphic spinner, TMP_Text unreadLabel, TMP_Text statusLabel)
    {
        _inbox = inbox;
        _refreshButton = refreshButton;
        _spinner = spinner;
        _unreadLabel = unreadLabel;
        _statusLabel = statusLabel;
    }

    private void OnEnable()
    {
        // Cancelled by SetActive(false); disposed on destroy.
        var shown = this.GetActiveLifetime();

        // An area under the active lifetime: SetActive(false) cancels it, so it is kept and reused.
        _load ??= shown.CreateChild("Load");

        OnUnreadChanged(_inbox.Unread);
        shown.StartCoroutine(this, Spin());
        _refreshButton.onClick.Subscribe(Refresh, shown);
        _inbox.UnreadChanged.Subscribe(OnUnreadChanged, shown);
        _load.Run(LoadAsync);
    }

    // The running load stops here; the spinner and the listeners stay.
    private void Refresh()
    {
        _load.Cancel();
        _load.Run(LoadAsync);
    }

    private async UniTask LoadAsync(CancellationToken ct)
    {
        _statusLabel.text = "Loading...";
        var messages = await _inbox.FetchAsync(ct);
        _statusLabel.text = $"{messages.Count} message(s)";
    }

    private IEnumerator Spin()
    {
        while (true)
        {
            _spinner.rectTransform.Rotate(0f, 0f, -360f * Time.deltaTime);
            yield return null;
        }
    }

    private void OnUnreadChanged(int unread) => _unreadLabel.text = $"Unread: {unread}";
}
```

What happens at each moment:

- **The panel is enabled for the first time.** `shown` is the active lifetime. `_load` is `null`, so
  `shown.CreateChild("Load")` creates the area, once. The spinner coroutine and the two
  subscriptions are registered on `shown`, and the load is started in `_load`.
- **Refresh is clicked while the load runs.** `_load.Cancel()` ends the fetch at its `await`; the
  line that writes the message count never runs for it. `_load.Run(LoadAsync)` starts a new load in
  the next generation of `_load`. The spinner and both subscriptions are on `shown` and are not
  touched.
- **The panel is hidden with `SetActive(false)`.** The active lifetime is cancelled and `_load`
  with it: the spinner, both listeners and the load stop. `_load` is cancelled, not disposed, so
  the field still holds a usable area.
- **The panel is enabled again.** `_load ??=` finds the field set and creates nothing. `OnEnable`
  registers everything again, in new generations of `shown` and `_load`.
- **The panel is destroyed.** The active lifetime is disposed, and `_load` with it.

Three facts carry this shape:

- **`SetActive(false)` cancels an area under the active lifetime; it does not dispose it.** One
  area serves every activation. Creating a new area in each `OnEnable` would leave the earlier
  ones behind as empty children of the active lifetime.
- **`CreateChild` on an active lifetime is allowed while the GameObject is inactive**, and before
  it was ever active. Nothing is logged. Only registrations are refused there (JANITOR108).
- **`Awake` does not run before the first activation.** An area that is created in `Awake` is still
  `null` when a public method is called on an object that was never active. Create the area where
  it is first needed, with `??=`, as `OnEnable` does here. `GateOpener` in the DOTween Usage sample
  does the same inside a public method, after it has returned early for an inactive object (see
  [DOTween](DOTween.md)).

### An area under `App`

`Lifetime.App` exists for one Play Mode session. A plain C# object that must outlive scenes and
caches an area under `App` in a static field has to notice a new session: with domain reload
disabled the static field survives and holds an area of the previous session, which is disposed.
The demo of the sample checks `IsDisposed` for exactly that reason:

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

---

## The categories service

A **category** is an area that several classes share: one registers work into it, another stops it.
The usual first attempt at this is a global store with string keys:

<!-- illustrative: before -->
```csharp
public sealed class WorkRegistry
{
    public static readonly WorkRegistry Instance = new WorkRegistry();

    private readonly Dictionary<string, List<IDisposable>> _items = new();

    public void Register(string key, IDisposable item)
    {
        if (!_items.TryGetValue(key, out var list))
        {
            list = new List<IDisposable>();
            _items[key] = list;
        }

        list.Add(item);
    }

    public void CancelAll(string key)
    {
        if (!_items.TryGetValue(key, out var list))
        {
            return;
        }

        foreach (var item in list)
        {
            item.Dispose();
        }

        list.Clear();
    }
}

public sealed class DamageNumbers
{
    public void Pop(IDisposable popAnimation) => WorkRegistry.Instance.Register("combat", popAnimation);
}

public sealed class CombatFlow
{
    public void OnCombatInterrupted() => WorkRegistry.Instance.CancelAll("Combat");
}
```

The line that fails is `WorkRegistry.Instance.CancelAll("Combat");`. The work was registered under
`"combat"`. The lookup finds nothing, returns, and nothing stops. No error is raised, in the editor
or anywhere else. The other problems are quieter:

- `list.Add(item)` keeps every item until someone cancels that key. Items of objects that were
  destroyed in the meantime stay in the list and stay reachable.
- `Instance` is static. It survives scene loads, and with domain reload disabled it survives Play
  Mode sessions.
- Every class in the project can cancel every key. Who may stop what is written down nowhere.

The replacement is a small class with one property per category. This is the one from the Basic
Usage sample:

<!-- source: Samples~/BasicUsage/GameplayLifetimes.cs -->
```csharp
/// <summary>Named areas that objects join with GetLifetime(area) or GetActiveLifetime(area).</summary>
public sealed class GameplayLifetimes
{
    public Lifetime Popups { get; }
    public Lifetime Combat { get; }

    public GameplayLifetimes(Lifetime life)
    {
        Popups = life.CreateChild("Popups");
        Combat = life.CreateChild("Combat");
    }
}
```

It is constructed once, from a lifetime that decides how long the categories exist. Without a
container, one object of the scene constructs it and hands it on. In the Basic Usage sample that
object is `GameplayRoot`: its `Awake` creates the `Gameplay` area and builds `GameplayLifetimes`
from it, and the class exposes the result as its `Lifetimes` property. The demo reads that
property and passes it to the popup and to each combat effect through their `Initialize` methods.
With Zenject the class is bound in the scene context and receives an injected lifetime (see
[Zenject](Zenject.md)).

<!-- source: Samples~/BasicUsage/GameplayRoot.cs -->
```csharp
private void Awake()
{
    _gameplay = this.GetLifetime().CreateChild("Gameplay");   // disposed with this object or its scene
    Lifetimes = new GameplayLifetimes(_gameplay);
}
```

Classes that need a category receive the service. The DOTween Usage sample has the two classes of
the "before" block in this form. One registers into `Combat`:

<!-- source: Samples~/DOTweenUsage/DamageNumbers.cs -->
```csharp
/// <summary>Registers its tweens into the Combat category.</summary>
public sealed class DamageNumbers
{
    private readonly GameplayLifetimes _lifetimes;

    public DamageNumbers(GameplayLifetimes lifetimes) => _lifetimes = lifetimes;

    public void Pop(Transform label) => label.DOPunchScale(Vector3.one * 0.3f, 0.2f).AddTo(_lifetimes.Combat);
}
```

(`AddTo(lifetime)` on a tween is the DOTween integration; see [DOTween](DOTween.md).) The other
stops the category, without knowing who registered what:

<!-- source: Samples~/DOTweenUsage/CombatFlow.cs -->
```csharp
/// <summary>Stops the Combat category without knowing who registered what into it.</summary>
public sealed class CombatFlow
{
    private readonly GameplayLifetimes _lifetimes;

    public CombatFlow(GameplayLifetimes lifetimes) => _lifetimes = lifetimes;

    public void OnCombatInterrupted() => _lifetimes.Combat.Cancel();
}
```

What happens at each moment:

- **`Pop(label)` is called.** The punch tween is registered in the current generation of `Combat`.
- **`OnCombatInterrupted()` is called.** `Combat.Cancel()` kills every tween registered in it, in
  the middle of the punch, and stops every other item in `Combat`. The category is usable again at
  once. `Popups` is a sibling and is not touched.
- **The lifetime that was given to `GameplayLifetimes` ends**
  (the `GameplayRoot` object is destroyed, or the scene is disposed). Both categories are disposed
  with it.
- **A typo.** `_lifetimes.Combta` does not compile.

One thing to keep in mind: work registered **directly** into a category is owned by the category,
not by the object that registered it, and not by the object it acts on. A timer started with
`_lifetimes.Combat.Every(...)` keeps firing after the object that started it is destroyed, until
`Combat` is cancelled or disposed. When the object can be destroyed while the work still runs, use
one of two shapes:

- **The object owns the work, and its lifetime is placed in the category.** The work then ends with
  the object and with the category. `CombatEffect` in the next section is built this way.
- **A class that works on another object passes that object as the owner.** The work then ends
  with that object, and the category does not reach it. `CountdownTracker` does this for a
  subscription (see [Events](Events.md#the-owner-is-the-side-that-ends-first)).

---

## Placing an object in a category

<!-- signature -->
```csharp
public static Lifetime GetLifetime(this MonoBehaviour behaviour, Lifetime parent);
public static Lifetime GetActiveLifetime(this Component component, Lifetime parent);
```

Passing a category to `GetLifetime` or `GetActiveLifetime` makes the object's own lifetime a child
of that category. The shop popup of the sample joins `Popups` this way:

<!-- source: Samples~/BasicUsage/ShopPopup.cs -->
```csharp
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
```

`shown` is still the popup's own active lifetime. It is cancelled when the popup is deactivated and
disposed when the popup is destroyed, as without a category. In addition `Popups.Cancel()` now
reaches it, because it sits under `Popups` in the tree. The popup's work has two ways to stop and
needs no extra code for either.

The component lifetime is placed the same way. `CombatEffect` in the sample joins `Combat` in
`Awake` and keeps the lifetime in a field:

<!-- source: Samples~/BasicUsage/CombatEffect.cs -->
```csharp
/// <summary>A one-shot effect in the Combat category: its task is never restarted, and one cancel removes it.</summary>
public sealed class CombatEffect : MonoBehaviour
{
    private GameplayLifetimes _lifetimes;
    private TMP_Text _label;
    private Lifetime _life;

    // Call it before the object is enabled, so Awake sees the category.
    public void Initialize(GameplayLifetimes lifetimes, TMP_Text label)
    {
        _lifetimes = lifetimes;
        _label = label;
    }

    private void Awake()
    {
        // The first access decides the parent, so join Combat before anything else uses this lifetime.
        _life = this.GetLifetime(_lifetimes.Combat);
        _life.OnCancel(gameObject, static go => Destroy(go));      // the one place that removes the effect
    }

    public void Play(int damage)
    {
        _life.Run(ct => PlayAsync(damage, ct));
        _life.After(1f, _life, static life => life.Cancel());      // the normal end is a cancel too
    }

    private async UniTask PlayAsync(int damage, CancellationToken ct)
    {
        _label.text = "-" + damage;
        var start = transform.position;
        for (var elapsed = 0f; elapsed < 1f; elapsed += Time.deltaTime)
        {
            transform.position = start + Vector3.up * (elapsed * 80f);
            await UniTask.Yield(ct);
        }
    }
}
```

- **`Awake` runs.** `this.GetLifetime(_lifetimes.Combat)` is the first access to the component's
  lifetime, so the lifetime is created as a child of `Combat`. One `OnCancel` action is registered
  on it. That action is the only code in the class that destroys the GameObject.
- **`Play` is called.** The rising number and a one second timer are registered on the same
  lifetime.
- **One second passes.** The timer does not destroy anything. It cancels the effect's own lifetime,
  and the cancel runs the `OnCancel` action, which destroys the GameObject. The destruction then
  disposes the lifetime and removes it from `Combat`.
- **`Combat` is cancelled while the effect plays**, directly or through `Gameplay`. The effect's
  lifetime is under `Combat`, so its task stops, its timer is cancelled and the same `OnCancel`
  action runs. Every effect that is playing goes with one call, and the class that cancels `Combat`
  does not know that effects exist.

The normal end and the early end take one path. Had the timer destroyed the object itself, the
class would have two places that remove the effect, and a change to one of them could miss the
other.

### The first access decides

The parent of an object's lifetime is fixed at the moment the lifetime is created, which is the
first time anything asks for it. A lifetime is asked for explicitly by `GetLifetime(...)` and
`GetActiveLifetime(...)`, and implicitly by everything that takes the component as its owner:
`this.Run`, `this.After`, `this.Every`, `this.OnCancel`, the paired `this.Subscribe(...)`,
`x.AddTo(this)`, `evt.Subscribe(handler, this)`, and a `Lifetime` injected into a `MonoBehaviour`
by the Zenject integration.

- A later call with a **different** parent returns the existing lifetime unchanged and logs
  [JANITOR113](Troubleshooting.md#janitor113) in the editor and in development builds. The parent is
  never changed silently: that would move work that is already registered under another owner and
  change which `Cancel()` reaches it.
- A later call with the **same** parent, or without a parent, returns the existing lifetime without
  a warning. `ShopPopup` passes `Popups` on every `OnEnable`.
- So the call with the category has to come first: the first line of `Awake` for the component
  lifetime, as in `CombatEffect`, and the first line of `OnEnable` for the active lifetime, as in
  `ShopPopup`.
- If the category is already disposed at the first access, the lifetime is created under the scene
  lifetime and JANITOR113 is logged. That lifetime was never placed, so a later call with a live
  category is a call with a different parent: it is ignored with the same warning.
- Passing the object's own scene lifetime as the parent is the same as passing none.
- The component lifetime and the active lifetime are two lifetimes and are placed separately.
  `ShopPopup` places only its active lifetime. A `this.Run(...)` in the same class would run on the
  component lifetime, which sits under the scene and is not reached by `Popups.Cancel()`.
  `CombatEffect` places its component lifetime and registers everything on the lifetime it got
  back.
- `gameObject.GetLifetime()` has no form with a parent.

### When a category is disposed

`Cancel()` on a category is the normal operation and leaves everything in place. `Dispose()` on a
category that still has live objects placed in it is a special case, because the category does not
own those objects:

1. Areas under the category are disposed with it.
2. The lifetime of each placed object is **cancelled**: its work stops exactly as for a `Cancel()`.
3. That lifetime is then moved under the object's scene lifetime (`App` for a DontDestroyOnLoad
   object) and opens a new generation. The Janitor window records this as
   [JANITOR114](Troubleshooting.md#janitor114), an informational entry.

The object keeps working. Its lifetime is not disposed because every `this.Run(...)` and every
`Subscribe(handler, this)` of that object goes through it; a disposed lifetime would terminate all
later registrations and leave an object that silently does nothing. Areas the object created under
its own lifetime are cancelled, not disposed, for the same reason.

Two details:

- If the disposal is caused by the object's own scene lifetime, or by `App`, the placed lifetime is
  disposed with it. There is nothing to move it to.
- A lifetime that was moved this way cannot be placed again, in the disposed category or in any
  other. A call such as `this.GetActiveLifetime(category)` returns the moved lifetime, which stays
  under its scene, and logs JANITOR113 once for that lifetime. Dispose a category only when the
  objects that join it are gone or will not ask for it again; otherwise cancel it.

In the Basic Usage sample this happens when the `GameplayRoot` object is destroyed while the popup
lives: `Popups` is disposed, and the popup's lifetime moves under the scene.

### Scene membership

Where the category lives decides how a scene-wide operation treats an object placed in it.

- **The category is inside the object's scene.** `GameplayRoot` creates its categories under its own
  component lifetime, which is under the scene lifetime. The placed popup is therefore still inside
  the scene's subtree: both `SceneLifetimes.Get(scene).Cancel()` and `SceneLifetimes.Dispose(scene)`
  reach it through the tree.
- **The category is outside the object's scene**, for example under `Lifetime.App`, or bound in a
  context that outlives the scene. The placed lifetime is then not under its scene lifetime, so the
  package gives it a **dispose-only membership** in that scene lifetime:
  - `SceneLifetimes.Dispose(scene)` and `DisposeAll()` still dispose it, before Unity destroys the
    object;
  - a scene `Cancel()` does not reach it. For cancellation it belongs to its category.

Objects that change scene are handled when it matters. Right before a scene lifetime is cancelled
or disposed, the package checks in both directions:

- an object that **left** the scene after its lifetime was created (`MoveGameObjectToScene`, a new
  parent in another scene, `DontDestroyOnLoad`) is moved under the lifetime of the scene it is in
  now, or under `App`;
- an object that **moved into** the scene is taken in, so the cancel or the dispose reaches it.

A placed lifetime keeps its category in both cases; only its membership moves, and for a
DontDestroyOnLoad object the membership is dropped.

Destroying a placed object removes its lifetime from the category.

---

## Intent methods or raw areas

Whoever holds a `Lifetime` can register work into it, cancel it and, for an area, dispose it. That
is the right amount of access for a category, which exists so that outsiders can register into it.
For everything else, keep the area private and expose a method named after what the caller wants.

`Countdown` does not expose `_run`. It exposes two intents:

<!-- source: Samples~/BasicUsage/Countdown.cs -->
```csharp
public void StartCountdown(int seconds)
{
    _run.Cancel();                                    // a countdown already running stops here
    _run.Run(ct => TickAsync(seconds, ct));
}

public void StopCountdown() => _run.Cancel();
```

`GameplayRoot` does not expose its `Gameplay` area either; it exposes `OnResetRequested()`. In the
DOTween Usage sample one service cancels a running operation of another service through such a
method:

<!-- source: Samples~/DOTweenUsage/ProfileResetService.cs -->
```csharp
/// <summary>Resets the profile; it cancels a running apply through an intent method, not through the lifetime.</summary>
public sealed class ProfileResetService
{
    private readonly CustomizationService _customization;

    public ProfileResetService(CustomizationService customization) => _customization = customization;

    public void ResetProfile()
    {
        _customization.CancelApply();
        // reset profile data here
    }
}
```

`CancelApply()` is one line in the other service, `_apply.Cancel()`. `ProfileResetService` depends
on `CustomizationService` in its constructor, visibly and checked by the compiler, and it can do
exactly one thing to the apply: cancel it.

The guideline:

- Expose a **raw area** when other classes are meant to register work into it (`Popups`, `Combat`).
- Expose an **intent method** when other classes only need to stop or restart something
  (`StopCountdown()`, `CancelApply()`, `OnResetRequested()`).

---

## Names are display only

The string passed to `CreateChild` is a label. It names the row in the Janitor window and appears in
`LifetimeErrorContext.LifetimeName` and in warnings. Nothing is ever looked up by it: there is no
`Find("Popups")`, duplicates are allowed, and renaming an area changes no behaviour.

- An area created without a name is labelled by its call site, in the form `Member:Line`, for
  example `Awake:22`. `Name` returns `null` for it.
- Package-owned lifetimes are named by the package: `App`, the scene's name, the component's type
  name for a component lifetime, the GameObject's name for a GameObject or active lifetime.

---

## Why there is no string-keyed registry

The package has no `Get("Popups")`, no GUID-keyed store and no global list of lifetimes, and will
not get one. The "before" class above shows why:

- **A key owns nothing.** An entry in a global store outlives the object that made it, unless that
  object remembers to remove it, which is the bug class this package removes.
- **A typo fails silently, at runtime.** A property on a typed class fails at compile time.
- **Keys go stale.** After their owners are gone, the entries remain.
- **Anyone can cancel anything.** A key is a reference that every class can forge. A `Lifetime`
  reference has to be handed over, in a constructor or by injection, so the dependency is visible.

Sharing a category therefore always means passing a `Lifetime`, usually through a class like
`GameplayLifetimes`. A key would have to be stored and passed around too; the typed reference is the
same amount of plumbing with the checks included. The categories also end by themselves, with the
lifetime the service was built from, and they show up in the Janitor window under their owner.

---

## See also

- [Concepts](Concepts.md): the tree, and `Cancel` versus `Dispose`
- [Stopping work](Stopping-Work.md): cancelling an area, a category, one item
- [Components and scenes](Components-and-Scenes.md): the component and active lifetimes that get
  placed
- [Zenject](Zenject.md): injecting a lifetime and binding a categories service
- [Diagnostics](Diagnostics.md): reading the tree in the Janitor window
- [Troubleshooting](Troubleshooting.md#janitor113): JANITOR113, a parent that was ignored
