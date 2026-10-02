#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ecanakli.Janitor
{
    // A script reload (the editor reloads the domain around Play Mode) wipes the statics. The core cannot use SessionState,
    // so the editor window carries the data across: ExportState before the reload, ImportState after it.
    // Objects and lifetime references are not persisted; the labels and counters are.
    public static partial class LifetimeDiagnostics
    {
        // The only format ImportState accepts.
        private const int StateVersion = 1;

        // A JSON string with the recently disposed buffer and the warning list, or null on failure. Never throws.
        internal static string ExportState()
        {
            try
            {
                var recent = new List<RecentLifetime>(RecentCapacity);
                CopyRecent(recent);

                // Oldest first, so ImportState pushes them back in order.
                var persistedRecent = new PersistedRecentLifetime[recent.Count];
                for (var i = 0; i < recent.Count; i++)
                {
                    var source = recent[recent.Count - 1 - i];
                    persistedRecent[i] = new PersistedRecentLifetime
                    {
                        Id = source.Id,
                        ParentId = source.ParentId,
                        Kind = (int)source.Kind,
                        Generation = source.Generation,
                        CancelCount = source.CancelCount,
                        TotalRegistered = source.TotalRegistered,
                        RefusedCount = source.RefusedCount,
                        CreatedFrame = source.CreatedFrame,
                        DisposedFrame = source.DisposedFrame,
                        Session = source.Session,
                        Label = source.FormatLabel(),
                    };
                }

                var warnings = new List<DiagnosticWarning>(32);
                CopyWarnings(warnings);
                var persistedWarnings = new PersistedWarning[warnings.Count];
                for (var i = 0; i < warnings.Count; i++)
                {
                    var source = warnings[i];
                    persistedWarnings[i] = new PersistedWarning
                    {
                        Sequence = source.Sequence,
                        DiagnosticId = source.DiagnosticId,
                        Severity = (int)source.Severity,
                        Message = source.Message,
                        LifetimeId = source.LifetimeId,
                        LifetimeLabel = source.LifetimeLabel,
                        Member = source.Member,
                        Line = source.Line,
                        FirstFrame = source.FirstFrame,
                        LastFrame = source.LastFrame,
                        Count = source.Count,
                        Session = source.Session,
                    };
                }

                return JsonUtility.ToJson(new PersistedDiagnostics { Version = StateVersion, Session = s_session, Recent = persistedRecent, Warnings = persistedWarnings });
            }
            catch (Exception exception)
            {
                Failed(exception);
                return null;
            }
        }

        // Replaces the recently disposed buffer and the warning list with the exported data. A null, empty, unreadable or
        // wrong-shaped string (no or another format version) changes nothing. Never throws.
        internal static void ImportState(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            try
            {
                var state = JsonUtility.FromJson<PersistedDiagnostics>(json);
                if (state == null || state.Version != StateVersion)
                {
                    return;
                }

                if (state.Session > s_session)
                {
                    s_session = state.Session;
                }

                ClearRecent();
                var recent = state.Recent;
                for (var i = 0; recent != null && i < recent.Length; i++)
                {
                    var source = recent[i];
                    PushRecent(new RecentLifetime
                    {
                        Id = source.Id,
                        ParentId = source.ParentId,
                        Kind = (LifetimeKind)source.Kind,
                        Generation = source.Generation,
                        CancelCount = source.CancelCount,
                        TotalRegistered = source.TotalRegistered,
                        RefusedCount = source.RefusedCount,
                        CreatedFrame = source.CreatedFrame,
                        DisposedFrame = source.DisposedFrame,
                        Session = source.Session,
                        LabelText = source.Label,
                    });
                }

                lock (WarningsGate)
                {
                    WarningList.Clear();
                    var warnings = state.Warnings;
                    for (var i = 0; warnings != null && i < warnings.Length; i++)
                    {
                        var source = warnings[i];
                        WarningList.Add(new DiagnosticWarning
                        {
                            Sequence = source.Sequence,
                            DiagnosticId = source.DiagnosticId,
                            Severity = (DiagnosticSeverity)source.Severity,
                            Message = source.Message,
                            LifetimeId = source.LifetimeId,
                            LifetimeLabel = NullIfEmpty(source.LifetimeLabel),
                            Member = NullIfEmpty(source.Member),
                            Line = source.Line,
                            FirstFrame = source.FirstFrame,
                            LastFrame = source.LastFrame,
                            Count = source.Count,
                            Session = source.Session,
                        });

                        if (source.Sequence > s_warningSequence)
                        {
                            s_warningSequence = source.Sequence;
                        }
                    }

                    s_warningsVersion++;
                }
            }
            catch (Exception exception)
            {
                Failed(exception);
            }
        }

        // JsonUtility writes a null string back as "", and the window tells "no label" and "no call site" by null.
        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
#endif
