using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.Coroutines
{
    // The wrapper's own state machine, reached through the owner's entry: stray resumes, disposal, and ending the lifetime from inside a step.
    [TestFixture]
    public sealed class LifetimeCoroutineWrapperTests
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

        // Stray resumes

        // Guard: MoveNext returns false with a null Current once stopped; Unity may still resume a stopped enumerator.
        [Test]
        public void MoveNext_AfterCancel_YieldsNothingAndTheRoutineDoesNotStep()
        {
            var counter = new Counter();
            var host = _f.NewHost();
            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(counter));
            var wrapper = CoroutineFixture.WrapperOf(_f.Area);
            registration.Cancel();
            var stepsAtCancel = counter.Value;

            var more = wrapper.MoveNext();

            Assert.That(more, Is.False);
            Assert.That(wrapper.Current, Is.Null);
            Assert.That(counter.Value, Is.EqualTo(stepsAtCancel), "a stray resume must not step the routine");
            Assert.That(wrapper.MoveNext(), Is.False, "and stays false");
        }

        // Guard: Finish sets the stopped flag and drops the routine, so a resume after natural completion touches nothing.
        [UnityTest]
        public IEnumerator MoveNext_AfterTheRoutineFinished_YieldsNothingAndTheRoutineDoesNotStep()
        {
            var counter = new Counter();
            var host = _f.NewHost();
            _f.Area.StartCoroutine(host, CoroutineRoutines.FinishesAfter(counter, 1));
            var wrapper = CoroutineFixture.WrapperOf(_f.Area);
            yield return CoroutineFixture.Frames(4);
            Assert.That(_f.Area.EntryCount, Is.Zero, "precondition: the routine finished");
            var steps = counter.Value;

            var more = true;
            Assert.DoesNotThrow(() => more = wrapper.MoveNext());

            Assert.That(more, Is.False);
            Assert.That(wrapper.Current, Is.Null);
            Assert.That(counter.Value, Is.EqualTo(steps));
        }

        // Disposal

        // Guard: Dispose forwards to the routine once (finally blocks run as without the wrapper) and releases the entry.
        [Test]
        public void Dispose_OnTheWrapper_ForwardsToTheRoutineExactlyOnceAndReleasesTheEntry()
        {
            var host = _f.NewHost();
            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.TryFinally(_f.Log, "r"));
            var wrapper = CoroutineFixture.WrapperOf(_f.Area);

            wrapper.Dispose();
            wrapper.Dispose();

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "r:start", "r:finally" }));
            Assert.That(registration.IsActive, Is.False);
            Assert.That(_f.Area.EntryCount, Is.Zero);
            Assert.That(wrapper.MoveNext(), Is.False, "a disposed wrapper does not resume the routine");
        }

        // Guard: DisposeRoutine catches and routes a failing finally block.
        [Test]
        public void Dispose_RoutineFinallyThrows_IsRoutedAsCoroutineAndNeverThrown()
        {
            var host = _f.NewHost();
            _f.Area.StartCoroutine(host, CoroutineRoutines.FinallyThrows(new InvalidOperationException("finally boom")));
            var wrapper = CoroutineFixture.WrapperOf(_f.Area);

            Assert.DoesNotThrow(() => wrapper.Dispose());

            Assert.That(_f.Errors.Count, Is.EqualTo(1));
            Assert.That(_f.Errors[0].Context.Source, Is.EqualTo(LifetimeErrorSource.Coroutine));
            Assert.That(_f.Errors[0].Exception.Message, Is.EqualTo("finally boom"));
            _f.Errors.Clear();
        }

        // Guard: the _stepping flag defers a Dispose that arrives during a step until the step returns.
        [UnityTest]
        public IEnumerator Dispose_FromInsideTheRoutinesOwnStep_IsDeferredUntilTheStepReturns()
        {
            var holder = new WrapperHolder();
            var host = _f.NewHost();
            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.DisposesItsWrapperMidStep(holder, _f.Log));
            holder.Wrapper = CoroutineFixture.WrapperOf(_f.Area);
            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "step1" }));

            yield return CoroutineFixture.Frames(4);

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "step1", "step2", "step2-after-dispose", "finally" }), "the dispose waits for the step; step3 never runs");
            Assert.That(registration.IsActive, Is.False);
            Assert.That(_f.Area.EntryCount, Is.Zero);
            Assert.That(_f.Errors.Count, Is.Zero);
        }

        // Ending the lifetime from inside a step

        // Guard: MoveNext returns false after a step that stopped the wrapper, so the routine does not resume at its next yield.
        [UnityTest]
        public IEnumerator SelfCancel_BeforeTheFirstYield_StartsNothingAndTheRoutineNeverResumes()
        {
            var host = _f.NewHost();
            var generation = _f.Area.Generation;

            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.EndsItsLifetime(_f.Area.Cancel, _f.Log, 0));
            yield return CoroutineFixture.Frames(4);

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "end", "after-end" }), "the rest of the step runs, the next step does not");
            Assert.That(registration.IsActive, Is.False);
            Assert.That(_f.Area.EntryCount, Is.Zero);
            Assert.That(_f.Area.Generation, Is.EqualTo(generation + 1));
            Assert.That(_f.Area.State, Is.EqualTo(LifetimeState.Active));
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: Dispose from inside a step takes the same stop path as Cancel, and the routine does not resume.
        [UnityTest]
        public IEnumerator SelfDispose_AfterSeveralYields_StopsTheRoutineAtTheNextYield()
        {
            var host = _f.NewHost();
            var child = _f.Area.CreateChild("child");

            var registration = child.StartCoroutine(host, CoroutineRoutines.EndsItsLifetime(child.Dispose, _f.Log, 2));
            yield return CoroutineFixture.Frames(6);

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "step0", "step1", "end", "after-end" }));
            Assert.That(child.IsDisposed, Is.True);
            Assert.That(child.EntryCount, Is.Zero);
            Assert.That(registration.IsActive, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: a step that ends its lifetime and then throws still routes the error once and finishes.
        [UnityTest]
        public IEnumerator SelfCancel_ThenThrowsInTheSameStep_RoutesTheErrorOnceAndFinishes()
        {
            var host = _f.NewHost();

            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.EndsItsLifetimeThenThrows(_f.Area.Cancel, new InvalidOperationException("late boom")));
            yield return CoroutineFixture.Frames(4);

            Assert.That(_f.Errors.Count, Is.EqualTo(1));
            Assert.That(_f.Errors[0].Context.Source, Is.EqualTo(LifetimeErrorSource.Coroutine));
            Assert.That(_f.Errors[0].Exception.Message, Is.EqualTo("late boom"));
            Assert.That(registration.IsActive, Is.False);
            Assert.That(_f.Area.EntryCount, Is.Zero);
            Assert.That(_f.Area.State, Is.EqualTo(LifetimeState.Active));
            LogAssert.NoUnexpectedReceived();
            _f.Errors.Clear();
        }
    }
}
