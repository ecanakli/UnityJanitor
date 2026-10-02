using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Ecanakli.Janitor.EditorTools
{
    /// <summary>
    /// The Warnings tab: one row per recorded warning with its ID, title, lifetime, message, repeat count and first frame, and a Docs
    /// button that opens the Troubleshooting entry. Choosing a row raises <see cref="WarningChosen"/>.
    /// </summary>
    internal sealed class WarningsView : VisualElement
    {
        private readonly WarningListModel _model;
        private readonly MultiColumnListView _list;
        private readonly Label _empty;
        private int _appliedRevision = -1;
        private bool _applying;

        internal WarningsView(WarningListModel model)
        {
            _model = model;
            style.flexGrow = 1;

            _empty = new Label();
            _empty.AddToClassList(ViewHelpers.EmptyClass);

            _list = ViewHelpers.CreateList();
            _list.itemsSource = model.Rows;
            ViewHelpers.AddTextColumn<WarningRow>(_list, "id", "ID", 88f, static row => row.IdText, decorate: DecorateId);
            ViewHelpers.AddTextColumn<WarningRow>(_list, "title", "Title", 260f, static row => row.TitleText, decorate: DecorateTitle);
            ViewHelpers.AddTextColumn<WarningRow>(_list, "lifetime", "Lifetime", 170f, static row => row.LifetimeText);
            ViewHelpers.AddTextColumn<WarningRow>(_list, "message", "Message", 380f, static row => row.MessageText, stretch: true, decorate: DecorateMessage);
            ViewHelpers.AddTextColumn<WarningRow>(_list, "count", "Count", 56f, static row => row.RepeatText, numeric: true);
            ViewHelpers.AddTextColumn<WarningRow>(_list, "first", "First seen", 100f, static row => row.FirstSeenText);
            _list.columns.Add(new Column
            {
                name = "docs",
                title = "Docs",
                width = ViewHelpers.Pixels(70f),
                minWidth = ViewHelpers.Pixels(70f),
                stretchable = false,
                sortable = false,
                resizable = false,
                makeCell = MakeDocsCell,
                bindCell = BindDocsCell,
            });
            _list.selectedIndicesChanged += OnSelectedIndices;
            _list.itemsChosen += OnItemsChosen;

            Add(_empty);
            Add(_list);
        }

        /// <summary>Raised when the user selects or double-clicks a warning row.</summary>
        internal event Action<WarningRow> WarningChosen;

        /// <summary>Shows the model. <paramref name="emptyText"/> replaces the list when it is not null.</summary>
        internal void ShowModel(string emptyText)
        {
            var showEmpty = emptyText != null;
            ViewHelpers.SetDisplay(_empty, showEmpty);
            ViewHelpers.SetDisplay(_list, !showEmpty);
            if (showEmpty)
            {
                ViewHelpers.SetText(_empty, emptyText);
            }

            if (_model.Revision == _appliedRevision)
            {
                return;
            }

            _appliedRevision = _model.Revision;
            _applying = true;
            try
            {
                _list.RefreshItems();
            }
            finally
            {
                _applying = false;
            }
        }

        private static void DecorateId(WarningRow row, Label label)
        {
            label.EnableInClassList("janitor-info", row.IsInfo);
        }

        private static void DecorateTitle(WarningRow row, Label label)
        {
            if (!ReferenceEquals(label.tooltip, row.TitleText))
            {
                label.tooltip = row.TitleText;
            }
        }

        private static void DecorateMessage(WarningRow row, Label label)
        {
            if (!ReferenceEquals(label.tooltip, row.Message))
            {
                label.tooltip = row.Message;
            }
        }

        private VisualElement MakeDocsCell()
        {
            var button = new Button { text = "Docs" };
            button.AddToClassList("janitor-docs");
            button.clicked += () => OnDocsClicked(button.userData as WarningRow);
            return button;
        }

        private void BindDocsCell(VisualElement element, int index)
        {
            try
            {
                var button = element as Button;
                if (button == null || index < 0 || index >= _model.Rows.Count)
                {
                    return;
                }

                var row = _model.Rows[index];
                button.userData = row;
                button.SetEnabled(row.HasDocs);
                var tip = row.HasDocs ? row.DocsUrl : WarningRow.NoDocsTooltip;
                if (!ReferenceEquals(button.tooltip, tip))
                {
                    button.tooltip = tip;
                }
            }
            catch (Exception)
            {
                // A broken row must not break the list.
            }
        }

        private static void OnDocsClicked(WarningRow row)
        {
            if (row != null)
            {
                DocLinks.Open(row.DiagnosticId);
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
                    if (index >= 0 && index < _model.Rows.Count)
                    {
                        WarningChosen?.Invoke(_model.Rows[index]);
                    }

                    return;
                }
            }
            catch (Exception)
            {
                // A selection event must not throw into the UI.
            }
        }

        private void OnItemsChosen(IEnumerable<object> items)
        {
            try
            {
                foreach (var item in items)
                {
                    if (item is WarningRow row)
                    {
                        WarningChosen?.Invoke(row);
                    }

                    return;
                }
            }
            catch (Exception)
            {
                // A double-click event must not throw into the UI.
            }
        }
    }
}
