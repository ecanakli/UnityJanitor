#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace Ecanakli.Janitor
{
    // The read side for the editor window: a traversal of the live tree plus the warning and recently disposed lists,
    // copied into reusable buffers. Main thread only. A refresh allocates nothing in steady state: node and entry views are
    // structs in lists whose capacity is kept, labels are cached per lifetime, and entry labels are formatted on demand.
    public static partial class LifetimeDiagnostics
    {
        private static readonly DiagnosticsSnapshot ScratchSnapshot = new DiagnosticsSnapshot();

        // The play session's tree, or an empty tree view when there is none (Edit Mode).
        internal static void CaptureDefault(DiagnosticsSnapshot snapshot)
        {
            Capture(LifetimeTree.Default, snapshot);
        }

        // Fills the snapshot: the tree below tree.App in pre-order, the warnings and the recently disposed lifetimes.
        // Pending task overruns and orphaned lifetimes are detected here first, so the copied warning list includes them.
        internal static void Capture(LifetimeTree tree, DiagnosticsSnapshot snapshot)
        {
            snapshot.Clear();
            try
            {
                var frame = CurrentFrame();
                snapshot.Frame = frame;
                snapshot.Session = s_session;
                TaskOverrunTracker.Scan(frame);
                if (tree != null)
                {
                    snapshot.HasTree = true;
                    CaptureTree(tree.App, snapshot, frame);
                }

                snapshot.WarningsVersion = s_warningsVersion;
                CopyWarnings(snapshot.Warnings);
                snapshot.RecentVersion = s_recentVersion;
                CopyRecent(snapshot.Recent);
            }
            catch (Exception exception)
            {
                Failed(exception);
            }
        }

        // The orphan check without a window: a snapshot into a scratch buffer, thrown away.
        internal static void ScanOrphans(LifetimeTree tree)
        {
            Capture(tree, ScratchSnapshot);
            ScratchSnapshot.Clear();
        }

        // The items of one lifetime, newest first, then the tasks that outlived its generation. Reuses the list.
        internal static void CaptureEntries(Lifetime lifetime, List<EntryView> into)
        {
            into.Clear();
            if (lifetime == null)
            {
                return;
            }

            try
            {
                var frame = CurrentFrame();
                var generation = lifetime.Generation;
                var id = lifetime.NewestEntryId;
                for (var guard = lifetime.EntryCount; id != 0 && guard >= 0; guard--)
                {
                    ref var slot = ref lifetime.EntryAt(id);

                    // A finished tween or a stopped coroutine keeps its entry until the next sweep; it is not live work.
                    if (!IsStillRunning(in slot))
                    {
                        id = slot.Next;
                        continue;
                    }

                    var labelKind = EntryClassifier.Classify(in slot);
                    var registered = slot.DiagFrame == 0 ? -1 : slot.DiagFrame - 1;
                    var view = new EntryView
                    {
                        Kind = EntryClassifier.KindOf(labelKind),
                        LabelKind = labelKind,
                        Status = EntryStatus.Live,
                        Member = slot.Member,
                        Line = slot.Line,
                        Generation = generation,
                        RegisteredFrame = registered,
                        AgeFrames = registered < 0 ? -1 : frame - registered,
                        Registration = new LifetimeRegistration(lifetime, generation, id, slot.Version),
                        Trace = slot.DiagTrace,
                        A = slot.A,
                        B = slot.B,
                    };
                    if (labelKind == EntryLabelKind.Task && slot.A is LifetimeTaskEntry task)
                    {
                        view.TaskKind = task.Kind;
                    }

                    into.Add(view);
                    id = slot.Next;
                }

                TaskOverrunTracker.AppendOutlived(lifetime, frame, into);
            }
            catch (Exception exception)
            {
                Failed(exception);
            }
        }

        // Pre-order walk over the intrusive links: no recursion, no stack, no allocation.
        private static void CaptureTree(Lifetime root, DiagnosticsSnapshot snapshot, int frame)
        {
            var current = root;
            var depth = 0;
            while (current != null)
            {
                AppendNode(current, depth, frame, snapshot);
                if (current.FirstChild != null)
                {
                    current = current.FirstChild;
                    depth++;
                    continue;
                }

                while (true)
                {
                    if (ReferenceEquals(current, root))
                    {
                        return;
                    }

                    if (current.NextSibling != null)
                    {
                        current = current.NextSibling;
                        break;
                    }

                    current = current.Parent;
                    depth--;
                    if (current == null)
                    {
                        return;
                    }
                }
            }
        }

        private static void AppendNode(Lifetime lifetime, int depth, int frame, DiagnosticsSnapshot snapshot)
        {
            var parent = lifetime.Parent;
            var node = new LifetimeNode
            {
                Lifetime = lifetime,
                Id = lifetime.DiagId,
                ParentId = parent == null ? 0 : parent.DiagId,
                Depth = depth,
                Label = LabelOf(lifetime),
                Kind = lifetime.Kind,
                State = lifetime.State,
                Generation = lifetime.Generation,
                CancelCount = lifetime.DiagCancelCount,
                CreatedFrame = lifetime.DiagCreatedFrame,
                AgeFrames = frame - lifetime.DiagCreatedFrame,
                EntryCount = lifetime.EntryCount,
                ChildCount = lifetime.ChildCount,
                TotalRegistered = lifetime.DiagTotalRegistered,
                RefusedCount = lifetime.DiagRefusedCount,
                FirstRefusedMember = lifetime.DiagFirstRefusedMember,
                FirstRefusedLine = lifetime.DiagFirstRefusedLine,
                Owner = lifetime.OwnerObject,
                Placed = lifetime.Placed,
            };

            CountEntries(lifetime, ref node);

            // JANITOR103: the owner was destroyed but the lifetime is still alive. Unity's == tells a destroyed object from a C# null.
            if (lifetime.IsObjectKind && !ReferenceEquals(node.Owner, null) && node.Owner == null)
            {
                node.OwnerDestroyed = true;
                if (lifetime.State != LifetimeState.Disposed && (lifetime.DiagWarned & Lifetime.DiagWarnedOrphan) == 0 && IsOrphanConfirmed(lifetime, frame))
                {
                    lifetime.DiagWarned |= Lifetime.DiagWarnedOrphan;
                    var message = "The owner of the lifetime '" + (node.Label ?? "unnamed") + "' was destroyed, but the lifetime is still alive, so the work registered on it was not stopped. "
                        + "A lifetime first used while its GameObject was inactive ends only with the GameObject, and one first used inside OnDestroy does not end with its owner; "
                        + "destroy the GameObject instead of only the component, and do not register work from OnDestroy.";
                    AddWarning(DiagnosticIds.OrphanLifetime, lifetime, message, null, null, 0);
                }
            }

            snapshot.Nodes.Add(node);
        }

        // UniTask ends the lifetime of a never-activated object one frame after Unity destroys it, so a snapshot in that gap
        // sees a destroyed owner and a live lifetime. An orphan counts only when a snapshot in a later frame still sees it.
        private static bool IsOrphanConfirmed(Lifetime lifetime, int frame)
        {
            if (lifetime.DiagOrphanSeenFrame == 0)
            {
                lifetime.DiagOrphanSeenFrame = frame + 1;
                return false;
            }

            return frame + 1 > lifetime.DiagOrphanSeenFrame;
        }

        // Splits the live entries by kind. A finished tween or a stopped coroutine that waits for a sweep is left out.
        private static void CountEntries(Lifetime lifetime, ref LifetimeNode node)
        {
            var id = lifetime.NewestEntryId;
            for (var guard = lifetime.EntryCount; id != 0 && guard >= 0; guard--)
            {
                ref var slot = ref lifetime.EntryAt(id);
                if (!IsStillRunning(in slot))
                {
                    id = slot.Next;
                    continue;
                }

                switch (EntryClassifier.KindOf(EntryClassifier.Classify(in slot)))
                {
                    case EntryKind.Task:
                        node.Tasks++;
                        break;
                    case EntryKind.Tween:
                        node.Tweens++;
                        break;
                    case EntryKind.Coroutine:
                        node.Coroutines++;
                        break;
                    case EntryKind.Subscription:
                        node.Subscriptions++;
                        break;
                    default:
                        node.Others++;
                        break;
                }

                id = slot.Next;
            }
        }

        // The label is built once per lifetime and cached; a lifetime without one stays null.
        private static string LabelOf(Lifetime lifetime)
        {
            var label = lifetime.DiagLabel;
            if (label == null)
            {
                label = lifetime.Label;
                lifetime.DiagLabel = label;
            }

            return label;
        }
    }
}
#endif
