using System;
using UnityEngine.UIElements;

namespace Ecanakli.Janitor.EditorTools
{
    /// <summary>Small UI Toolkit helpers shared by the window's views: cells, columns, text and visibility.</summary>
    internal static class ViewHelpers
    {
        internal const string CellClass = "janitor-cell";
        internal const string NumberCellClass = "janitor-cell--number";
        internal const string EmptyClass = "janitor-empty";
        internal const float RowHeight = 20f;
        internal const float MinColumnWidth = 36f;

        internal static Length Pixels(float value)
        {
            return new Length(value, LengthUnit.Pixel);
        }

        /// <summary>Shows or hides an element without removing it from the tree.</summary>
        internal static void SetDisplay(VisualElement element, bool visible)
        {
            if (element != null)
            {
                element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>Sets a label's text only when it differs, so rebinding an unchanged row does no work.</summary>
        internal static void SetText(Label label, string text)
        {
            if (label == null)
            {
                return;
            }

            var value = text ?? string.Empty;
            if (!string.Equals(label.text, value))
            {
                label.text = value;
            }
        }

        internal static Label MakeLabelCell(bool numeric)
        {
            var label = new Label();
            label.AddToClassList(CellClass);
            if (numeric)
            {
                label.AddToClassList(NumberCellClass);
            }

            return label;
        }

        /// <summary>A list with the shared look: single selection, fixed row height, alternating rows.</summary>
        internal static MultiColumnListView CreateList()
        {
            var list = new MultiColumnListView
            {
                selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.All,
                fixedItemHeight = RowHeight,
            };
            list.style.flexGrow = 1;
            return list;
        }

        /// <summary>
        /// Adds a text column to a list. <paramref name="text"/> reads the cell text from a row; <paramref name="decorate"/> may toggle
        /// style classes or set a tooltip. The cell callbacks never throw.
        /// </summary>
        internal static void AddTextColumn<T>(
            MultiColumnListView list,
            string name,
            string title,
            float width,
            Func<T, string> text,
            bool numeric = false,
            bool stretch = false,
            Action<T, Label> decorate = null)
            where T : class
        {
            list.columns.Add(new Column
            {
                name = name,
                title = title,
                width = Pixels(width),
                minWidth = Pixels(MinColumnWidth),
                stretchable = stretch,
                sortable = false,
                resizable = true,
                makeCell = () => MakeLabelCell(numeric),
                bindCell = (element, index) => BindTextCell(list, element, index, text, decorate),
            });
        }

        private static void BindTextCell<T>(MultiColumnListView list, VisualElement element, int index, Func<T, string> text, Action<T, Label> decorate)
            where T : class
        {
            try
            {
                var source = list.itemsSource;
                if (source == null || index < 0 || index >= source.Count)
                {
                    return;
                }

                var label = element as Label;
                var item = source[index] as T;
                if (label == null || item == null)
                {
                    return;
                }

                SetText(label, text(item));
                decorate?.Invoke(item, label);
            }
            catch (Exception)
            {
                // A broken row must not break the list; the cell keeps its old text.
            }
        }
    }
}
