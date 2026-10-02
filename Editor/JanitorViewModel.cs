namespace Ecanakli.Janitor.EditorTools
{
    /// <summary>The tabs of the pane below the lifetime tree.</summary>
    internal enum JanitorTab
    {
        Details = 0,
        Warnings = 1,
        Recent = 2,
    }

    /// <summary>
    /// Everything the Janitor window shows, as plain data: the lifetime tree rows, the details of the selected lifetime, the warning
    /// and recently disposed rows, the empty-state lines and the tab labels. The window only binds this to UI elements. It has no
    /// UI types, so tests can feed a snapshot to <see cref="Apply"/> and read the result without opening a window.
    /// </summary>
    internal sealed class JanitorViewModel
    {
        internal const string DetailsTabText = "Details";
        internal const string WarningsTabBase = "Warnings";
        internal const string RecentTabBase = "Recently disposed";

        private readonly DiagnosticsSnapshot _snapshot = new DiagnosticsSnapshot();
        private DiagnosticsSnapshot _current;
        private int _warningsBadge = -1;
        private int _recentBadge = -1;

        internal JanitorViewModel()
        {
            _current = _snapshot;
            WarningsTabText = WarningsTabBase;
            RecentTabText = RecentTabBase;
        }

        internal LifetimeTreeModel Tree { get; } = new LifetimeTreeModel();

        internal DetailsModel Details { get; } = new DetailsModel();

        internal WarningListModel Warnings { get; } = new WarningListModel();

        internal RecentListModel Recent { get; } = new RecentListModel();

        internal JanitorTab Tab { get; private set; } = JanitorTab.Details;

        internal bool Playing { get; private set; }

        internal bool Tracking { get; private set; } = true;

        /// <summary>The line to show instead of the tree, or null when the tree has rows.</summary>
        internal string TreeEmptyText { get; private set; }

        /// <summary>The line to show instead of the warning list, or null when there are warnings.</summary>
        internal string WarningsEmptyText { get; private set; }

        /// <summary>The line to show instead of the recently disposed list, or null when there are entries.</summary>
        internal string RecentEmptyText { get; private set; }

        /// <summary>The label of the Warnings tab, with the number of recorded warnings.</summary>
        internal string WarningsTabText { get; private set; }

        /// <summary>The label of the "Recently disposed" tab, with the number of entries.</summary>
        internal string RecentTabText { get; private set; }

        /// <summary>The snapshot of the last <see cref="Refresh"/>.</summary>
        internal DiagnosticsSnapshot Snapshot => _snapshot;

        internal void SetTab(JanitorTab tab)
        {
            if (Tab == tab)
            {
                return;
            }

            Tab = tab;

            // The lists of a hidden tab are not kept up to date, so the tab being opened rebuilds on the next apply.
            Warnings.Invalidate();
            Recent.Invalidate();
        }

        /// <summary>Selects a lifetime in the details pane and reads its entries.</summary>
        internal void SelectLifetime(int id)
        {
            Details.Select(id);
            Details.Update(Tree, Playing, true);
        }

        /// <summary>Captures the core diagnostics and applies them.</summary>
        internal void Refresh(bool playing, bool tracking)
        {
            if (playing)
            {
                LifetimeDiagnostics.CaptureDefault(_snapshot);
            }
            else
            {
                CaptureHistory(_snapshot);
            }

            Apply(_snapshot, playing, tracking);
        }

        /// <summary>Applies the last snapshot again, after the filter, the sort, the tab or the selection changed. Captures nothing.</summary>
        internal void Reapply(bool playing, bool tracking)
        {
            Apply(_current, playing, tracking);
        }

        /// <summary>
        /// Updates every model from a snapshot. The tree is shown in Play Mode with tracking on only; the warning and recently disposed
        /// lists are the history and are shown in every state. Lists of a tab that is not open are left alone.
        /// </summary>
        internal void Apply(DiagnosticsSnapshot snapshot, bool playing, bool tracking)
        {
            var source = snapshot ?? _snapshot;
            _current = source;
            Playing = playing;
            Tracking = tracking;

            Tree.Update(playing && tracking ? source : null);
            Details.Update(Tree, playing, Tab == JanitorTab.Details);

            if (Tab == JanitorTab.Warnings)
            {
                Warnings.Update(source);
            }

            if (Tab == JanitorTab.Recent)
            {
                Recent.Update(source);
            }

            TreeEmptyText = EmptyStates.ForTree(playing, tracking, Tree.TotalCount, Tree.Shown.Count);
            WarningsEmptyText = EmptyStates.ForWarnings(tracking, source.Warnings.Count);
            RecentEmptyText = EmptyStates.ForRecent(tracking, source.Recent.Count);
            WarningsTabText = TabText(WarningsTabBase, source.Warnings.Count, ref _warningsBadge, WarningsTabText);
            RecentTabText = TabText(RecentTabBase, source.Recent.Count, ref _recentBadge, RecentTabText);
        }

        // Outside Play Mode there is no tree: only the history is read, through the documented snapshot API.
        private static void CaptureHistory(DiagnosticsSnapshot snapshot)
        {
            snapshot.Clear();
            snapshot.Session = LifetimeDiagnostics.Session;
            snapshot.WarningsVersion = LifetimeDiagnostics.WarningsVersion;
            LifetimeDiagnostics.CopyWarnings(snapshot.Warnings);
            snapshot.RecentVersion = LifetimeDiagnostics.RecentVersion;
            LifetimeDiagnostics.CopyRecent(snapshot.Recent);
        }

        // The label changes only when the count does, so a steady refresh builds no string.
        private static string TabText(string baseText, int count, ref int cachedCount, string cachedText)
        {
            if (count == cachedCount && cachedText != null)
            {
                return cachedText;
            }

            cachedCount = count;
            return count == 0 ? baseText : baseText + " (" + count + ")";
        }
    }
}
