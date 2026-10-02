using System;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Ecanakli.Janitor.Tests
{
    // Run, timers, OwnedEvent and paired Subscribe: 0 B in steady state. Every test warms the exact call sites first and asserts that
    // the measured block really did the work. The manual clock and the pooled completion sources allocate nothing
    // themselves, so a failure points at the package. Cancel paths that complete a delay as cancelled allocate the
    // exception UniTask creates for it; those calls are kept outside the measured blocks.
    [TestFixture]
    public sealed class TaskAndEventAllocationTests
    {
        private const int Count = 16;
        private const int Mixed = 8;

        private TestScope _t;
        private ManualClock _clock;
        private Lifetime _area;

        [SetUp]
        public void SetUp()
        {
            _t = new TestScope();
            _clock = _t.Tree.UseManualClock();
            _area = _t.App.CreateChild("area");
            PooledSource.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            PooledSource.Clear();
            _t.Complete();
        }

        // Run

        [Test]
        public void Run_StateWithStaticSynchronousTask_AllocatesNothing()
        {
            var counter = new Counter();
            RunSynchronously(_area, counter, Count);
            RunSynchronously(_area, counter, Count);

            Assert.That(() => RunSynchronously(_area, counter, Count), Is.Not.AllocatingGCMemory());

            Assert.That(counter.Value, Is.EqualTo(3 * Count), "the measured block must really have run the work");
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void Run_StateWithPendingTaskFromAPooledSource_AllocatesNothing()
        {
            RunPending(_area, Count);
            RunPending(_area, Count);

            Assert.That(() => RunPending(_area, Count), Is.Not.AllocatingGCMemory());

            Assert.That(_area.EntryCount, Is.Zero, "every pending task must have completed and removed its entry");
        }

        // OwnedEvent

        [Test]
        public void OwnedEventSubscribe_CachedHandlerAcrossOwners_AllocatesNothing()
        {
            var sink = new Sink();
            var evt = new OwnedEvent<int>("Alloc");
            Action<int> handler = sink.OnInt;
            var owners = CreateOwners(Count);
            SubscribeAll(evt, handler, owners);
            CancelAll(owners);
            SubscribeAll(evt, handler, owners);
            CancelAll(owners);

            Assert.That(() => SubscribeAll(evt, handler, owners), Is.Not.AllocatingGCMemory());

            Assert.That(evt.SubscriberCount, Is.EqualTo(Count), "the measured block must really have subscribed");
            evt.Invoke(1);
            Assert.That(sink.Value, Is.EqualTo(Count));
        }

        [Test]
        public void OwnedEventSubscribe_DifferentCachedHandlersForOneOwner_AllocatesNothing()
        {
            var evt = new OwnedEvent<int>("Alloc");
            var handlers = CreateHandlerPairs(Count / 2);
            SubscribeAll(evt, handlers, _area);
            _area.Cancel();
            SubscribeAll(evt, handlers, _area);
            _area.Cancel();

            Assert.That(() => SubscribeAll(evt, handlers, _area), Is.Not.AllocatingGCMemory(), "the duplicate scan compares delegates of the same target");

            Assert.That(evt.SubscriberCount, Is.EqualTo(Count));
            Assert.That(_area.EntryCount, Is.EqualTo(Count));
        }

        [Test]
        public void OwnedEventSubscribeAndOwnerCancel_RepeatedCycles_AllocateNothing()
        {
            var sink = new Sink();
            var evt = new OwnedEvent<int>("Alloc");
            Action<int> handler = sink.OnInt;
            var owners = CreateOwners(Count);
            SubscribeAll(evt, handler, owners);
            CancelAll(owners);

            Assert.That(() =>
            {
                for (var i = 0; i < 100; i++)
                {
                    SubscribeAll(evt, handler, owners);
                    CancelAll(owners);
                }
            }, Is.Not.AllocatingGCMemory());

            Assert.That(evt.SubscriberCount, Is.Zero);
            Assert.That(evt.SlotCount, Is.Zero, "the slots of ended owners are gone");
        }

        [Test]
        public void OwnedEventInvoke_SixteenHandlers_AllocatesNothing()
        {
            var sink = new Sink();
            var evt = new OwnedEvent<int>("Alloc");
            Action<int> handler = sink.OnInt;
            SubscribeAll(evt, handler, CreateOwners(Count));
            evt.Invoke(1);
            evt.Invoke(1);

            Assert.That(() => InvokeMany(evt, 100), Is.Not.AllocatingGCMemory());

            Assert.That(sink.Value, Is.EqualTo(Count * 102), "every invoke must have reached every handler");
        }

        [Test]
        public void OwnedEventInvoke_NoArgumentSixteenHandlers_AllocatesNothing()
        {
            var sink = new Sink();
            var evt = new OwnedEvent("Alloc");
            Action handler = sink.OnZero;
            var owners = CreateOwners(Count);
            for (var i = 0; i < Count; i++)
            {
                evt.Subscribe(handler, owners[i]);
            }

            evt.Invoke();
            evt.Invoke();

            Assert.That(() =>
            {
                for (var i = 0; i < 100; i++)
                {
                    evt.Invoke();
                }
            }, Is.Not.AllocatingGCMemory());

            Assert.That(sink.Value, Is.EqualTo(Count * 102));
        }

        [Test]
        public void OwnedEventInvoke_TwoAndThreeArgumentsSixteenHandlers_AllocateNothing()
        {
            var sink = new Sink();
            var two = new OwnedEvent<int, string>("Alloc2");
            var three = new OwnedEvent<int, string, bool>("Alloc3");
            Action<int, string> pairHandler = sink.OnPair;
            Action<int, string, bool> tripleHandler = sink.OnTriple;
            var owners = CreateOwners(Count);
            for (var i = 0; i < Count; i++)
            {
                two.Subscribe(pairHandler, owners[i]);
                three.Subscribe(tripleHandler, owners[i]);
            }

            // Warm the measured delegate itself: Mono allocates on the first ldstr of an un-run lambda.
            TestDelegate measured = () =>
            {
                for (var i = 0; i < 100; i++)
                {
                    two.Invoke(1, "x");
                    three.Invoke(1, "x", true);
                }
            };
            measured();

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(sink.Value, Is.EqualTo(Count * 400), "warm-up and measured runs: 2 x 100 invokes on each event");
        }

        // Paired Subscribe

        [Test]
        public void PairedSubscribe_StaticLambdasAndCachedHandlers_AllocatesNothing()
        {
            var handlers = CreateZeroHandlerPairs(Count);
            PairedSubscribeMany(_area, handlers);
            _area.Cancel();
            PairedSubscribeMany(_area, handlers);
            _area.Cancel();

            Assert.That(() => PairedSubscribeMany(_area, handlers), Is.Not.AllocatingGCMemory(), "the duplicate scan compares delegates of the same target");

            Assert.That(PooledSource.Count, Is.EqualTo(2 * Count), "add must have run for every call");
            Assert.That(_area.EntryCount, Is.EqualTo(2 * Count));
            _area.Cancel();
            Assert.That(PooledSource.Count, Is.Zero, "remove must have run for every call");
        }

        [Test]
        public void PairedSubscribe_GenericFormWithStaticLambdasAndCachedHandlers_AllocatesNothing()
        {
            var handlers = CreateHandlerPairs(Count);
            PairedSubscribeManyInt(_area, handlers);
            _area.Cancel();
            PairedSubscribeManyInt(_area, handlers);
            _area.Cancel();

            Assert.That(() => PairedSubscribeManyInt(_area, handlers), Is.Not.AllocatingGCMemory());

            Assert.That(PooledSource.IntCount, Is.EqualTo(2 * Count));
            _area.Cancel();
            Assert.That(PooledSource.IntCount, Is.Zero);
        }

        // After and Every

        [Test]
        public void After_StateWithStaticCallback_AllocatesNothing()
        {
            var counter = new Counter();
            ScheduleAfter(_area, _clock, counter, Count);
            ScheduleAfter(_area, _clock, counter, Count);

            Assert.That(() => ScheduleAfter(_area, _clock, counter, Count), Is.Not.AllocatingGCMemory());

            Assert.That(counter.Value, Is.EqualTo(3 * Count), "every scheduled callback must have fired");
            Assert.That(_area.EntryCount, Is.Zero);
            Assert.That(_clock.PendingCount, Is.Zero);
        }

        [Test]
        public void Every_StartingTimers_AllocatesNothing()
        {
            var counter = new Counter();
            StartEvery(_area, counter, Count);
            StopEvery(_area, _clock);
            StartEvery(_area, counter, Count);
            StopEvery(_area, _clock);
            _ = _area.Token; // the CTS of the new generation is the documented floor, created outside the block

            Assert.That(() => StartEvery(_area, counter, Count), Is.Not.AllocatingGCMemory());

            Assert.That(_area.EntryCount, Is.EqualTo(Count));
            Assert.That(_clock.PendingCount, Is.EqualTo(Count));
            StopEvery(_area, _clock);
        }

        [Test]
        public void Every_SteadyTicks_AllocateNothing()
        {
            var counter = new Counter();
            StartEvery(_area, counter, Count);
            for (var i = 0; i < 3; i++)
            {
                _clock.Advance(1f);
            }

            Assert.That(() => AdvanceMany(_clock, 100), Is.Not.AllocatingGCMemory());

            Assert.That(counter.Value, Is.EqualTo(Count * 103), "every timer must have fired on every tick");
            Assert.That(_area.EntryCount, Is.EqualTo(Count));
        }

        // Teardown of task, timer and event entries

        [Test]
        public void Cancel_WithPendingTasksTimersAndEventSubscriptions_AllocatesNothing()
        {
            var counter = new Counter();
            var sources = new AutoResetUniTaskCompletionSource[Mixed];
            var evt = new OwnedEvent<int>("Mixed");
            var handlers = CreateHandlerPairs(Mixed / 2);
            for (var round = 0; round < 2; round++)
            {
                FillMixed(_area, sources, counter, evt, handlers);
                _area.Cancel();
                DrainMixed(sources, _clock);
            }

            FillMixed(_area, sources, counter, evt, handlers);
            Assert.That(_area.EntryCount, Is.EqualTo(3 * Mixed));

            Assert.That(() => _area.Cancel(), Is.Not.AllocatingGCMemory());

            Assert.That(_area.EntryCount, Is.Zero);
            Assert.That(evt.SubscriberCount, Is.Zero);
            DrainMixed(sources, _clock);
        }

        [Test]
        public void Dispose_WithPendingTasksTimersAndEventSubscriptions_AllocatesNothing()
        {
            var counter = new Counter();
            var sources = new AutoResetUniTaskCompletionSource[Mixed];
            var evt = new OwnedEvent<int>("Mixed");
            var handlers = CreateHandlerPairs(Mixed / 2);
            for (var round = 0; round < 2; round++)
            {
                var warm = _area.CreateChild("warm");
                FillMixed(warm, sources, counter, evt, handlers);
                warm.Dispose();
                DrainMixed(sources, _clock);
            }

            var doomed = _area.CreateChild("doomed");
            FillMixed(doomed, sources, counter, evt, handlers);
            Assert.That(doomed.EntryCount, Is.EqualTo(3 * Mixed));

            Assert.That(() => doomed.Dispose(), Is.Not.AllocatingGCMemory());

            Assert.That(doomed.IsDisposed, Is.True);
            Assert.That(evt.SubscriberCount, Is.Zero);
            DrainMixed(sources, _clock);
        }

        private static void RunSynchronously(Lifetime area, Counter counter, int count)
        {
            for (var i = 0; i < count; i++)
            {
                area.Run(counter, static (c, ct) =>
                {
                    c.Value++;
                    return UniTask.CompletedTask;
                });
            }
        }

        private static void RunPending(Lifetime area, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var source = AutoResetUniTaskCompletionSource.Create();
                area.Run(source, static (s, ct) => s.Task);
                source.TrySetResult();
            }
        }

        private Lifetime[] CreateOwners(int count)
        {
            var owners = new Lifetime[count];
            for (var i = 0; i < count; i++)
            {
                owners[i] = _area.CreateChild("owner" + i);
            }

            return owners;
        }

        // Two handlers per sink object, so the duplicate scan meets equal targets with different methods.
        private static Action<int>[] CreateHandlerPairs(int sinks)
        {
            var handlers = new Action<int>[2 * sinks];
            for (var i = 0; i < sinks; i++)
            {
                var sink = new Sink();
                handlers[2 * i] = sink.OnInt;
                handlers[(2 * i) + 1] = sink.OnIntAlt;
            }

            return handlers;
        }

        private static Action[] CreateZeroHandlerPairs(int sinks)
        {
            var handlers = new Action[2 * sinks];
            for (var i = 0; i < sinks; i++)
            {
                var sink = new Sink();
                handlers[2 * i] = sink.OnZero;
                handlers[(2 * i) + 1] = sink.OnZeroAlt;
            }

            return handlers;
        }

        private static void SubscribeAll(OwnedEvent<int> evt, Action<int> handler, Lifetime[] owners)
        {
            for (var i = 0; i < owners.Length; i++)
            {
                evt.Subscribe(handler, owners[i]);
            }
        }

        private static void SubscribeAll(OwnedEvent<int> evt, Action<int>[] handlers, Lifetime owner)
        {
            for (var i = 0; i < handlers.Length; i++)
            {
                evt.Subscribe(handlers[i], owner);
            }
        }

        private static void CancelAll(Lifetime[] owners)
        {
            for (var i = 0; i < owners.Length; i++)
            {
                owners[i].Cancel();
            }
        }

        private static void InvokeMany(OwnedEvent<int> evt, int times)
        {
            for (var i = 0; i < times; i++)
            {
                evt.Invoke(1);
            }
        }

        // Distinct handlers: the same add, remove and handler twice on one owner is a repeat and is ignored.
        private static void PairedSubscribeMany(Lifetime area, Action[] handlers)
        {
            for (var i = 0; i < handlers.Length; i++)
            {
                area.Subscribe(static h => PooledSource.Add(h), static h => PooledSource.Remove(h), handlers[i]);
            }
        }

        private static void PairedSubscribeManyInt(Lifetime area, Action<int>[] handlers)
        {
            for (var i = 0; i < handlers.Length; i++)
            {
                area.Subscribe<int>(static h => PooledSource.AddInt(h), static h => PooledSource.RemoveInt(h), handlers[i]);
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

        private static void StartEvery(Lifetime area, Counter counter, int count)
        {
            for (var i = 0; i < count; i++)
            {
                area.Every(1f, counter, static c => c.Value++);
            }
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

        // Pending tasks, timers and event subscriptions all owned by the target.
        private void FillMixed(Lifetime target, AutoResetUniTaskCompletionSource[] sources, Counter counter, OwnedEvent<int> evt, Action<int>[] handlers)
        {
            for (var i = 0; i < Mixed; i++)
            {
                sources[i] = AutoResetUniTaskCompletionSource.Create();
                target.Run(sources[i], static (s, ct) => s.Task);
                target.Every(1f, counter, static c => c.Value++);
                evt.Subscribe(handlers[i], target);
            }
        }

        // Finishes the tasks that outlived their generation and completes the cancelled delays.
        private static void DrainMixed(AutoResetUniTaskCompletionSource[] sources, ManualClock clock)
        {
            for (var i = 0; i < sources.Length; i++)
            {
                sources[i].TrySetResult();
            }

            clock.Advance(1f);
        }

        private sealed class Sink
        {
            public int Value;

            public void OnInt(int value)
            {
                Value += value;
            }

            public void OnIntAlt(int value)
            {
                Value += 2 * value;
            }

            public void OnZero()
            {
                Value++;
            }

            public void OnZeroAlt()
            {
                Value += 2;
            }

            public void OnPair(int number, string text)
            {
                Value++;
            }

            public void OnTriple(int number, string text, bool flag)
            {
                Value++;
            }
        }

        // A static event source that stores handlers by reference in fixed arrays, so it never allocates itself.
        private static class PooledSource
        {
            private static readonly Action[] Handlers = new Action[128];
            private static readonly Action<int>[] IntHandlers = new Action<int>[128];

            public static int Count
            {
                get
                {
                    var count = 0;
                    for (var i = 0; i < Handlers.Length; i++)
                    {
                        if (!ReferenceEquals(Handlers[i], null))
                        {
                            count++;
                        }
                    }

                    return count;
                }
            }

            public static int IntCount
            {
                get
                {
                    var count = 0;
                    for (var i = 0; i < IntHandlers.Length; i++)
                    {
                        if (!ReferenceEquals(IntHandlers[i], null))
                        {
                            count++;
                        }
                    }

                    return count;
                }
            }

            public static void Add(Action handler)
            {
                for (var i = 0; i < Handlers.Length; i++)
                {
                    if (ReferenceEquals(Handlers[i], null))
                    {
                        Handlers[i] = handler;
                        return;
                    }
                }
            }

            public static void Remove(Action handler)
            {
                for (var i = 0; i < Handlers.Length; i++)
                {
                    if (ReferenceEquals(Handlers[i], handler))
                    {
                        Handlers[i] = null;
                        return;
                    }
                }
            }

            public static void AddInt(Action<int> handler)
            {
                for (var i = 0; i < IntHandlers.Length; i++)
                {
                    if (ReferenceEquals(IntHandlers[i], null))
                    {
                        IntHandlers[i] = handler;
                        return;
                    }
                }
            }

            public static void RemoveInt(Action<int> handler)
            {
                for (var i = 0; i < IntHandlers.Length; i++)
                {
                    if (ReferenceEquals(IntHandlers[i], handler))
                    {
                        IntHandlers[i] = null;
                        return;
                    }
                }
            }

            public static void Clear()
            {
                Array.Clear(Handlers, 0, Handlers.Length);
                Array.Clear(IntHandlers, 0, IntHandlers.Length);
            }
        }
    }
}
