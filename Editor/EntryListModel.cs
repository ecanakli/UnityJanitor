using System;
using System.Collections.Generic;

namespace Ecanakli.Janitor.EditorTools
{
    /// <summary>The identity of the selected entry. It stays valid while the entry lives, so the selection survives the list shifting.</summary>
    internal readonly struct EntryKey
    {
        internal readonly bool IsSet;
        internal readonly int Id;
        internal readonly int Version;
        internal readonly string Member;
        internal readonly int Line;
        internal readonly EntryStatus Status;

        internal EntryKey(int id, int version, string member, int line, EntryStatus status)
        {
            IsSet = true;
            Id = id;
            Version = version;
            Member = member;
            Line = line;
            Status = status;
        }
    }

    /// <summary>One entry (a registered item, or a task that outlived its generation) in the details list.</summary>
    internal sealed class EntryRow
    {
        internal const string OutlivedClass = "janitor-outlived";

        private object _labelFirst;
        private object _labelSecond;
        private EntryLabelKind _labelKind;
        private LifetimeTaskKind _taskKind;
        private string _site;
        private int _siteLine;

        // A copy of the captured view: it carries the registration handle and the stack trace.
        internal EntryView View;
        internal string ItemText;
        internal string SiteText;
        internal string GenerationText;
        internal string AgeText;
        internal string StatusText;

        internal bool IsOutlived => View.Status == EntryStatus.Outlived;

        internal bool HasTrace => View.HasTrace;

        internal EntryKey Key => new EntryKey(View.Registration.EntryId, View.Registration.EntryVersion, View.Member, View.Line, View.Status);

        internal bool Matches(in EntryKey key)
        {
            return key.IsSet
                && View.Registration.EntryId == key.Id
                && View.Registration.EntryVersion == key.Version
                && View.Line == key.Line
                && View.Status == key.Status
                && string.Equals(View.Member, key.Member, StringComparison.Ordinal);
        }

        // The label is formatted again only when the item behind it changed; the age is a cached number.
        internal void Update(in EntryView view)
        {
            View = view;

            if (ItemText == null
                || _labelKind != view.LabelKind
                || _taskKind != view.TaskKind
                || !ReferenceEquals(_labelFirst, view.A)
                || !ReferenceEquals(_labelSecond, view.B))
            {
                _labelKind = view.LabelKind;
                _taskKind = view.TaskKind;
                _labelFirst = view.A;
                _labelSecond = view.B;
                ItemText = FormatItem(in view);
            }

            if (SiteText == null || !ReferenceEquals(_site, view.Member) || _siteLine != view.Line)
            {
                _site = view.Member;
                _siteLine = view.Line;
                SiteText = view.Member == null ? CountText.Unknown : view.Member + ":" + view.Line;
            }

            GenerationText = CountText.Get(view.Generation);
            AgeText = CountText.Frames(view.AgeFrames);
            StatusText = DisplayNames.Status(view.Status);
        }

        // Drops the references an entry holds, for a row that goes back to the pool.
        internal void Release()
        {
            View = default;
            ItemText = null;
            SiteText = null;
            _labelFirst = null;
            _labelSecond = null;
            _site = null;
        }

        private static string FormatItem(in EntryView view)
        {
            try
            {
                return view.FormatLabel() ?? "Entry";
            }
            catch (Exception)
            {
                return "Entry";
            }
        }
    }

    /// <summary>
    /// The entries of the selected lifetime. Rows are pooled and updated in place; the selected entry is tracked by its key and its
    /// stack trace text is built once per selection.
    /// </summary>
    internal sealed class EntryListModel
    {
        internal const string NoTraceText = "No stack trace for this entry. Turn on Stack traces in the toolbar; it applies to entries registered afterwards.";

        private readonly List<EntryView> _buffer = new List<EntryView>(64);
        private readonly List<EntryRow> _spare = new List<EntryRow>();
        private EntryKey _selected;
        private bool _traceBuilt;

        /// <summary>The rows shown in the list, newest entry first. The same list instance for the life of the model.</summary>
        internal List<EntryRow> Rows { get; } = new List<EntryRow>();

        /// <summary>The position of the selected entry in <see cref="Rows"/>, or -1.</summary>
        internal int SelectedIndex { get; private set; } = -1;

        /// <summary>The stack trace of the selected entry, <see cref="NoTraceText"/> when it has none, or null when nothing is selected.</summary>
        internal string TraceText { get; private set; }

        /// <summary>Captures the lifetime's entries into the rows.</summary>
        internal void Update(Lifetime lifetime)
        {
            LifetimeDiagnostics.CaptureEntries(lifetime, _buffer);
            SyncRows();
            _buffer.Clear();
            ResolveSelection();
        }

        /// <summary>Empties the list and drops the selection.</summary>
        internal void Clear()
        {
            if (Rows.Count == 0 && !_selected.IsSet)
            {
                return;
            }

            _buffer.Clear();
            SyncRows();
            ClearSelection();
        }

        /// <summary>Selects the entry at a list position; an invalid position clears the selection.</summary>
        internal void SelectIndex(int index)
        {
            if (index < 0 || index >= Rows.Count)
            {
                ClearSelection();
                return;
            }

            _selected = Rows[index].Key;
            _traceBuilt = false;
            ResolveSelection();
        }

        internal void ClearSelection()
        {
            _selected = default;
            _traceBuilt = false;
            SelectedIndex = -1;
            TraceText = null;
        }

        private void SyncRows()
        {
            var count = _buffer.Count;
            while (Rows.Count > count)
            {
                var last = Rows[Rows.Count - 1];
                Rows.RemoveAt(Rows.Count - 1);
                last.Release();
                _spare.Add(last);
            }

            while (Rows.Count < count)
            {
                EntryRow row;
                if (_spare.Count > 0)
                {
                    row = _spare[_spare.Count - 1];
                    _spare.RemoveAt(_spare.Count - 1);
                }
                else
                {
                    row = new EntryRow();
                }

                Rows.Add(row);
            }

            for (var i = 0; i < count; i++)
            {
                var view = _buffer[i];
                Rows[i].Update(in view);
            }
        }

        private void ResolveSelection()
        {
            SelectedIndex = -1;
            if (!_selected.IsSet)
            {
                TraceText = null;
                return;
            }

            for (var i = 0; i < Rows.Count; i++)
            {
                if (Rows[i].Matches(in _selected))
                {
                    SelectedIndex = i;
                    break;
                }
            }

            if (SelectedIndex < 0)
            {
                // The entry ended: nothing is selected any more.
                ClearSelection();
                return;
            }

            if (!_traceBuilt)
            {
                var row = Rows[SelectedIndex];
                TraceText = row.HasTrace ? FormatTrace(row) : NoTraceText;
                _traceBuilt = true;
            }
        }

        private static string FormatTrace(EntryRow row)
        {
            try
            {
                return row.View.FormatTrace() ?? NoTraceText;
            }
            catch (Exception exception)
            {
                return "The stack trace could not be formatted: " + exception.Message;
            }
        }
    }
}
