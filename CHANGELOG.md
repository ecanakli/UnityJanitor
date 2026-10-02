# Changelog

All notable changes to this package are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the package uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.1.0] - 2026-10-02

### Added

- **Lifetimes and the tree.** `Lifetime` owns work and forms a tree under `Lifetime.App`. The tree
  lasts for one play session: it ends when the application quits or the editor returns to Edit
  Mode, and in the editor a script recompile during Play Mode starts a new session with one warning.
  `CreateChild(name)` creates an area under any lifetime; `Name`, `IsDisposed` and `Token` describe
  it. Subscriptions, disposables, coroutines and cleanup actions return a `LifetimeRegistration`
  (`IsActive`, `Cancel()`) that ends that one item. `OnCancel` registers custom cleanup with zero,
  one or two state values and an optional `isFinished` probe.
- **Cancel and dispose.** `Cancel()` ends the current generation of a lifetime and of every
  descendant and leaves them usable; `Dispose()` ends an area for good. Tokens are cancelled before
  items are ended, items end deepest lifetime first and newest first, and neither call throws.
  Lifetimes the package owns ignore `Dispose()`, and a call from another thread is deferred to the
  main thread.
- **Tasks and timers.** `Run`, `After` and `Every` on `Lifetime` and on `MonoBehaviour`, each with a
  `TState` overload that allocates nothing with a static delegate, and `ignoreTimeScale` on the
  timers. Work receives the token of the lifetime's current generation; cancellation is silent.
  `After` and `Every` reject NaN, infinity and more than 900000000000 seconds.
- **Owned events.** `OwnedEvent`, `OwnedEvent<T>`, `OwnedEvent<T1, T2>` and `OwnedEvent<T1, T2, T3>`
  with `Subscribe(handler, owner)`, `Invoke` and `SubscriberCount`, plus the subscribe-only views
  `IOwnedEvent` to `IOwnedEvent<T1, T2, T3>`. Handlers run in subscription order, each isolated; a
  handler whose owner ended is skipped, also when it ended earlier in the same invoke.
- **Paired, UnityEvent and IDisposable subscriptions.** `Subscribe(add, remove, handler)` for events
  the game does not own, in five forms on `Lifetime` and on `MonoBehaviour`.
  `Subscribe(handler, owner)` on `UnityEvent` with zero to four arguments. `AddTo(owner)` for any
  `IDisposable`, with a `Lifetime`, a `Component` or a `GameObject` as the owner. A repeated
  subscription of the same handler for the same owner is ignored; for the paired form the `add` and
  `remove` delegates have to be equal as well.
- **Component, active and scene lifetimes.** `GetLifetime()` on a `MonoBehaviour` or a `GameObject`
  is disposed when its owner is destroyed. `GetActiveLifetime()` is cancelled on every deactivation
  of the GameObject, through the `ActiveLifetimeTrigger` component, which is hidden in the
  Inspector; the active lifetime and the areas under it refuse work while the GameObject is
  inactive. `GetLifetime(parent)` and `GetActiveLifetime(parent)` place the lifetime in a category.
  `SceneLifetimes.Get`, `Dispose` and `DisposeAll` give each scene a lifetime and end its work
  before the scene is unloaded, including the work of objects that were moved into the scene.
  Disposing a scene lifetime is final.
- **Coroutines.** `lifetime.StartCoroutine(host, routine)` binds a coroutine to any lifetime and
  returns its registration. A missing, inactive or destroyed host is refused before Unity logs an
  error.
- **Error routing.** Everything the package catches goes to `LifetimeErrors.Handler` as a
  `LifetimeErrorHandler` call with a `LifetimeErrorContext` (`LifetimeErrorSource`, owner, lifetime
  name, member and line). The default handler logs a `LifetimeWorkException`.
- **DOTween integration.** `Ecanakli.Janitor.DOTween` adds `tween.AddTo(owner, TweenCancelMode)`
  with `TweenCancelMode.Kill` and `TweenCancelMode.Complete`, and `AwaitCompletionAsync` with a
  `Lifetime` or a `CancellationToken`, which ends in a cancellation when the tween is killed or its
  lifetime ends. Tween callbacks have to be set before the await. A registered tween is made
  non-recyclable, so it is its own handle. Activated by the `DOTWEEN` define or by a UPM DOTween
  install.
- **Zenject integration.** `Ecanakli.Janitor.DependencyInjection.Zenject` with
  `LifetimeInstaller.Install(container)`, which binds `Lifetime` per injectee and, in a
  SceneContext, disposes the scene lifetime before the container's other disposables, and
  `SignalBus.Subscribe<TSignal>(handler, owner)`. `Install` belongs in the ProjectContext, in every
  SceneContext and in every GameObjectContext whose plain services inject a `Lifetime`. Activated
  by `versionDefines` for a UPM Extenject 9.0.0 or newer, or by the
  `Tools/Janitor/Zenject Integration` menu toggle for a Zenject copy under `Assets/`.
- **The Janitor window and diagnostics.** `Window > Analysis > Janitor` shows the live lifetime
  tree with its entries and call sites, a details pane with a Cancel button, the recorded warnings
  with links to their documentation, and the recently disposed lifetimes. Sixteen stable IDs,
  `JANITOR101` to `JANITOR116`, are listed in `DiagnosticIds`: a task that outlives its generation,
  a tween that cannot be killed, an orphaned lifetime, a scene unloaded without the scene call, a
  duplicate subscription, growth (256 entries or 64 child areas on one lifetime), an ignored
  `Dispose()`, work refused on an inactive object, work refused on a disposed scene, a coroutine
  that was not started, a stop from another thread, a missing Zenject install, a parent mismatch, a
  re-homed lifetime, event re-entrancy, and work that `OnEnable` registers again on a lifetime that
  does not follow activation. `LifetimeDiagnostics.Report` lets another assembly record its own.
  The window and the recording exist only in the editor.
- **Samples.** Basic Usage (core only), DOTween Usage and Zenject Usage. Each builds its UI in code
  and has its own README.
- **Documentation.** `README.md`, the guides under `Documentation~/` (concepts, stopping work, tasks
  and errors, events, organizing work, components and scenes, coroutines, pooling, DOTween, Zenject,
  diagnostics, troubleshooting, threading, performance, limitations, migration, an FAQ and an API
  reference with every public member as a signature), the architecture notes with five decision
  records, and `llms.txt` with `llms-full.txt` for AI tools.
