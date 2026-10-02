#if UNITY_EDITOR
using System;

namespace Ecanakli.Janitor
{
    // One recorded warning. Repeats of the same id, lifetime and message are merged: Count goes up and LastFrame moves.
    internal struct DiagnosticWarning
    {
        // Grows with every distinct warning; a stable key for a list row.
        internal int Sequence;
        internal string DiagnosticId;
        internal DiagnosticSeverity Severity;
        internal string Message;

        // The lifetime it belongs to, or null. LifetimeId is 0 and LifetimeLabel null in that case.
        internal Lifetime Lifetime;
        internal int LifetimeId;
        internal string LifetimeLabel;

        // An object the window pings when the row is chosen, or null.
        internal UnityEngine.Object Context;

        // The registration call site when it is known (coroutine start, the item whose cancel action reported), else null and 0.
        internal string Member;
        internal int Line;
        internal int FirstFrame;
        internal int LastFrame;
        internal int Count;
        internal int Session;

        // Null for an ID that is not one of the package's IDs.
        internal string Title => DiagnosticIds.GetTitle(DiagnosticId);

        internal string Anchor => DiagnosticIds.GetAnchor(DiagnosticId);
    }

    // One entry of the "recently disposed" ring buffer: plain data, so a disposed lifetime costs a struct copy.
    internal struct RecentLifetime
    {
        internal int Id;
        internal int ParentId;
        internal LifetimeKind Kind;

        // The last generation the lifetime lived in, the number of Cancel calls and the totals while tracking was on.
        internal int Generation;
        internal int CancelCount;
        internal int TotalRegistered;
        internal int RefusedCount;
        internal int CreatedFrame;
        internal int DisposedFrame;

        // The play session it was disposed in.
        internal int Session;

        // Label pieces, formatted lazily. LabelText is set only for an entry restored by ImportState.
        internal string Name;
        internal string Member;
        internal int Line;
        internal string LabelText;

        internal int LifetimeFrames => DisposedFrame - CreatedFrame;

        internal string FormatLabel()
        {
            if (LabelText != null)
            {
                return LabelText;
            }

            if (Name != null)
            {
                return Name;
            }

            return Member != null ? Member + ":" + Line : "unnamed";
        }
    }

    // The persisted form of the ring buffer and the warning list, for JsonUtility (see LifetimeDiagnostics.ExportState).
    [Serializable]
    internal sealed class PersistedRecentLifetime
    {
        public int Id;
        public int ParentId;
        public int Kind;
        public int Generation;
        public int CancelCount;
        public int TotalRegistered;
        public int RefusedCount;
        public int CreatedFrame;
        public int DisposedFrame;
        public int Session;
        public string Label;
    }

    [Serializable]
    internal sealed class PersistedWarning
    {
        public int Sequence;
        public string DiagnosticId;
        public int Severity;
        public string Message;
        public int LifetimeId;
        public string LifetimeLabel;
        public string Member;
        public int Line;
        public int FirstFrame;
        public int LastFrame;
        public int Count;
        public int Session;
    }

    [Serializable]
    internal sealed class PersistedDiagnostics
    {
        // No initializer on purpose: JSON without the field must read as 0, which ImportState rejects.
        public int Version;
        public int Session;
        public PersistedRecentLifetime[] Recent;
        public PersistedWarning[] Warnings;
    }
}
#endif
