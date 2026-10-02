using System;
using System.Collections.Generic;

namespace Ecanakli.Janitor.EditorTools
{
    /// <summary>The columns of the lifetime tree, in display order. The values are the column indices.</summary>
    internal enum TreeColumn
    {
        None = -1,
        Name = 0,
        Kind = 1,
        State = 2,
        Generation = 3,
        Tasks = 4,
        Tweens = 5,
        Coroutines = 6,
        Subscriptions = 7,
        Other = 8,
        Children = 9,
        Age = 10,
    }

    /// <summary>
    /// Turns a diagnostics snapshot into the rows of the lifetime tree. It keeps one row object per lifetime and updates it in
    /// place, applies the text filter and the sort, and bumps <see cref="StructureVersion"/> only when the visible rows or
    /// their order changed, so the view rebuilds the tree only then. It has no UI types, so it can be tested without a window.
    /// A refresh allocates nothing in steady state: every buffer keeps its capacity.
    /// </summary>
    internal sealed class LifetimeTreeModel
    {
        private readonly Dictionary<int, LifetimeRow> _rows = new Dictionary<int, LifetimeRow>();
        private readonly List<LifetimeRow> _all = new List<LifetimeRow>(256);
        private readonly List<LifetimeRow> _shown = new List<LifetimeRow>(256);
        private readonly List<int> _purge = new List<int>();
        private readonly List<int> _stack = new List<int>();
        private readonly List<int> _siblings = new List<int>();

        // The ids and depths of the shown rows, in order: equal signatures mean an identical tree.
        private List<int> _signature = new List<int>(512);
        private List<int> _nextSignature = new List<int>(512);

        // Per-pass buffers indexed by the position in _all; one extra slot is the virtual root of the sorted walk.
        private int[] _parentIndex = new int[257];
        private int[] _firstChild = new int[257];
        private int[] _nextSibling = new int[257];
        private bool[] _visible = new bool[257];

        private int _stamp;
        private string _filter = string.Empty;

        /// <summary>Changes whenever the shown rows, their order or their depths change.</summary>
        internal int StructureVersion { get; private set; }

        /// <summary>The rows to display, in tree (pre-order) order, with the filter and the sort applied.</summary>
        internal IReadOnlyList<LifetimeRow> Shown => _shown;

        /// <summary>The number of lifetimes in the last snapshot, before filtering.</summary>
        internal int TotalCount => _all.Count;

        internal TreeColumn SortColumn { get; private set; } = TreeColumn.None;

        internal bool SortDescending { get; private set; }

        /// <summary>The filter text, trimmed. Matching rows stay visible together with their ancestors.</summary>
        internal string Filter
        {
            get => _filter;
            set => _filter = (value ?? string.Empty).Trim();
        }

        internal bool HasFilter => _filter.Length > 0;

        /// <summary>Sorts the children of every node by a column. <see cref="TreeColumn.None"/> restores creation order.</summary>
        internal void SetSort(TreeColumn column, bool descending)
        {
            SortColumn = column;
            SortDescending = descending;
        }

        /// <summary>The row of a lifetime id, or null. Hidden rows are found too.</summary>
        internal LifetimeRow Find(int id)
        {
            return _rows.TryGetValue(id, out var row) ? row : null;
        }

        /// <summary>
        /// Rebuilds the rows from the snapshot nodes; null means no tree. Call it again after changing the filter or the sort
        /// to apply them to the same data.
        /// </summary>
        internal void Update(DiagnosticsSnapshot snapshot)
        {
            _stamp++;
            _all.Clear();

            var nodes = snapshot == null ? null : snapshot.Nodes;
            var count = nodes == null ? 0 : nodes.Count;
            EnsureCapacity(count);

            for (var i = 0; i < count; i++)
            {
                var node = nodes[i];
                if (!_rows.TryGetValue(node.Id, out var row))
                {
                    row = new LifetimeRow();
                    _rows.Add(node.Id, row);
                }
                else if (row.Stamp == _stamp)
                {
                    // The same id twice in one snapshot: keep the first.
                    continue;
                }

                row.Update(in node);
                row.Stamp = _stamp;
                row.Index = _all.Count;

                // The parent is resolved by id, so a snapshot with odd depths still gives a consistent tree.
                var parentIndex = -1;
                var depth = 0;
                if (node.ParentId != 0 && _rows.TryGetValue(node.ParentId, out var parent) && parent.Stamp == _stamp)
                {
                    parentIndex = parent.Index;
                    depth = parent.Depth + 1;
                }

                row.Depth = depth;
                _parentIndex[row.Index] = parentIndex;
                _all.Add(row);
            }

            if (_rows.Count != _all.Count)
            {
                PurgeStale();
            }

            ComputeVisibility();
            BuildShown();
            UpdateSignature();
        }

        private void PurgeStale()
        {
            _purge.Clear();
            foreach (var pair in _rows)
            {
                if (pair.Value.Stamp != _stamp)
                {
                    _purge.Add(pair.Key);
                }
            }

            for (var i = 0; i < _purge.Count; i++)
            {
                _rows.Remove(_purge[i]);
            }
        }

        // A row is visible when it matches, or when a descendant does. Parents precede children, so one reverse pass is enough.
        private void ComputeVisibility()
        {
            var count = _all.Count;
            for (var i = 0; i < count; i++)
            {
                _visible[i] = !HasFilter || _all[i].Matches(_filter);
            }

            if (HasFilter)
            {
                for (var i = count - 1; i >= 0; i--)
                {
                    var parent = _parentIndex[i];
                    if (_visible[i] && parent >= 0)
                    {
                        _visible[parent] = true;
                    }
                }
            }

            for (var i = 0; i < count; i++)
            {
                _all[i].Shown = _visible[i];
            }
        }

        private void BuildShown()
        {
            _shown.Clear();
            var count = _all.Count;
            if (SortColumn == TreeColumn.None)
            {
                for (var i = 0; i < count; i++)
                {
                    if (_visible[i])
                    {
                        _shown.Add(_all[i]);
                    }
                }

                return;
            }

            // Sorted: link the visible children of every node (creation order), sort each group, then walk pre-order.
            var root = count;
            for (var i = 0; i <= count; i++)
            {
                _firstChild[i] = -1;
            }

            for (var i = count - 1; i >= 0; i--)
            {
                if (!_visible[i])
                {
                    continue;
                }

                var parent = _parentIndex[i] < 0 ? root : _parentIndex[i];
                _nextSibling[i] = _firstChild[parent];
                _firstChild[parent] = i;
            }

            for (var i = 0; i <= count; i++)
            {
                SortChildren(i);
            }

            _stack.Clear();
            var current = _firstChild[root];
            while (current >= 0 || _stack.Count > 0)
            {
                if (current < 0)
                {
                    var top = _stack[_stack.Count - 1];
                    _stack.RemoveAt(_stack.Count - 1);
                    current = _nextSibling[top];
                    continue;
                }

                _shown.Add(_all[current]);
                var child = _firstChild[current];
                if (child >= 0)
                {
                    _stack.Add(current);
                    current = child;
                }
                else
                {
                    current = _nextSibling[current];
                }
            }
        }

        private void SortChildren(int parent)
        {
            var first = _firstChild[parent];
            if (first < 0 || _nextSibling[first] < 0)
            {
                return;
            }

            _siblings.Clear();
            for (var child = first; child >= 0; child = _nextSibling[child])
            {
                _siblings.Add(child);
            }

            // Insertion sort: stable and allocation-free (List.Sort allocates on Mono); O(k^2) per sibling group, fine for typical child counts.
            for (var i = 1; i < _siblings.Count; i++)
            {
                var value = _siblings[i];
                var j = i - 1;
                while (j >= 0 && CompareRows(_siblings[j], value) > 0)
                {
                    _siblings[j + 1] = _siblings[j];
                    j--;
                }

                _siblings[j + 1] = value;
            }

            for (var i = 0; i < _siblings.Count; i++)
            {
                _nextSibling[_siblings[i]] = i + 1 < _siblings.Count ? _siblings[i + 1] : -1;
            }

            _firstChild[parent] = _siblings[0];
        }

        // Descending swaps the operands, so equal rows stay equal and the stable sort keeps their creation order either way.
        private int CompareRows(int left, int right)
        {
            return SortDescending
                ? CompareColumn(_all[right], _all[left], SortColumn)
                : CompareColumn(_all[left], _all[right], SortColumn);
        }

        internal static int CompareColumn(LifetimeRow a, LifetimeRow b, TreeColumn column)
        {
            switch (column)
            {
                case TreeColumn.Name:
                    return string.Compare(a.NameText, b.NameText, StringComparison.OrdinalIgnoreCase);
                case TreeColumn.Kind:
                    return ((int)a.Kind).CompareTo((int)b.Kind);
                case TreeColumn.State:
                    return ((int)a.State).CompareTo((int)b.State);
                case TreeColumn.None:
                    return 0;
                default:
                    return a.CountOf(column).CompareTo(b.CountOf(column));
            }
        }

        private void UpdateSignature()
        {
            _nextSignature.Clear();
            for (var i = 0; i < _shown.Count; i++)
            {
                _nextSignature.Add(_shown[i].Id);
                _nextSignature.Add(_shown[i].Depth);
            }

            var same = _nextSignature.Count == _signature.Count;
            for (var i = 0; same && i < _signature.Count; i++)
            {
                same = _nextSignature[i] == _signature[i];
            }

            if (same)
            {
                return;
            }

            var swap = _signature;
            _signature = _nextSignature;
            _nextSignature = swap;
            StructureVersion++;
        }

        private void EnsureCapacity(int count)
        {
            var needed = count + 1;
            if (_parentIndex.Length >= needed)
            {
                return;
            }

            var size = Math.Max(needed, _parentIndex.Length * 2);
            Array.Resize(ref _parentIndex, size);
            Array.Resize(ref _firstChild, size);
            Array.Resize(ref _nextSibling, size);
            Array.Resize(ref _visible, size);
        }
    }
}
