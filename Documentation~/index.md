# Janitor

Janitor binds UniTask work, DOTween tweens, coroutines, timers and event subscriptions to an owner
called a `Lifetime`. The work stops when its owner is destroyed, deactivated, cancelled, or when its
scene is disposed, and you never write the removal line (`-=`, `RemoveListener`, `Kill`,
`StopCoroutine`, `cts.Cancel()`): registering through a lifetime is the cleanup. The core needs
Unity 6000.0 or newer and UniTask; DOTween and Zenject support are optional assemblies that compile
only when those libraries are present.

Requirements, installation, a five minute start in six steps and the API at a glance are in the
[README](../README.md). Every public type and member is listed in the
[API reference](API-Reference.md).

## Which guide answers my question

**Starting out**

- How do I install the package and its prerequisites? [README](../README.md)
- What is a lifetime, an area and a generation, and what is the difference between `Cancel()` and
  `Dispose()`? [Concepts](Concepts.md)
- I have classes full of `OnDestroy` and `OnDisable` cleanup. How do I convert them?
  [Migration](Migration.md)
- I have a short question and want a short answer. [FAQ](FAQ.md)
- What is the exact signature of a call, and which overloads exist? Does a method with a given name
  exist? [API reference](API-Reference.md)

**Stopping work**

- How do I stop everything when the player leaves gameplay? How do I stop one scene, one object, one
  area, one item or one task? [Stopping work](Stopping-Work.md)
- How do I restart something that is already running (cancel the previous run, start a new one)?
  [Stopping work](Stopping-Work.md)
- How do I restart work that also has to stop when its GameObject is deactivated?
  [Organizing work](Organizing-Work.md)
- Why did my listener stop firing? [Troubleshooting](Troubleshooting.md)
- Why is `Token` different after a `Cancel()`, and in which order do things end?
  [Concepts](Concepts.md)

**Kinds of work**

- How do I start async work that ends with its owner, and where do its exceptions go? How do I run
  something after a delay or every few seconds? [Tasks and errors](Tasks-and-Errors.md)
- How do I subscribe to an event without writing the unsubscribe? What about `UnityEvent`, an SDK's
  C# event or an `IDisposable`? Which side should own a subscription when the publisher is
  shorter-lived than the listener? [Events](Events.md)
- How do I bind a tween, land it on its end value when cancelled, or await it? [DOTween](DOTween.md)
- How do I bind a coroutine to something other than its host? [Coroutines](Coroutines.md)

**Objects and scenes**

- What is the difference between `GetLifetime()` and `GetActiveLifetime()`? What happens to an
  object that is never activated, or that lives in DontDestroyOnLoad? What do I call before loading
  a scene, and what if the load can fail? [Components and scenes](Components-and-Scenes.md)
- How do I make the scene call when I only have the name of the scene, or when the load is
  synchronous? [Components and scenes](Components-and-Scenes.md)
- How do I use it on a pooled object that is switched with `SetActive`? [Pooling](Pooling.md)

**Organizing a project**

- How do I group work into categories such as `Popups` and `Combat`, share a category between
  classes, and decide between exposing an area and exposing a method?
  [Organizing work](Organizing-Work.md)
- How do I inject a lifetime with Zenject, and bind a SignalBus subscription to an owner? Where does
  a plain service that injects a lifetime start its own work? [Zenject](Zenject.md)

**When something looks wrong**

- How do I see what is registered where, and what do the columns of the Janitor window mean?
  [Diagnostics](Diagnostics.md)
- The console or the window shows `JANITOR1xx`. What does it mean and how do I fix it?
  [Troubleshooting](Troubleshooting.md)
- Work I start in `OnEnable` runs twice after the object was disabled and enabled again
  (`JANITOR116`). [Troubleshooting](Troubleshooting.md#janitor116)
- My scripts recompiled while the game was playing, and the work that was running is gone.
  [Troubleshooting](Troubleshooting.md)
- Can I call this from a worker thread? [Threading](Threading.md)
- What does a registration, a `Cancel()` or an event invoke allocate? [Performance](Performance.md)
- What does the package deliberately not do? [Limitations](Limitations.md)

**Why it is built this way**

- [Architecture](Design/Architecture.md) and the decision records listed under
  [Design and decisions](#design-and-decisions).

---

## One class from start to end

This section follows one popup through its whole life. The three classes below are the Basic Usage
sample: a categories service, a wallet that publishes an event, and the popup that uses both.

`GameplayLifetimes` holds two **areas**. An area is a lifetime you create yourself with
`CreateChild`; here each one is a category that other classes join or register work into. The names
are display labels for the Janitor window and are never used for lookup.

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

The sample's `GameplayRoot` component creates the instance from an area of its own:

<!-- source: Samples~/BasicUsage/GameplayRoot.cs -->
```csharp
private void Awake()
{
    _gameplay = this.GetLifetime().CreateChild("Gameplay");   // disposed with this object or its scene
    Lifetimes = new GameplayLifetimes(_gameplay);
}

// A signal triggers, the tree executes: one handler, no broadcast.
public void OnResetRequested() => _gameplay.Cancel();          // every area stops; each stays usable
```

`Wallet` declares its event as an `OwnedEvent<int>`. It keeps the event private and exposes the
subscribe-only view `IOwnedEvent<int>`, so other classes can subscribe and only `Wallet` can invoke.

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

`ShopPopup` is the whole file, `using` lines included. It has an `OnEnable` and no `OnDisable`, no
`OnDestroy`, no field for a coroutine handle and no `CancellationTokenSource`. `OfferService` is a
stand-in for the game's store code; its `FetchAsync` takes 1.5 seconds.

<!-- source: Samples~/BasicUsage/ShopPopup.cs -->
```csharp
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.Janitor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
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
}
```

Once the popup is open, the lifetimes involved form this tree. Each line is a row in the Janitor
window (`Window > Analysis > Janitor`).

```text
App                          Lifetime.App
  <scene name>               the scene's lifetime
    GameplayRoot             component lifetime of the GameplayRoot object
      Gameplay               area created in GameplayRoot.Awake
        Popups               area (a category)
          ShopPopup          active lifetime of the popup's GameObject, placed in Popups
        Combat               area (a category)
```

### The popup is enabled

Unity calls `OnEnable` when the popup's GameObject becomes active.

1. `this.GetActiveLifetime(_lifetimes.Popups)` returns the **active lifetime** of the GameObject.
   The first call creates it as a child of `Popups` and adds a hidden `ActiveLifetimeTrigger`
   component to the GameObject; every later call returns the same lifetime and allocates nothing.
   The first access decides the parent, which is why this is the first line of `OnEnable`.
2. `shown.StartCoroutine(this, BlinkBadge())` starts the coroutine on the popup (`this` is the host
   that runs it) and ties it to `shown`. The badge starts blinking.
3. `shown.Run(LoadOffersAsync)` calls `LoadOffersAsync` at once and hands it the token of the
   lifetime's current generation. The method runs up to its first unfinished `await` (the 1.5 second
   fetch) and `Run` returns. `Run` returns nothing: there is no task to store and nothing to forget.
4. `_buyButton.onClick.Subscribe(OnBuyClicked, shown)` adds the click listener.
5. `_wallet.CoinsChanged.Subscribe(OnCoinsChanged, shown)` adds the popup to the wallet's event.

The popup's row under `Popups` now holds four items: a coroutine, a task and two subscriptions.

### A coin is added

Something calls `wallet.Add(25)`. `Wallet` invokes its event, and the event calls its subscribers in
subscription order, each inside its own `try`/`catch`. `OnCoinsChanged` runs and the label shows the
new total. If a handler throws, the exception is routed to `LifetimeErrors.Handler` and the
remaining handlers still run. The default handler logs one console error that names the lifetime and
the member and line that subscribed, with the owner object as the log's context.

Meanwhile, 1.5 seconds after the popup opened, the fetch completes, `LoadOffersAsync` continues and
`_offers.Show(offers)` runs. The task removes its own entry; the row is down to three items.

### The popup is disabled

Something calls `popup.gameObject.SetActive(false)`. The hidden trigger receives `OnDisable` and
calls `Cancel()` on the active lifetime. Inside that one call:

1. The generation's token is cancelled. If `LoadOffersAsync` is still waiting for the fetch, the
   awaited delay throws `OperationCanceledException` the next time it checks the token, `Run`
   swallows that exception without logging, and `_offers.Show` never runs.
2. The items are ended, newest first: the wallet subscription is removed, the click listener is
   removed, the entry of the task is released (if the task is still running), the coroutine is
   stopped.
3. A new generation opens. The lifetime object is the same and stays usable, but every token and
   handle from before the cancel is dead.

While the popup is inactive, `wallet.Add` no longer reaches it, because the subscription is gone,
not because a flag is checked. Work registered on an active lifetime while its GameObject is
inactive is ended immediately (and reported as [JANITOR108](Troubleshooting.md#janitor108) in the
editor and in development builds).

When the popup is activated again, `OnEnable` runs again and registers everything into the new
generation. Nothing is registered twice, because nothing from the previous generation is left.

The active lifetime follows the activation of the GameObject, not the `enabled` flag of the popup:
`popup.enabled = false` cancels nothing. The one component whose `enabled` flag matters is the
package's own hidden trigger. A loop that disables every `Behaviour` of the object reaches it, and
the active lifetime's work is then cancelled once; the trigger enables itself again the next time
the lifetime is used.

### `Popups` is cancelled

With the popup open, something calls `lifetimes.Popups.Cancel()`. The popup's active lifetime is a
child of `Popups`, so the same three steps happen to it, together with everything else registered in
or placed under `Popups`. The popup's GameObject is not touched: it stays active and visible, but
the badge stops blinking, the label stops following the wallet and the Buy button does nothing.
Unity does not call `OnEnable` again, so the popup stays inert until it is closed and opened.
`Popups` itself is usable again as soon as `Cancel()` returns.

`GameplayRoot.OnResetRequested()` cancels `Gameplay`, which is the parent of both categories, so one
call stops `Popups` and `Combat` together.

### The object is destroyed

Something calls `Destroy(popup.gameObject)`. Unity first disables the object, so the trigger cancels
the active lifetime as described above: the popup's work has ended before any `OnDestroy` runs. As
part of the destruction Unity cancels the destroy token that the lifetime subscribed to when it was
created, and the lifetime is **disposed**: it leaves the tree for good, and any later registration
on it is ended immediately. The class has no `OnDestroy`, because nothing is left to clean.

A component lifetime (`this.GetLifetime()`, or the `this.Run(...)` family) has no `OnDisable` step.
It is disposed between the component's `OnDisable` and its `OnDestroy`; a test pins that order on
Unity 6000.3.

### The scene is disposed

The sample changes scenes through `SceneFlow`:

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

`Load` takes a scene path. `LoadAsync` first checks that the scene is in the Build Settings, because
what follows cannot be undone: a disposed scene lifetime accepts no more work, so the check comes
before the dispose and a wrong path changes nothing. A load that can still fail at run time should
hold the scene activation and dispose right before it; `LoadingScreenFlow` in the same sample does
that, and [Components and scenes](Components-and-Scenes.md) explains it.

`SceneLifetimes.DisposeAll()` then disposes the lifetime of every loaded scene, the last loaded
scene first. For the popup's scene that is one operation over the whole subtree shown above: every
token is cancelled first, then every item is ended, deepest lifetime first and newest item first,
and then every lifetime in the subtree is disposed. This happens while all objects still exist, so
no handler or continuation runs against a half-destroyed scene. The load then destroys the objects,
and their destroy callbacks find lifetimes that are already disposed.

`Lifetime.App` and the lifetimes of DontDestroyOnLoad objects are not touched. That is why
`SceneFlow` receives a lifetime that is a child of `Lifetime.App`: a lifetime under the scene would
be disposed by the call, and the task that is loading the next scene would be cancelled with it.

If the call is skipped, cleanup still happens, but late and in whatever order Unity destroys the
objects; the editor and development builds report it once per scene as
[JANITOR104](Troubleshooting.md#janitor104). See [Components and scenes](Components-and-Scenes.md).

---

## About the code in these guides

Every piece of correct-usage code in these pages is copied from a file that ships with the package
and compiles there: one of the three samples under `Samples~/`, or a test under `Tests/`. Nothing is
written for the documentation alone, so a snippet cannot drift from the API. In the Markdown source,
an HTML comment above each block names the file it was copied from. Two other kinds of block appear:

- **Before** blocks show the hand-written cleanup that the package replaces, or a wrong usage in
  [Troubleshooting](Troubleshooting.md). They are illustrations and are not compiled.
- **Signature** blocks show the shape of an API without bodies. The caller-information parameters
  (`[CallerMemberName]`, `[CallerLineNumber]`) are left out; the compiler fills them in.

To read or run the samples, open the Package Manager window, select **Janitor**, open the
**Samples** tab and press **Import** next to a sample. Unity copies it into
`Assets/Samples/Janitor/0.1.0/`.

| Sample | Needs | Setup and what to try |
|---|---|---|
| Basic Usage | The core only | [README](../Samples~/BasicUsage/README.md) |
| DOTween Usage | DOTween and the active DOTween integration | [README](../Samples~/DOTweenUsage/README.md) |
| Zenject Usage | Zenject or Extenject and the active Zenject integration | [README](../Samples~/ZenjectUsage/README.md) |

All three demos need `com.unity.ugui` (which includes TextMeshPro) for their UI. The DOTween and
Zenject samples are gated like the integration assemblies themselves: their assembly definitions
compile only while the integration is active.

The samples contain no scene and no prefab. Each demo builds its UI in code, and code cannot assign
another class's private serialized field. That is the only reason the sample classes receive their
view references through an `Initialize(...)` method. In a game those references are
`[SerializeField]` fields set in the Inspector, or injected, and `Initialize` disappears; nothing
else in the classes changes. For the same reason the demos create components that read their
references in `Awake` or `OnEnable` on an inactive GameObject, call `Initialize`, and then activate
it.

## Design and decisions

For reviewers and contributors: why the package is shaped the way it is.

- [Architecture](Design/Architecture.md): the assemblies, the lifetime tree, teardown in passes, the
  Unity binding, the editor diagnostics path, and what changed between the design and the code
- [ADR-001 The Janitor brand and the `Lifetime` type](Design/Decisions/ADR-001-Janitor-Brand-And-The-Lifetime-Type.md):
  why the product and the main type have different names, and which other libraries the names
  collide with
- [ADR-002 Reusable `Cancel` versus terminal `Dispose`](Design/Decisions/ADR-002-Reusable-Cancel-Versus-Terminal-Dispose.md):
  two operations, generations, and why package-owned lifetimes ignore `Dispose`
- [ADR-003 Tweens are not recyclable](Design/Decisions/ADR-003-Tweens-Are-Not-Recyclable.md): why a
  registered tween leaves DOTween's pool
- [ADR-004 Opt-in scene call and no StopAll broadcast](Design/Decisions/ADR-004-Opt-In-Scene-Call-And-No-StopAll-Broadcast.md):
  why the scene call is one explicit line, and why stopping is a call on the tree and not a message
- [ADR-005 No removal lines in user code](Design/Decisions/ADR-005-No-Removal-Lines-In-User-Code.md):
  `OwnedEvent`, paired `Subscribe`, and why `Cancel()` removes subscriptions too

## See also

- [README](../README.md): requirements, installation, a five minute start in six steps and the API
  at a glance
- [API reference](API-Reference.md): every public type and member as signatures
- [Concepts](Concepts.md): the model behind everything on this page
- [Migration](Migration.md): the same ideas as recipes for existing code
- [Troubleshooting](Troubleshooting.md): one section per `JANITOR1xx` ID
