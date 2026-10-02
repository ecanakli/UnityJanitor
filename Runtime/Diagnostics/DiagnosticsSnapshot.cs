#if UNITY_EDITOR
using System.Collections.Generic;

namespace Ecanakli.Janitor
{
    // The coarse kind of a registered item: the five live-count columns of the window.
    internal enum EntryKind : byte
    {
        Task,
        Tween,
        Coroutine,
        Subscription,
        Other,
    }

    // The finer kind behind an item's label; chosen from the terminate action's identity and the item's type.
    internal enum EntryLabelKind : byte
    {
        Task,
        Coroutine,
        UnityEvent,
        OwnedEvent,
        Signal,
        Paired,
        Tween,
        Dispose,
        Action,
        State,
    }

    internal enum EntryStatus : byte
    {
        // Registered in the lifetime's current generation.
        Live,

        // A task whose generation ended but which is still running.
        Outlived,
    }

    internal enum DiagnosticSeverity : byte
    {
        Info,
        Warning,
    }

    // One lifetime of the live tree, copied into a reusable buffer. Only cheap data: labels are cached strings,
    // and the owner and the lifetime are references the window can ping or cancel.
    internal struct LifetimeNode
    {
        internal Lifetime Lifetime;

        // Stable for the life of the lifetime; usable as a tree view id. ParentId is 0 for the root.
        internal int Id;
        internal int ParentId;
        internal int Depth;

        // Display label (name, or the call site such as "Awake:12"); built once per lifetime, null only when it has none.
        internal string Label;
        internal LifetimeKind Kind;
        internal LifetimeState State;
        internal int Generation;
        internal int CancelCount;
        internal int CreatedFrame;
        internal int AgeFrames;

        // Live entries now, split by kind; the five kinds add up to EntryCount.
        internal int EntryCount;
        internal int Tasks;
        internal int Tweens;
        internal int Coroutines;
        internal int Subscriptions;
        internal int Others;
        internal int ChildCount;

        // Totals since creation (while tracking was on). The first refused call site is Member:Line.
        internal int TotalRegistered;
        internal int RefusedCount;
        internal string FirstRefusedMember;
        internal int FirstRefusedLine;

        // The owning object, or null (App, scenes and areas have none). OwnerDestroyed is true for a destroyed owner.
        internal UnityEngine.Object Owner;
        internal bool OwnerDestroyed;

        // True when the parent is a category chosen through GetLifetime(parent).
        internal bool Placed;
    }

    // One registered item of a lifetime (or a task that outlived its generation). Holds raw references so the label
    // is formatted only when the window asks (FormatLabel, FormatTrace).
    internal struct EntryView
    {
        internal EntryKind Kind;
        internal EntryLabelKind LabelKind;
        internal EntryStatus Status;
        internal LifetimeTaskKind TaskKind;

        // Where it was registered.
        internal string Member;
        internal int Line;

        // The generation it belongs to.
        internal int Generation;

        // Frames since registration (or since the generation ended, for an outlived task); -1 when unknown.
        internal int AgeFrames;
        internal int RegisteredFrame;

        // Cancels exactly this item. Default for an outlived task, which has no entry any more.
        internal LifetimeRegistration Registration;

        // The opt-in stack trace captured at registration (a System.Diagnostics.StackTrace), or null.
        internal object Trace;

        // Raw item data for the lazy label.
        internal object A;
        internal object B;

        internal bool HasTrace => Trace != null;

        internal string FormatLabel()
        {
            return EntryClassifier.FormatLabel(LabelKind, TaskKind, A, B);
        }

        internal string FormatTrace()
        {
            return Trace == null ? null : Trace.ToString();
        }
    }

    // Everything the window reads in one refresh. Reuse one instance: Clear keeps the list capacity, so a refresh in
    // steady state allocates nothing. Nodes are in pre-order (parents first, children in creation order).
    internal sealed class DiagnosticsSnapshot
    {
        internal readonly List<LifetimeNode> Nodes = new List<LifetimeNode>(256);
        internal readonly List<DiagnosticWarning> Warnings = new List<DiagnosticWarning>(64);
        internal readonly List<RecentLifetime> Recent = new List<RecentLifetime>(LifetimeDiagnostics.RecentCapacity);

        // False when there is no tree to show (Nodes is empty); Warnings and Recent are still filled.
        internal bool HasTree;
        internal int Frame;
        internal int Session;

        // Bumped whenever the warning list or the recently disposed list changes; lets the window skip a rebuild.
        internal int WarningsVersion;
        internal int RecentVersion;

        internal void Clear()
        {
            Nodes.Clear();
            Warnings.Clear();
            Recent.Clear();
            HasTree = false;
            Frame = 0;
            Session = 0;
            WarningsVersion = 0;
            RecentVersion = 0;
        }
    }
}
#endif
