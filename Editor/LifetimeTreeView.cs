using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Ecanakli.Janitor.EditorTools
{
    /// <summary>
    /// The lifetime tree: a <see cref="MultiColumnTreeView"/> bound to a <see cref="LifetimeTreeModel"/>. The tree items point at the
    /// model's reusable rows, so while the structure is unchanged a refresh only rebinds the visible cells.
    /// </summary>
    internal sealed class LifetimeTreeView : VisualElement
    {
        private const int ExpandAllThreshold = 16;
        private const int PruneThreshold = 512;

        private readonly LifetimeTreeModel _model;
        private readonly MultiColumnTreeView _tree;
        private readonly Label _empty;
        private readonly HashSet<int> _autoExpanded = new HashSet<int>();
        private readonly List<int> _toExpand = new List<int>();
        private readonly Predicate<int> _isGone;
        private int _appliedStructure = -1;
        private bool _firstPopulation = true;
        private bool _applying;

        internal LifetimeTreeView(LifetimeTreeModel model)
        {
            _model = model;
            _isGone = id => _model.Find(id) == null;
            style.flexGrow = 1;

            _tree = new MultiColumnTreeView
            {
                selectionType = SelectionType.Single,
                fixedItemHeight = ViewHelpers.RowHeight,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                sortingMode = ColumnSortingMode.Custom,
            };
            _tree.style.flexGrow = 1;
            AddColumns();
            _tree.selectedIndicesChanged += OnSelectedIndices;
            _tree.columnSortingChanged += OnColumnSortingChanged;

            _empty = new Label();
            _empty.AddToClassList(ViewHelpers.EmptyClass);

            Add(_empty);
            Add(_tree);
        }

        /// <summary>Raised with the lifetime id when the user selects a row.</summary>
        internal event Action<int> LifetimeSelected;

        /// <summary>Raised after the user changed the sorted column; the model already has the new sort.</summary>
        internal event Action SortChanged;

        /// <summary>
        /// Shows the model. <paramref name="emptyText"/> replaces the tree when it is not null. The tree is rebuilt only when the
        /// model's structure changed; otherwise the visible cells are rebound.
        /// </summary>
        internal void ShowModel(string emptyText, int selectedId)
        {
            var showEmpty = emptyText != null;
            ViewHelpers.SetDisplay(_empty, showEmpty);
            ViewHelpers.SetDisplay(_tree, !showEmpty);
            if (showEmpty)
            {
                ViewHelpers.SetText(_empty, emptyText);
            }

            _applying = true;
            try
            {
                if (_model.StructureVersion != _appliedStructure)
                {
                    Rebuild();
                    _appliedStructure = _model.StructureVersion;
                    RestoreSelection(selectedId);
                }
                else if (!showEmpty)
                {
                    _tree.RefreshItems();
                }
            }
            finally
            {
                _applying = false;
            }
        }

        /// <summary>Selects a lifetime in the tree, expanding its ancestors and scrolling to it. Does nothing for a hidden or missing row.</summary>
        internal void SelectLifetimeRow(int id)
        {
            var row = _model.Find(id);
            if (row == null || !row.Shown)
            {
                return;
            }

            _applying = true;
            try
            {
                ExpandAncestors(row);
                _tree.SetSelectionById(id);
                _tree.ScrollToItemById(id);
            }
            catch (Exception)
            {
                // Selection is a convenience; a failure leaves the tree as it was.
            }
            finally
            {
                _applying = false;
            }
        }

        private void AddColumns()
        {
            AddColumn("name", "Name", 240f, TreeColumn.Name, false);
            AddColumn("kind", "Kind", 84f, TreeColumn.Kind, false);
            AddColumn("state", "State", 120f, TreeColumn.State, false);
            AddColumn("generation", "Generation", 78f, TreeColumn.Generation, true);
            AddColumn("tasks", "Tasks", 56f, TreeColumn.Tasks, true);
            AddColumn("tweens", "Tweens", 60f, TreeColumn.Tweens, true);
            AddColumn("coroutines", "Coroutines", 78f, TreeColumn.Coroutines, true);
            AddColumn("subscriptions", "Subscriptions", 94f, TreeColumn.Subscriptions, true);
            AddColumn("other", "Other", 54f, TreeColumn.Other, true);
            AddColumn("children", "Children", 68f, TreeColumn.Children, true);
            AddColumn("age", "Age (frames)", 90f, TreeColumn.Age, true);
        }

        private void AddColumn(string name, string title, float width, TreeColumn column, bool numeric)
        {
            _tree.columns.Add(new Column
            {
                name = name,
                title = title,
                width = ViewHelpers.Pixels(width),
                minWidth = ViewHelpers.Pixels(ViewHelpers.MinColumnWidth),
                stretchable = column == TreeColumn.Name,
                sortable = true,
                resizable = true,
                makeCell = () => ViewHelpers.MakeLabelCell(numeric),
                bindCell = (element, index) => BindCell(column, element, index),
            });
        }

        private void BindCell(TreeColumn column, VisualElement element, int index)
        {
            try
            {
                var label = element as Label;
                var row = _tree.GetItemDataForIndex<LifetimeRow>(index);
                if (label == null || row == null)
                {
                    return;
                }

                ViewHelpers.SetText(label, row.TextOf(column));
                switch (column)
                {
                    case TreeColumn.Name:
                        label.EnableInClassList("janitor-disposed", row.IsDisposed);
                        break;
                    case TreeColumn.State:
                        label.EnableInClassList("janitor-orphan", row.OwnerDestroyed && !row.IsDisposed);
                        break;
                    case TreeColumn.Tasks:
                    case TreeColumn.Tweens:
                    case TreeColumn.Coroutines:
                    case TreeColumn.Subscriptions:
                    case TreeColumn.Other:
                    case TreeColumn.Children:
                        label.EnableInClassList("janitor-zero", row.CountOf(column) == 0);
                        break;
                }
            }
            catch (Exception)
            {
                // A broken row must not break the tree; the cell keeps its old text.
            }
        }

        private void Rebuild()
        {
            var shown = _model.Shown;
            var roots = new List<TreeViewItemData<LifetimeRow>>();
            var index = 0;
            while (index < shown.Count)
            {
                var next = BuildLevel(shown, index, shown[index].Depth, roots);
                index = next > index ? next : index + 1;
            }

            _tree.SetRootItems(roots);
            _tree.Rebuild();
            ExpandNew(shown);
            Prune(shown.Count);
        }

        // The rows are in pre-order, so a level ends at the first row that is shallower than it.
        private static int BuildLevel(IReadOnlyList<LifetimeRow> rows, int start, int depth, List<TreeViewItemData<LifetimeRow>> into)
        {
            var i = start;
            while (i < rows.Count && rows[i].Depth == depth)
            {
                var row = rows[i];
                i++;
                List<TreeViewItemData<LifetimeRow>> children = null;
                if (i < rows.Count && rows[i].Depth > depth)
                {
                    children = new List<TreeViewItemData<LifetimeRow>>();
                    i = BuildLevel(rows, i, depth + 1, children);
                }

                into.Add(new TreeViewItemData<LifetimeRow>(row.Id, row, children));
            }

            return i;
        }

        // A parent is expanded the first time it has children; after that the user's collapse is respected.
        private void ExpandNew(IReadOnlyList<LifetimeRow> shown)
        {
            _toExpand.Clear();
            for (var i = 0; i + 1 < shown.Count; i++)
            {
                if (shown[i + 1].Depth > shown[i].Depth && _autoExpanded.Add(shown[i].Id))
                {
                    _toExpand.Add(shown[i].Id);
                }
            }

            if (_toExpand.Count == 0)
            {
                return;
            }

            if (_firstPopulation || _toExpand.Count > ExpandAllThreshold)
            {
                _tree.ExpandAll();
            }
            else
            {
                for (var i = 0; i < _toExpand.Count; i++)
                {
                    _tree.ExpandItem(_toExpand[i]);
                }
            }

            _firstPopulation = false;
        }

        private void Prune(int shownCount)
        {
            if (_autoExpanded.Count > PruneThreshold && _autoExpanded.Count > shownCount * 4)
            {
                _autoExpanded.RemoveWhere(_isGone);
            }
        }

        private void RestoreSelection(int selectedId)
        {
            if (selectedId == 0)
            {
                return;
            }

            var row = _model.Find(selectedId);
            if (row != null && row.Shown)
            {
                _tree.SetSelectionById(selectedId);
            }
        }

        private void ExpandAncestors(LifetimeRow row)
        {
            var parentId = row.ParentId;
            for (var guard = 0; parentId != 0 && guard < 64; guard++)
            {
                if (!_tree.IsExpanded(parentId))
                {
                    _tree.ExpandItem(parentId);
                }

                var parent = _model.Find(parentId);
                if (parent == null)
                {
                    break;
                }

                parentId = parent.ParentId;
            }
        }

        private void OnSelectedIndices(IEnumerable<int> indices)
        {
            if (_applying)
            {
                return;
            }

            try
            {
                foreach (var index in indices)
                {
                    LifetimeSelected?.Invoke(_tree.GetIdForIndex(index));
                    return;
                }
            }
            catch (Exception)
            {
                // A selection event must not throw into the UI.
            }
        }

        private void OnColumnSortingChanged()
        {
            try
            {
                var column = TreeColumn.None;
                var descending = false;
                foreach (var description in _tree.sortedColumns)
                {
                    column = (TreeColumn)description.columnIndex;
                    descending = description.direction == SortDirection.Descending;
                    break;
                }

                _model.SetSort(column, descending);
                SortChanged?.Invoke();
            }
            catch (Exception)
            {
                // A sort event must not throw into the UI.
            }
        }
    }
}
