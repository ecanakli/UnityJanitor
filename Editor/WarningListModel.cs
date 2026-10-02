using System.Collections.Generic;

namespace Ecanakli.Janitor.EditorTools
{
    /// <summary>One row of the Warnings tab.</summary>
    internal sealed class WarningRow
    {
        internal const string UnknownTitle = "Custom diagnostic";
        internal const string NoDocsTooltip = "No documentation for this ID.";

        internal int Sequence;
        internal string DiagnosticId;
        internal bool IsInfo;
        internal string IdText;
        internal string TitleText;
        internal string LifetimeText;
        internal string Message;
        internal string MessageText;
        internal string RepeatText;
        internal string FirstSeenText;
        internal int Count;
        internal int FirstFrame;
        internal int LastFrame;
        internal int Session;

        // The Troubleshooting URL, or null for an ID that is not one of the package's.
        internal string DocsUrl;

        internal Lifetime Lifetime;
        internal int LifetimeId;
        internal UnityEngine.Object Context;

        internal bool HasDocs => DocsUrl != null;

        internal void Update(in DiagnosticWarning warning)
        {
            Sequence = warning.Sequence;
            DiagnosticId = warning.DiagnosticId;
            IsInfo = warning.Severity == DiagnosticSeverity.Info;
            IdText = warning.DiagnosticId ?? CountText.Unknown;
            TitleText = warning.Title ?? UnknownTitle;
            LifetimeText = BuildLifetimeText(in warning);
            Message = warning.Message ?? string.Empty;
            MessageText = Message.Replace('\n', ' ').Replace('\r', ' ');
            Count = warning.Count;
            RepeatText = warning.Count > 1 ? "x" + warning.Count : string.Empty;
            FirstFrame = warning.FirstFrame;
            LastFrame = warning.LastFrame;
            FirstSeenText = "frame " + warning.FirstFrame;
            Session = warning.Session;
            DocsUrl = DocLinks.BuildUrl(warning.DiagnosticId);
            Lifetime = warning.Lifetime;
            LifetimeId = warning.LifetimeId;
            Context = warning.Context;
        }

        /// <summary>True when the row no longer shows the warning: a repeat raised the count, or another warning took the sequence.</summary>
        internal bool IsOutdated(in DiagnosticWarning warning)
        {
            return Count != warning.Count
                || LastFrame != warning.LastFrame
                || !string.Equals(DiagnosticId, warning.DiagnosticId, System.StringComparison.Ordinal)
                || !string.Equals(Message, warning.Message ?? string.Empty, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// The row of the warning's lifetime in the tree, or null when it is not alive any more. Restored warnings carry no lifetime
        /// reference, and ids restored after a domain reload can collide with new ones, so the lifetime object must match too.
        /// </summary>
        internal LifetimeRow FindAliveLifetime(LifetimeTreeModel tree)
        {
            if (Lifetime == null || tree == null)
            {
                return null;
            }

            var row = tree.Find(LifetimeId);
            return row != null && ReferenceEquals(row.Lifetime, Lifetime) && !row.IsDisposed ? row : null;
        }

        private static string BuildLifetimeText(in DiagnosticWarning warning)
        {
            var label = warning.LifetimeLabel;
            if (warning.Member == null)
            {
                return label ?? CountText.Unknown;
            }

            return (label ?? CountText.Unknown) + " @ " + warning.Member + ":" + warning.Line;
        }
    }

    /// <summary>
    /// The rows of the Warnings tab, newest first. They follow the recorded list only when it changed, and only the rows whose
    /// warning is new or changed are built again, so a warning that repeats every frame costs one row per refresh.
    /// </summary>
    internal sealed class WarningListModel
    {
        private int _appliedVersion = -1;
        private int _appliedCount = -1;
        private bool _forced;

        /// <summary>The rows. The same list instance for the life of the model.</summary>
        internal List<WarningRow> Rows { get; } = new List<WarningRow>();

        /// <summary>Changes whenever a row was added, removed or changed.</summary>
        internal int Revision { get; private set; }

        /// <summary>Makes the next <see cref="Update"/> report a change, so the view binds the rows again.</summary>
        internal void Invalidate()
        {
            _appliedVersion = -1;
            _forced = true;
        }

        /// <summary>Brings the rows in line with the snapshot's warning list. Returns true when a row was added, removed or changed.</summary>
        internal bool Update(DiagnosticsSnapshot snapshot)
        {
            var warnings = snapshot == null ? null : snapshot.Warnings;
            var count = warnings == null ? 0 : warnings.Count;
            var version = snapshot == null ? 0 : snapshot.WarningsVersion;
            if (version == _appliedVersion && count == _appliedCount)
            {
                return false;
            }

            _appliedVersion = version;
            _appliedCount = count;
            var changed = Sync(warnings, count) || _forced;
            _forced = false;
            if (changed)
            {
                Revision++;
            }

            return changed;
        }

        // The recorded list is oldest first with rising sequence numbers; the rows are newest first. New warnings are the ones above
        // the newest row, the oldest may have been dropped, and a repeat only changes a warning in place.
        private bool Sync(List<DiagnosticWarning> warnings, int count)
        {
            var changed = false;
            var newest = count - 1;
            var added = 0;
            if (Rows.Count > 0 && count > 0)
            {
                var known = Rows[0].Sequence;
                while (added < count && warnings[newest - added].Sequence > known)
                {
                    added++;
                }
            }

            for (var k = 0; k < added; k++)
            {
                Rows.Insert(k, NewRow(warnings[newest - k]));
                changed = true;
            }

            for (var r = added; r < count; r++)
            {
                var warning = warnings[newest - r];
                if (r >= Rows.Count)
                {
                    Rows.Add(NewRow(warning));
                    changed = true;
                    continue;
                }

                var row = Rows[r];
                if (row.Sequence != warning.Sequence)
                {
                    // Not the list the rows came from (a restored history, or a warning dropped from the middle): start again.
                    return Rebuild(warnings, count);
                }

                if (row.IsOutdated(in warning))
                {
                    row.Update(in warning);
                    changed = true;
                }
            }

            if (Rows.Count > count)
            {
                Rows.RemoveRange(count, Rows.Count - count);
                changed = true;
            }

            return changed;
        }

        private bool Rebuild(List<DiagnosticWarning> warnings, int count)
        {
            Rows.Clear();
            for (var i = count - 1; i >= 0; i--)
            {
                Rows.Add(NewRow(warnings[i]));
            }

            return true;
        }

        private static WarningRow NewRow(in DiagnosticWarning warning)
        {
            var row = new WarningRow();
            row.Update(in warning);
            return row;
        }
    }
}
