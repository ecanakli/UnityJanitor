using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.Coroutines
{
    // Pins how Unity treats a stopped coroutine's enumerator and a StopCoroutine from inside its own step. Each test runs a plain Unity coroutine next to a package
    // coroutine, records what the engine did with Assert.Pass, and asserts that the wrapper did the same.
    [TestFixture]
    public sealed class CoroutineEngineProbeTests
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

        // Does Unity call Dispose on the enumerator of a stopped coroutine? (a try/finally routine shows it)

        // Guard: the wrapper forwards Dispose exactly when Unity calls it, so finally blocks behave as without the package.
        [UnityTest]
        public IEnumerator EngineProbe_15a_StopCoroutine_DisposesTheStoppedEnumerator_AndTheWrapperMatchesIt()
        {
            var observation = new DisposeObservation();
            yield return ObserveDispose(observation, (host, plainHandle, registration) =>
            {
                host.StopCoroutine(plainHandle);
                registration.Cancel();
            });

            Assert.That(observation.WrappedFinallyCount, Is.LessThanOrEqualTo(1), "the routine is disposed at most once");
            Assert.That(observation.WrappedDisposed, Is.EqualTo(observation.PlainDisposed), "Cancel must behave like a plain StopCoroutine");
            Assert.That(_f.Area.EntryCount, Is.Zero, "Cancel removes the entry whether or not Unity disposes");
            Assert.Pass("StopCoroutine: Unity disposed the enumerator (finally ran): " + observation.PlainDisposed + "; the wrapped routine's finally ran: " + observation.WrappedDisposed);
        }

        // Guard: deactivation stops the coroutine without the package hearing about it; the entry is released by Dispose or left for the sweep.
        [UnityTest]
        public IEnumerator EngineProbe_15b_HostDeactivated_DisposesTheStoppedEnumerator_AndTheWrapperMatchesIt()
        {
            var observation = new DisposeObservation();
            yield return ObserveDispose(observation, (host, plainHandle, registration) => host.gameObject.SetActive(false));

            Assert.That(observation.WrappedDisposed, Is.EqualTo(observation.PlainDisposed), "a deactivated host must treat the wrapper like a plain coroutine");
            Assert.That(observation.EntriesAfter, Is.EqualTo(observation.WrappedDisposed ? 0 : 1), "released by Dispose when Unity disposes, otherwise left for the sweep");
            _f.Area.Cancel();
            Assert.That(_f.Area.EntryCount, Is.Zero);
            Assert.Pass("host deactivated: Unity disposed the enumerator (finally ran): " + observation.PlainDisposed + "; the wrapped routine's finally ran: " + observation.WrappedDisposed + "; entries left before any sweep: " + observation.EntriesAfter);
        }

        // Guard: same, for a destroyed host.
        [UnityTest]
        public IEnumerator EngineProbe_15c_HostDestroyed_DisposesTheStoppedEnumerator_AndTheWrapperMatchesIt()
        {
            var observation = new DisposeObservation();
            yield return ObserveDispose(observation, (host, plainHandle, registration) => Object.Destroy(host.gameObject));

            Assert.That(observation.WrappedDisposed, Is.EqualTo(observation.PlainDisposed), "a destroyed host must treat the wrapper like a plain coroutine");
            Assert.That(observation.EntriesAfter, Is.EqualTo(observation.WrappedDisposed ? 0 : 1), "released by Dispose when Unity disposes, otherwise left for the sweep");
            _f.Area.Cancel();
            Assert.That(_f.Area.EntryCount, Is.Zero);
            Assert.Pass("host destroyed: Unity disposed the enumerator (finally ran): " + observation.PlainDisposed + "; the wrapped routine's finally ran: " + observation.WrappedDisposed + "; entries left before any sweep: " + observation.EntriesAfter);
        }

        // Is StopCoroutine(own handle) from inside the coroutine's own step safe?

        // Records only: the plain engine behaviour. Unity's own logs are captured, not failed on.
        [UnityTest]
        public IEnumerator EngineProbe_16a_StopCoroutine_OfTheOwnHandle_FromInsideItsOwnStep_Plain()
        {
            var log = new CallLog();
            var holder = new HandleHolder();
            var host = _f.NewHost("PlainHost");
            string engineLogs;
            using (var capture = new LogCapture())
            {
                holder.Handle = host.StartCoroutine(CoroutineRoutines.StopsItself(host, holder, log));
                yield return CoroutineFixture.Frames(4);
                engineLogs = capture.Describe();
            }

            Assert.Pass("plain StopCoroutine(own handle) inside its step: steps [" + log + "]; Unity logs: " + engineLogs);
        }

        // Guard: Stop calls StopCoroutine(own handle) from inside the running step; it must not log, must not throw, and the routine must not resume.
        [UnityTest]
        public IEnumerator EngineProbe_16b_LifetimeCancel_FromInsideTheRoutinesOwnStep_StopsItWithoutUnityLogs()
        {
            var host = _f.NewHost();

            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.EndsItsLifetime(_f.Area.Cancel, _f.Log, 2));
            yield return CoroutineFixture.Frames(6);

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "step0", "step1", "end", "after-end" }), "the rest of the step runs, the next step does not");
            Assert.That(registration.IsActive, Is.False);
            Assert.That(_f.Area.EntryCount, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        // Runs a plain and a wrapped try/finally routine on one host, applies stop, and reports what happened to both.
        private IEnumerator ObserveDispose(DisposeObservation observation, Action<CoroutineTestHost, Coroutine, LifetimeRegistration> stop)
        {
            var plainLog = new CallLog();
            var host = _f.NewHost();
            var plainHandle = host.StartCoroutine(CoroutineRoutines.TryFinally(plainLog, "plain"));
            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.TryFinally(_f.Log, "wrapped"));
            yield return CoroutineFixture.Frames(2);
            Assert.That(plainLog.ToArray(), Is.EqualTo(new[] { "plain:start" }), "precondition: the plain routine is suspended inside its try block");
            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "wrapped:start" }), "precondition: the wrapped routine is suspended inside its try block");

            stop(host, plainHandle, registration);
            yield return CoroutineFixture.Frames(2);

            observation.PlainDisposed = CoroutineFixture.Has(plainLog, "plain:finally");
            observation.WrappedDisposed = CoroutineFixture.Has(_f.Log, "wrapped:finally");
            observation.WrappedFinallyCount = CoroutineFixture.CountOf(_f.Log, "wrapped:finally");
            observation.EntriesAfter = _f.Area.EntryCount;
        }

        private sealed class DisposeObservation
        {
            internal bool PlainDisposed;
            internal bool WrappedDisposed;
            internal int WrappedFinallyCount;
            internal int EntriesAfter;
        }
    }
}
