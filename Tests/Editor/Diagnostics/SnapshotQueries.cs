using System.Collections.Generic;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // Small lookups over a captured snapshot, shared by the EditMode diagnostics fixtures.
    internal static class SnapshotQueries
    {
        internal static bool Contains(DiagnosticsSnapshot snapshot, Lifetime lifetime)
        {
            for (var i = 0; i < snapshot.Nodes.Count; i++)
            {
                if (ReferenceEquals(snapshot.Nodes[i].Lifetime, lifetime))
                {
                    return true;
                }
            }

            return false;
        }

        internal static LifetimeNode NodeOf(DiagnosticsSnapshot snapshot, Lifetime lifetime)
        {
            for (var i = 0; i < snapshot.Nodes.Count; i++)
            {
                if (ReferenceEquals(snapshot.Nodes[i].Lifetime, lifetime))
                {
                    return snapshot.Nodes[i];
                }
            }

            Assert.Fail("The lifetime is not in the snapshot.");
            return default;
        }

        // The node labels in snapshot order.
        internal static List<string> Labels(DiagnosticsSnapshot snapshot)
        {
            var labels = new List<string>(snapshot.Nodes.Count);
            for (var i = 0; i < snapshot.Nodes.Count; i++)
            {
                labels.Add(snapshot.Nodes[i].Label);
            }

            return labels;
        }

        internal static List<string> EntryLabels(List<EntryView> entries)
        {
            var labels = new List<string>(entries.Count);
            for (var i = 0; i < entries.Count; i++)
            {
                labels.Add(entries[i].FormatLabel());
            }

            return labels;
        }
    }
}
