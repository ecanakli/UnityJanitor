# Diagnostics

[Back to index](index.md)

The package records what every lifetime owns while the game runs in the editor, and shows it in one
window: `Window > Analysis > Janitor`. This page is a tour of that window, the settings behind it,
the sixteen diagnostic IDs, and the hook that lets other packages report into it.

The window, the recording behind it and the reporting hook exist **in the editor only**. None of
that is compiled into a player build; [Performance](Performance.md) says exactly what that means.
Two things on this page reach further than the editor: the console warnings of nine IDs are also
compiled into development builds, and JANITOR115 is routed to the error handler in every build.
[The diagnostic IDs](#the-diagnostic-ids) lists the scope of each ID.

This page describes what the window's code builds. It has no screenshots.

---

## What the window is for

Three questions come up when cleanup is automatic:

- **Who owns this work right now?** The tree shows every live lifetime and how many tasks, tweens,
  coroutines and subscriptions each one holds.
- **Did it really stop?** Deactivate a popup or cancel an area and watch its row drop to zero
  entries. A task that kept running after its lifetime ended is listed as outlived.
- **Did I misuse something?** The Warnings tab lists every diagnostic the package raised, each with
  a button that opens its section in [Troubleshooting](Troubleshooting.md).

The window reads the live tree four times per second while the editor is in Play Mode.

---

## The parts of the window

From top to bottom the window has a toolbar, the lifetime tree, and a tabbed pane with three tabs:
**Details**, **Warnings** and **Recently disposed**. The tree stays visible whichever tab is open.

### The toolbar

| Control | What it does |
|---|---|
| **Tracking** | The master switch of the recording. Off records nothing: no counters, no frames, no stack traces, no warnings, no disposed history. The tree is replaced by a one-line message while it is off. Console warnings are not affected. |
| **Stack traces** | Captures a stack trace for every registration made while it is on. It costs an allocation per registration, so it is off by default. It applies to entries registered afterwards, not to the ones already there. |
| **Pause** | Freezes the window so a row can be read. Recording continues underneath. Pause is cleared when Play Mode starts. |
| **Overrun frames** | A number from 1 to 3600, 3 by default. A task that is still running more than this many frames after its generation ended is reported as [JANITOR101](Troubleshooting.md#janitor101). The minimum is 1, because a task that honours its token needs a frame to notice the cancel. |
| Filter field | Filters the tree by name, kind or state, case-insensitive. A matching row stays visible together with its ancestors. |

### The tree

One row per live lifetime, nested the way the lifetimes are nested: `App` at the root, scene
lifetimes under it, object lifetimes and areas under those. A lifetime that is disposed leaves the
tree and moves to the Recently disposed tab.

| Column | Meaning |
|---|---|
| **Name** | The lifetime's display name. See the naming rules below. |
| **Kind** | `App`, `Scene`, `Component`, `GameObject`, `Active` or `Area`. |
| **State** | `Active` in practice; `Cancelling` and `Disposing` exist only while a teardown is running. The suffix `(orphan)` marks an object lifetime whose owner was destroyed while the lifetime is still alive; see [JANITOR103](Troubleshooting.md#janitor103). |
| **Generation** | How many times this lifetime's generation has ended. It goes up by one with every `Cancel()` that reaches it. |
| **Tasks** | Live `Run`, `After` and `Every` entries. |
| **Tweens** | Live tweens registered with `AddTo` or awaited through the lifetime. A Sequence registered by its root counts as one. |
| **Coroutines** | Live lifetime-bound coroutines. |
| **Subscriptions** | Live `OwnedEvent`, UnityEvent, SignalBus and paired `Subscribe` subscriptions. |
| **Other** | Everything else: `IDisposable` items and `OnCancel` actions. |
| **Children** | The number of direct children. |
| **Age (frames)** | Frames since the lifetime was created. |

The five entry columns count the lifetime's **own** entries, not those of its children.

A tween that finished by itself and a coroutine that Unity stopped are left out of the counts and of
the entry list. Their entries stay in the lifetime until its next sweep
([Performance](Performance.md#bounded-memory)), but they are not live work, so the window does not
show them.

How a row gets its name:

- `App` is the root.
- A scene lifetime has the scene's name. A scene that was never saved has none; its row is called
  `Untitled`, as in the editor's Hierarchy.
- A component lifetime has the component's type name, such as `ShopPopup`.
- A GameObject lifetime and an active lifetime have the GameObject's name.
- An area has the name passed to `CreateChild("Popups")`. An unnamed area shows the place it was
  created, as `Member:Line`, for example `Awake:22`.
- A lifetime that Zenject injected into a plain class is an ordinary area (kind `Area`) named after
  that class.

Clicking a column header sorts the children of every parent by that column; the hierarchy itself is
kept. A parent is expanded automatically the first time it has children. After that, a row you
collapse stays collapsed.

### The Details tab

Select a row in the tree. The Details tab then shows:

- **A header:** `Name (Kind, State), generation N`.
- **A summary line:** how many items were registered on the lifetime in total, how many
  registrations it refused (with the call site of the first one), how many times it was cancelled,
  and the frame it was created in.
- **Ping owner.** Highlights the owning Unity object in the editor. It is enabled only for a
  lifetime that has a live owner object: component, GameObject and active lifetimes.
- **Cancel.** Calls `Cancel()` on the selected lifetime, which also cancels its descendants. It is a
  debugging aid for answering "what would stop if this were cancelled". Cancelling `App` asks for
  confirmation first, because it stops the whole session's work.
- **The entry list,** newest first: one row per item the lifetime currently holds.

| Entry column | Meaning |
|---|---|
| **Item** | What the item is. See the labels below. |
| **Registered at** | The call site of the registration, as `Member:Line`. |
| **Generation** | The generation the item belongs to. |
| **Age (frames)** | Frames since it was registered. `-` when it was registered while Tracking was off. |
| **Status** | `Live`, or `Outlived` for a task whose generation has ended while the task is still running. |

The item labels:

| Label | Registered by |
|---|---|
| `Task.Run`, `Task.After`, `Task.Every` | `Run`, `After`, `Every` |
| `Tween` | `tween.AddTo(...)` or `tween.AwaitCompletionAsync(lifetime)` |
| `Coroutine` | `lifetime.StartCoroutine(host, routine)` |
| `OwnedEvent<Int32> "CoinsChanged"` | `ownedEvent.Subscribe(handler, owner)`; the argument types, then the event's name if it has one |
| `UnityEvent`, `UnityEvent<Single>` | `unityEvent.Subscribe(handler, owner)` |
| `Signal<StoreRefreshedSignal>`, `Signal` | `signalBus.Subscribe<TSignal>(handler, owner)`; the signal type when the handler takes the signal, plain `Signal` when it takes no argument |
| `Paired<Action>`, `Paired<Action<String>>` | The paired `Subscribe(add, remove, handler)`, and nothing else; the handler's delegate type |
| `IDisposable Timer` | `disposable.AddTo(...)`; the item's type |
| `OnCancel`, `OnCancel<CoinPickup>`, `OnCancel<Action>` | `OnCancel(...)`; the state types. A state that is itself a delegate is still an `OnCancel` entry and counts under Other, not under Subscriptions |

Select an entry to see its stack trace below the list. If none was captured, a hint says to turn on
Stack traces, which applies to entries registered afterwards.

Two things about call sites:

- **`Member:Line` is where the registration call was written.** If every subscription goes through
  one helper method of yours, every row shows that helper. Subscribe at the place that owns the
  subscription and the rows tell them apart.
- **The "refused" count covers every kind of registration:** `Run`, `After`, `Every`, every
  `Subscribe`, `StartCoroutine`, `OnCancel` and `AddTo`. A call is counted when the lifetime turned
  it away because it was ending, disposed, or belongs to a GameObject that is inactive (an active
  lifetime or an area under one). Two cases are left out on purpose. A coroutine whose host is
  null, destroyed or inactive is reported as [JANITOR110](Troubleshooting.md#janitor110) instead,
  because the host failed and the lifetime did not refuse anything. And a paired `Subscribe` whose
  lifetime ended inside its own `add` call is not counted.

An **outlived** task has no entry any more; its lifetime ended and let go of it. The row is there to
show that the code behind it is still running. Its age counts from the moment the generation ended.

### The Warnings tab

One row per recorded diagnostic, newest first. The tab's title shows how many there are.

| Column | Meaning |
|---|---|
| **ID** | The diagnostic ID, `JANITOR101` to `JANITOR116`, or a custom ID reported by another package. |
| **Title** | The one-line title of the ID. |
| **Lifetime** | The lifetime it is attached to, followed by `@ Member:Line` when the call site is known. `-` when it is not attached to a lifetime. |
| **Message** | The message, on one line. The tooltip has the full text. |
| **Count** | `x3` when the same diagnostic was raised three times. Empty for one. |
| **First seen** | The frame of the first occurrence. |
| **Docs** | A button that opens the ID's section of the troubleshooting guide in the browser. |

Behaviour worth knowing:

- **Repeats are merged.** The same ID with the same lifetime and the same message is one row with a
  counter.
- **Selecting a row pings the object the warning is about** in the editor, when the warning carries
  one and it still exists, **and selects its lifetime in the tree,** if that lifetime is still
  alive.
- **The Docs button points at the installed version.** The URL carries the release tag of the
  installed package (`v0.1.1` for this version), so the text you read matches the code you run:

  ```text
  https://github.com/ecanakli/UnityJanitor/blob/v0.1.1/Documentation~/Troubleshooting.md#janitor101
  ```

  If the version cannot be read it falls back to the `main` branch. A custom ID has no documentation
  and its button is disabled.
- **The list holds 256 distinct rows.** Beyond that the oldest is dropped.
- **Warnings are cleared when the next play session starts.** A script recompile during Play Mode
  starts a new session too, at the first Janitor call after it; see
  [Outside Play Mode](#outside-play-mode).

### The Recently disposed tab

The last 200 lifetimes that were disposed, newest first. This is where a row goes when it leaves the
tree, so "it was there a second ago" can still be answered.

| Column | Meaning |
|---|---|
| **Name**, **Kind** | As in the tree. |
| **Generation** | The last generation it lived in. |
| **Cancels** | How many times it was cancelled before it was disposed. |
| **Registered** | How many items were registered on it in total. |
| **Refused** | How many registrations it refused. |
| **Lived (frames)** | Frames between creation and disposal. |
| **Disposed (frame)** | The frame it was disposed in. |
| **Session** | A counter of play sessions, so rows from different runs can be told apart. |

Disposals that are part of the application or Play Mode shutting down are not recorded. Otherwise
every exit would push 200 shutdown rows over the history you wanted to keep. The list is kept across
play sessions.

---

## Outside Play Mode

No lifetime exists outside Play Mode, so there is no tree. The window then shows:

- in place of the tree: `Not in Play Mode. Enter Play Mode to see live lifetimes; the tabs below
  keep the last session's history.`
- on the Details tab: `Details are available in Play Mode.`
- on the Warnings and Recently disposed tabs: what the last session recorded.

The history survives the script reload that the editor performs around Play Mode. Rows restored that
way carry text and numbers only: they no longer point at a lifetime or an object, so selecting one
does not jump anywhere.

A script recompile in the middle of Play Mode is a different case. The reload discards every
lifetime, and the first Janitor call after it starts a new session with a new tree and logs one
console warning; [Troubleshooting](Troubleshooting.md#scripts-were-recompiled-during-play-mode)
has the details. Starting that session clears the Warnings tab like any other session start. The
Recently disposed tab is kept.

Other one-line messages you may see instead of a list:

| Message | When |
|---|---|
| `Tracking is off. Turn it on in the toolbar to record lifetimes.` | Play Mode, Tracking off |
| `No lifetimes yet. They appear as soon as code creates one.` | Play Mode, nothing created yet |
| `No lifetime matches the filter.` | The filter hides every row |
| `Select a lifetime in the tree to see its entries.` | Details tab, nothing selected |
| `The selected lifetime is no longer alive.` | Details tab, the selected lifetime was disposed |
| `No warnings recorded.` | Warnings tab, empty |

If a refresh of the window itself fails, a line at the top says
`Janitor window error: ... The window keeps running.` and the exception is logged once. A fault
inside the recording is contained the same way: it is logged once per play session and never reaches
the `Cancel`, `Dispose` or registration call that triggered it.

---

## Settings

The three toolbar settings are stored in `EditorPrefs`. They belong to your editor on your machine;
they are not part of the project and are not shared through version control.

| EditorPrefs key | Type | Default | Toolbar control |
|---|---|---|---|
| `Ecanakli.Janitor.Tracking` | bool | `true` | Tracking |
| `Ecanakli.Janitor.StackTraces` | bool | `false` | Stack traces |
| `Ecanakli.Janitor.OverrunFrames` | int, clamped to 1..3600 | `3` | Overrun frames |

Pause, the filter, the sort column and the selected tab are window state and are not stored.

Recording does not depend on the window being open. With Tracking on (the default), the package
records in every editor play session, so the Warnings and Recently disposed tabs have content
whenever you open the window later.

---

## When a diagnostic is detected

Most diagnostics are raised at the moment of the mistake: the duplicate `Subscribe` call, the
`Dispose()` on a package-owned lifetime, the registration on an inactive object. Two are found by
looking afterwards:

- **JANITOR101 (task overrun)** is found by a scan. When a generation ends while one of its tasks is
  still running, the package starts checking that task once per frame and reports it when it is more
  than "Overrun frames" past the end. A task that finishes in time is forgotten. The scan runs only
  while there is something to check, and it does not need the window.
- **JANITOR103 (orphan lifetime)** is found only while the window refreshes its tree in Play Mode.
  With the window closed it is not detected. It is recorded when two refreshes in different frames
  both see the orphan, about a quarter of a second apart, so an owner whose destruction the
  lifetime learns about one frame late is not reported.

One more is raised at the moment of the mistake but needs the recording to recognise it:
**JANITOR116 (work registered again in `OnEnable`)** compares a new registration with the entries
the lifetime already holds, so it exists only in the editor and only with Tracking on.

All three need Tracking to be on.

---

## Console messages

Some diagnostics are also written to the console. The format is always:

```text
[JANITOR107] Dispose() was ignored on the package-owned lifetime 'ShopPopup'. Use Cancel() instead. (see Troubleshooting#janitor107)
```

Console warnings are compiled into the editor and into development builds, and removed from release
builds. The exception is JANITOR115, which is routed to the error handler in every build. The
Tracking switch does not affect the console.

---

## The diagnostic IDs

The IDs are stable. Each links to its section in [Troubleshooting](Troubleshooting.md), which says
what triggers it, how to fix it and whether it is harmless.

| ID | Meaning | Console | Window |
|---|---|---|---|
| [JANITOR101](Troubleshooting.md#janitor101) | A `Run`, `After` or `Every` task is still running after its generation ended | No | Yes |
| [JANITOR102](Troubleshooting.md#janitor102) | A tween is still active right after it was killed, most likely because it is nested in a Sequence | No | Yes |
| [JANITOR103](Troubleshooting.md#janitor103) | The owner of a lifetime was destroyed but the lifetime is still alive | No | Yes |
| [JANITOR104](Troubleshooting.md#janitor104) | A scene went away without `SceneLifetimes.Dispose` or `DisposeAll` | Editor and development builds | Yes |
| [JANITOR105](Troubleshooting.md#janitor105) | A duplicate subscription was ignored | Editor and development builds | Yes |
| [JANITOR106](Troubleshooting.md#janitor106) | One lifetime holds more than 256 entries or 64 live child areas | No | Yes |
| [JANITOR107](Troubleshooting.md#janitor107) | `Dispose()` was ignored on a package-owned lifetime | Editor and development builds | Yes |
| [JANITOR108](Troubleshooting.md#janitor108) | A registration was refused because the active lifetime's GameObject is inactive; this includes registrations on areas under that lifetime | Editor and development builds | Yes |
| [JANITOR109](Troubleshooting.md#janitor109) | A registration was made, or an area was created, on a disposed scene lifetime while the scene is still loaded | Editor and development builds | Yes |
| [JANITOR110](Troubleshooting.md#janitor110) | A coroutine was not started because its host is null, destroyed or inactive | Editor and development builds | Yes |
| [JANITOR111](Troubleshooting.md#janitor111) | `Cancel` or `Dispose` was called off the main thread and deferred to it | Editor and development builds | Yes |
| [JANITOR112](Troubleshooting.md#janitor112) | Zenject: a service received a lifetime from an outer context, because its SceneContext or GameObjectContext never called `LifetimeInstaller.Install` | Editor and development builds | Yes |
| [JANITOR113](Troubleshooting.md#janitor113) | `GetLifetime(parent)` or `GetActiveLifetime(parent)` was ignored (the lifetime already has another parent, or was moved back under its scene), or the parent was disposed | Editor and development builds | Yes |
| [JANITOR114](Troubleshooting.md#janitor114) | Information: a category was disposed and an object lifetime placed in it was moved back under its scene | No | Yes, marked as information |
| [JANITOR115](Troubleshooting.md#janitor115) | An `OwnedEvent` was invoked more than 64 calls deep; the invoke was skipped | Every build, as a routed error | Yes |
| [JANITOR116](Troubleshooting.md#janitor116) | Work registered in `OnEnable` on a component or GameObject lifetime, which does not follow activation, was registered again by a later activation | No | Yes |

"Window: Yes" means the editor's Janitor window, with Tracking on.

Read by build, the table says:

- **In the editor** all sixteen exist: every ID has a row in the window, and ten of them also
  write to the console (nine as warnings, JANITOR115 as a routed error).
- **In a development build** there is no window. The nine console warnings (JANITOR104, 105, 107
  to 113) and JANITOR115 remain. JANITOR101, 102, 103, 106, 114 and 116 are not reported at all.
- **In a release build** only JANITOR115 remains.

---

## Reporting from your own package

Integration assemblies report into the same window through one public method. The DOTween and
Zenject integrations of this package use nothing else.

<!-- signature -->
```csharp
public static partial class LifetimeDiagnostics
{
    [Conditional("UNITY_EDITOR")]
    public static void Report(Lifetime lifetime, string diagnosticId, string message, UnityEngine.Object context = null);
}

public static class DiagnosticIds
{
    public const string TaskOverrun = "JANITOR101";
    public const string UnkillableTween = "JANITOR102";
    public const string OrphanLifetime = "JANITOR103";
    public const string SceneDisposedLate = "JANITOR104";
    public const string DuplicateSubscription = "JANITOR105";
    public const string Growth = "JANITOR106";
    public const string DisposeIgnored = "JANITOR107";
    public const string RegistrationOnInactiveObject = "JANITOR108";
    public const string RegistrationOnDisposedScene = "JANITOR109";
    public const string CoroutineNotStarted = "JANITOR110";
    public const string Marshalled = "JANITOR111";
    public const string MissingSceneInstall = "JANITOR112";
    public const string ParentMismatch = "JANITOR113";
    public const string Rehomed = "JANITOR114";
    public const string DepthExceeded = "JANITOR115";
    public const string RepeatedOnEnable = "JANITOR116";

    public static string GetTitle(string diagnosticId);
    public static string GetAnchor(string diagnosticId);
}
```

How `Report` behaves:

- **It records a row in the Warnings tab and nothing else.** It does not write to the console. Keep
  your own `Debug.LogWarning` if you want one.
- **It costs nothing in a player.** The method is conditional on `UNITY_EDITOR`, so the compiler
  removes the call and the evaluation of its arguments from player builds.
- **`lifetime`** is the lifetime the row is attached to. Pass `null` when you do not know it.
- **Inside a cancel action it finds the lifetime for you.** If `Report` is called on the main thread
  while an item's cancel action is running, and `lifetime` is `null` or is the lifetime being ended,
  the row is attached to that lifetime and carries the call site where the item was registered. This
  is how the DOTween integration reports JANITOR102 against the `AddTo` line.
- **`diagnosticId`** is normally one of the `DiagnosticIds` constants. Any other non-empty string is
  accepted and shown with the title "Custom diagnostic" and a disabled Docs button. A `null` or
  empty ID is ignored.
- **`context`** is an optional Unity object kept with the row. Selecting the row in the Warnings tab
  pings it.
- **It is safe from any thread and never throws.** It does nothing while Tracking is off.

`GetTitle` returns the one-line title of a package ID and `GetAnchor` the path of its
troubleshooting section, for example `Documentation~/Troubleshooting.md#janitor101`. Both return
`null` for an ID that is not one of the package's.

## See also

- [Troubleshooting](Troubleshooting.md)
- [Performance](Performance.md)
- [Concepts](Concepts.md)
- [Tasks and errors](Tasks-and-Errors.md)
- [Limitations](Limitations.md)
