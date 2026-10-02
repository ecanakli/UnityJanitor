using System;
using System.Collections.Generic;

namespace Ecanakli.Janitor
{
    // The four passes of Cancel and Dispose (mark, signal, drain, finalize) over a pooled subtree snapshot.
    // It keeps no state between operations; the running stack only lets nested operations find their buffer.
    internal sealed class LifetimeTeardown
    {
        private readonly LifetimeTree _tree;
        private readonly List<SnapshotBuffer> _running = new List<SnapshotBuffer>(4);
        private int _lastOpId;

        internal LifetimeTeardown(LifetimeTree tree)
        {
            _tree = tree;
        }

        // Operations currently on the stack; zero whenever no Cancel or Dispose is running.
        internal int RunningCount => _running.Count;

        internal void Cancel(Lifetime root)
        {
            if (root.State != LifetimeState.Active)
            {
                return;
            }

            // Objects that left the scene must not be touched by its operation (user code can run here).
            if (SceneBinding.NeedsPrePass(root))
            {
                SceneBinding.PrePass(_tree, root);
                if (root.State != LifetimeState.Active)
                {
                    return;
                }
            }

            Run(root, false);
        }

        internal void Dispose(Lifetime root)
        {
            var state = root.State;
            if (state == LifetimeState.Disposing || state == LifetimeState.Disposed)
            {
                return;
            }

            if (SceneBinding.NeedsPrePass(root))
            {
                SceneBinding.PrePass(_tree, root);
                state = root.State;
                if (state == LifetimeState.Disposing || state == LifetimeState.Disposed)
                {
                    return;
                }
            }

            Run(root, true);
        }

        // Lets a child created while its parent is ending join the parent's running operation.
        internal bool TryAddBorn(Lifetime child, int opId)
        {
            for (var i = _running.Count - 1; i >= 0; i--)
            {
                var buffer = _running[i];
                if (buffer.OpId == opId)
                {
                    buffer.Add(child);
                    return true;
                }
            }

            return false;
        }

        private void Run(Lifetime root, bool disposing)
        {
            var opId = NextOpId();
            var buffer = _tree.Buffers.Rent(opId);
            _running.Add(buffer);
            try
            {
                Mark(root, buffer, opId, disposing);
                Signal(buffer, opId);
                Drain(buffer, opId);
            }
            catch (Exception exception)
            {
                // Only an internal fault can land here: user code is guarded at each call site.
                _tree.ReportError(exception, LifetimeErrorSource.CancelAction, root, null, 0);
            }
            finally
            {
                FinalizeNodes(buffer, opId);
                PopRunning(buffer);
                _tree.Buffers.Return(buffer);
            }
        }

        private int NextOpId()
        {
            unchecked
            {
                _lastOpId++;
                if (_lastOpId == 0)
                {
                    _lastOpId = 1;
                }

                return _lastOpId;
            }
        }

        // Pass 1, no user code: snapshot the subtree pre-order and mark every node this operation owns.
        private static void Mark(Lifetime root, SnapshotBuffer buffer, int opId, bool disposing)
        {
            MarkSubtree(root, root, buffer, opId, disposing);
            if (!disposing)
            {
                return;
            }

            // Placed objects of a scene being disposed go with it, wherever their category lives (a scene Cancel skips this).
            for (var i = 0; i < buffer.Count; i++)
            {
                var node = buffer.Items[i];
                if (node.Kind != LifetimeKind.Scene || node.Members == null || !node.IsInOperation(opId) || node.State != LifetimeState.Disposing)
                {
                    continue;
                }

                var members = node.Members;
                for (var j = 0; j < members.Count; j++)
                {
                    MarkSubtree(members[j], root, buffer, opId, true);
                }
            }
        }

        // Walks start's subtree. A skipped node is skipped with its whole subtree; the subtree of a re-home node is
        // marked with Cancel semantics. cause is the root of the whole operation and decides who is re-homed.
        private static void MarkSubtree(Lifetime start, Lifetime cause, SnapshotBuffer buffer, int opId, bool disposing)
        {
            Lifetime cancelRoot = null;
            var node = start;
            while (true)
            {
                var descend = cancelRoot == null && disposing
                    ? TryMarkForDispose(node, start, cause, buffer, opId, ref cancelRoot)
                    : TryMark(node, buffer, opId, false);

                if (descend && node.FirstChild != null)
                {
                    node = node.FirstChild;
                    continue;
                }

                while (true)
                {
                    // node's subtree is finished here.
                    if (ReferenceEquals(node, cancelRoot))
                    {
                        cancelRoot = null;
                    }

                    if (ReferenceEquals(node, start))
                    {
                        return;
                    }

                    if (node.NextSibling != null)
                    {
                        node = node.NextSibling;
                        break;
                    }

                    node = node.Parent;
                }
            }
        }

        private static bool TryMarkForDispose(Lifetime node, Lifetime start, Lifetime cause, SnapshotBuffer buffer, int opId, ref Lifetime cancelRoot)
        {
            if (!ReferenceEquals(node, start) && SceneBinding.ShouldRehome(node, cause))
            {
                var state = node.State;
                if (state != LifetimeState.Active && state != LifetimeState.Cancelling)
                {
                    return false;
                }

                node.MarkRehome(opId);
                buffer.Add(node);
                cancelRoot = node;
                return true;
            }

            return TryMark(node, buffer, opId, true);
        }

        private static bool TryMark(Lifetime node, SnapshotBuffer buffer, int opId, bool disposing)
        {
            switch (node.State)
            {
                case LifetimeState.Active:
                    break;
                case LifetimeState.Cancelling:
                    if (!disposing)
                    {
                        return false;
                    }

                    break;
                default:
                    return false;
            }

            node.Mark(disposing ? LifetimeState.Disposing : LifetimeState.Cancelling, opId);
            buffer.Add(node);
            return true;
        }

        // Pass 2: cancel every token, pre-order, before any item is terminated.
        private static void Signal(SnapshotBuffer buffer, int opId)
        {
            for (var i = 0; i < buffer.Count; i++)
            {
                var node = buffer.Items[i];
                if (node.IsInOperation(opId))
                {
                    node.SignalToken();
                }
            }
        }

        // Pass 3: deepest first, newest first. Nodes born during the operation have no entries.
        private static void Drain(SnapshotBuffer buffer, int opId)
        {
            for (var i = buffer.Count - 1; i >= 0; i--)
            {
                var node = buffer.Items[i];
                if (node.IsInOperation(opId))
                {
                    node.DrainEntries(opId);
                }
            }
        }

        // Pass 4, no user code: a node another operation took over is left alone, so Disposed never flips back.
        private void FinalizeNodes(SnapshotBuffer buffer, int opId)
        {
            for (var i = buffer.Count - 1; i >= 0; i--)
            {
                var node = buffer.Items[i];
                if (!node.IsInOperation(opId))
                {
                    continue;
                }

                if (node.State == LifetimeState.Disposing)
                {
                    node.FinishDispose();
                }
                else if (node.IsRehome)
                {
                    FinishRehome(node);
                }
                else
                {
                    node.FinishCancel();
                }
            }
        }

        // The category is gone: the object lifetime opens a fresh generation under its scene lifetime (App for
        // DontDestroyOnLoad). If its owner, scene or App is going away, it ends with them instead.
        private void FinishRehome(Lifetime node)
        {
            Lifetime home;
            try
            {
                home = SceneBinding.ResolveHome(_tree, node);
            }
            catch (Exception exception)
            {
                _tree.ReportError(exception, LifetimeErrorSource.CancelAction, node, null, 0);
                home = _tree.App;
            }

            if (home == null || home.State == LifetimeState.Disposing || home.State == LifetimeState.Disposed)
            {
                FinishDisposeSubtree(node);
                return;
            }

            node.FinishCancel();
            node.Unlink();
            home.AttachChild(node);
            node.Placed = false;
            node.Rehomed = true;
            node.DetachMembership();
            _tree.RehomeCount++;
            UnityBindingHooks.Rehomed(node);
        }

        // No home: the node ends with the areas below it, which were finalized as cancels and hold no entries.
        private static void FinishDisposeSubtree(Lifetime root)
        {
            var node = root;
            while (true)
            {
                var child = FirstActiveChild(node);
                if (child != null)
                {
                    node = child;
                    continue;
                }

                var parent = node.Parent;
                node.FinishDispose();
                if (ReferenceEquals(node, root))
                {
                    return;
                }

                node = parent;
            }
        }

        private static Lifetime FirstActiveChild(Lifetime parent)
        {
            for (var child = parent.FirstChild; child != null; child = child.NextSibling)
            {
                if (child.State == LifetimeState.Active)
                {
                    return child;
                }
            }

            return null;
        }

        private void PopRunning(SnapshotBuffer buffer)
        {
            for (var i = _running.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_running[i], buffer))
                {
                    _running.RemoveAt(i);
                    return;
                }
            }
        }
    }
}
