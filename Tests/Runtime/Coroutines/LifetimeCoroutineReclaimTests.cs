using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.Coroutines
{
    // Host death is reclaimed by the probe sweep, or earlier by the wrapper's Dispose if Unity calls it; tests accept both.
    [TestFixture]
    public sealed class LifetimeCoroutineReclaimTests
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

        // The probe predicate

        // Guard: IsFinishedCore is false while the routine runs on a live host.
        [Test]
        public void IsFinishedProbe_RoutineRunningOnALiveHost_ReportsNotFinished()
        {
            var host = _f.NewHost();
            _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(new Counter()));

            Assert.That(LifetimeCoroutine.IsFinishedProbe(CoroutineFixture.WrapperOf(_f.Area)), Is.False);
        }

        // Guard: IsFinishedCore reports a routine that ended by itself.
        [UnityTest]
        public IEnumerator IsFinishedProbe_RoutineThatEndedByItself_ReportsFinished()
        {
            var host = _f.NewHost();
            _f.Area.StartCoroutine(host, CoroutineRoutines.FinishesAfter(new Counter(), 1));
            var wrapper = CoroutineFixture.WrapperOf(_f.Area);
            Assert.That(LifetimeCoroutine.IsFinishedProbe(wrapper), Is.False, "precondition: still running");

            yield return CoroutineFixture.Frames(4);

            Assert.That(LifetimeCoroutine.IsFinishedProbe(wrapper), Is.True);
        }

        // Guard: IsFinishedCore checks activeInHierarchy, so a deactivated host is reported in the same frame.
        [Test]
        public void IsFinishedProbe_HostDeactivated_ReportsFinished()
        {
            var host = _f.NewHost();
            _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(new Counter()));
            var wrapper = CoroutineFixture.WrapperOf(_f.Area);

            host.gameObject.SetActive(false);

            Assert.That(LifetimeCoroutine.IsFinishedProbe(wrapper), Is.True);
        }

        // Guard: the host == null check comes first, so a destroyed host is never dereferenced (that would throw MissingReferenceException).
        [Test]
        public void IsFinishedProbe_HostDestroyed_ReportsFinishedWithoutTouchingTheHost()
        {
            var host = _f.NewHost();
            _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(new Counter()));
            var wrapper = CoroutineFixture.WrapperOf(_f.Area);

            Object.DestroyImmediate(host.gameObject);

            var finished = false;
            Assert.DoesNotThrow(() => finished = LifetimeCoroutine.IsFinishedProbe(wrapper));
            Assert.That(finished, Is.True);
        }

        // The sweep

        // Guard: the sweep drops entries whose probe says finished, without running Stop, and keeps the live ones.
        [UnityTest]
        public IEnumerator Sweep_HostDeactivated_ReclaimsTheDeadEntriesWhenTheThresholdIsReached()
        {
            yield return SweepScenario(dead => dead.gameObject.SetActive(false));
        }

        // Guard: same, for a destroyed host (IsFinishedCore must not dereference it).
        [UnityTest]
        public IEnumerator Sweep_HostDestroyed_ReclaimsTheDeadEntriesWhenTheThresholdIsReached()
        {
            yield return SweepScenario(dead => Object.Destroy(dead.gameObject));
        }

        // Cancel after the host is gone

        // Guard: StopHandle returns when host == null, because a destroyed host has already lost its coroutines.
        [Test]
        public void Cancel_AfterTheHostWasDestroyed_StopsSilently()
        {
            var counter = new Counter();
            var host = _f.NewHost();
            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(counter));
            Object.DestroyImmediate(host.gameObject);

            Assert.DoesNotThrow(() => _f.Area.Cancel());

            Assert.That(_f.Area.EntryCount, Is.Zero);
            Assert.That(registration.IsActive, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: StopCoroutine on a finished handle of an inactive host stays silent, and a reactivated host does not restart it.
        [UnityTest]
        public IEnumerator Cancel_AfterTheHostWasDeactivated_StopsSilentlyAndAReactivationDoesNotRestartTheRoutine()
        {
            var counter = new Counter();
            var host = _f.NewHost();
            _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(counter));
            yield return CoroutineFixture.Frames(2);
            host.gameObject.SetActive(false);
            yield return CoroutineFixture.Frames(2);

            Assert.DoesNotThrow(() => _f.Area.Cancel());

            Assert.That(_f.Area.EntryCount, Is.Zero);
            host.gameObject.SetActive(true);
            var atReactivation = counter.Value;
            yield return CoroutineFixture.Frames(3);
            Assert.That(counter.Value, Is.EqualTo(atReactivation), "a stopped routine stays stopped");
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: a lifetime Dispose after the host died runs the same silent stop.
        [UnityTest]
        public IEnumerator Dispose_AfterTheHostWasDestroyed_StopsSilently()
        {
            var host = _f.NewHost();
            var child = _f.Area.CreateChild("child");
            child.StartCoroutine(host, CoroutineRoutines.Forever(new Counter()));
            Object.Destroy(host.gameObject);
            yield return CoroutineFixture.Frames(2);

            Assert.DoesNotThrow(() => child.Dispose());

            Assert.That(child.IsDisposed, Is.True);
            Assert.That(child.EntryCount, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        // 12 routines on a host that dies, 3 on a host that lives; the 16th registration reaches the sweep threshold.
        private IEnumerator SweepScenario(Action<CoroutineTestHost> killDeadHost)
        {
            var deadCounter = new Counter();
            var liveCounter = new Counter();
            var dead = _f.NewHost("DeadHost");
            var live = _f.NewHost("LiveHost");
            for (var i = 0; i < 12; i++)
            {
                _f.Area.StartCoroutine(dead, CoroutineRoutines.Forever(deadCounter));
            }

            for (var i = 0; i < 3; i++)
            {
                _f.Area.StartCoroutine(live, CoroutineRoutines.Forever(liveCounter));
            }

            Assert.That(_f.Area.EntryCount, Is.EqualTo(15), "below the sweep threshold nothing is dropped");

            killDeadHost(dead);
            yield return CoroutineFixture.Frames(2);

            var beforeSweep = _f.Area.EntryCount;
            Assert.That(beforeSweep, Is.EqualTo(15).Or.EqualTo(3), "15: waiting for the sweep. 3: Unity disposed the stopped enumerators and the wrapper released its entries itself");
            var deadAfterKill = deadCounter.Value;

            _f.Area.OnCancel(() => { });

            Assert.That(_f.Area.EntryCount, Is.EqualTo(4), "the 12 dead entries are gone; 3 live routines and the new item remain");
            var liveBefore = liveCounter.Value;
            yield return CoroutineFixture.Frames(3);
            Assert.That(liveCounter.Value, Is.GreaterThan(liveBefore), "the sweep must not touch a live routine");
            Assert.That(deadCounter.Value, Is.EqualTo(deadAfterKill), "the dead host's routines stay dead");

            _f.Area.Cancel();
            var liveAtCancel = liveCounter.Value;
            yield return CoroutineFixture.Frames(3);
            Assert.That(_f.Area.EntryCount, Is.Zero);
            Assert.That(liveCounter.Value, Is.EqualTo(liveAtCancel));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
