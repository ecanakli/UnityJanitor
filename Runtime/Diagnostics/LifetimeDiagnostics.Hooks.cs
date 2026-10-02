using System.Diagnostics;
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
#endif

namespace Ecanakli.Janitor
{
    // The recording hooks the core calls. Every one is conditional on UNITY_EDITOR, so the call sites and their arguments
    // are removed from players, and every body that touches editor-only data sits inside #if UNITY_EDITOR as well.
    // A hook never throws: a fault is contained by Failed.
    public static partial class LifetimeDiagnostics
    {
        // A warning recorded next to a console warning (DevWarnings) or in place of one (JANITOR103, 114).
        [Conditional("UNITY_EDITOR")]
        internal static void Record(string diagnosticId, Lifetime lifetime, string message, UnityEngine.Object context = null, string member = null, int line = 0)
        {
#if UNITY_EDITOR
            AddWarning(diagnosticId, lifetime, message, context, member, line);
#endif
        }

        // A lifetime was constructed: gives it its window id and creation frame (also while tracking is off).
        [Conditional("UNITY_EDITOR")]
        internal static void LifetimeCreated(Lifetime lifetime)
        {
#if UNITY_EDITOR
            try
            {
                lifetime.DiagId = NextLifetimeId();
                lifetime.DiagCreatedFrame = CurrentFrame();
            }
            catch (Exception exception)
            {
                Failed(exception);
            }
#endif
        }

        // An item was added to the entry list (after the amortized sweep). Counts it, stamps its frame and the opt-in
        // stack trace, and checks the entry growth limit and, for an OnEnable call site, the repeat of an earlier registration (JANITOR116).
        // The only allocation is the stack trace, and only when it is on; a report allocates its message once.
        [Conditional("UNITY_EDITOR")]
        internal static void RegistrationAdded(Lifetime lifetime, int slot, int version)
        {
#if UNITY_EDITOR
            if (!TrackingEnabled)
            {
                return;
            }

            try
            {
                lifetime.DiagTotalRegistered++;

                // A sweep probe may already have dropped the entry again.
                if (lifetime.IsRegistrationLive(lifetime.Generation, slot, version))
                {
                    ref var entry = ref lifetime.EntryAt(slot);
                    entry.DiagFrame = CurrentFrame() + 1;
                    if (CaptureStackTraces)
                    {
                        entry.DiagTrace = new StackTrace(2, true);
                    }

                    if ((lifetime.Kind == LifetimeKind.Component || lifetime.Kind == LifetimeKind.GameObject) && IsOnEnable(entry.Member))
                    {
                        CheckRepeatedOnEnable(lifetime, slot, in entry);
                    }
                }

                var count = lifetime.EntryCount;
                if (count > GrowthEntryLimit && (lifetime.DiagWarned & Lifetime.DiagWarnedEntries) == 0)
                {
                    lifetime.DiagWarned |= Lifetime.DiagWarnedEntries;
                    ReportGrowth(lifetime, count, false);
                }
            }
            catch (Exception exception)
            {
                Failed(exception);
            }
#endif
        }

        // A registration was refused because the lifetime is ending, disposed, or its GameObject is inactive.
        [Conditional("UNITY_EDITOR")]
        internal static void RegistrationRefused(Lifetime lifetime, string member, int line)
        {
#if UNITY_EDITOR
            if (!TrackingEnabled)
            {
                return;
            }

            lifetime.DiagRefusedCount++;
            if (lifetime.DiagRefusedCount == 1)
            {
                lifetime.DiagFirstRefusedMember = member;
                lifetime.DiagFirstRefusedLine = line;
            }
#endif
        }

        // A child was attached to parent (a new area or a re-home); checks the child growth limit.
        // Only areas count, under every kind of parent: object lifetimes are children of their scene or category by design.
        [Conditional("UNITY_EDITOR")]
        internal static void ChildAttached(Lifetime parent)
        {
#if UNITY_EDITOR
            try
            {
                // The attached child is the newest one; an object lifetime cannot raise the area count.
                if (!IsAreaKind(parent.LastChild.Kind))
                {
                    return;
                }

                // Upper bound of the live area children: every area attach counts, an exact count corrects it once it passes the limit.
                if (++parent.DiagAreaBound <= GrowthChildLimit || !TrackingEnabled || (parent.DiagWarned & Lifetime.DiagWarnedChildren) != 0)
                {
                    return;
                }

                var areas = CountAreaChildren(parent, GrowthChildLimit + 1);
                parent.DiagAreaBound = areas;
                if (areas > GrowthChildLimit)
                {
                    parent.DiagWarned |= Lifetime.DiagWarnedChildren;
                    ReportGrowth(parent, areas, true);
                }
            }
            catch (Exception exception)
            {
                Failed(exception);
            }
#endif
        }

        // A generation ended by Cancel (or by a re-home).
        [Conditional("UNITY_EDITOR")]
        internal static void GenerationCancelled(Lifetime lifetime)
        {
#if UNITY_EDITOR
            if (TrackingEnabled)
            {
                lifetime.DiagCancelCount++;
            }
#endif
        }

        // The lifetime is being finalized as disposed. Called first, so its generation and links are still intact.
        // Disposals during the application exit are not recorded, so they do not push the real history out of the buffer.
        [Conditional("UNITY_EDITOR")]
        internal static void LifetimeDisposed(Lifetime lifetime)
        {
#if UNITY_EDITOR
            if (!TrackingEnabled)
            {
                return;
            }

            try
            {
                if (lifetime.DiagDisposedFrame >= 0)
                {
                    return;
                }

                lifetime.DiagDisposedFrame = CurrentFrame();
                if (lifetime.Tree.DiagShuttingDown || PlayModeBootstrap.IsExiting)
                {
                    return;
                }

                RecordDisposed(lifetime);
            }
            catch (Exception exception)
            {
                Failed(exception);
            }
#endif
        }

        // A new play session begins: the old session's warnings and tracked tasks are dropped.
        [Conditional("UNITY_EDITOR")]
        internal static void SessionStarted()
        {
#if UNITY_EDITOR
            try
            {
                BeginSession();
            }
            catch (Exception exception)
            {
                Failed(exception);
            }
#endif
        }

#if UNITY_EDITOR
        // JANITOR114 (information): a placed object lifetime was re-homed under its scene lifetime.
        internal static void RecordRehomed(Lifetime lifetime)
        {
            if (!TrackingEnabled)
            {
                return;
            }

            var message = "The lifetime '" + Describe(lifetime) + "' was re-homed under '" + Describe(lifetime.Parent)
                + "' because its category lifetime was disposed while the object still lives. Its work was cancelled and a fresh generation started.";
            AddWarning(DiagnosticIds.Rehomed, lifetime, message, lifetime.OwnerObject, null, 0);
        }

        // JANITOR106: raised once per lifetime and kind of growth. For children the count is the number of live child areas.
        private static void ReportGrowth(Lifetime lifetime, int count, bool children)
        {
            var message = children
                ? "The lifetime '" + Describe(lifetime) + "' has " + count + " live children that are areas (the limit is " + GrowthChildLimit
                    + "). Areas are created faster than they are disposed; dispose the areas you no longer need."
                : "The lifetime '" + Describe(lifetime) + "' holds " + count + " entries (the limit is " + GrowthEntryLimit
                    + "). Work is registered faster than it ends; check for a registration made every frame or inside a loop.";
            AddWarning(DiagnosticIds.Growth, lifetime, message, lifetime.OwnerObject, null, 0);
        }

        private static bool IsOnEnable(string member)
        {
            return member != null && string.Equals(member, "OnEnable", StringComparison.Ordinal);
        }

        // JANITOR116: a registration from OnEnable on a lifetime that does not follow activation, while an earlier registration from the
        // same line is still live. Entries of the same frame are one OnEnable call (a loop), so only an earlier frame counts.
        private static void CheckRepeatedOnEnable(Lifetime lifetime, int slot, in EntrySlot added)
        {
            var reported = lifetime.DiagRepeatedLines;
            if (reported != null && reported.Contains(added.Line))
            {
                return;
            }

            var id = lifetime.NewestEntryId;
            for (var guard = lifetime.EntryCount; id != 0 && guard >= 0; guard--)
            {
                ref var other = ref lifetime.EntryAt(id);
                var next = other.Next;
                if (id != slot && other.Line == added.Line && other.DiagFrame != 0 && other.DiagFrame != added.DiagFrame
                    && string.Equals(other.Member, added.Member, StringComparison.Ordinal) && IsStillRunning(in other))
                {
                    ReportRepeatedOnEnable(lifetime, added.Member, added.Line);
                    return;
                }

                id = next;
            }
        }

        // A tween or a coroutine entry stays in the list after it finished until a sweep drops it; the package's own probe says so.
        // Any other probe is user code and is not called here.
        private static bool IsStillRunning(in EntrySlot entry)
        {
            if (entry.Probe == null || entry.ProbeInvoker == null)
            {
                return true;
            }

            var kind = EntryClassifier.Classify(in entry);
            if (kind != EntryLabelKind.Tween && kind != EntryLabelKind.Coroutine)
            {
                return true;
            }

            try
            {
                return !entry.ProbeInvoker(entry.A, entry.Probe);
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static void ReportRepeatedOnEnable(Lifetime lifetime, string member, int line)
        {
            if (lifetime.DiagRepeatedLines == null)
            {
                lifetime.DiagRepeatedLines = new HashSet<int>();
            }

            lifetime.DiagRepeatedLines.Add(line);
            var message = "Work registered by " + member + ":" + line + " on the lifetime '" + Describe(lifetime) + "' was registered again while the earlier registration from the same line is still live. "
                + "This lifetime does not follow activation, so every time the object is enabled again the work is added once more. "
                + "Register it on GetActiveLifetime() instead; that lifetime is cancelled when the GameObject is deactivated.";
            AddWarning(DiagnosticIds.RepeatedOnEnable, lifetime, message, lifetime.OwnerObject, member, line);
        }

        // Area and Injected both count as areas; the other kinds are object lifetimes, the scene and App.
        private static bool IsAreaKind(LifetimeKind kind)
        {
            return kind == LifetimeKind.Area || kind == LifetimeKind.Injected;
        }

        // The live area children of parent, counted up to cap; the walk stops there.
        private static int CountAreaChildren(Lifetime parent, int cap)
        {
            var count = 0;
            for (var child = parent.FirstChild; child != null && count < cap; child = child.NextSibling)
            {
                if (IsAreaKind(child.Kind))
                {
                    count++;
                }
            }

            return count;
        }

        private static string Describe(Lifetime lifetime)
        {
            return lifetime == null ? "unnamed" : lifetime.Label ?? "unnamed";
        }
#endif
    }
}
