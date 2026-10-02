# DOTween Usage

Requires DOTween and the package's DOTween integration to be active (`Ecanakli.Janitor.DOTween`, gated behind
`DOTWEEN || ECANAKLI_JANITOR_DOTWEEN`). Also needs the uGUI package (`com.unity.ugui`, which includes TextMeshPro).
The sample calls only DOTween's core assembly, no `DOTweenModuleUI` extensions.

## Setup

1. Make sure the integration is active. A DOTween install from the Asset Store defines `DOTWEEN` through its own
   setup panel; a self-made UPM package named `com.demigiant.dotween` is picked up through `versionDefines`.
2. Import TextMeshPro's Essential Resources once: `Window > TextMeshPro > Import TMP Essential Resources`.
3. Open an empty scene (`File > New Scene`), create one empty GameObject and add the `DOTweenUsageDemo` component.
4. Add an Event System with `GameObject > UI > Event System`. The demo creates none, because the right input
   module depends on your project's input handling; it logs one warning when the scene has no Event System. If
   the Input System package is your only input handling, use the button the Event System's Inspector offers to
   replace the input module.
5. Press Play and open `Window > Analysis > Janitor`. The demo picks a landscape or a portrait layout from the shape of the Game view when Play starts, and stays centered at any size.

## What to do

- **Launch coins** rents pooled coins whose `DOMove` tween flies to the counter. A coin's row shows one tween, one
  timer task and one cleanup entry while it flies. However the flight ends (the timer, or a Cancel on the `Coin`
  row in the window), the coin goes back to the pool; afterwards the row stays with no entries.
- **Roll counter twice** starts a roll, and 0.2 s later a second one. `Complete` mode lands the first roll on
  its end value (the number jumps), and the second roll continues from there. Each press adds 200.
- **Reward: show** plays `RewardView` for about three seconds: a shake, a count-up, a two second wait, then the
  chest hides. **Reward: cancel** stops it, and the stage decides what you see. Cancel during the shake and it
  just ends. Cancel during the count-up and the label jumps to the final amount (the `catch` path). Cancel
  during the wait and the chest stays visible (the `SuppressCancellationThrow` path). Press **Reward: show**
  again while it plays: the first show stops and the second starts from the beginning, and the label never
  shows the first show's amount.
- **Outfit: apply twice** applies red, and 0.4 s later blue. The second call cancels the first before it shows
  anything, so only blue is shown and the Console gets one "Saved outfit 'blue'" line. **Outfit: reset profile**
  calls `ProfileResetService`, which calls `CancelApply()`. Press it within two seconds of an apply and the apply
  stops where it is: no save line appears.
- **Tutorial: show step** starts the arrow tween, the hint pulse and a listener on the Target button. Click
  Target and the step completes. The **Skip** button inside the card cancels the whole step. **Player moved**
  cancels only the pulse; the arrow keeps moving. **Abort** cancels the whole controller lifetime, so the Skip
  button stops working too; showing a step again still works, skipping does not.
- **Damage: pop numbers** punches five labels, all registered into the `Combat` category. **Damage: pop, then
  interrupt** cancels `Combat` 0.1 s later, in the middle of the punch, and the labels stay mid-pop.
- **Intro panel: show / hide** toggles `PanelIntro`. The Sequence it plays counts as a single tween in the
  window, because only the root is registered. Press Cancel on its row in the window during the intro: the
  Sequence completes (`TweenCancelMode.Complete`) and the panel lands on its end pose instead of freezing small.
- **Toast: show** pops `ToastView` in, waits, and pops it out. Press it again while a toast is showing: the first
  one stops where it is and the new message starts. The orange badge pulses for as long as the component exists,
  whichever toast is showing.

## What to look at

- `CoinPickup.cs` - a `SetActive` pooled object with a `DOMove` tween: `GetActiveLifetime()` in `OnEnable`, the tween,
  an `After` that only deactivates the coin, and one `OnCancel` action that returns it to the pool, so every end of
  the flight (the timer, `SetActive(false)`, an outside `Cancel()`) returns it exactly once.
- `CoinCounter.cs` - `TweenCancelMode.Complete`: a cancelled roll finishes instead of freezing.
- `RewardView.cs` - `AwaitCompletionAsync(ct)` on a tween, and the three ways to handle a cancel inside an async
  method: let `Run` end it silently, catch and rethrow to clean up, or branch on `SuppressCancellationThrow`.
  `Show` restarts in a child area (`Cancel()` then `Run`), and the count-up awaits with `cancelImmediately` so the
  cleanup runs inside `Cancel()`, before the restarted show resets the label.
- `CustomizationService.cs` - a plain class that owns a child area: `Cancel()` and then `Run` restarts the work,
  and `CancelApply()` lets another service stop it without seeing the area.
- `ProfileResetService.cs` - the other service; it only calls `CancelApply()`.
- `TutorialController.cs` - the three ways to cancel: one registration (`_hintPulse.Cancel()`), a child area
  (`_step.Cancel()`) and the whole lifetime (`GetLifetime().Cancel()`). The arrow uses `DOLocalMove` on the
  `RectTransform`, which is core DOTween.
- `GameplayLifetimes.cs`, `DamageNumbers.cs`, `CombatFlow.cs` - one class registers tweens into a category, another
  cancels it, and neither knows the other.
- `PanelIntro.cs` - a Sequence registered by its root only; the tweens nested in it follow the root. It uses
  `TweenCancelMode.Complete`, so a cut-short intro lands on its end pose.
- `ToastView.cs` - `tween.AwaitCompletionAsync(lifetime)`, the overload that registers the tween on a lifetime and
  awaits it, and `tween.AddTo(this)`, the component owner form, for a looping badge.
- `Support/` - stand-ins for the game's own code (`CoinPool`, `AvatarPreview`, `IOutfitRepository`, `TutorialStep`).
  `CoinPool.Release` ignores a coin the pool already holds, and `Rent` skips a coin that was destroyed while pooled.
- `Demo/` - the bootstrap and the uGUI builder. The demo's own button subscriptions use `Subscribe(handler, this)`
  too, and nothing is removed by hand.
- `GateOpener.cs` - an awaited tween that restarts and stops on deactivation: `Open()` runs on an area under the
  active lifetime, created lazily, and awaits a `DOLocalMoveY` with `AwaitCompletionAsync(ct)` before it enables a
  collider. Calling it twice leaves one tween, and it returns early while the object is inactive. Not wired into
  the demo.

The documented classes receive their views through `Initialize(...)` because the demo builds its UI in code; in
a scene you would use `[SerializeField]` fields. Components that read them in `Awake` or `OnEnable` are created
inactive, initialized, and then activated.
