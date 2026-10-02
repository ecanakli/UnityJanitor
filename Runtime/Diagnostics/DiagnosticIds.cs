using System;
using System.Collections.Generic;

namespace Ecanakli.Janitor
{
    /// <summary>
    /// The stable diagnostic IDs (<c>JANITOR101</c> to <c>JANITOR116</c>) with their one-line titles and the anchors of
    /// their sections in <c>Documentation~/Troubleshooting.md</c>. Console messages are formatted
    /// <c>[JANITOR1xx] message (see Troubleshooting#janitor1xx)</c>. Integration assemblies pass these IDs to
    /// <see cref="LifetimeDiagnostics.Report"/>.
    /// </summary>
    public static class DiagnosticIds
    {
        /// <summary>JANITOR101: a Run, After or Every task is still running after its generation ended.</summary>
        public const string TaskOverrun = "JANITOR101";

        /// <summary>JANITOR102: a tween is still active after termination killed it, most likely because it is nested in a Sequence.</summary>
        public const string UnkillableTween = "JANITOR102";

        /// <summary>JANITOR103: the owner of a lifetime was destroyed but the lifetime is still alive.</summary>
        public const string OrphanLifetime = "JANITOR103";

        /// <summary>JANITOR104: a scene was unloaded without SceneLifetimes.Dispose or DisposeAll.</summary>
        public const string SceneDisposedLate = "JANITOR104";

        /// <summary>JANITOR105: a duplicate subscription was ignored.</summary>
        public const string DuplicateSubscription = "JANITOR105";

        /// <summary>JANITOR106: one lifetime holds more than 256 entries or 64 live child areas.</summary>
        public const string Growth = "JANITOR106";

        /// <summary>JANITOR107: Dispose was ignored on a package-owned lifetime.</summary>
        public const string DisposeIgnored = "JANITOR107";

        /// <summary>JANITOR108: a registration was refused because the active lifetime's GameObject is inactive.</summary>
        public const string RegistrationOnInactiveObject = "JANITOR108";

        /// <summary>JANITOR109: a registration was made on a disposed scene lifetime while the scene is still loaded.</summary>
        public const string RegistrationOnDisposedScene = "JANITOR109";

        /// <summary>JANITOR110: a coroutine was not started because its host is null, destroyed or inactive.</summary>
        public const string CoroutineNotStarted = "JANITOR110";

        /// <summary>JANITOR111: Cancel or Dispose was called off the main thread and marshalled.</summary>
        public const string Marshalled = "JANITOR111";

        /// <summary>JANITOR112: Zenject gave a service the lifetime of an outer context because its SceneContext or GameObjectContext never installed the lifetime binding.</summary>
        public const string MissingSceneInstall = "JANITOR112";

        /// <summary>JANITOR113: a lifetime was requested under another parent than the one it already has, or under a disposed parent.</summary>
        public const string ParentMismatch = "JANITOR113";

        /// <summary>JANITOR114: a category was disposed while a placed object lifetime lived, so the lifetime was re-homed (information).</summary>
        public const string Rehomed = "JANITOR114";

        /// <summary>JANITOR115: OwnedEvent re-entrancy depth exceeded 64 and the invoke was skipped.</summary>
        public const string DepthExceeded = "JANITOR115";

        /// <summary>JANITOR116: work registered in OnEnable on a component or GameObject lifetime was registered again by a later activation; it belongs on the active lifetime.</summary>
        public const string RepeatedOnEnable = "JANITOR116";

        // Index i of each array describes JANITOR(101 + i).
        private static readonly string[] AllIds =
        {
            TaskOverrun, UnkillableTween, OrphanLifetime, SceneDisposedLate, DuplicateSubscription,
            Growth, DisposeIgnored, RegistrationOnInactiveObject, RegistrationOnDisposedScene, CoroutineNotStarted,
            Marshalled, MissingSceneInstall, ParentMismatch, Rehomed, DepthExceeded,
            RepeatedOnEnable,
        };

        private static readonly string[] Titles =
        {
            "Task overrun: a Run, After or Every task is still running after its generation ended",
            "Unkillable tween: still active after termination killed it, most likely nested in a Sequence",
            "Orphan lifetime: its owner was destroyed but the lifetime is still alive",
            "A scene was unloaded without SceneLifetimes.Dispose or DisposeAll",
            "A duplicate subscription was ignored",
            "Growth: more than 256 entries or 64 live child areas on one lifetime",
            "Dispose() was ignored on a package-owned lifetime; use Cancel()",
            "Registration refused: the active lifetime's GameObject is inactive",
            "Registration on a disposed scene lifetime while the scene is still loaded",
            "Coroutine not started: the host is null, destroyed or inactive",
            "Cancel or Dispose was called off the main thread and marshalled",
            "Zenject: a service received a lifetime from an outer context, because its SceneContext or GameObjectContext never called LifetimeInstaller.Install",
            "Parent mismatch: GetLifetime(parent) or GetActiveLifetime(parent) was ignored or the parent was disposed",
            "Re-homed: a category was disposed while a placed object lifetime lives",
            "OwnedEvent re-entrancy depth exceeded 64; the invoke was skipped",
            "Work registered in OnEnable on a lifetime that does not follow activation was registered again",
        };

        private static readonly string[] Anchors =
        {
            "Documentation~/Troubleshooting.md#janitor101",
            "Documentation~/Troubleshooting.md#janitor102",
            "Documentation~/Troubleshooting.md#janitor103",
            "Documentation~/Troubleshooting.md#janitor104",
            "Documentation~/Troubleshooting.md#janitor105",
            "Documentation~/Troubleshooting.md#janitor106",
            "Documentation~/Troubleshooting.md#janitor107",
            "Documentation~/Troubleshooting.md#janitor108",
            "Documentation~/Troubleshooting.md#janitor109",
            "Documentation~/Troubleshooting.md#janitor110",
            "Documentation~/Troubleshooting.md#janitor111",
            "Documentation~/Troubleshooting.md#janitor112",
            "Documentation~/Troubleshooting.md#janitor113",
            "Documentation~/Troubleshooting.md#janitor114",
            "Documentation~/Troubleshooting.md#janitor115",
            "Documentation~/Troubleshooting.md#janitor116",
        };

        // Every known ID in order; tests and the docs-consistency check read it.
        internal static IReadOnlyList<string> All => AllIds;

        /// <summary>The one-line title of a diagnostic.</summary>
        /// <param name="diagnosticId">An ID such as <see cref="TaskOverrun"/>.</param>
        /// <returns>The title, or null when the ID is not one of the package's IDs.</returns>
        public static string GetTitle(string diagnosticId)
        {
            var index = IndexOf(diagnosticId);
            return index < 0 ? null : Titles[index];
        }

        /// <summary>
        /// The package-relative path of the diagnostic's section in the troubleshooting guide, for example
        /// <c>Documentation~/Troubleshooting.md#janitor101</c>.
        /// </summary>
        /// <param name="diagnosticId">An ID such as <see cref="TaskOverrun"/>.</param>
        /// <returns>The anchor path, or null when the ID is not one of the package's IDs.</returns>
        public static string GetAnchor(string diagnosticId)
        {
            var index = IndexOf(diagnosticId);
            return index < 0 ? null : Anchors[index];
        }

        private static int IndexOf(string diagnosticId)
        {
            if (diagnosticId == null)
            {
                return -1;
            }

            for (var i = 0; i < AllIds.Length; i++)
            {
                if (string.Equals(AllIds[i], diagnosticId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
