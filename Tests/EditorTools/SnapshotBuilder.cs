namespace Ecanakli.Janitor.EditorTools.Tests
{
    // Hand-built diagnostics snapshots: the seam that lets the window's view-model be tested without a live tree.
    internal static class SnapshotBuilder
    {
        // A lifetime node with the given identity; Active, generation 1, no entries.
        internal static LifetimeNode Node(int id, int parentId, string label, LifetimeKind kind = LifetimeKind.Area)
        {
            return new LifetimeNode
            {
                Id = id,
                ParentId = parentId,
                Label = label,
                Kind = kind,
                State = LifetimeState.Active,
                Generation = 1,
            };
        }

        internal static DiagnosticsSnapshot Snapshot(params LifetimeNode[] nodes)
        {
            var snapshot = new DiagnosticsSnapshot();
            snapshot.HasTree = nodes.Length > 0;
            for (var i = 0; i < nodes.Length; i++)
            {
                snapshot.Nodes.Add(nodes[i]);
            }

            return snapshot;
        }

        // 1 App > 2 Level > (3 Player > 4 PlayerController, 5 Awake:12), 6 Services. Ids are in pre-order.
        internal static DiagnosticsSnapshot SampleTree()
        {
            var controller = Node(4, 3, "PlayerController", LifetimeKind.Component);
            controller.State = LifetimeState.Cancelling;
            return Snapshot(
                Node(1, 0, "App", LifetimeKind.App),
                Node(2, 1, "Level", LifetimeKind.Scene),
                Node(3, 2, "Player", LifetimeKind.GameObject),
                controller,
                Node(5, 2, "Awake:12"),
                Node(6, 1, "Services"));
        }

        internal static DiagnosticWarning Warning(int sequence, string diagnosticId, string message)
        {
            return new DiagnosticWarning
            {
                Sequence = sequence,
                DiagnosticId = diagnosticId,
                Severity = DiagnosticSeverity.Warning,
                Message = message,
                Count = 1,
                FirstFrame = 10,
                LastFrame = 10,
            };
        }

        internal static RecentLifetime Recent(int id, string name, int createdFrame, int disposedFrame)
        {
            return new RecentLifetime
            {
                Id = id,
                Kind = LifetimeKind.Area,
                Name = name,
                CreatedFrame = createdFrame,
                DisposedFrame = disposedFrame,
            };
        }
    }
}
