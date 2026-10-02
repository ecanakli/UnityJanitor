#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Ecanakli.Janitor
{
    // JANITOR101. A task is tracked from the moment its generation ends until it finishes. The first scan that finds it still
    // running more than OverrunFrames frames later reports it, once. The scan is event driven: it is scheduled on the player loop
    // only while a task is tracked and not yet reported, so it costs nothing otherwise. Main thread only.
    internal static class TaskOverrunTracker
    {
        // A task that never finishes stays here; the cap keeps a leak from growing the list without bound.
        private const int MaxRecords = 512;

        private static readonly List<Record> Records = new List<Record>(16);
        private static readonly Action ScanAction = ScanFromLoop;
        private static bool s_scheduled;

        internal static int Count => Records.Count;

        // The generation of a task ended while it was still running. Copies what the report needs, because the pooled
        // entry object is reused as soon as the task finishes.
        internal static void Ended(LifetimeTaskEntry task)
        {
            if (!LifetimeDiagnostics.TrackingEnabled)
            {
                return;
            }

            try
            {
                // A tree being shut down (session end, application exit) ends every task at once; those records would only linger.
                if (task.Owner.Tree.DiagShuttingDown || PlayModeBootstrap.IsExiting)
                {
                    return;
                }

                if (Records.Count >= MaxRecords)
                {
                    Records.RemoveAt(0);
                }

                Records.Add(new Record
                {
                    Task = task,
                    Owner = task.Owner,
                    Member = task.Member,
                    Line = task.Line,
                    Kind = task.Kind,
                    Generation = task.Generation,
                    EndedFrame = LifetimeDiagnostics.CurrentFrame(),
                    Reported = false,
                });
                ScheduleScan();
            }
            catch (Exception exception)
            {
                LifetimeDiagnostics.Failed(exception);
            }
        }

        // The task completed, faulted or stopped. Drops its record, reported or not.
        internal static void Finished(LifetimeTaskEntry task)
        {
            if (Records.Count == 0)
            {
                return;
            }

            for (var i = Records.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(Records[i].Task, task))
                {
                    Records.RemoveAt(i);
                    return;
                }
            }
        }

        // Reports every unreported task that has run on for more than OverrunFrames frames since its generation ended.
        internal static void Scan(int frame)
        {
            var threshold = LifetimeDiagnostics.EffectiveOverrunFrames;
            for (var i = 0; i < Records.Count; i++)
            {
                var record = Records[i];
                if (record.Reported || frame - record.EndedFrame <= threshold)
                {
                    continue;
                }

                record.Reported = true;
                Records[i] = record;
                var message = "The " + record.Kind + " task started at " + (record.Member ?? "unknown") + ":" + record.Line + " is still running more than "
                    + threshold + (threshold == 1 ? " frame" : " frames") + " after the generation of the lifetime '" + (record.Owner.Label ?? "unnamed") + "' ended. "
                    + "Pass the lifetime's token to every await in it, and check cancellation inside loops. "
                    + "A task that already does so is reported too when it waits on something that observes the token late, such as a FixedUpdate await while Time.timeScale is 0 or a thread-pool job in flight; it ends when that wait does.";
                LifetimeDiagnostics.AddWarning(DiagnosticIds.TaskOverrun, record.Owner, message, record.Owner.OwnerObject, record.Member, record.Line);
            }
        }

        // The tasks of one lifetime that are past their generation, as entry views for the details pane.
        internal static void AppendOutlived(Lifetime lifetime, int frame, List<EntryView> into)
        {
            for (var i = 0; i < Records.Count; i++)
            {
                var record = Records[i];
                if (!ReferenceEquals(record.Owner, lifetime))
                {
                    continue;
                }

                into.Add(new EntryView
                {
                    Kind = EntryKind.Task,
                    LabelKind = EntryLabelKind.Task,
                    Status = EntryStatus.Outlived,
                    TaskKind = record.Kind,
                    Member = record.Member,
                    Line = record.Line,
                    Generation = record.Generation,
                    AgeFrames = frame - record.EndedFrame,
                    RegisteredFrame = -1,
                });
            }
        }

        internal static void Reset()
        {
            Records.Clear();
            s_scheduled = false;
        }

        private static void ScheduleScan()
        {
            if (s_scheduled)
            {
                return;
            }

            s_scheduled = true;
            try
            {
                var scheduler = LifetimeDiagnostics.Scheduler;
                if (scheduler != null)
                {
                    scheduler(ScanAction);
                }
                else
                {
                    PlayerLoopHelper.AddContinuation(PlayerLoopTiming.Update, ScanAction);
                }
            }
            catch (Exception)
            {
                // No player loop to schedule on: the next snapshot still scans.
                s_scheduled = false;
            }
        }

        private static void ScanFromLoop()
        {
            s_scheduled = false;
            try
            {
                Scan(LifetimeDiagnostics.CurrentFrame());
            }
            catch (Exception exception)
            {
                LifetimeDiagnostics.Failed(exception);
            }

            if (HasUnreported())
            {
                ScheduleScan();
            }
        }

        private static bool HasUnreported()
        {
            for (var i = 0; i < Records.Count; i++)
            {
                if (!Records[i].Reported)
                {
                    return true;
                }
            }

            return false;
        }

        private struct Record
        {
            internal object Task;
            internal Lifetime Owner;
            internal string Member;
            internal int Line;
            internal LifetimeTaskKind Kind;
            internal int Generation;
            internal int EndedFrame;
            internal bool Reported;
        }
    }
}
#endif
