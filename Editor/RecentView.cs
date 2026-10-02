using UnityEngine.UIElements;

namespace Ecanakli.Janitor.EditorTools
{
    /// <summary>The "Recently disposed" tab: the ring of the last disposed lifetimes, newest first.</summary>
    internal sealed class RecentView : VisualElement
    {
        private readonly RecentListModel _model;
        private readonly MultiColumnListView _list;
        private readonly Label _empty;
        private int _appliedRevision = -1;

        internal RecentView(RecentListModel model)
        {
            _model = model;
            style.flexGrow = 1;

            _empty = new Label();
            _empty.AddToClassList(ViewHelpers.EmptyClass);

            _list = ViewHelpers.CreateList();
            _list.itemsSource = model.Rows;
            ViewHelpers.AddTextColumn<RecentRow>(_list, "name", "Name", 230f, static row => row.NameText, stretch: true);
            ViewHelpers.AddTextColumn<RecentRow>(_list, "kind", "Kind", 84f, static row => row.KindText);
            ViewHelpers.AddTextColumn<RecentRow>(_list, "generation", "Generation", 78f, static row => row.GenerationText, numeric: true);
            ViewHelpers.AddTextColumn<RecentRow>(_list, "cancels", "Cancels", 62f, static row => row.CancelsText, numeric: true);
            ViewHelpers.AddTextColumn<RecentRow>(_list, "registered", "Registered", 80f, static row => row.RegisteredText, numeric: true);
            ViewHelpers.AddTextColumn<RecentRow>(_list, "refused", "Refused", 62f, static row => row.RefusedText, numeric: true);
            ViewHelpers.AddTextColumn<RecentRow>(_list, "lived", "Lived (frames)", 100f, static row => row.LivedText, numeric: true);
            ViewHelpers.AddTextColumn<RecentRow>(_list, "disposed", "Disposed (frame)", 110f, static row => row.DisposedText, numeric: true);
            ViewHelpers.AddTextColumn<RecentRow>(_list, "session", "Session", 64f, static row => row.SessionText, numeric: true);

            Add(_empty);
            Add(_list);
        }

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
            _list.RefreshItems();
        }
    }
}
