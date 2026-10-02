#if UNITY_EDITOR
using System.Collections.Generic;

namespace Ecanakli.Janitor
{
    // Editor-only diagnostics fields of a lifetime. Plain counters and cached data, so recording allocates nothing.
    public sealed partial class Lifetime
    {
        // Bits of DiagWarned: each growth warning and the orphan warning are raised once per lifetime.
        internal const byte DiagWarnedEntries = 1;
        internal const byte DiagWarnedChildren = 2;
        internal const byte DiagWarnedOrphan = 4;

        // A unique id for the window's tree view; assigned at creation.
        internal int DiagId;
        internal int DiagCreatedFrame;

        // -1 while the lifetime lives.
        internal int DiagDisposedFrame = -1;

        // Completed Cancel calls (generations ended by Cancel or by a re-home).
        internal int DiagCancelCount;
        internal int DiagTotalRegistered;
        internal int DiagRefusedCount;
        internal string DiagFirstRefusedMember;
        internal int DiagFirstRefusedLine;
        internal byte DiagWarned;

        // Upper bound of the live area children, kept for the child growth check (see ChildAttached).
        internal int DiagAreaBound;

        // The frame plus one in which the destroyed owner was first seen with the lifetime alive; 0 until then (JANITOR103).
        internal int DiagOrphanSeenFrame;

        // The lines of the OnEnable call sites already reported for this lifetime (JANITOR116); null until the first report.
        internal HashSet<int> DiagRepeatedLines;

        // Cached display label, so a refresh does not rebuild "Awake:12" every time.
        internal string DiagLabel;
    }
}
#endif
