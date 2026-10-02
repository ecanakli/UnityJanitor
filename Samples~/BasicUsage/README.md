# Basic Usage

Core only: no DOTween, no Zenject. Needs the uGUI package (`com.unity.ugui`, which includes TextMeshPro).

## Setup

1. Import TextMeshPro's Essential Resources once: `Window > TextMeshPro > Import TMP Essential Resources`.
   The demo's labels need the default font.
2. Open an empty scene (`File > New Scene`), create one empty GameObject and add the `BasicUsageDemo` component.
3. Add an Event System with `GameObject > UI > Event System`. The demo creates none, because the right input
   module depends on your project's input handling; it logs one warning when the scene has no Event System. If
   the Input System package is your only input handling, use the button the Event System's Inspector offers to
   replace the input module.
4. Optional, for the **Reload scene** button: add the scene to the Build Settings scene list. Without that the
   button is disabled and says so.
5. Press Play and open `Window > Analysis > Janitor`. The demo picks a landscape or a portrait layout from the shape of the Game view when Play starts, and stays centered at any size.

## What to do

- **Shop: open / close** opens `ShopPopup`. Its row appears under `Popups` with a coroutine, a task and two
  subscriptions. The task is the 1.5 s offer load: close the popup before it ends and the "Showing 3 offers"
  line never reaches the Console; keep it open and the line appears.
- **Add 25 coins** updates the HUD. With the popup open its coin label follows; with the popup closed it does
  not, because its subscription ended with `SetActive(false)`.
- **Cancel popups** with the popup open calls `GameplayLifetimes.Popups.Cancel()`. The popup stays on screen,
  but its row drops to zero entries: the badge stops blinking, the coin label stops following and Buy does
  nothing. Close and open it again and everything is registered again.
- **Countdown: start / stop** runs the `Run` area of `Countdown`. Pressing start twice restarts the count;
  stop cancels it. When it reaches zero the `Finished` owned event logs one line.
- **Launch coins** rents pooled coins that fly to the HUD. While a coin flies its row holds the flight, the
  return timer and one cleanup entry. However the flight ends (the timer, or a Cancel on the `Coin` row in the
  window), the coin goes back to the pool. After they land the `Coin` rows stay in the tree with no entries,
  and the next launch reuses them.
- **Ad: reward granted** and **Ad: closed** raise the fake SDK's events; `PlatformHooks` logs them. Its row
  holds the three paired subscriptions.
- **Play sound** plays a one second beep; `SfxPlayer` holds one release timer in its `Release` area until the
  beep ends. Press it again while the beep plays: the new sound starts and the pending release is replaced, so
  the second beep is never cut.
- **Cancel everything** calls `GameplayRoot.OnResetRequested()`. It stops everything under `Gameplay`: the
  popup's work and the combat ticker (the "Combat ticks" label stops counting). The countdown, the coins and
  the hooks live outside `Gameplay` and keep working.
- **Reload scene** goes through `SceneFlow`: the loaded scene's lifetimes are disposed first, then the scene
  loads. The old scene's rows leave the window and `SceneFlow` stays under `App`.
- **Raise an error** starts a `Run` task that throws after 0.3 s. The Console shows two entries: an
  `[ErrorReporter]` line from the game's reporter, which `ErrorReporting.Install` plugged in at startup, and
  Janitor's own logged exception, which stays because the installed handler calls the default one too.
- **Combat effect** spawns a rising number that removes itself after a second. It sits in the `Combat` category,
  so **Cancel everything** while it rises removes it at once. The one-second timer ends the effect with a cancel,
  so a single `OnCancel` action removes it on both paths.
- **Hint pulse: start / stop** pulses the "Tap the shop button" label with a coroutine. Stop cancels that one
  registration; the rest of the `HintPulse` lifetime is untouched.
- **Pause / resume game time** sets `Time.timeScale` to 0 and back. The `SessionClock` label counts "Game" seconds
  with a scaled timer and "Real" seconds with an `ignoreTimeScale` timer, so only the second keeps counting.
  The "Wallet" label is `CoinsLabel`: it follows the coins whether the popup is open or not. Opening and
  closing the shop writes the `[InputGate]` lines of `ModalBlocker`, which lives while the popup is active.
- **Spawn a countdown** creates a short-lived `Countdown` with its own label and starts a three second count. The
  scene-long `CountdownTracker` subscribes to its `Finished` event and logs one line when it fires. The countdown
  is destroyed after five seconds. In the window the subscription is an entry of the spawned countdown's row,
  not of the tracker's, so it leaves the tree with the countdown and the tracker holds none.
- **Reveal a score** creates a label with a `ScoreReveal` and counts a random score up in about a second. The
  object is destroyed after three seconds; destroying it is the only thing that would stop the count early.

## What to look at

- `GameplayLifetimes.cs` - the categories service: one area per category, created from the lifetime it is given.
- `GameplayRoot.cs` - creates the `Gameplay` area, builds `GameplayLifetimes` from it, and cancels the whole
  area in one call. This is the only place categories are created; every other class receives them.
- `Wallet.cs` - an `OwnedEvent<int>` kept private and exposed as a subscribe-only `IOwnedEvent<int>`.
- `ShopPopup.cs` - `GetActiveLifetime(category)` in `OnEnable`, then a coroutine, a task, a `Button` click and
  an owned event, all registered on that lifetime. There is no `OnDisable`.
- `Countdown.cs` - restart with `Cancel()` and then `Run`; an owned event without arguments.
- `CoinPickup.cs` - a `SetActive` pooled object: `GetActiveLifetime()` in `OnEnable`, a `Run` flight loop, an
  `After` that only deactivates the coin, and one `OnCancel` action that returns it to the pool, so every end of
  the flight (the timer, `SetActive(false)`, an outside `Cancel()`) returns it exactly once.
- `PlatformHooks.cs` - the three paired `Subscribe` forms: a custom delegate, `Action<string>` and plain `Action`.
- `SfxPlayer.cs` - `After` in a child area instead of `async void` and a token-less delay: `Cancel()` then `After`
  replaces the pending release, with `ignoreTimeScale: true` because audio runs in real time.
- `SceneFlow.cs` - `SceneLifetimes.DisposeAll()` right before the load, in a service that outlives the scenes.
  The demo builds it by hand from a child of `Lifetime.App`. It refuses a scene that is not in the Build Settings
  before it disposes anything; a load that can fail at runtime belongs in `LoadingScreenFlow`.
- `ErrorReporting.cs` - installs `LifetimeErrors.Handler` in every Play Mode session (Janitor resets it at the
  start) and forwards each error with its source, lifetime name, owner object and call site to the game's
  reporter, then calls the default handler so the console keeps its log.
- `SessionClock.cs` - two `Every` timers on the component, one of them with `ignoreTimeScale: true`.
- `CoinsLabel.cs` - `Wallet.CoinsChanged.Subscribe(handler, this)` in `Awake`: the component is the owner, so the
  subscription lives as long as the component, unlike the one in `ShopPopup`.
- `CountdownTracker.cs` - the owner is whichever side ends first: a scene-long tracker subscribes to the `Finished`
  event of each spawned `Countdown` and passes that countdown as the owner, not `this`, so no entry piles up in
  the tracker's lifetime.
- `ScoreReveal.cs` - `this.Run(ct => RevealAsync(score, ct))` from a method that is called once per object, with a
  parameter and no area: it ends with the object (or with a cancel of its scene). A second call would start a
  second task; `Countdown` shows the restart form.
- `PreviewRenderer.cs` - `OnCancel(state, static callback)` on the active lifetime releases a temporary
  `RenderTexture` and clears the references to it; `Render()` takes a new texture after any end of the lifetime,
  so a `Cancel()` never leaves a released texture in use.
- `ModalBlocker.cs` - `disposable.AddTo(lifetime)`: an input block that is disposed when the object is deactivated.
- `CombatEffect.cs` - a component placed in a category with `GetLifetime(category)`, one `Run` task, an `After`
  that ends the effect with a cancel, and the single `OnCancel` action that removes it, both with a state and a
  static callback.
- `HintPulse.cs` - keeps the `LifetimeRegistration` of one coroutine and cancels only that.
- `LoadingScreenFlow.cs` - a load behind a loading screen: `allowSceneActivation = false`, wait for 0.9, then
  `SceneLifetimes.DisposeAll()` right before activation. A `finally` releases the held load and hides the screen
  on a cancel or a fault, and a second call while one load runs is ignored. A started load cannot be aborted, so
  a cancel lets it finish.
- `AdditiveSceneFlow.cs` - `SceneLifetimes.Dispose(scene)` before `UnloadSceneAsync` (after checking that Unity
  will accept the unload), and `SceneLifetimes.Get(scene).Cancel()` to stop a scene's work and keep the scene.
- `BackgroundWork.cs` - a `Run` task in a child area that switches to the thread pool, checks the token in its
  loop and switches back with the token before touching a Unity object; a second call replaces the first.
- `PreviewRenderer`, `LoadingScreenFlow`, `AdditiveSceneFlow` and `BackgroundWork` are not wired into the demo:
  they need a camera, extra scenes in the Build Settings or a long-running job.
- `Support/` - stand-ins for the game's own code (`OfferService`, `CoinPool`, `IAdsSdk`). `CoinPool.Release` ignores
  a coin the pool already holds, and `Rent` skips a coin that was destroyed while pooled.
- `Demo/` - the bootstrap and the uGUI builder. The demo's own button subscriptions use `Subscribe(handler, this)`
  too, and nothing is removed by hand.
- `InboxPanel.cs` (with `Support/InboxService.cs`) - restartable work that also stops on `SetActive(false)`: a
  spinner coroutine, a refresh button and an owned event on the active lifetime, and the load task on an area
  under it, created lazily with `_load ??= shown.CreateChild("Load")`. The refresh handler does `Cancel()` then
  `Run`, so only the load restarts. Not wired into the demo.
- `SceneNameFlow.cs` - the scene call for a scene name: `Application.CanStreamedLevelBeLoaded(sceneName)` before
  anything is disposed, then `SceneLifetimes.DisposeAll()` right before the load, as an async method and as a
  synchronous one (`SceneManager.LoadScene`). Same ownership as `SceneFlow`. Not wired into the demo.
- `GemPickup.cs` (with `Support/GemPool.cs`) - a second pooled shape: the flight task writes the end position and
  deactivates the object itself, with no separate timer; `OnEnable` returns early until the per-use data is set,
  which is what an object instantiated from an active prefab needs; one `OnCancel` returns it to the pool once.
  Not wired into the demo. receive their views through `Initialize(...)` because the demo builds its UI in code; in
a scene you would use `[SerializeField]` fields. Components that read them in `Awake` or `OnEnable` are created
inactive, initialized, and then activated.
