namespace Ecanakli.Janitor.EditorTools
{
    /// <summary>Display names of the core enums. Static strings, so binding a row allocates nothing.</summary>
    internal static class DisplayNames
    {
        internal const string Unknown = "Unknown";
        internal const string LiveStatus = "Live";
        internal const string OutlivedStatus = "Outlived";

        private static readonly string[] Kinds = { "App", "Scene", "Component", "GameObject", "Active", "Area", "Injected" };
        private static readonly string[] States = { "Active", "Cancelling", "Disposing", "Disposed" };
        private static readonly string[] OrphanStates = { "Active (orphan)", "Cancelling (orphan)", "Disposing (orphan)", "Disposed" };

        internal static string Kind(LifetimeKind kind)
        {
            var index = (int)kind;
            return index >= 0 && index < Kinds.Length ? Kinds[index] : Unknown;
        }

        // An orphan is a live lifetime whose owner object was destroyed.
        internal static string State(LifetimeState state, bool orphan)
        {
            var index = (int)state;
            if (index < 0 || index >= States.Length)
            {
                return Unknown;
            }

            return orphan ? OrphanStates[index] : States[index];
        }

        internal static string Status(EntryStatus status)
        {
            return status == EntryStatus.Outlived ? OutlivedStatus : LiveStatus;
        }
    }
}
