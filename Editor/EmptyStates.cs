namespace Ecanakli.Janitor.EditorTools
{
    /// <summary>The one-line messages shown in place of an empty view. A null result means there is something to show.</summary>
    internal static class EmptyStates
    {
        internal const string NotPlaying = "Not in Play Mode. Enter Play Mode to see live lifetimes; the tabs below keep the last session's history.";
        internal const string TrackingOff = "Tracking is off. Turn it on in the toolbar to record lifetimes.";
        internal const string NoLifetimes = "No lifetimes yet. They appear as soon as code creates one.";
        internal const string NoFilterMatch = "No lifetime matches the filter.";
        internal const string DetailsNotPlaying = "Details are available in Play Mode.";
        internal const string NoSelection = "Select a lifetime in the tree to see its entries.";
        internal const string SelectionGone = "The selected lifetime is no longer alive.";
        internal const string NoWarnings = "No warnings recorded.";
        internal const string NoWarningsTrackingOff = "Tracking is off, so warnings are not recorded.";
        internal const string NoRecentTrackingOff = "Tracking is off, so disposed lifetimes are not recorded.";

        internal static readonly string NoRecent = "Nothing disposed yet. The last " + LifetimeDiagnostics.RecentCapacity + " disposed lifetimes appear here.";

        internal static string ForTree(bool playing, bool tracking, int totalCount, int shownCount)
        {
            if (!playing)
            {
                return NotPlaying;
            }

            if (!tracking)
            {
                return TrackingOff;
            }

            if (totalCount == 0)
            {
                return NoLifetimes;
            }

            return shownCount == 0 ? NoFilterMatch : null;
        }

        internal static string ForWarnings(bool tracking, int count)
        {
            if (count > 0)
            {
                return null;
            }

            return tracking ? NoWarnings : NoWarningsTrackingOff;
        }

        internal static string ForRecent(bool tracking, int count)
        {
            if (count > 0)
            {
                return null;
            }

            return tracking ? NoRecent : NoRecentTrackingOff;
        }
    }
}
