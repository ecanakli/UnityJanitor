using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests
{
    // Run routes failures, stays silent on cancellation, never starts on an ending lifetime and removes its entry.
    [TestFixture]
    public sealed class LifetimeRunTests
    {
        private TestScope _t;
        private Lifetime _area;

        [SetUp]
        public void SetUp()
        {
            _t = new TestScope();
            _area = _t.App.CreateChild("area");
        }

        [TearDown]
        public void TearDown()
        {
            _t.Complete();
        }

        // Completion and entry removal

        [Test]
        public void Run_SynchronouslyCompletedTask_RunsTheWorkAndRemovesTheEntryAtOnce()
        {
            var counter = new Counter();

            _area.Run(ct =>
            {
                counter.Value++;
                return UniTask.CompletedTask;
            });

            Assert.That(counter.Value, Is.EqualTo(1));
            Assert.That(_area.EntryCount, Is.Zero, "a synchronous completion must remove the entry before Run returns");
        }

        [Test]
        public void Run_Work_ReceivesTheCurrentGenerationToken()
        {
            var seen = default(CancellationToken);

            _area.Run(ct =>
            {
                seen = ct;
                return UniTask.CompletedTask;
            });

            Assert.That(seen, Is.EqualTo(_area.Token));
            Assert.That(seen.IsCancellationRequested, Is.False);
        }

        [Test]
        public void Run_PendingTask_KeepsTheEntryUntilItCompletes()
        {
            var source = new UniTaskCompletionSource();

            _area.Run(ct => source.Task);

            Assert.That(_area.EntryCount, Is.EqualTo(1));
            source.TrySetResult();
            Assert.That(_area.EntryCount, Is.Zero, "completion must remove the entry");
            Assert.That(_t.Errors.Count, Is.Zero);
        }

        [Test]
        public void Run_ManySynchronousRuns_LeaveNoEntriesAndReuseTheSlots()
        {
            var counter = new Counter();

            for (var i = 0; i < 200; i++)
            {
                _area.Run(counter, static (c, ct) =>
                {
                    c.Value++;
                    return UniTask.CompletedTask;
                });
            }

            Assert.That(counter.Value, Is.EqualTo(200));
            Assert.That(_area.EntryCount, Is.Zero);
            Assert.That(_area.EntryCapacity, Is.LessThanOrEqualTo(4), "each run must reuse the freed slot");
        }

        [Test]
        public void Run_ManyPendingTasksCompletingInMixedOrder_LeaveNoEntries()
        {
            const int Count = 60;
            var sources = new UniTaskCompletionSource[Count];
            for (var i = 0; i < Count; i++)
            {
                sources[i] = new UniTaskCompletionSource();
                _area.Run(sources[i], static (s, ct) => s.Task);
            }

            Assert.That(_area.EntryCount, Is.EqualTo(Count));

            for (var i = 1; i < Count; i += 2)
            {
                sources[i].TrySetResult();
            }

            Assert.That(_area.EntryCount, Is.EqualTo(Count / 2));

            for (var i = Count - 2; i >= 0; i -= 2)
            {
                sources[i].TrySetResult();
            }

            Assert.That(_area.EntryCount, Is.Zero);
            Assert.That(_t.Errors.Count, Is.Zero);
        }

        [Test]
        public void Run_FinishingAfterCancel_DoesNotRemoveEntriesOfTheNewGeneration()
        {
            var source = new UniTaskCompletionSource();
            _area.Run(source, static (s, ct) => s.Task);
            _area.Cancel();
            _area.Record(_t.Log, "new");
            Assert.That(_area.EntryCount, Is.EqualTo(1));

            source.TrySetResult();

            Assert.That(_area.EntryCount, Is.EqualTo(1), "a finished old-generation task must not remove a new-generation entry");
            _area.Cancel();
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "new" }));
        }

        // The state overload

        [Test]
        public void Run_WithState_PassesTheStateAndTheToken()
        {
            var counter = new Counter();
            var seen = default(CancellationToken);

            _area.Run(counter, (c, ct) =>
            {
                c.Value++;
                seen = ct;
                return UniTask.CompletedTask;
            });

            Assert.That(counter.Value, Is.EqualTo(1));
            Assert.That(seen, Is.EqualTo(_area.Token));
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void Run_WithStructState_PassesTheValue()
        {
            var counter = new Counter();

            _area.Run(new Payload(counter, 7), static (p, ct) =>
            {
                p.Target.Value += p.Amount;
                return UniTask.CompletedTask;
            });

            Assert.That(counter.Value, Is.EqualTo(7));
        }

        [Test]
        public void Run_WithStatePendingTask_KeepsTheEntryUntilItCompletes()
        {
            var source = new UniTaskCompletionSource();

            _area.Run(source, static (s, ct) => s.Task);

            Assert.That(_area.EntryCount, Is.EqualTo(1));
            source.TrySetResult();
            Assert.That(_area.EntryCount, Is.Zero);
        }

        // Error routing

        [Test]
        public void Run_SynchronousThrow_IsRoutedWithTaskSourceLifetimeNameAndCallSite()
        {
            _area.Run(ct => throw new InvalidOperationException("sync boom"));

            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            var captured = _t.Errors[0];
            Assert.That(captured.Exception, Is.TypeOf<InvalidOperationException>());
            Assert.That(captured.Exception.Message, Is.EqualTo("sync boom"));
            Assert.That(captured.Context.Source, Is.EqualTo(LifetimeErrorSource.Task));
            Assert.That(captured.Context.LifetimeName, Is.EqualTo("area"));
            Assert.That(captured.Context.Member, Is.EqualTo(nameof(Run_SynchronousThrow_IsRoutedWithTaskSourceLifetimeNameAndCallSite)));
            Assert.That(captured.Context.Line, Is.GreaterThan(0));
            Assert.That(_area.EntryCount, Is.Zero, "a failed start must not leave an entry");
            _t.Errors.Clear();
        }

        [Test]
        public void Run_WithStateSynchronousThrow_IsRoutedAndTheEntryRemoved()
        {
            _area.Run(_t.Log, static (log, ct) => throw new InvalidOperationException("sync boom"));

            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].Context.Source, Is.EqualTo(LifetimeErrorSource.Task));
            Assert.That(_area.EntryCount, Is.Zero);
            _t.Errors.Clear();
        }

        [Test]
        public void Run_AsynchronousFault_IsRoutedWithTaskSourceLifetimeNameAndCallSite()
        {
            var source = new UniTaskCompletionSource();
            _area.Run(ct => source.Task);

            source.TrySetException(new InvalidOperationException("async boom"));

            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            var captured = _t.Errors[0];
            Assert.That(captured.Exception, Is.TypeOf<InvalidOperationException>());
            Assert.That(captured.Exception.Message, Is.EqualTo("async boom"));
            Assert.That(captured.Context.Source, Is.EqualTo(LifetimeErrorSource.Task));
            Assert.That(captured.Context.LifetimeName, Is.EqualTo("area"));
            Assert.That(captured.Context.Member, Is.EqualTo(nameof(Run_AsynchronousFault_IsRoutedWithTaskSourceLifetimeNameAndCallSite)));
            Assert.That(captured.Context.Line, Is.GreaterThan(0));
            Assert.That(_area.EntryCount, Is.Zero);
            _t.Errors.Clear();
        }

        [Test]
        public void Run_TaskAlreadyFaultedWhenReturned_IsRoutedAndTheEntryRemovedAtOnce()
        {
            _area.Run(ct => UniTask.FromException(new InvalidOperationException("faulted")));

            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].Exception.Message, Is.EqualTo("faulted"));
            Assert.That(_t.Errors[0].Context.Source, Is.EqualTo(LifetimeErrorSource.Task));
            Assert.That(_area.EntryCount, Is.Zero);
            _t.Errors.Clear();
        }

        [Test]
        public void Run_FaultAfterCancel_IsStillRouted()
        {
            var source = new UniTaskCompletionSource();
            _area.Run(ct => source.Task);
            _area.Cancel();
            Assert.That(_area.EntryCount, Is.Zero, "the drain already took the entry");

            source.TrySetException(new InvalidOperationException("late fault"));

            Assert.That(_t.Errors.Count, Is.EqualTo(1), "a fault after the generation ended is a real bug and must be routed");
            Assert.That(_t.Errors[0].Context.Source, Is.EqualTo(LifetimeErrorSource.Task));
            Assert.That(_t.Errors[0].Exception.Message, Is.EqualTo("late fault"));
            _t.Errors.Clear();
        }

        [Test]
        public void Run_FaultAfterDispose_IsStillRouted()
        {
            var child = _area.CreateChild("child");
            var source = new UniTaskCompletionSource();
            child.Run(ct => source.Task);
            child.Dispose();

            source.TrySetException(new InvalidOperationException("late fault"));

            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].Context.LifetimeName, Is.EqualTo("child"));
            _t.Errors.Clear();
        }

        // Cancellation is silent

        [Test]
        public void Run_SynchronousOperationCanceledException_IsSilent()
        {
            _area.Run(ct => throw new OperationCanceledException());

            Assert.That(_t.Errors.Count, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void Run_SynchronousTaskCanceledException_IsSilent()
        {
            _area.Run(ct => throw new TaskCanceledException());

            Assert.That(_t.Errors.Count, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void Run_TaskAlreadyCancelledByAnExternalToken_IsSilent()
        {
            var external = new CancellationTokenSource();
            external.Cancel();

            _area.Run(ct => UniTask.FromCanceled(external.Token));

            Assert.That(_t.Errors.Count, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void Run_PendingTaskCancelledByTheLifetimeToken_IsSilent()
        {
            var source = new UniTaskCompletionSource();
            var token = default(CancellationToken);
            _area.Run(ct =>
            {
                token = ct;
                return source.Task;
            });
            _area.Cancel();

            source.TrySetCanceled(token);

            Assert.That(_t.Errors.Count, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void Run_PendingTaskCancelledByAnExternalToken_IsSilentAndRemovesTheEntry()
        {
            var source = new UniTaskCompletionSource();
            var external = new CancellationTokenSource();
            _area.Run(ct => source.Task);

            external.Cancel();
            source.TrySetCanceled(external.Token);

            Assert.That(_t.Errors.Count, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero, "cancellation still ends the task and removes its entry");
        }

        [Test]
        public void Run_PendingTaskFaultedWithOperationCanceledException_IsSilent()
        {
            var source = new UniTaskCompletionSource();
            _area.Run(ct => source.Task);

            source.TrySetException(new OperationCanceledException());

            Assert.That(_t.Errors.Count, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void Run_PendingTaskFaultedWithTaskCanceledException_IsSilent()
        {
            var source = new UniTaskCompletionSource();
            _area.Run(ct => source.Task);

            source.TrySetException(new TaskCanceledException());

            Assert.That(_t.Errors.Count, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero);
        }

        // Not started while the lifetime is ending or gone

        [Test]
        public void Run_WhileTheLifetimeIsCancelling_IsNotStarted()
        {
            var counter = new Counter();
            _area.OnCancel(() => _area.Run(ct =>
            {
                counter.Value++;
                return UniTask.CompletedTask;
            }));

            _area.Cancel();

            Assert.That(counter.Value, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero, "nothing may be left behind for the next generation");
            Assert.That(_area.Generation, Is.EqualTo(1));
        }

        [Test]
        public void Run_InsideATokenCallbackOfTheEndingGeneration_IsNotStarted()
        {
            var counter = new Counter();
            _area.Token.Register(() => _area.Run(ct =>
            {
                counter.Value++;
                return UniTask.CompletedTask;
            }));

            _area.Cancel();

            Assert.That(counter.Value, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void Run_OnADisposedLifetime_IsNotStarted()
        {
            var counter = new Counter();
            var child = _area.CreateChild("child");
            child.Dispose();

            child.Run(ct =>
            {
                counter.Value++;
                return UniTask.CompletedTask;
            });
            child.Run(counter, static (c, ct) =>
            {
                c.Value++;
                return UniTask.CompletedTask;
            });

            Assert.That(counter.Value, Is.Zero);
            Assert.That(child.EntryCount, Is.Zero);
        }

        [Test]
        public void Run_AfterCancelReturns_StartsInTheNewGeneration()
        {
            var counter = new Counter();
            _area.Cancel();

            _area.Run(ct =>
            {
                counter.Value++;
                return UniTask.CompletedTask;
            });

            Assert.That(counter.Value, Is.EqualTo(1));
        }

        // The token

        [Test]
        public void Run_TokenAwareAwaitCancelledByTheLifetime_StopsSilentlyAndSkipsTheCodeAfterTheAwait()
        {
            var gate = new UniTaskCompletionSource();
            var afterAwait = new Counter();
            var seen = default(CancellationToken);
            _area.Run(ct =>
            {
                seen = ct;
                return AwaitGateThenCount(gate, afterAwait, ct);
            });
            Assert.That(_area.EntryCount, Is.EqualTo(1));
            Assert.That(seen.IsCancellationRequested, Is.False);

            _area.Cancel();

            Assert.That(seen.IsCancellationRequested, Is.True, "the task must see its token cancelled");
            Assert.That(_area.Token.IsCancellationRequested, Is.False, "the next generation gets a live token");
            Assert.That(_area.EntryCount, Is.Zero);
            gate.TrySetResult();
            Assert.That(afterAwait.Value, Is.Zero, "code after a token-aware await must not run after Cancel");
            Assert.That(_t.Errors.Count, Is.Zero, "the cancellation is silent");
        }

        [Test]
        public void Run_WorkThatIgnoresItsToken_KeepsRunningAfterCancelButItsEntryIsGone()
        {
            var source = new UniTaskCompletionSource();
            var counter = new Counter();
            _area.Run(ct => CompleteThenCount(source, counter));

            _area.Cancel();

            Assert.That(_area.EntryCount, Is.Zero, "the generation's entry is drained even when the task lives on");
            source.TrySetResult();
            Assert.That(counter.Value, Is.EqualTo(1), "only a token-aware await stops; the documented pooling hole");
            Assert.That(_t.Errors.Count, Is.Zero);
        }

        [Test]
        public void Run_WorkThatCancelsItsOwnLifetime_EndsTheGenerationAndRunsNormally()
        {
            var source = new UniTaskCompletionSource();
            var token = default(CancellationToken);
            _area.Run(ct =>
            {
                token = ct;
                _area.Cancel();
                return source.Task;
            });

            Assert.That(token.IsCancellationRequested, Is.True, "the caller's token is cancelled");
            Assert.That(_area.Generation, Is.EqualTo(1));
            Assert.That(_area.EntryCount, Is.Zero);
            source.TrySetResult();
            Assert.That(_t.Errors.Count, Is.Zero);
        }

        [Test]
        public void Run_SynchronousWorkThatCancelsItsOwnLifetime_LeavesNoEntry()
        {
            _area.Run(ct =>
            {
                _area.Cancel();
                return UniTask.CompletedTask;
            });

            Assert.That(_area.EntryCount, Is.Zero);
            Assert.That(_area.Generation, Is.EqualTo(1));
        }

        // Threads

        [Test]
        public void Run_TaskCompletedOnAWorkerThread_HopsBackForBookkeeping()
        {
            var source = new UniTaskCompletionSource();
            _area.Run(ct => source.Task);

            var failure = ThreadRunner.Run(() => source.TrySetResult());

            Assert.That(failure, Is.Null);
            Assert.That(_t.Tree.PostedDrainCount, Is.EqualTo(1), "the completion must ask for a main-thread hop");
            Assert.That(_area.EntryCount, Is.EqualTo(1), "the entry must not be touched off the main thread");
            _t.Tree.RunPostedDrains();
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void Run_TaskFaultedOnAWorkerThread_IsRoutedOnTheMainThreadAfterTheHop()
        {
            var mainThread = Thread.CurrentThread.ManagedThreadId;
            var source = new UniTaskCompletionSource();
            _area.Run(ct => source.Task);

            ThreadRunner.Run(() => source.TrySetException(new InvalidOperationException("worker fault")));

            Assert.That(_t.Errors.Count, Is.Zero, "nothing may be routed before the hop");
            _t.Tree.RunPostedDrains();
            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].ThreadId, Is.EqualTo(mainThread), "the error handler must run on the main thread");
            Assert.That(_t.Errors[0].Context.Source, Is.EqualTo(LifetimeErrorSource.Task));
            Assert.That(_area.EntryCount, Is.Zero);
            _t.Errors.Clear();
        }

        [Test]
        public void Run_WorkThatSwitchesToTheThreadPool_RoutesItsFaultOnTheMainThread()
        {
            var mainThread = Thread.CurrentThread.ManagedThreadId;
            var workThread = 0;
            using (var gate = new ManualResetEventSlim(false))
            {
                _area.Run(async ct =>
                {
                    await UniTask.SwitchToThreadPool();
                    workThread = Thread.CurrentThread.ManagedThreadId;
                    gate.Wait(10000);
                    throw new InvalidOperationException("pool boom");
                });

                // Run has returned, so its continuation is attached before the pool thread is released.
                gate.Set();
                Assert.That(_t.Tree.WaitForPost(), Is.True, "the pool completion must ask for a main-thread hop");
            }

            Assert.That(workThread, Is.Not.EqualTo(mainThread));
            Assert.That(_t.Errors.Count, Is.Zero);
            Assert.That(_area.EntryCount, Is.EqualTo(1));

            _t.Tree.RunPostedDrains();

            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].ThreadId, Is.EqualTo(mainThread));
            Assert.That(_t.Errors[0].Exception.Message, Is.EqualTo("pool boom"));
            Assert.That(_area.EntryCount, Is.Zero);
            _t.Errors.Clear();
        }

        [Test]
        public void Run_WorkThatSwitchesToTheThreadPoolAndSucceeds_RemovesTheEntryOnTheMainThread()
        {
            using (var gate = new ManualResetEventSlim(false))
            {
                _area.Run(async ct =>
                {
                    await UniTask.SwitchToThreadPool();
                    gate.Wait(10000);
                });

                gate.Set();
                Assert.That(_t.Tree.WaitForPost(), Is.True);
            }

            Assert.That(_area.EntryCount, Is.EqualTo(1), "removal waits for the main thread");
            _t.Tree.RunPostedDrains();
            Assert.That(_area.EntryCount, Is.Zero);
            Assert.That(_t.Errors.Count, Is.Zero);
        }

        // Arguments

        [Test]
        public void Run_NullLifetime_ThrowsAndStartsNothing()
        {
            var counter = new Counter();
            Lifetime none = null;

            var exception = Assert.Throws<ArgumentNullException>(() => none.Run(ct =>
            {
                counter.Value++;
                return UniTask.CompletedTask;
            }));

            Assert.That(exception.ParamName, Is.EqualTo("lifetime"));
            Assert.That(counter.Value, Is.Zero);
        }

        [Test]
        public void Run_NullWork_ThrowsAndRegistersNothing()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => _area.Run((Func<CancellationToken, UniTask>)null));
            var stateException = Assert.Throws<ArgumentNullException>(() => _area.Run(_t.Log, (Func<CallLog, CancellationToken, UniTask>)null));

            Assert.That(exception.ParamName, Is.EqualTo("work"));
            Assert.That(stateException.ParamName, Is.EqualTo("work"));
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void Run_OffMainThread_ThrowsInvalidOperationExceptionAndStartsNothing()
        {
            var counter = new Counter();

            var failure = ThreadRunner.Run(() => _area.Run(ct =>
            {
                counter.Value++;
                return UniTask.CompletedTask;
            }));

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(counter.Value, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero);
        }

        private static async UniTask AwaitGateThenCount(UniTaskCompletionSource gate, Counter afterAwait, CancellationToken ct)
        {
            await gate.Task.AttachExternalCancellation(ct);
            afterAwait.Value++;
        }

        private static async UniTask CompleteThenCount(UniTaskCompletionSource source, Counter counter)
        {
            await source.Task;
            counter.Value++;
        }

        private readonly struct Payload
        {
            internal readonly Counter Target;
            internal readonly int Amount;

            internal Payload(Counter target, int amount)
            {
                Target = target;
                Amount = amount;
            }
        }
    }
}
