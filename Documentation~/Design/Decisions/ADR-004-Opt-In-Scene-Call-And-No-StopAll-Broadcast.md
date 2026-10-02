# ADR-004: An opt-in scene call, and no StopAll broadcast

- **Status:** Accepted, implemented in 0.1.0
- **Related:** [Architecture section 7](../Architecture.md#7-the-unity-binding),
  [ADR-002](ADR-002-Reusable-Cancel-Versus-Terminal-Dispose.md),
  [Components and scenes](../../Components-and-Scenes.md), [Stopping work](../../Stopping-Work.md)

## Context

This record covers two decisions that answer the same question from two sides: *who tells the work
to stop, and in what order does it stop?*

### The scene side

When a scene is unloaded, or replaced by a Single-mode load, Unity destroys its objects. Two engine
facts shape everything (both verified on Unity 6000.3):

- The order of events is `OnDisable` and `OnDestroy` for the objects, then
  `SceneManager.sceneUnloaded`, then `activeSceneChanged` and `sceneLoaded`. **No engine event is
  raised before the destruction starts.**
- The objects are destroyed one after another, in an order the game does not control.

So every hook the package could attach to by itself fires too late. A cleanup that runs per object,
as each object is destroyed, leaves a window in which the work of objects that are still alive runs
against objects that are already gone: a task of one object writes to a view that was destroyed a
moment earlier, an event reaches a listener whose dependencies no longer exist. Plain C# services
that belong to the scene have no destroy callback at all.

### The "stop everything" side

Games need to stop whole groups of work at once: leaving gameplay, resetting a run, interrupting
combat. The common way to build that is a broadcast: a `StopAll` event or signal that every class
subscribes to and reacts to by cleaning itself up.

A broadcast has the same shape as the bug the package exists to remove. Every class has to remember
to subscribe, and to react completely, and a class that forgets keeps running with no error. The
order in which subscribers react is the order in which they happened to subscribe, and nothing
guarantees that all of them have finished when the broadcast returns.

## Decision

### 1. One explicit line before the loader

The game calls the package right before it starts a load or an unload:

<!-- signature -->
```csharp
public static class SceneLifetimes
{
    public static Lifetime Get(Scene scene);
    public static void Dispose(Scene scene);
    public static void DisposeAll();
}
```

- `SceneLifetimes.DisposeAll()` before a Single-mode load, `SceneLifetimes.Dispose(scene)` before
  unloading one scene.
- It works with any loader (`SceneManager`, Addressables, a DI scene loader), because the only
  requirement is that it runs first. The package wraps none of them and depends on none of them.
- It disposes the scene lifetimes last loaded scene first. Within a scene all tokens are cancelled
  first and items are then ended deepest lifetime first, while every object still exists.
- It reaches the objects that are in the scene at that moment, including objects that were moved
  into it after their lifetimes were created. Objects that moved away are left alone.
- `Lifetime.App` and DontDestroyOnLoad objects are never touched.
- Disposal is terminal. Work registered on that scene's objects afterwards is ended immediately.

As it ships in the Basic Usage sample:

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

The class that makes the call has to outlive the scenes it unloads. Here its lifetime is a child of
`Lifetime.App`; a lifetime under the scene would be disposed by the call, and the task that is
awaiting the load would be cancelled with it.

Because disposal is terminal, the sample checks what it can before the call: a scene that is not in
the Build Settings is refused and nothing is disposed. A load that can still fail at run time should
not be preceded by the call at all. It should start with the scene activation held, and dispose
right before the activation, when the load itself can no longer fail; `LoadingScreenFlow` in the
same sample does that.

### 2. The call is opt-in, and skipping it degrades without breaking

Nothing fails when the line is missing. Three fallbacks apply, in this order:

1. Every object lifetime is disposed by its own destroy token when Unity destroys the object. This
   is the engine's order, but it still needs no `OnDestroy` in user code.
2. With the Zenject integration installed in a SceneContext, a disposable that runs first among the
   container's disposables disposes the scene lifetime.
3. The package's `sceneUnloaded` handler disposes any scene lifetime that is still alive.

Fallbacks 2 and 3 report `JANITOR104` once per scene, in the editor and in development builds, so a
skipped call is visible. They do not report at application exit, where everything ends together.

### 3. No broadcast: a stop is a call on the tree

There is no `StopAll` message and no API that every class has to listen to. Stopping is `Cancel()`
on a node of the lifetime tree, and the tree carries it to every descendant.

- The game's everyday "stop everything" is a **root area that the game creates**, with its
  categories as children. One handler cancels the root.
- `Lifetime.App.Cancel()` is the literal everything, including every listener registered on app,
  scene and object lifetimes.
- To stop a scene's work without unloading it: `SceneLifetimes.Get(scene).Cancel()`.
- A game event may *trigger* the stop. The handler of that event calls `Cancel()`; the tree executes
  it. No other class takes part.

As it ships in the Basic Usage sample:

<!-- source: Samples~/BasicUsage/GameplayRoot.cs -->
```csharp
/// <summary>The root area of the gameplay: it builds the categories and cancels everything under them.</summary>
public sealed class GameplayRoot : MonoBehaviour
{
    private Lifetime _gameplay;

    public GameplayLifetimes Lifetimes { get; private set; }

    private void Awake()
    {
        _gameplay = this.GetLifetime().CreateChild("Gameplay");   // disposed with this object or its scene
        Lifetimes = new GameplayLifetimes(_gameplay);
    }

    // A signal triggers, the tree executes: one handler, no broadcast.
    public void OnResetRequested() => _gameplay.Cancel();          // every area stops; each stays usable
}
```

## Alternatives considered

### Wrap the scene loaders

Rejected. A package-provided `LoadSceneAsync` could call the dispose itself, so nobody could forget
it.

**Concrete failure modes.** It ties the package to the API of every loader a game may use
(`SceneManager`, Addressables, a DI framework's scene loader), which means dependencies on packages
the core must not reference and a wrapper to maintain per loader version. And it does not remove the
forgettable step, it moves it: any direct call to the real loader, in game code or in another
package, bypasses the wrapper silently. One line that composes with every loader is a smaller
promise that can be kept.

### Hook `sceneUnloaded` or `activeSceneChanged` and make it automatic

Rejected as the mechanism, kept as the last fallback. Both events are raised after the objects were
destroyed. Cleanup at that point is cleanup of work that has already had the chance to run against
destroyed objects.

### Rely on each object's destroy token only

Rejected as the mechanism, kept as the first fallback. It gives the engine's destruction order,
which is the window described in the context, and it does nothing for plain C# services owned by the
scene.

### Use the DI container's scene disposal

Rejected as the mechanism, kept as the second fallback. It exists only for games that use that
container, and it runs while the scene context's object is being destroyed, at a point whose order
relative to the other objects of the scene is not guaranteed.

### Make the call mandatory

Rejected. The package cannot know that a load is about to happen, so it could only complain
afterwards, and an error after the fact is a warning with a worse name. A project that adopts the
package one class at a time also needs the degraded mode to be safe.

### A broadcast `StopAll` event or signal

Rejected. See the context: it depends on every class remembering to subscribe and to react, it has
no defined order, and it has no completion guarantee. It would also need a messaging mechanism,
which the package deliberately does not contain.

### A static "cancel everything registered" over a flat list

Rejected. Without a hierarchy there is no way to say "this part and not that part", so the call
would also stop services that must survive (a scene loader, audio, anything in DontDestroyOnLoad).
The fix for that is a key or a tag per registration, which is the keyed registry the package refuses
to have: keys are unchecked references, and a flat store has no owner.

### Asynchronous, graceful stopping

Rejected. Awaiting every task to wind down before the load continues needs an ordering and timeout
policy for work that does not cooperate, and it makes every `Cancel()` an operation that can hang.
Teardown is synchronous: when `Cancel()` or `DisposeAll()` returns, every token is cancelled and
every item has been ended. A task observes its cancelled token at its next await.

## Consequences

**Positive**

- A scene change has a defined order: all scene-owned work ends before any object is destroyed.
- The mechanism is independent of how scenes are loaded, and adds no dependency.
- The same call (`Cancel()`) stops work at every size, from one area to the whole app. A class does
  not have to do anything to be stoppable; registering its work was enough.
- When the call returns, every token is cancelled and every item has been ended. A task notices at
  its next await.

**Negative / accepted costs**

- **This is the one line in the package that a developer can forget.** Forgetting it does not break
  the game; it returns the scene change to the engine's destruction order. The reminder
  (`JANITOR104`) exists in the editor and in development builds only. A release build is silent.
- **The caller must not be owned by a scene it disposes.** A scene loader whose lifetime sits under
  the scene cancels its own load. The samples show the right ownership, and the Zenject sample needs
  a second installer in the ProjectContext for exactly this reason.
- **Timing is the game's responsibility.** Disposal is terminal, so calling it too early (at the
  start of a long background load, while the old scene is still on screen) stops the old scene's
  work for the whole load. Call it right before the old scene is replaced.
- **A load that fails after the call leaves a dead scene.** The old scene stays loaded, and its
  lifetime stays disposed: its objects can register no more work, and nothing revives it. The
  one-line form is therefore right only for loads that cannot fail once they were started; the
  held-activation flow is the pattern for the others. The package has no operation that brings a
  disposed scene lifetime back.
- **Work registered between the call and the actual unload is refused,** with `JANITOR109` (once per
  scene) while the scene is still loaded. This is intended, and it surprises code that reacts to the
  dispose by starting something.
- **The game has to design its root area.** `Lifetime.App.Cancel()` is rarely the right "stop
  everything", because it also removes listeners that should survive.
- **Only registered work is stopped.** A raw task, tween or subscription that bypassed the package
  is not part of any tree.

**Enforced by tests**

- `SceneLifetimeTests.DisposeAll_ThenASingleLoad_EndsTasksAndSubscriptionsBeforeAnyOnDestroyRuns`.
- `SceneLifetimeTests.SceneUnloaded_WithoutDispose_DisposesTheSceneLifetimeLateWithJanitor104`.
- `SceneLifetimeTests.DisposeAll_DisposesLoadedScenesLastLoadedFirst` and
  `DontDestroyOnLoad_ObjectsSurviveDisposeAllAndDispose`.
- `SceneLifetimeTests.Register_OnADisposedSceneLifetime_RaisesJanitor109AndTerminatesTheItem`.
- `SceneLifetimeTests.MoveGameObjectToScene_ThenDisposeOfTheTargetScene_EndsTheMovedObjectBeforeAnyDestroy`
  and `SetParent_ThenCancelOfTheTargetScene_CancelsTheMovedObjectAndKeepsItUsable`.
- `SceneDisposerTests.Destroy_OfTheSceneContext_EndsTheSceneLifetimeBeforeAnyServiceDisposes` and
  `Destroy_OfTheSceneContext_WhenTheOneLineCallWasSkipped_LogsJanitor104OnceAndTheUnloadAddsNothing`.
- `TweenSceneTests.DisposeAll_KillsTheTweenOfAnObjectInTheSceneWhileTheObjectIsStillAlive`.
- `LifetimeAppTests.Cancel_OnApp_CancelsEveryDescendantWithoutDisposingAnything`.
- The engine order that the decision rests on is pinned by the tests under `Tests/Runtime/Probes/`.

## See also

- [Components and scenes](../../Components-and-Scenes.md)
- [Stopping work](../../Stopping-Work.md)
- [Zenject](../../Zenject.md)
- [Troubleshooting: JANITOR104](../../Troubleshooting.md#janitor104)
