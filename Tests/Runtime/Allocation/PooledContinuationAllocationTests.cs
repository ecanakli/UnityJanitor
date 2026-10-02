using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Ecanakli.Janitor.Tests
{
    // Run, After, Every and OnCancel in the forms the other allocation fixtures do not measure (struct state, cached delegates,
    // the probe form), and bursts above the sizes the pooled continuations used to be capped at. Every test warms the exact call
    // sites first and asserts that the measured block really did the work. Cancelled delays complete as cancelled outside the blocks.
    [TestFixture]
    public sealed class PooledContinuationAllocationTests
    {
        private const int Count = 16;
        private const int Burst = 100;
        private const int PerKind = 70;

        private TestScope _t;
        private ManualClock _clock;
        private Lifetime _area;

        [SetUp]
        public void SetUp()
        {
            _t = new TestScope();
            _clock = _t.Tree.UseManualClock();
            _area = _t.App.CreateChild("area");
        }

        [TearDown]
        public void TearDown()
        {
            _t.Complete();
        }

        // Struct state

        [Test]
        public void After_StructState_AllocatesNothing()
        {
            var counter = new Counter();
            ScheduleAfterWithStruct(_area, _clock, counter, Count);
            ScheduleAfterWithStruct(_area, _clock, counter, Count);

            Assert.That(() => ScheduleAfterWithStruct(_area, _clock, counter, Count), Is.Not.AllocatingGCMemory());

            Assert.That(counter.Value, Is.EqualTo(3 * Count * 2), "every scheduled callback must have fired with its struct");
            Assert.That(_area.EntryCount, Is.Zero);
            Assert.That(_clock.PendingCount, Is.Zero);
        }

        [Test]
        public void Every_StructState_StartingAndTicking_AllocatesNothing()
        {
            var counter = new Counter();
            StartEveryWithStruct(_area, counter, Count);
            AdvanceMany(_clock, 3);
            StopEvery(_area, _clock);
            StartEveryWithStruct(_area, counter, Count);
            AdvanceMany(_clock, 3);
            StopEvery(_area, _clock);
            _ = _area.Token; // the CTS of the new generation is the documented floor, created outside the block
            var before = counter.Value;

            Assert.That(() => StartEveryWithStruct(_area, counter, Count), Is.Not.AllocatingGCMemory());
            Assert.That(() => AdvanceMany(_clock, 100), Is.Not.AllocatingGCMemory());

            Assert.That(counter.Value - before, Is.EqualTo(Count * 100 * 2), "every timer must have ticked 100 times");
            Assert.That(_area.EntryCount, Is.EqualTo(Count));
            StopEvery(_area, _clock);
        }

        [Test]
        public void Run_StructState_WithPendingTasks_AllocatesNothing()
        {
            var counter = new Counter();
            var sources = new AutoResetUniTaskCompletionSource[Count];
            RunWithStruct(_area, sources, counter);
            RunWithStruct(_area, sources, counter);

            Assert.That(() => RunWithStruct(_area, sources, counter), Is.Not.AllocatingGCMemory());

            Assert.That(counter.Value, Is.EqualTo(3 * Count), "every task must have started");
            Assert.That(_area.EntryCount, Is.Zero, "every pending task must have completed and removed its entry");
        }

        // Cached delegates

        [Test]
        public void After_CachedActionDelegate_AllocatesNothing()
        {
            var sink = new Sink();
            Action tick = sink.Tick;
            ScheduleAfterCached(_area, _clock, tick, Count);
            ScheduleAfterCached(_area, _clock, tick, Count);

            Assert.That(() => ScheduleAfterCached(_area, _clock, tick, Count), Is.Not.AllocatingGCMemory());

            Assert.That(sink.Value, Is.EqualTo(3 * Count), "every scheduled callback must have fired");
            Assert.That(_area.EntryCount, Is.Zero);
            Assert.That(_clock.PendingCount, Is.Zero);
        }

        [Test]
        public void Every_CachedActionDelegate_StartingAndTicking_AllocatesNothing()
        {
            var sink = new Sink();
            Action tick = sink.Tick;
            StartEveryCached(_area, tick, Count);
            AdvanceMany(_clock, 3);
            StopEvery(_area, _clock);
            StartEveryCached(_area, tick, Count);
            AdvanceMany(_clock, 3);
            StopEvery(_area, _clock);
            _ = _area.Token; // the CTS of the new generation is the documented floor, created outside the block
            var before = sink.Value;

            Assert.That(() => StartEveryCached(_area, tick, Count), Is.Not.AllocatingGCMemory());
            Assert.That(() => AdvanceMany(_clock, 100), Is.Not.AllocatingGCMemory());

            Assert.That(sink.Value - before, Is.EqualTo(Count * 100), "every timer must have ticked 100 times");
            Assert.That(_area.EntryCount, Is.EqualTo(Count));
            StopEvery(_area, _clock);
        }

        [Test]
        public void Run_CachedWorkDelegate_AllocatesNothing()
        {
            var work = new PendingWork();
            Func<CancellationToken, UniTask> cached = work.Work;
            RunCached(_area, work, cached, Count);
            RunCached(_area, work, cached, Count);

            Assert.That(() => RunCached(_area, work, cached, Count), Is.Not.AllocatingGCMemory());

            Assert.That(work.Started, Is.EqualTo(3 * Count), "every task must have started");
            Assert.That(_area.EntryCount, Is.Zero);
        }

        // OnCancel

        [Test]
        public void OnCancel_CachedAction_AllocatesNothing()
        {
            var sink = new Sink();
            Action cleanup = sink.Tick;
            RegisterActions(_area, cleanup, 4 * Count);
            _area.Cancel();
            RegisterActions(_area, cleanup, 4 * Count);
            _area.Cancel();

            Assert.That(() => RegisterActions(_area, cleanup, 4 * Count), Is.Not.AllocatingGCMemory());

            Assert.That(_area.EntryCount, Is.EqualTo(4 * Count), "the measured block must really have registered");
            _area.Cancel();
            Assert.That(sink.Value, Is.EqualTo(3 * 4 * Count));
        }

        [Test]
        public void OnCancel_StateWithAProbeThatIsNotFinished_AllocatesNothingAndRunsOnCancel()
        {
            var gate = new Gate();
            RegisterProbed(_area, gate, 4 * Count);
            _area.Cancel();
            RegisterProbed(_area, gate, 4 * Count);
            _area.Cancel();

            Assert.That(() => RegisterProbed(_area, gate, 4 * Count), Is.Not.AllocatingGCMemory());

            Assert.That(_area.EntryCount, Is.EqualTo(4 * Count), "a probe that says false keeps every item");
            Assert.That(gate.ProbeCalls, Is.GreaterThan(0), "the amortized sweep must have asked the probe");
            _area.Cancel();
            Assert.That(gate.Ran, Is.EqualTo(3 * 4 * Count));
        }

        [Test]
        public void OnCancel_StateWithAProbeThatIsFinished_AllocatesNothingAndDropsTheItems()
        {
            var gate = new Gate { Finished = true };
            RegisterProbed(_area, gate, 4 * Count);
            RegisterProbed(_area, gate, 4 * Count);
            var probeCalls = gate.ProbeCalls;

            Assert.That(() => RegisterProbed(_area, gate, 4 * Count), Is.Not.AllocatingGCMemory());

            Assert.That(gate.ProbeCalls, Is.GreaterThan(probeCalls), "the measured block must have swept");
            Assert.That(_area.EntryCount, Is.Zero, "finished items are dropped by the sweep");
            _area.Cancel();
            Assert.That(gate.Ran, Is.Zero, "a dropped item never runs its action");
        }

        // Bursts above the old pool sizes: the first cycles allocate the continuations, the third finds them in the pool

        [Test]
        public void After_ABurstOfOneHundredTimers_AllocatesNothingInTheThirdCycle()
        {
            var counter = new Counter();
            ScheduleAfter(_area, _clock, counter, Burst);
            ScheduleAfter(_area, _clock, counter, Burst);

            Assert.That(() => ScheduleAfter(_area, _clock, counter, Burst), Is.Not.AllocatingGCMemory());

            Assert.That(counter.Value, Is.EqualTo(3 * Burst), "every scheduled callback must have fired");
            Assert.That(_area.EntryCount, Is.Zero);
            Assert.That(_clock.PendingCount, Is.Zero);
        }

        [Test]
        public void Run_ABurstOfOneHundredPendingTasks_AllocatesNothingInTheThirdCycle()
        {
            var counter = new Counter();
            var sources = new AutoResetUniTaskCompletionSource[Burst];
            RunWithStruct(_area, sources, counter);
            RunWithStruct(_area, sources, counter);

            Assert.That(() => RunWithStruct(_area, sources, counter), Is.Not.AllocatingGCMemory());

            Assert.That(counter.Value, Is.EqualTo(3 * Burst), "every task must have started");
            Assert.That(_area.EntryCount, Is.Zero);
        }

        // Cancel and register again: Run, After and Every together, more of each than the old pools held

        [Test]
        public void CancelAndReregister_RunAfterAndEvery_AllocatesNothingOnceWarm()
        {
            var counter = new Counter();
            var sources = new AutoResetUniTaskCompletionSource[PerKind];
            for (var warm = 0; warm < 2; warm++)
            {
                FillAll(_area, sources, counter, PerKind);
                _area.Cancel();
                DrainAll(sources, _clock);
            }

            for (var round = 0; round < 3; round++)
            {
                _ = _area.Token; // the CTS of the new generation is the documented floor, created outside the blocks

                Assert.That(() => FillAll(_area, sources, counter, PerKind), Is.Not.AllocatingGCMemory());

                Assert.That(_area.EntryCount, Is.EqualTo(3 * PerKind), "the measured block must really have registered");
                Assert.That(() => _area.Cancel(), Is.Not.AllocatingGCMemory());
                Assert.That(_area.EntryCount, Is.Zero);
                DrainAll(sources, _clock);
            }

            Assert.That(counter.Value, Is.EqualTo(5 * PerKind), "one Run start per fill; no timer ever fired");
            Assert.That(_clock.PendingCount, Is.Zero);
        }

        private static void ScheduleAfterWithStruct(Lifetime area, ManualClock clock, Counter counter, int count)
        {
            for (var i = 0; i < count; i++)
            {
                area.After(1f, new Step(counter, 2), static s => s.Target.Value += s.Amount);
            }

            clock.Advance(1f);
        }

        private static void StartEveryWithStruct(Lifetime area, Counter counter, int count)
        {
            for (var i = 0; i < count; i++)
            {
                area.Every(1f, new Step(counter, 2), static s => s.Target.Value += s.Amount);
            }
        }

        private static void RunWithStruct(Lifetime area, AutoResetUniTaskCompletionSource[] sources, Counter counter)
        {
            for (var i = 0; i < sources.Length; i++)
            {
                sources[i] = AutoResetUniTaskCompletionSource.Create();
                area.Run(new RunState(counter, sources[i]), static (s, ct) =>
                {
                    s.Target.Value++;
                    return s.Source.Task;
                });
            }

            for (var i = 0; i < sources.Length; i++)
            {
                sources[i].TrySetResult();
            }
        }

        private static void ScheduleAfterCached(Lifetime area, ManualClock clock, Action tick, int count)
        {
            for (var i = 0; i < count; i++)
            {
                area.After(1f, tick);
            }

            clock.Advance(1f);
        }

        private static void StartEveryCached(Lifetime area, Action tick, int count)
        {
            for (var i = 0; i < count; i++)
            {
                area.Every(1f, tick);
            }
        }

        private static void RunCached(Lifetime area, PendingWork work, Func<CancellationToken, UniTask> cached, int count)
        {
            for (var i = 0; i < count; i++)
            {
                work.Source = AutoResetUniTaskCompletionSource.Create();
                area.Run(cached);
                work.Source.TrySetResult();
            }
        }

        private static void RegisterActions(Lifetime area, Action cleanup, int count)
        {
            for (var i = 0; i < count; i++)
            {
                area.OnCancel(cleanup);
            }
        }

        private static void RegisterProbed(Lifetime area, Gate gate, int count)
        {
            for (var i = 0; i < count; i++)
            {
                area.OnCancel(gate, static g => g.Ran++, static g => g.IsFinished());
            }
        }

        private static void ScheduleAfter(Lifetime area, ManualClock clock, Counter counter, int count)
        {
            for (var i = 0; i < count; i++)
            {
                area.After(1f, counter, static c => c.Value++);
            }

            clock.Advance(1f);
        }

        // Cancelling completes the armed delays as cancelled, which allocates UniTask's exception: keep it outside blocks.
        private static void StopEvery(Lifetime area, ManualClock clock)
        {
            area.Cancel();
            clock.Advance(1f);
        }

        private static void AdvanceMany(ManualClock clock, int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                clock.Advance(1f);
            }
        }

        // Pending tasks, timers that have not fired, and repeating timers, all owned by the target.
        private static void FillAll(Lifetime target, AutoResetUniTaskCompletionSource[] sources, Counter counter, int perKind)
        {
            for (var i = 0; i < perKind; i++)
            {
                sources[i] = AutoResetUniTaskCompletionSource.Create();
                target.Run(new RunState(counter, sources[i]), static (s, ct) =>
                {
                    s.Target.Value++;
                    return s.Source.Task;
                });
                target.After(1f, counter, static c => c.Value += 1000);
                target.Every(1f, counter, static c => c.Value += 1000);
            }
        }

        // Finishes the tasks that outlived their generation and completes the cancelled delays.
        private static void DrainAll(AutoResetUniTaskCompletionSource[] sources, ManualClock clock)
        {
            for (var i = 0; i < sources.Length; i++)
            {
                sources[i].TrySetResult();
            }

            clock.Advance(1f);
        }

        private readonly struct Step
        {
            public readonly Counter Target;
            public readonly int Amount;

            public Step(Counter target, int amount)
            {
                Target = target;
                Amount = amount;
            }
        }

        private readonly struct RunState
        {
            public readonly Counter Target;
            public readonly AutoResetUniTaskCompletionSource Source;

            public RunState(Counter target, AutoResetUniTaskCompletionSource source)
            {
                Target = target;
                Source = source;
            }
        }

        private sealed class Sink
        {
            public int Value;

            public void Tick()
            {
                Value++;
            }
        }

        private sealed class PendingWork
        {
            public AutoResetUniTaskCompletionSource Source;
            public int Started;

            public UniTask Work(CancellationToken token)
            {
                Started++;
                return Source.Task;
            }
        }

        private sealed class Gate
        {
            public bool Finished;
            public int Ran;
            public int ProbeCalls;

            public bool IsFinished()
            {
                ProbeCalls++;
                return Finished;
            }
        }
    }
}
