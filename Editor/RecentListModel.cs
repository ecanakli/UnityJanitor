using System.Collections.Generic;

namespace Ecanakli.Janitor.EditorTools
{
    /// <summary>One row of the "Recently disposed" tab.</summary>
    internal sealed class RecentRow
    {
        private bool _filled;
        private int _id;
        private int _session;
        private int _disposedFrame;

        internal string NameText;
        internal string KindText;
        internal string GenerationText;
        internal string CancelsText;
        internal string RegisteredText;
        internal string RefusedText;
        internal string LivedText;
        internal string DisposedText;
        internal string SessionText;

        // Formats the entry, unless the row already shows it. A ring entry never changes after it was written.
        internal void Update(in RecentLifetime item)
        {
            if (_filled && _id == item.Id && _session == item.Session && _disposedFrame == item.DisposedFrame)
            {
                return;
            }

            _filled = true;
            _id = item.Id;
            _session = item.Session;
            _disposedFrame = item.DisposedFrame;

            NameText = item.FormatLabel();
            KindText = DisplayNames.Kind(item.Kind);
            GenerationText = CountText.Get(item.Generation);
            CancelsText = CountText.Get(item.CancelCount);
            RegisteredText = CountText.Get(item.TotalRegistered);
            RefusedText = CountText.Get(item.RefusedCount);
            LivedText = CountText.Frames(item.LifetimeFrames);
            DisposedText = CountText.Get(item.DisposedFrame);
            SessionText = CountText.Get(item.Session);
        }
    }

    /// <summary>
    /// The rows of the "Recently disposed" tab, newest first. Rows are pooled by position and reformatted only when the entry at that
    /// position changed, and the whole list is skipped while the ring's version is unchanged.
    /// </summary>
    internal sealed class RecentListModel
    {
        private int _appliedVersion = -1;
        private int _appliedCount = -1;

        /// <summary>The rows. The same list instance for the life of the model.</summary>
        internal List<RecentRow> Rows { get; } = new List<RecentRow>(LifetimeDiagnostics.RecentCapacity);

        /// <summary>Changes whenever the rows were rebuilt.</summary>
        internal int Revision { get; private set; }

        /// <summary>Forces the next <see cref="Update"/> to rebuild.</summary>
        internal void Invalidate()
        {
            _appliedVersion = -1;
        }

        /// <summary>Rebuilds the rows when the snapshot's recently disposed list changed. Returns true when it did.</summary>
        internal bool Update(DiagnosticsSnapshot snapshot)
        {
            var recent = snapshot == null ? null : snapshot.Recent;
            var count = recent == null ? 0 : recent.Count;
            var version = snapshot == null ? 0 : snapshot.RecentVersion;
            if (version == _appliedVersion && count == _appliedCount)
            {
                return false;
            }

            _appliedVersion = version;
            _appliedCount = count;

            while (Rows.Count > count)
            {
                Rows.RemoveAt(Rows.Count - 1);
            }

            while (Rows.Count < count)
            {
                Rows.Add(new RecentRow());
            }

            for (var i = 0; i < count; i++)
            {
                var item = recent[i];
                Rows[i].Update(in item);
            }

            Revision++;
            return true;
        }
    }
}
