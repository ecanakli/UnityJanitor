#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace Ecanakli.Janitor
{
    // The warning list. Warnings are rare, so building a message string when one fires is fine; the list is bounded and
    // repeats are merged. Recording is safe from any thread (JANITOR111 is raised on a worker thread).
    public static partial class LifetimeDiagnostics
    {
        private const int MaxWarnings = 256;

        private static readonly object WarningsGate = new object();
        private static readonly List<DiagnosticWarning> WarningList = new List<DiagnosticWarning>(32);
        private static int s_warningSequence;
        private static int s_warningsVersion;

        // Bumped on every change, so the window can skip a rebuild.
        internal static int WarningsVersion => s_warningsVersion;

        internal static int WarningCount
        {
            get
            {
                lock (WarningsGate)
                {
                    return WarningList.Count;
                }
            }
        }

        // The single recording path for every warning. A repeat of the same id, lifetime and message only raises Count.
        internal static void AddWarning(string diagnosticId, Lifetime lifetime, string message, UnityEngine.Object context, string member, int line)
        {
            if (!TrackingEnabled || string.IsNullOrEmpty(diagnosticId))
            {
                return;
            }

            try
            {
                var frame = CurrentFrame();
                var text = message ?? string.Empty;
                var label = lifetime == null ? null : lifetime.Label;
                var severity = string.Equals(diagnosticId, DiagnosticIds.Rehomed, StringComparison.Ordinal) ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning;
                lock (WarningsGate)
                {
                    for (var i = 0; i < WarningList.Count; i++)
                    {
                        var existing = WarningList[i];
                        if (ReferenceEquals(existing.Lifetime, lifetime)
                            && string.Equals(existing.DiagnosticId, diagnosticId, StringComparison.Ordinal)
                            && string.Equals(existing.Message, text, StringComparison.Ordinal))
                        {
                            existing.Count++;
                            existing.LastFrame = frame;
                            WarningList[i] = existing;
                            s_warningsVersion++;
                            return;
                        }
                    }

                    if (WarningList.Count >= MaxWarnings)
                    {
                        WarningList.RemoveAt(0);
                    }

                    WarningList.Add(new DiagnosticWarning
                    {
                        Sequence = ++s_warningSequence,
                        DiagnosticId = diagnosticId,
                        Severity = severity,
                        Message = text,
                        Lifetime = lifetime,
                        LifetimeId = lifetime == null ? 0 : lifetime.DiagId,
                        LifetimeLabel = label,
                        Context = context,
                        Member = member,
                        Line = line,
                        FirstFrame = frame,
                        LastFrame = frame,
                        Count = 1,
                        Session = s_session,
                    });
                    s_warningsVersion++;
                }
            }
            catch (Exception exception)
            {
                Failed(exception);
            }
        }

        // Copies the list into a reusable buffer, oldest first.
        internal static void CopyWarnings(List<DiagnosticWarning> into)
        {
            into.Clear();
            lock (WarningsGate)
            {
                for (var i = 0; i < WarningList.Count; i++)
                {
                    into.Add(WarningList[i]);
                }
            }
        }

        // The number of warnings with this id; a convenience for tests and the window's badge.
        internal static int CountWarnings(string diagnosticId)
        {
            lock (WarningsGate)
            {
                var count = 0;
                for (var i = 0; i < WarningList.Count; i++)
                {
                    if (string.Equals(WarningList[i].DiagnosticId, diagnosticId, StringComparison.Ordinal))
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        internal static void ClearWarnings()
        {
            lock (WarningsGate)
            {
                WarningList.Clear();
                s_warningsVersion++;
            }
        }
    }
}
#endif
