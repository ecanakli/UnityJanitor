using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests
{
    // After and Every, driven by a ManualClock installed through LifetimeTree.Delay.
    [TestFixture]
    public sealed class LifetimeTimerTests
    {
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

        // After

        [Test]
        public void After_PositiveDelay_FiresOnceAfterTheDelay()
        {
            var fired = 0;
            _area.After(1f, () => fired++);
            Assert.That(fired, Is.Zero, "it must not fire in place");
            Assert.That(_area.EntryCount, Is.EqualTo(1));

            _clock.Advance(0.5f);
            Assert.That(fired, Is.Zero);
            _clock.Advance(0.5f);
            Assert.That(fired, Is.EqualTo(1));
            _clock.Advance(10f);

            Assert.That(fired, Is.EqualTo(1), "After fires once");
            Assert.That(_area.EntryCount, Is.Zero);
            Assert.That(_clock.PendingCount, Is.Zero);
        }

        [Test]
        public void After_WithState_PassesTheStateToTheCallback()
        {
            var counter = new Counter();

            _area.After(1f, counter, static c => c.Value += 5);
            _clock.Advance(1f);

            Assert.That(counter.Value, Is.EqualTo(5));
        }

        [Test]
        public void After_WithStructState_PassesTheValue()
        {
            var counter = new Counter();

            _area.After(1f, new Payload(counter, 9), static p => p.Target.Value += p.Amount);
            _clock.Advance(1f);

            Assert.That(counter.Value, Is.EqualTo(9));
        }

        [Test]
        public void After_TwoTimers_FireInDueOrder()
        {
            _area.After(2f, () => _t.Log.Add("late"));
            _area.After(1f, () => _t.Log.Add("early"));

            _clock.Advance(2f);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "early", "late" }));
        }

        [Test]
        public void After_CancelledBeforeTheDelayElapses_NeverFires()
        {
            var fired = 0;
            _area.After(1f, () => fired++);

            _area.Cancel();
            _clock.Advance(5f);

            Assert.That(fired, Is.Zero);
            Assert.That(_clock.PendingCount, Is.Zero, "the cancelled delay is consumed");
            Assert.That(_area.EntryCount, Is.Zero);
            Assert.That(_t.Errors.Count, Is.Zero, "cancellation is silent");
        }

        [Test]
        public void After_DisposedBeforeTheDelayElapses_NeverFires()
        {
            var child = _area.CreateChild("child");
            var fired = 0;
            child.After(1f, () => fired++);

            child.Dispose();
            _clock.Advance(5f);

            Assert.That(fired, Is.Zero);
            Assert.That(_t.Errors.Count, Is.Zero);
        }

        [Test]
        public void After_ProviderCompletesSuccessfullyAfterCancel_DoesNotRunTheCallback()
        {
            _clock.CompleteCancelledDelaysSuccessfully = true;
            var fired = 0;
            _area.After(1f, () => fired++);

            _area.Cancel();
            _clock.Advance(1f);

            Assert.That(_clock.PendingCount, Is.Zero, "the delay did complete");
            Assert.That(fired, Is.Zero, "a delay that completes normally after the generation ended must not fire");
            Assert.That(_t.Errors.Count, Is.Zero);
        }

        [Test]
        public void After_ProviderCompletesSuccessfullyAfterDispose_DoesNotRunTheCallback()
        {
            _clock.CompleteCancelledDelaysSuccessfully = true;
            var child = _area.CreateChild("child");
            var fired = 0;
            child.After(1f, () => fired++);

            child.Dispose();
            _clock.Advance(1f);

            Assert.That(fired, Is.Zero);
        }

        [Test]
        public void After_ScheduledInTheOldGeneration_DoesNotFireInTheNewOne()
        {
            _clock.CompleteCancelledDelaysSuccessfully = true;
            var oldFired = 0;
            var newFired = 0;
            _area.After(1f, () => oldFired++);
            _area.Cancel();
            _area.After(1f, () => newFired++);

            _clock.Advance(1f);

            Assert.That(oldFired, Is.Zero, "the old generation's timer is stale even though the lifetime is active again");
            Assert.That(newFired, Is.EqualTo(1));
        }

        [Test]
        public void After_ScheduledInTheOldGenerationWithACancelAwareProvider_OnlyTheNewOneFires()
        {
            var oldFired = 0;
            var newFired = 0;
            _area.After(1f, () => oldFired++);
            _area.Cancel();
            _area.After(1f, () => newFired++);

            _clock.Advance(1f);

            Assert.That(oldFired, Is.Zero);
            Assert.That(newFired, Is.EqualTo(1));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        public void After_NonPositiveSeconds_RunsSynchronouslyWithoutTouchingTheProvider(float seconds)
        {
            var counter = new Counter();

            _area.After(seconds, () => counter.Value++);
            _area.After(seconds, counter, static c => c.Value++);

            Assert.That(counter.Value, Is.EqualTo(2));
            Assert.That(_clock.RequestCount, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void After_ZeroSecondsWhileTheLifetimeIsCancelling_NeverRuns()
        {
            var counter = new Counter();
            _area.OnCancel(() => _area.After(0f, () => counter.Value++));

            _area.Cancel();

            Assert.That(counter.Value, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void After_ZeroSecondsOnADisposedLifetime_NeverRuns()
        {
            var counter = new Counter();
            var child = _area.CreateChild("child");
            child.Dispose();

            child.After(0f, () => counter.Value++);

            Assert.That(counter.Value, Is.Zero);
        }

        [Test]
        public void After_PositiveSecondsWhileTheLifetimeIsCancelling_IsNotScheduled()
        {
            var counter = new Counter();
            _area.OnCancel(() => _area.After(1f, () => counter.Value++));

            _area.Cancel();
            _clock.Advance(5f);

            Assert.That(counter.Value, Is.Zero);
            Assert.That(_clock.RequestCount, Is.Zero, "nothing may be scheduled on an ending generation");
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void After_ZeroSecondsCallbackThrows_IsRoutedWithTimerSource()
        {
            _area.After(0f, () => throw new InvalidOperationException("now boom"));

            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].Context.Source, Is.EqualTo(LifetimeErrorSource.Timer));
            Assert.That(_t.Errors[0].Context.LifetimeName, Is.EqualTo("area"));
            _t.Errors.Clear();
        }

        [Test]
        public void After_IgnoreTimeScale_ReachesTheProvider()
        {
            _area.After(1.5f, () => { });
            Assert.That(_clock.LastSeconds, Is.EqualTo(1.5f));
            Assert.That(_clock.LastIgnoreTimeScale, Is.False, "scaled time is the default");

            _area.After(2.5f, () => { }, true);

            Assert.That(_clock.LastSeconds, Is.EqualTo(2.5f));
            Assert.That(_clock.LastIgnoreTimeScale, Is.True);
        }

        [Test]
        public void After_WithStateIgnoreTimeScale_ReachesTheProvider()
        {
            _area.After(1f, _t.Log, static l => { }, true);

            Assert.That(_clock.LastIgnoreTimeScale, Is.True);
        }

        [Test]
        public void After_CallbackThrows_IsRoutedOnceWithTimerSourceLifetimeNameAndCallSite()
        {
            _area.After(1f, () => throw new InvalidOperationException("timer boom"));

            _clock.Advance(1f);
            _clock.Advance(1f);

            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            var captured = _t.Errors[0];
            Assert.That(captured.Exception.Message, Is.EqualTo("timer boom"));
            Assert.That(captured.Context.Source, Is.EqualTo(LifetimeErrorSource.Timer));
            Assert.That(captured.Context.LifetimeName, Is.EqualTo("area"));
            Assert.That(captured.Context.Member, Is.EqualTo(nameof(After_CallbackThrows_IsRoutedOnceWithTimerSourceLifetimeNameAndCallSite)));
            Assert.That(captured.Context.Line, Is.GreaterThan(0));
            Assert.That(_area.EntryCount, Is.Zero);
            _t.Errors.Clear();
        }

        [Test]
        public void After_CallbackThrowsOperationCanceledException_IsSilent()
        {
            _area.After(1f, () => throw new OperationCanceledException());

            _clock.Advance(1f);

            Assert.That(_t.Errors.Count, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void After_ProviderThrows_IsRoutedWithTimerSourceAndNothingRemains()
        {
            _clock.ThrowOnDelay = new InvalidOperationException("provider boom");
            var fired = 0;

            _area.After(1f, () => fired++);

            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].Context.Source, Is.EqualTo(LifetimeErrorSource.Timer));
            Assert.That(_t.Errors[0].Exception.Message, Is.EqualTo("provider boom"));
            Assert.That(fired, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero);
            _t.Errors.Clear();
        }

        [Test]
        public void After_ProviderCompletingSynchronously_FiresOnceInline()
        {
            _clock.CompleteSynchronously = true;
            var counter = new Counter();

            _area.After(1f, counter, static c => c.Value++);

            Assert.That(counter.Value, Is.EqualTo(1));
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void After_ManyTimers_AllFireAndLeaveNoEntries()
        {
            const int Count = 100;
            var counter = new Counter();
            for (var i = 0; i < Count; i++)
            {
                _area.After(1f + i, counter, static c => c.Value++);
            }

            Assert.That(_area.EntryCount, Is.EqualTo(Count));
            _clock.Advance(Count + 1f);

            Assert.That(counter.Value, Is.EqualTo(Count));
            Assert.That(_area.EntryCount, Is.Zero);
            Assert.That(_clock.PendingCount, Is.Zero);
        }

        // Every

        [Test]
        public void Every_FiresRepeatedlyEachInterval()
        {
            var fired = 0;
            _area.Every(1f, () => fired++);
            Assert.That(fired, Is.Zero);

            _clock.Advance(1f);
            _clock.Advance(1f);
            _clock.Advance(1f);

            Assert.That(fired, Is.EqualTo(3));
            Assert.That(_area.EntryCount, Is.EqualTo(1), "one entry owns the whole timer");
            Assert.That(_clock.PendingCount, Is.EqualTo(1), "the next delay is armed");
        }

        [Test]
        public void Every_PartialIntervals_FireOnlyWhenTheIntervalHasElapsed()
        {
            var fired = 0;
            _area.Every(1f, () => fired++);

            _clock.Advance(0.4f);
            _clock.Advance(0.4f);
            Assert.That(fired, Is.Zero);
            _clock.Advance(0.4f);

            Assert.That(fired, Is.EqualTo(1));
        }

        [Test]
        public void Every_WithState_PassesTheStateEachTick()
        {
            var counter = new Counter();
            _area.Every(1f, counter, static c => c.Value++);

            _clock.Advance(1f);
            _clock.Advance(1f);

            Assert.That(counter.Value, Is.EqualTo(2));
        }

        [Test]
        public void Every_Cancel_StopsFiringAndReleasesTheTimer()
        {
            var fired = 0;
            _area.Every(1f, () => fired++);
            _clock.Advance(1f);
            _clock.Advance(1f);
            Assert.That(fired, Is.EqualTo(2));

            _area.Cancel();
            _clock.Advance(1f);
            _clock.Advance(1f);

            Assert.That(fired, Is.EqualTo(2), "a cancelled timer must not fire again");
            Assert.That(_clock.PendingCount, Is.Zero, "the timer must not re-arm");
            Assert.That(_area.EntryCount, Is.Zero);
            Assert.That(_t.Errors.Count, Is.Zero);
        }

        [Test]
        public void Every_ProviderCompletesSuccessfullyAfterCancel_DoesNotFireAgain()
        {
            _clock.CompleteCancelledDelaysSuccessfully = true;
            var fired = 0;
            _area.Every(1f, () => fired++);
            _clock.Advance(1f);

            _area.Cancel();
            _clock.Advance(1f);
            _clock.Advance(1f);

            Assert.That(fired, Is.EqualTo(1));
            Assert.That(_clock.PendingCount, Is.Zero, "the timer must stop re-arming");
        }

        [Test]
        public void Every_CallbackCancelsItsOwnLifetime_StopsWithoutFurtherTicks()
        {
            var fired = 0;
            _area.Every(1f, () =>
            {
                fired++;
                if (fired == 2)
                {
                    _area.Cancel();
                }
            });

            _clock.Advance(1f);
            _clock.Advance(1f);
            _clock.Advance(1f);
            _clock.Advance(1f);

            Assert.That(fired, Is.EqualTo(2));
            Assert.That(_clock.PendingCount, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void Every_CallbackThrows_StopsAndRoutesExactlyOnce()
        {
            var fired = 0;
            _area.Every(1f, () =>
            {
                fired++;
                if (fired == 2)
                {
                    throw new InvalidOperationException("tick boom");
                }
            });

            for (var i = 0; i < 5; i++)
            {
                _clock.Advance(1f);
            }

            Assert.That(fired, Is.EqualTo(2), "an exception stops that timer");
            Assert.That(_t.Errors.Count, Is.EqualTo(1), "the failure is routed exactly once");
            var captured = _t.Errors[0];
            Assert.That(captured.Exception.Message, Is.EqualTo("tick boom"));
            Assert.That(captured.Context.Source, Is.EqualTo(LifetimeErrorSource.Timer));
            Assert.That(captured.Context.LifetimeName, Is.EqualTo("area"));
            Assert.That(captured.Context.Member, Is.EqualTo(nameof(Every_CallbackThrows_StopsAndRoutesExactlyOnce)));
            Assert.That(_clock.PendingCount, Is.Zero, "a stopped timer does not re-arm");
            Assert.That(_area.EntryCount, Is.Zero);
            _t.Errors.Clear();
        }

        [Test]
        public void Every_CallbackThrowsOnTheFirstTick_DoesNotAffectAnotherTimer()
        {
            var healthy = 0;
            _area.Every(1f, () => throw new InvalidOperationException("boom"));
            _area.Every(1f, () => healthy++);

            _clock.Advance(1f);
            _clock.Advance(1f);
            _clock.Advance(1f);

            Assert.That(healthy, Is.EqualTo(3));
            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            _t.Errors.Clear();
        }

        [Test]
        public void Every_ProviderThrowsWhenRearming_StopsAndRoutesOnce()
        {
            var fired = 0;
            _area.Every(1f, () =>
            {
                fired++;
                _clock.ThrowOnDelay = new InvalidOperationException("provider boom");
            });

            _clock.Advance(1f);
            _clock.Advance(1f);

            Assert.That(fired, Is.EqualTo(1));
            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].Context.Source, Is.EqualTo(LifetimeErrorSource.Timer));
            Assert.That(_area.EntryCount, Is.Zero);
            _t.Errors.Clear();
        }

        [Test]
        public void Every_ZeroSeconds_FiresEveryTick()
        {
            var fired = 0;
            _area.Every(0f, () => fired++);
            Assert.That(_clock.LastSeconds, Is.Zero);

            _clock.Advance(0f);
            _clock.Advance(0f);
            _clock.Advance(0f);

            Assert.That(fired, Is.EqualTo(3));
        }

        [Test]
        public void Every_NegativeSeconds_IsTreatedAsZero()
        {
            var fired = 0;
            _area.Every(-3f, () => fired++);

            Assert.That(_clock.LastSeconds, Is.Zero, "the provider must never see a negative delay");
            _clock.Advance(0f);
            _clock.Advance(0f);

            Assert.That(fired, Is.EqualTo(2));
        }

        [Test]
        public void Every_ProviderCompletingSynchronously_StopsAfter256TicksAndRoutesOneError()
        {
            _clock.CompleteSynchronously = true;
            var counter = new Counter();

            _area.Every(1f, counter, static c => c.Value++);

            Assert.That(counter.Value, Is.EqualTo(256), "the guard allows exactly 256 synchronous ticks in a row");
            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].Exception, Is.TypeOf<InvalidOperationException>());
            Assert.That(_t.Errors[0].Context.Source, Is.EqualTo(LifetimeErrorSource.Timer));
            Assert.That(_area.EntryCount, Is.Zero);
            _t.Errors.Clear();
        }

        [Test]
        public void Every_IgnoreTimeScale_ReachesTheProviderOnEveryArm()
        {
            var fired = 0;
            _area.Every(2f, () => fired++, true);
            Assert.That(_clock.LastIgnoreTimeScale, Is.True);
            Assert.That(_clock.LastSeconds, Is.EqualTo(2f));

            _clock.Advance(2f);

            Assert.That(fired, Is.EqualTo(1));
            Assert.That(_clock.RequestCount, Is.EqualTo(2), "the second delay was requested by the re-arm");
            Assert.That(_clock.LastIgnoreTimeScale, Is.True);
        }

        [Test]
        public void Every_WithStateIgnoreTimeScale_ReachesTheProvider()
        {
            _area.Every(1f, _t.Log, static l => { }, true);

            Assert.That(_clock.LastIgnoreTimeScale, Is.True);
        }

        [Test]
        public void Every_WhileTheLifetimeIsCancelling_IsNotScheduled()
        {
            var counter = new Counter();
            _area.OnCancel(() => _area.Every(1f, () => counter.Value++));

            _area.Cancel();
            _clock.Advance(5f);

            Assert.That(counter.Value, Is.Zero);
            Assert.That(_clock.RequestCount, Is.Zero);
        }

        [Test]
        public void Every_RegisteredAfterCancel_RunsInTheNewGenerationOnly()
        {
            var oldFired = 0;
            var newFired = 0;
            _area.Every(1f, () => oldFired++);
            _area.Cancel();
            _area.Every(1f, () => newFired++);

            _clock.Advance(1f);
            _clock.Advance(1f);

            Assert.That(oldFired, Is.Zero);
            Assert.That(newFired, Is.EqualTo(2));
        }

        [Test]
        public void Every_TimersOnSiblingAreas_CancelIndependently()
        {
            var first = _area.CreateChild("first");
            var second = _area.CreateChild("second");
            var firstFired = 0;
            var secondFired = 0;
            first.Every(1f, () => firstFired++);
            second.Every(1f, () => secondFired++);
            _clock.Advance(1f);

            first.Cancel();
            _clock.Advance(1f);
            _clock.Advance(1f);

            Assert.That(firstFired, Is.EqualTo(1));
            Assert.That(secondFired, Is.EqualTo(3));
        }

        [Test]
        public void Every_ParentCancel_StopsTimersOfDescendants()
        {
            var child = _area.CreateChild("child");
            var fired = 0;
            child.Every(1f, () => fired++);
            _clock.Advance(1f);

            _area.Cancel();
            _clock.Advance(1f);

            Assert.That(fired, Is.EqualTo(1));
            Assert.That(_clock.PendingCount, Is.Zero);
        }

        // Threads

        [Test]
        public void Every_DelayCompletingOffTheMainThread_RunsTheCallbackOnTheMainThreadAfterTheHop()
        {
            var mainThread = Thread.CurrentThread.ManagedThreadId;
            var threads = new List<int>();
            _area.Every(1f, () => threads.Add(Thread.CurrentThread.ManagedThreadId));

            var failure = ThreadRunner.Run(() => _clock.Advance(1f));

            Assert.That(failure, Is.Null);
            Assert.That(threads, Is.Empty, "the callback must wait for the main thread");
            Assert.That(_t.Tree.PostedDrainCount, Is.EqualTo(1));
            _t.Tree.RunPostedDrains();
            Assert.That(threads, Is.EqualTo(new[] { mainThread }));
            Assert.That(_clock.PendingCount, Is.EqualTo(1), "the timer re-armed on the main thread");
        }

        [Test]
        public void After_DelayCompletingOffTheMainThread_FiresOnTheMainThreadAfterTheHop()
        {
            var mainThread = Thread.CurrentThread.ManagedThreadId;
            var threads = new List<int>();
            _area.After(1f, () => threads.Add(Thread.CurrentThread.ManagedThreadId));

            ThreadRunner.Run(() => _clock.Advance(1f));

            Assert.That(threads, Is.Empty);
            Assert.That(_area.EntryCount, Is.EqualTo(1), "the entry is released on the main thread only");
            _t.Tree.RunPostedDrains();
            Assert.That(threads, Is.EqualTo(new[] { mainThread }));
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void Every_CancelledWhileTheHopIsPending_DoesNotFire()
        {
            var fired = 0;
            _area.Every(1f, () => fired++);
            ThreadRunner.Run(() => _clock.Advance(1f));

            _area.Cancel();
            _t.Tree.RunPostedDrains();

            Assert.That(fired, Is.Zero, "the liveness check runs after the hop");
            Assert.That(_clock.PendingCount, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero);
        }

        // Arguments

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void After_NonFiniteSeconds_ThrowsAndSchedulesNothing(float seconds)
        {
            var counter = new Counter();

            var plain = Assert.Throws<ArgumentOutOfRangeException>(() => _area.After(seconds, () => counter.Value++));
            var withState = Assert.Throws<ArgumentOutOfRangeException>(() => _area.After(seconds, counter, static c => c.Value++));

            Assert.That(plain.ParamName, Is.EqualTo("seconds"));
            Assert.That(withState.ParamName, Is.EqualTo("seconds"));
            Assert.That(counter.Value, Is.Zero);
            Assert.That(_clock.RequestCount, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void Every_NonFiniteSeconds_ThrowsAndSchedulesNothing(float seconds)
        {
            var counter = new Counter();

            var plain = Assert.Throws<ArgumentOutOfRangeException>(() => _area.Every(seconds, () => counter.Value++));
            var withState = Assert.Throws<ArgumentOutOfRangeException>(() => _area.Every(seconds, counter, static c => c.Value++));

            Assert.That(plain.ParamName, Is.EqualTo("seconds"));
            Assert.That(withState.ParamName, Is.EqualTo("seconds"));
            Assert.That(_clock.RequestCount, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void After_NullArguments_ThrowAndScheduleNothing()
        {
            Lifetime none = null;

            var lifetime = Assert.Throws<ArgumentNullException>(() => none.After(1f, () => { }));
            var action = Assert.Throws<ArgumentNullException>(() => _area.After(1f, (Action)null));
            var stateAction = Assert.Throws<ArgumentNullException>(() => _area.After(1f, _t.Log, (Action<CallLog>)null));

            Assert.That(lifetime.ParamName, Is.EqualTo("lifetime"));
            Assert.That(action.ParamName, Is.EqualTo("action"));
            Assert.That(stateAction.ParamName, Is.EqualTo("action"));
            Assert.That(_clock.RequestCount, Is.Zero);
        }

        [Test]
        public void Every_NullArguments_ThrowAndScheduleNothing()
        {
            Lifetime none = null;

            var lifetime = Assert.Throws<ArgumentNullException>(() => none.Every(1f, () => { }));
            var action = Assert.Throws<ArgumentNullException>(() => _area.Every(1f, (Action)null));
            var stateAction = Assert.Throws<ArgumentNullException>(() => _area.Every(1f, _t.Log, (Action<CallLog>)null));

            Assert.That(lifetime.ParamName, Is.EqualTo("lifetime"));
            Assert.That(action.ParamName, Is.EqualTo("action"));
            Assert.That(stateAction.ParamName, Is.EqualTo("action"));
            Assert.That(_clock.RequestCount, Is.Zero);
        }

        [Test]
        public void After_OffMainThread_ThrowsInvalidOperationExceptionAndSchedulesNothing()
        {
            var failure = ThreadRunner.Run(() => _area.After(1f, () => { }));
            var immediate = ThreadRunner.Run(() => _area.After(0f, () => { }));

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(immediate, Is.TypeOf<InvalidOperationException>(), "even the in-place path is main thread only");
            Assert.That(_clock.RequestCount, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void Every_OffMainThread_ThrowsInvalidOperationExceptionAndSchedulesNothing()
        {
            var failure = ThreadRunner.Run(() => _area.Every(1f, () => { }));

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(_clock.RequestCount, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero);
        }

        // The seam itself

        [Test]
        public void Delay_SetToNull_RestoresTheDefaultProvider()
        {
            var tree = new LifetimeTree(Thread.CurrentThread.ManagedThreadId, null);
            var defaultProvider = tree.Delay;

            tree.Delay = _clock.Delay;
            Assert.That(tree.Delay, Is.Not.EqualTo(defaultProvider));
            tree.Delay = null;

            Assert.That(tree.Delay, Is.EqualTo(defaultProvider));
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
