namespace Ecanakli.Janitor.EditorTools
{
    /// <summary>
    /// The details pane for the selected lifetime: a header, a one-line summary, the entry list and the button states.
    /// Texts are rebuilt only when their inputs change.
    /// </summary>
    internal sealed class DetailsModel
    {
        private string _headerName;
        private string _headerKind;
        private string _headerState;
        private string _headerGeneration;
        private int _summaryTotal = -1;
        private int _summaryRefused = -1;
        private int _summaryCancels = -1;
        private int _summaryCreated = -1;
        private string _summaryMember;
        private int _summaryLine;

        internal EntryListModel Entries { get; } = new EntryListModel();

        /// <summary>The selected lifetime id, or 0.</summary>
        internal int SelectedId { get; private set; }

        /// <summary>The current row of the selected lifetime, or null when none is selected or it is gone.</summary>
        internal LifetimeRow Row { get; private set; }

        internal string HeaderText { get; private set; }

        internal string SummaryText { get; private set; }

        /// <summary>A line to show instead of the details, or null when there are details.</summary>
        internal string HintText { get; private set; } = EmptyStates.NoSelection;

        internal bool CanPing => LifetimeActions.CanPing(Row);

        internal bool CanCancel => LifetimeActions.CanCancel(Row);

        /// <summary>Selects a lifetime by id; 0 clears the selection.</summary>
        internal void Select(int id)
        {
            if (id == SelectedId)
            {
                return;
            }

            SelectedId = id;
            Entries.ClearSelection();
            ResetTextCaches();
        }

        internal void Clear()
        {
            SelectedId = 0;
            Row = null;
            Entries.Clear();
            ResetTextCaches();
            HintText = EmptyStates.NoSelection;
        }

        /// <summary>Refreshes the details from the tree rows. <paramref name="captureEntries"/> reads the live entry list of the lifetime.</summary>
        internal void Update(LifetimeTreeModel tree, bool playing, bool captureEntries)
        {
            if (!playing)
            {
                Clear();
                HintText = EmptyStates.DetailsNotPlaying;
                return;
            }

            if (SelectedId == 0)
            {
                Row = null;
                Entries.Clear();
                HintText = EmptyStates.NoSelection;
                return;
            }

            Row = tree.Find(SelectedId);
            if (Row == null)
            {
                Entries.Clear();
                HintText = EmptyStates.SelectionGone;
                return;
            }

            HintText = null;
            RefreshTexts(Row);
            if (captureEntries)
            {
                Entries.Update(Row.Lifetime);
            }
        }

        private void ResetTextCaches()
        {
            _headerName = null;
            _summaryTotal = -1;
        }

        private void RefreshTexts(LifetimeRow row)
        {
            // The row texts are cached strings, so reference equality is enough to see a change.
            if (!ReferenceEquals(_headerName, row.NameText)
                || !ReferenceEquals(_headerKind, row.KindText)
                || !ReferenceEquals(_headerState, row.StateText)
                || !ReferenceEquals(_headerGeneration, row.GenerationText))
            {
                _headerName = row.NameText;
                _headerKind = row.KindText;
                _headerState = row.StateText;
                _headerGeneration = row.GenerationText;
                HeaderText = row.NameText + " (" + row.KindText + ", " + row.StateText + "), generation " + row.GenerationText;
            }

            if (_summaryTotal != row.TotalRegistered
                || _summaryRefused != row.RefusedCount
                || _summaryCancels != row.CancelCount
                || _summaryCreated != row.CreatedFrame
                || !ReferenceEquals(_summaryMember, row.FirstRefusedMember)
                || _summaryLine != row.FirstRefusedLine)
            {
                _summaryTotal = row.TotalRegistered;
                _summaryRefused = row.RefusedCount;
                _summaryCancels = row.CancelCount;
                _summaryCreated = row.CreatedFrame;
                _summaryMember = row.FirstRefusedMember;
                _summaryLine = row.FirstRefusedLine;
                SummaryText = BuildSummary(row);
            }
        }

        private static string BuildSummary(LifetimeRow row)
        {
            var text = "Registered " + row.TotalRegistered + ", refused " + row.RefusedCount;
            if (row.RefusedCount > 0 && row.FirstRefusedMember != null)
            {
                text += " (first at " + row.FirstRefusedMember + ":" + row.FirstRefusedLine + ")";
            }

            return text + ", cancelled " + row.CancelCount + " times, created at frame " + row.CreatedFrame + ".";
        }
    }
}
