#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // Small lookups over a captured snapshot, shared by the PlayMode diagnostics fixtures.
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

        internal static List<string> EntryLabels(List<EntryView> entries)
        {
            var labels = new List<string>(entries.Count);
            for (var i = 0; i < entries.Count; i++)
            {
                labels.Add(entries[i].FormatLabel());
            }

            return labels;
        }

        // Whether the recorded entries of one id include one attached to this lifetime.
        internal static bool HasFor(List<RecordedDiagnostic> rows, Lifetime lifetime)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                if (ReferenceEquals(rows[i].Lifetime, lifetime))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
#endif
