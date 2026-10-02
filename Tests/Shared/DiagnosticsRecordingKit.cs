#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests
{
    // A recorded warning as the tests see it. A plain copy, so no internal type crosses an assembly boundary.
    public sealed class RecordedDiagnostic
    {
        public string Id;
        public string Message;
        public Lifetime Lifetime;
        public string LifetimeLabel;
        public UnityEngine.Object Context;
        public string Member;
        public int Line;
        public int Count;
        public int FirstFrame;
        public int LastFrame;
        public bool IsInfo;
    }

    // Per-test isolation of the editor diagnostics: nothing recorded, defaults restored, and (unless asked otherwise) frames and
    // the overrun scan under the test's control. Frame moves only through AdvanceFrames; the scan runs only through RunScheduledScan.
    public sealed class DiagnosticsRecordingKit : IDisposable
    {
        private readonly List<DiagnosticWarning> _buffer = new List<DiagnosticWarning>(16);
        private Action _scan;

        // manualFrames false leaves the real frame counter and the real player loop in place.
        public DiagnosticsRecordingKit(bool manualFrames = true)
        {
            LifetimeDiagnostics.ResetAll();
            Frame = 100;
            if (manualFrames)
            {
                LifetimeDiagnostics.FrameProvider = ProvideFrame;
                LifetimeDiagnostics.Scheduler = CaptureScan;
            }
        }

        public int Frame { get; set; }

        // How often the core asked for the frame since the kit was created.
        public int FrameProviderCalls { get; private set; }

        // How often the core asked to schedule the overrun scan.
        public int SchedulerCalls { get; private set; }

        public bool ScanScheduled => _scan != null;

        public int Total => LifetimeDiagnostics.WarningCount;

        public void AdvanceFrames(int frames)
        {
            Frame += frames;
        }

        // Runs the scan the core asked for, once. False when none was scheduled.
        public bool RunScheduledScan()
        {
            var scan = _scan;
            if (scan == null)
            {
                return false;
            }

            _scan = null;
            scan();
            return true;
        }

        // Distinct recorded entries with this id (repeats are merged into one entry).
        public int Count(string diagnosticId)
        {
            return LifetimeDiagnostics.CountWarnings(diagnosticId);
        }

        // How many times this id was recorded, repeats included.
        public int Occurrences(string diagnosticId)
        {
            var total = 0;
            var rows = Warnings(diagnosticId);
            for (var i = 0; i < rows.Count; i++)
            {
                total += rows[i].Count;
            }

            return total;
        }

        // Copies of the recorded entries, oldest first; null returns every id.
        public List<RecordedDiagnostic> Warnings(string diagnosticId = null)
        {
            LifetimeDiagnostics.CopyWarnings(_buffer);
            var rows = new List<RecordedDiagnostic>(_buffer.Count);
            for (var i = 0; i < _buffer.Count; i++)
            {
                var source = _buffer[i];
                if (diagnosticId != null && !string.Equals(source.DiagnosticId, diagnosticId, StringComparison.Ordinal))
                {
                    continue;
                }

                rows.Add(new RecordedDiagnostic
                {
                    Id = source.DiagnosticId,
                    Message = source.Message,
                    Lifetime = source.Lifetime,
                    LifetimeLabel = source.LifetimeLabel,
                    Context = source.Context,
                    Member = source.Member,
                    Line = source.Line,
                    Count = source.Count,
                    FirstFrame = source.FirstFrame,
                    LastFrame = source.LastFrame,
                    IsInfo = source.Severity == DiagnosticSeverity.Info,
                });
            }

            return rows;
        }

        // The one entry with this id; fails with a list of everything recorded when there is none or more than one.
        public RecordedDiagnostic Single(string diagnosticId)
        {
            var rows = Warnings(diagnosticId);
            Assert.That(rows.Count, Is.EqualTo(1), "expected exactly one recorded " + diagnosticId + " but found " + rows.Count + ". Recorded: " + Describe());
            return rows[0];
        }

        public string Describe()
        {
            var rows = Warnings();
            var builder = new StringBuilder();
            for (var i = 0; i < rows.Count; i++)
            {
                builder.Append("[").Append(rows[i].Id).Append(" x").Append(rows[i].Count).Append("] ").Append(rows[i].Message).Append(" | ");
            }

            return rows.Count == 0 ? "(nothing)" : builder.ToString();
        }

        public void Dispose()
        {
            LifetimeDiagnostics.ResetAll();
        }

        private int ProvideFrame()
        {
            FrameProviderCalls++;
            return Frame;
        }

        private void CaptureScan(Action scan)
        {
            SchedulerCalls++;
            _scan = scan;
        }
    }
}
#endif
