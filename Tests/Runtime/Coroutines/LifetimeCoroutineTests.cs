using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.Coroutines
{
    // StartCoroutine on a lifetime: start, run, stop and finish. Steps are counted, so "stopped" means the counter stops moving.
    [TestFixture]
    public sealed class LifetimeCoroutineTests
    {
        private CoroutineFixture _f;

        [SetUp]
        public void SetUp()
        {
            _f = new CoroutineFixture();
        }

        [TearDown]
        public void TearDown()
        {
            _f.Complete();
        }

        // Start and run

        // Guard: Start registers the wrapper, then StartCoroutine runs the first step in place and returns the live registration.
        [UnityTest]
        public IEnumerator StartCoroutine_ActiveHost_RunsTheFirstStepInPlaceAndKeepsSteppingEveryFrame()
        {
            var counter = new Counter();
            var host = _f.NewHost();

            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(counter));

            Assert.That(counter.Value, Is.EqualTo(1), "the first step runs inside StartCoroutine, as in Unity");
            Assert.That(registration.IsActive, Is.True);
            Assert.That(_f.Area.EntryCount, Is.EqualTo(1));
            yield return CoroutineFixture.Frames(3);

            Assert.That(counter.Value, Is.GreaterThan(1), "the routine must keep stepping");
            Assert.That(registration.IsActive, Is.True, "still running, still registered");
        }

        // Guard: LifetimeCoroutine.Terminate and IsFinishedProbe identify and reclaim the entry (the diagnostics label an entry by its Terminate identity).
        [Test]
        public void StartCoroutine_ActiveHost_RegistersAnEntryThatIdentifiesAsACoroutineWithItsCallSite()
        {
            var host = _f.NewHost();
            _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(new Counter()));

            var entry = _f.Area.EntryAt(_f.Area.NewestEntryId);

            Assert.That(entry.A, Is.InstanceOf<LifetimeCoroutine>());
            Assert.That(entry.Terminate, Is.SameAs(LifetimeCoroutine.Terminate));
            Assert.That(entry.Probe, Is.Not.Null, "the probe is what lets the sweep reclaim routines whose host is gone");
            Assert.That(entry.Member, Is.EqualTo(nameof(StartCoroutine_ActiveHost_RegistersAnEntryThatIdentifiesAsACoroutineWithItsCallSite)));
            Assert.That(entry.Line, Is.GreaterThan(0));
        }

        // Guard: no enabled check anywhere (documented): a disabled host neither blocks a start nor stops a running routine, as in Unity.
        [UnityTest]
        public IEnumerator Routine_HostBehaviourDisabled_StartsAndKeepsRunningLikeAPlainCoroutine()
        {
            var counter = new Counter();
            var host = _f.NewHost();
            host.enabled = false;

            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(counter));
            yield return CoroutineFixture.Frames(2);
            var before = counter.Value;
            yield return CoroutineFixture.Frames(3);

            Assert.That(registration.IsActive, Is.True, "a disabled behaviour can still host a coroutine");
            Assert.That(counter.Value, Is.GreaterThan(before), "and it keeps stepping");
            Assert.That(LifetimeCoroutine.IsFinishedProbe(CoroutineFixture.WrapperOf(_f.Area)), Is.False, "the sweep must not reclaim it");
        }

        // Guard: LifetimeCoroutineExtensions argument checks run before anything is started.
        [Test]
        public void StartCoroutine_NullRoutineOrNullLifetime_ThrowsAndStartsNothing()
        {
            var host = _f.NewHost();
            Lifetime none = null;

            var routineNull = Assert.Throws<ArgumentNullException>(() => _f.Area.StartCoroutine(host, null));
            var lifetimeNull = Assert.Throws<ArgumentNullException>(() => none.StartCoroutine(host, CoroutineRoutines.Forever(new Counter())));

            Assert.That(routineNull.ParamName, Is.EqualTo("routine"));
            Assert.That(lifetimeNull.ParamName, Is.EqualTo("lifetime"));
            Assert.That(_f.Area.EntryCount, Is.Zero);
        }

        // Guard: LifetimeCoroutine.Start calls EnsureMainThread first.
        [Test]
        public void StartCoroutine_OffTheMainThread_ThrowsBeforeTheRoutineTakesAStep()
        {
            var counter = new Counter();
            var host = _f.NewHost();

            var failure = ThreadRunner.Run(() => _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(counter)));

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(counter.Value, Is.Zero);
            Assert.That(_f.Area.EntryCount, Is.Zero);
        }

        // Stop

        // Guard: the terminate action (Stop) sets the stopped flag and the entry is drained by Cancel.
        [UnityTest]
        public IEnumerator Cancel_RunningRoutine_StopsItAndRemovesTheEntry()
        {
            var counter = new Counter();
            var host = _f.NewHost();
            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(counter));
            yield return CoroutineFixture.Frames(2);

            _f.Area.Cancel();
            var atCancel = counter.Value;
            yield return CoroutineFixture.Frames(5);

            Assert.That(counter.Value, Is.EqualTo(atCancel), "a cancelled routine must not step again");
            Assert.That(registration.IsActive, Is.False);
            Assert.That(_f.Area.EntryCount, Is.Zero);
        }

        // Guard: Dispose drains the same entries as Cancel.
        [UnityTest]
        public IEnumerator Dispose_RunningRoutine_StopsItAndRemovesTheEntry()
        {
            var counter = new Counter();
            var host = _f.NewHost();
            var child = _f.Area.CreateChild("child");
            var registration = child.StartCoroutine(host, CoroutineRoutines.Forever(counter));
            yield return CoroutineFixture.Frames(2);

            child.Dispose();
            var atDispose = counter.Value;
            yield return CoroutineFixture.Frames(5);

            Assert.That(counter.Value, Is.EqualTo(atDispose));
            Assert.That(registration.IsActive, Is.False);
            Assert.That(child.EntryCount, Is.Zero);
        }

        // Guard: Stop calls host.StopCoroutine(handle), not just the wrapper's flag. Parity with a plain StopCoroutine on the same instruction.
        [UnityTest]
        public IEnumerator Cancel_RoutineParkedOnAYieldInstruction_StopsTheEngineCoroutineLikeAPlainStopCoroutine()
        {
            var plainPolls = new Counter();
            var wrappedPolls = new Counter();
            var host = _f.NewHost();
            var plainHandle = host.StartCoroutine(CoroutineRoutines.WaitsForever(plainPolls));
            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.WaitsForever(wrappedPolls));
            yield return CoroutineFixture.Frames(3);
            Assert.That(plainPolls.Value, Is.GreaterThan(0), "precondition: Unity polls the instruction of a plain coroutine every frame");
            Assert.That(wrappedPolls.Value, Is.GreaterThan(0), "precondition: the instruction reaches Unity unchanged through the wrapper");

            host.StopCoroutine(plainHandle);
            registration.Cancel();
            var plainAtStop = plainPolls.Value;
            var wrappedAtStop = wrappedPolls.Value;
            yield return CoroutineFixture.Frames(5);

            var plainStopped = plainPolls.Value == plainAtStop;
            var wrappedStopped = wrappedPolls.Value == wrappedAtStop;
            Assert.That(wrappedStopped, Is.EqualTo(plainStopped), "the wrapper must stop the engine coroutine exactly as a plain StopCoroutine does (plain stopped: " + plainStopped + ")");
            Assert.That(_f.Area.EntryCount, Is.Zero);
        }

        // Guard: CancelRegistrationOnMainThread takes one exact (slot, version) entry.
        [UnityTest]
        public IEnumerator RegistrationCancel_StopsExactlyOneCoroutine()
        {
            var first = new Counter();
            var second = new Counter();
            var third = new Counter();
            var host = _f.NewHost();
            _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(first));
            var middle = _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(second));
            _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(third));
            yield return CoroutineFixture.Frames(2);

            middle.Cancel();
            var secondAtCancel = second.Value;
            var firstBefore = first.Value;
            var thirdBefore = third.Value;
            yield return CoroutineFixture.Frames(4);

            Assert.That(second.Value, Is.EqualTo(secondAtCancel), "the cancelled routine stops");
            Assert.That(first.Value, Is.GreaterThan(firstBefore), "the first routine keeps running");
            Assert.That(third.Value, Is.GreaterThan(thirdBefore), "the third routine keeps running");
            Assert.That(middle.IsActive, Is.False);
            Assert.That(_f.Area.EntryCount, Is.EqualTo(2));
        }

        // Guard: a repeated or late registration cancel is a no-op and never stops anything twice.
        [UnityTest]
        public IEnumerator RegistrationCancel_CalledTwiceAndAfterTheLifetimeCancel_IsANoOp()
        {
            var counter = new Counter();
            var host = _f.NewHost();
            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(counter));
            yield return CoroutineFixture.Frames(2);

            registration.Cancel();
            registration.Cancel();
            _f.Area.Cancel();
            registration.Cancel();
            yield return CoroutineFixture.Frames(2);

            Assert.That(_f.Area.EntryCount, Is.Zero);
            Assert.That(_f.Area.State, Is.EqualTo(LifetimeState.Active));
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: Cancel opens a new generation, so a fresh coroutine is accepted and stopped by the next Cancel.
        [UnityTest]
        public IEnumerator Cancel_ThenStartAgain_RunsInTheNewGenerationAndStopsOnTheNextCancel()
        {
            var first = new Counter();
            var second = new Counter();
            var host = _f.NewHost();
            _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(first));
            _f.Area.Cancel();

            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(second));
            yield return CoroutineFixture.Frames(3);
            Assert.That(registration.IsActive, Is.True);
            Assert.That(second.Value, Is.GreaterThan(1));

            _f.Area.Cancel();
            var atCancel = second.Value;
            yield return CoroutineFixture.Frames(4);

            Assert.That(second.Value, Is.EqualTo(atCancel));
            Assert.That(first.Value, Is.EqualTo(1), "the routine of the ended generation took only its first step");
        }

        // Guard: CancelRegistrationOnMainThread compares the generation, so an old handle cannot stop a new routine.
        [UnityTest]
        public IEnumerator RegistrationCancel_FromAnEarlierGeneration_DoesNotStopTheNewGenerationsRoutine()
        {
            var oldCounter = new Counter();
            var newCounter = new Counter();
            var host = _f.NewHost();
            var stale = _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(oldCounter));
            _f.Area.Cancel();
            var current = _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(newCounter));

            stale.Cancel();
            yield return CoroutineFixture.Frames(2);
            var before = newCounter.Value;
            yield return CoroutineFixture.Frames(3);

            Assert.That(current.IsActive, Is.True);
            Assert.That(newCounter.Value, Is.GreaterThan(before), "the stale handle must not touch the new routine");
            Assert.That(_f.Area.EntryCount, Is.EqualTo(1));
        }

        // Never yields

        // Guard: Start releases its entry and returns default when the handle is null and never calls StopCoroutine(null).
        [UnityTest]
        public IEnumerator StartCoroutine_RoutineThatNeverYields_RaisesNothingAndLeavesNoEntry()
        {
            var counter = new Counter();
            var host = _f.NewHost();

            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.NeverYields(counter));

            Assert.That(counter.Value, Is.EqualTo(1), "the only step ran in place");
            Assert.That(registration.IsActive, Is.False);
            Assert.That(_f.Area.EntryCount, Is.Zero);
            Assert.That(_f.Errors.Count, Is.Zero);
            LogAssert.NoUnexpectedReceived();

            _f.Area.Cancel();
            registration.Cancel();
            yield return CoroutineFixture.Frames(2);

            Assert.That(counter.Value, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: the entry is released on every call, so slots are reused and the list never grows.
        [Test]
        public void StartCoroutine_RoutineThatNeverYields_RepeatedManyTimes_KeepsTheEntryListEmptyAndSmall()
        {
            var counter = new Counter();
            var host = _f.NewHost();

            for (var i = 0; i < 50; i++)
            {
                _f.Area.StartCoroutine(host, CoroutineRoutines.NeverYields(counter));
            }

            Assert.That(counter.Value, Is.EqualTo(50));
            Assert.That(_f.Area.EntryCount, Is.Zero);
            Assert.That(_f.Area.EntryCapacity, Is.LessThanOrEqualTo(4), "a released slot is reused");
        }

        // Completion

        // Guard: Finish releases the wrapper's own entry when the routine ends; a later Cancel finds nothing to stop.
        [UnityTest]
        public IEnumerator Completion_RoutineFinishesByItself_RemovesTheEntryAndALaterCancelIsSilent()
        {
            var counter = new Counter();
            var host = _f.NewHost();
            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.FinishesAfter(counter, 2));
            Assert.That(registration.IsActive, Is.True);
            Assert.That(_f.Area.EntryCount, Is.EqualTo(1));

            yield return CoroutineFixture.Frames(5);

            Assert.That(counter.Value, Is.EqualTo(3), "two yields plus the final step");
            Assert.That(registration.IsActive, Is.False);
            Assert.That(_f.Area.EntryCount, Is.Zero);
            _f.Area.Cancel();
            registration.Cancel();
            yield return CoroutineFixture.Frames(2);
            Assert.That(counter.Value, Is.EqualTo(3));
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: MoveNext forwards Current untouched, so Unity sees the same instructions as without the wrapper.
        [UnityTest]
        public IEnumerator Completion_RoutineYieldingUnityInstructionsAndNestedRoutines_RunsToTheEndUnchanged()
        {
            var release = new Counter();
            var host = _f.NewHost();
            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.MixedInstructions(_f.Log, release));

            yield return CoroutineFixture.WaitFor(() => CoroutineFixture.Has(_f.Log, "d"));
            Assert.That(CoroutineFixture.Has(_f.Log, "d"), Is.True, "the routine must get past the timed wait and the nested routine");
            Assert.That(CoroutineFixture.Has(_f.Log, "e"), Is.False, "the predicate wait must hold the routine");
            Assert.That(registration.IsActive, Is.True);

            release.Value = 1;
            yield return CoroutineFixture.WaitFor(() => CoroutineFixture.Has(_f.Log, "e"));

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "a", "b", "c", "n1", "n2", "d", "e" }));
            yield return CoroutineFixture.Frames(2);
            Assert.That(registration.IsActive, Is.False);
            Assert.That(_f.Area.EntryCount, Is.Zero);
        }

        // Exceptions

        // Guard: Step catches, Report routes with Source = Coroutine and the call site, Finish removes the entry.
        [UnityTest]
        public IEnumerator Exception_AfterAYield_IsRoutedAsCoroutineWithTheCallSiteAndStopsTheRoutine()
        {
            var counter = new Counter();
            var host = _f.NewHost();
            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.ThrowsAfter(counter, 1, new InvalidOperationException("routine boom")));
            Assert.That(_f.Errors.Count, Is.Zero, "the first step did not throw");
            Assert.That(registration.IsActive, Is.True);

            yield return CoroutineFixture.Frames(3);

            Assert.That(_f.Errors.Count, Is.EqualTo(1));
            var captured = _f.Errors[0];
            Assert.That(captured.Exception.Message, Is.EqualTo("routine boom"));
            Assert.That(captured.Context.Source, Is.EqualTo(LifetimeErrorSource.Coroutine));
            Assert.That(captured.Context.LifetimeName, Is.EqualTo("area"));
            Assert.That(captured.Context.Member, Is.EqualTo(nameof(Exception_AfterAYield_IsRoutedAsCoroutineWithTheCallSiteAndStopsTheRoutine)));
            Assert.That(captured.Context.Line, Is.GreaterThan(0));
            Assert.That(registration.IsActive, Is.False);
            Assert.That(_f.Area.EntryCount, Is.Zero);
            var steps = counter.Value;
            yield return CoroutineFixture.Frames(3);
            Assert.That(counter.Value, Is.EqualTo(steps), "the routine must not step again");
            Assert.That(_f.Errors.Count, Is.EqualTo(1), "the error is routed once");
            LogAssert.NoUnexpectedReceived();
            _f.Errors.Clear();
        }

        // Guard: a throw on the first step runs inside StartCoroutine; it is routed, nothing stays registered, and the call does not throw.
        [Test]
        public void Exception_OnTheFirstStep_IsRoutedAndNothingStaysRegistered()
        {
            var counter = new Counter();
            var host = _f.NewHost();

            LifetimeRegistration registration = default;
            Assert.DoesNotThrow(() => registration = _f.Area.StartCoroutine(host, CoroutineRoutines.ThrowsAfter(counter, 0, new InvalidOperationException("first boom"))));

            Assert.That(registration.IsActive, Is.False);
            Assert.That(_f.Area.EntryCount, Is.Zero);
            Assert.That(_f.Errors.Count, Is.EqualTo(1));
            Assert.That(_f.Errors[0].Context.Source, Is.EqualTo(LifetimeErrorSource.Coroutine));
            Assert.That(_f.Errors[0].Exception.Message, Is.EqualTo("first boom"));
            LogAssert.NoUnexpectedReceived();
            _f.Errors.Clear();
        }

        // Guard: LifetimeErrors.Dispatch drops cancellation, so a routine that throws OperationCanceledException ends silently.
        [UnityTest]
        public IEnumerator Exception_OperationCanceledException_IsNotRoutedAndTheEntryIsRemoved()
        {
            var counter = new Counter();
            var host = _f.NewHost();
            _f.Area.StartCoroutine(host, CoroutineRoutines.ThrowsAfter(counter, 1, new OperationCanceledException()));

            yield return CoroutineFixture.Frames(3);

            Assert.That(_f.Errors.Count, Is.Zero, "cancellation is not an error");
            Assert.That(_f.Area.EntryCount, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: a failing routine ends only itself; its neighbours on the same lifetime and host keep running.
        [UnityTest]
        public IEnumerator Exception_InOneRoutine_DoesNotStopTheOtherRoutinesOfTheLifetime()
        {
            var healthy = new Counter();
            var failing = new Counter();
            var host = _f.NewHost();
            var survivor = _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(healthy));
            _f.Area.StartCoroutine(host, CoroutineRoutines.ThrowsAfter(failing, 1, new InvalidOperationException("one boom")));

            yield return CoroutineFixture.Frames(2);
            var before = healthy.Value;
            yield return CoroutineFixture.Frames(3);

            Assert.That(_f.Errors.Count, Is.EqualTo(1));
            Assert.That(healthy.Value, Is.GreaterThan(before), "the healthy routine keeps stepping");
            Assert.That(survivor.IsActive, Is.True);
            Assert.That(_f.Area.EntryCount, Is.EqualTo(1));
            _f.Errors.Clear();
        }
    }
}
