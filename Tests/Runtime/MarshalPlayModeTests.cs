using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.Binding
{
    // Cancel, Dispose and registration.Cancel from worker threads through the production tree: the hop goes to UniTask's
    // player loop, each call runs once on the main thread, and a burst from several threads loses nothing.
    // Every wait is a frame yield with a frame cap.
    [TestFixture]
    public sealed class MarshalPlayModeTests
    {
        private const int HopFrames = 5;
        private const int LoadFrames = 600;
        private const int DrainFrames = 20;

        private BindingSession _s;
#if UNITY_EDITOR
        private DiagnosticsRecordingKit _d;
#endif

        [SetUp]
        public void SetUp()
        {
            _s = BindingSession.Begin();
#if UNITY_EDITOR
            _d = new DiagnosticsRecordingKit(manualFrames: false);
#endif
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
            {
                yield return _s.CompleteAsync();
            }
            finally
            {
#if UNITY_EDITOR
                _d.Dispose();
#endif
            }
        }

        [UnityTest]
        public IEnumerator OffThreadCancelDisposeAndRegistrationCancel_EachRunsOnceOnTheMainThreadWithinAFewFrames()
        {
            var mainThread = Thread.CurrentThread.ManagedThreadId;
            var ran = new ConcurrentQueue<Ran>();
            var cancelled = Lifetime.App.CreateChild("cancelled");
            var disposed = Lifetime.App.CreateChild("disposed");
            var held = Lifetime.App.CreateChild("held");
            cancelled.OnCancel(ran, "cancel", static (queue, label) => queue.Enqueue(new Ran(label, Thread.CurrentThread.ManagedThreadId)));
            disposed.OnCancel(ran, "dispose", static (queue, label) => queue.Enqueue(new Ran(label, Thread.CurrentThread.ManagedThreadId)));
            var registration = held.OnCancel(ran, "registration", static (queue, label) => queue.Enqueue(new Ran(label, Thread.CurrentThread.ManagedThreadId)));
            Assert.That(_s.Tree.Guard.MainThreadId, Is.EqualTo(mainThread), "premise: the test runs on the tree's main thread");

            var failure = ThreadRunner.Run(() =>
            {
                cancelled.Cancel();
                disposed.Dispose();
                registration.Cancel();
            });

            Assert.That(failure, Is.Null, "an off-thread call never throws");
            Assert.That(ran.Count, Is.Zero, "nothing runs on the worker thread");
            Assert.That(_s.Tree.Marshal.PendingCount, Is.EqualTo(3), "the three calls wait in the queue");
            Assert.That(cancelled.Generation, Is.Zero);
            Assert.That(disposed.IsDisposed, Is.False);
            Assert.That(registration.IsActive, Is.True);

            var frames = 0;
            while (ran.Count < 3 && frames < HopFrames)
            {
                yield return null;
                frames++;
            }

            Assert.That(ran.Count, Is.EqualTo(3), "all three calls ran within " + HopFrames + " frames");
            yield return BindingScenes.Frames(2);

            var items = new List<Ran>(ran);
            Assert.That(items.Count, Is.EqualTo(3), "and none ran twice");
            AssertRanOnce(items, "cancel", mainThread);
            AssertRanOnce(items, "dispose", mainThread);
            AssertRanOnce(items, "registration", mainThread);
            Assert.That(cancelled.Generation, Is.EqualTo(1));
            Assert.That(cancelled.IsDisposed, Is.False, "Cancel keeps the lifetime usable");
            Assert.That(disposed.IsDisposed, Is.True);
            Assert.That(registration.IsActive, Is.False);
            Assert.That(held.Generation, Is.Zero, "only the one item was cancelled, not its lifetime");
            Assert.That(_s.Tree.Marshal.PendingCount, Is.Zero);
            Assert.That(_s.Tree.Teardown.RunningCount, Is.Zero);
#if UNITY_EDITOR
            Assert.That(_d.Occurrences(DiagnosticIds.Marshalled), Is.EqualTo(3), "JANITOR111 is recorded once per off-thread call");
#endif
        }

        [UnityTest]
        public IEnumerator OffThreadCancelsFromSeveralThreads_WhileFramesAdvance_LoseNothingAndLeaveEveryLifetimeUsable()
        {
            const int lifetimeCount = 20;
            const int threadCount = 4;
            const int callsPerThread = 2000;
            var state = new LoadState { MainThread = Thread.CurrentThread.ManagedThreadId };
            var lifetimes = new Lifetime[lifetimeCount];
            for (var i = 0; i < lifetimeCount; i++)
            {
                lifetimes[i] = Lifetime.App.CreateChild("load" + i);
                lifetimes[i].OnCancel(state, static s => s.Visit());
            }

            var failures = new ConcurrentQueue<Exception>();
            var finished = 0;
            var workers = new Thread[threadCount];
            for (var t = 0; t < threadCount; t++)
            {
                var offset = t * 5;
                workers[t] = new Thread(() =>
                {
                    try
                    {
                        for (var call = 0; call < callsPerThread; call++)
                        {
                            lifetimes[(call + offset) % lifetimeCount].Cancel();
                            if ((call & 15) == 0)
                            {
                                Thread.Yield();
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        failures.Enqueue(exception);
                    }
                    finally
                    {
                        Interlocked.Increment(ref finished);
                    }
                });
                workers[t].IsBackground = true;
            }

            // Each call logs a development warning; the load needs the work, not eight thousand console lines.
            var filter = Debug.unityLogger.filterLogType;
            Debug.unityLogger.filterLogType = LogType.Error;
            var frames = 0;
            try
            {
                for (var t = 0; t < threadCount; t++)
                {
                    workers[t].Start();
                }

                while (Volatile.Read(ref finished) < threadCount && frames < LoadFrames)
                {
                    yield return null;
                    frames++;
                }

                var drain = 0;
                while ((_s.Tree.Marshal.PendingCount > 0 || drain < 2) && drain < DrainFrames)
                {
                    yield return null;
                    drain++;
                }
            }
            finally
            {
                Debug.unityLogger.filterLogType = filter;
            }

            Assert.That(Volatile.Read(ref finished), Is.EqualTo(threadCount), "every worker finished within " + LoadFrames + " frames");
            Assert.That(failures.Count, Is.Zero, "no worker call threw: " + FirstFailure(failures));
            Assert.That(_s.Tree.Marshal.PendingCount, Is.Zero, "the queue is empty at the end");
            Assert.That(_s.Tree.Teardown.RunningCount, Is.Zero);
            Assert.That(state.Runs, Is.EqualTo(lifetimeCount), "each lifetime's one item ran exactly once, however often it was cancelled");
            Assert.That(state.RunsOffTheMainThread, Is.Zero, "and always on the main thread");

            var generations = 0;
            for (var i = 0; i < lifetimeCount; i++)
            {
                generations += lifetimes[i].Generation;
            }

            Assert.That(generations, Is.EqualTo(threadCount * callsPerThread), "every queued Cancel ended one generation");
#if UNITY_EDITOR
            Assert.That(_d.Occurrences(DiagnosticIds.Marshalled), Is.EqualTo(threadCount * callsPerThread), "JANITOR111 is recorded for every call");
#endif

            var log = new CallLog();
            for (var i = 0; i < lifetimeCount; i++)
            {
                var lifetime = lifetimes[i];
                Assert.That(lifetime.State, Is.EqualTo(LifetimeState.Active));
                Assert.That(lifetime.Token.IsCancellationRequested, Is.False, "a live token for the new generation");
                Assert.That(lifetime.Record(log, "again" + i).IsActive, Is.True);
                lifetime.Cancel();
            }

            Assert.That(log.Count, Is.EqualTo(lifetimeCount), "a Cancel on the main thread still ends the generation at once");
        }

        private static void AssertRanOnce(List<Ran> items, string label, int mainThread)
        {
            var count = 0;
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].Label != label)
                {
                    continue;
                }

                count++;
                Assert.That(items[i].ThreadId, Is.EqualTo(mainThread), label + " must run on the main thread");
            }

            Assert.That(count, Is.EqualTo(1), label + " must run exactly once");
        }

        private static string FirstFailure(ConcurrentQueue<Exception> failures)
        {
            return failures.TryPeek(out var first) ? first.ToString() : "none";
        }

        private readonly struct Ran
        {
            internal readonly string Label;
            internal readonly int ThreadId;

            internal Ran(string label, int threadId)
            {
                Label = label;
                ThreadId = threadId;
            }
        }

        private sealed class LoadState
        {
            internal int MainThread;
            internal int Runs;
            internal int RunsOffTheMainThread;

            internal void Visit()
            {
                Runs++;
                if (Thread.CurrentThread.ManagedThreadId != MainThread)
                {
                    RunsOffTheMainThread++;
                }
            }
        }
    }
}
