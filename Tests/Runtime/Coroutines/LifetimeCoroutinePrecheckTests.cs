using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.Coroutines
{
    // StartCoroutine pre-checks: a coroutine is not started when it could only fail. The tree is the default one, so component and active lifetimes work.
    [TestFixture]
    public sealed class LifetimeCoroutinePrecheckTests
    {
        private CoroutineFixture _f;

        [SetUp]
        public void SetUp()
        {
            _f = new CoroutineFixture(makeDefault: true);
        }

        [TearDown]
        public void TearDown()
        {
            _f.Complete();
        }

        // Host pre-check

        // Guard: Start checks the host before StartCoroutine, so Unity's own "inactive" error is never produced.
        [UnityTest]
        public IEnumerator StartCoroutine_InactiveHost_IsNotStartedWithJanitor110AndNoUnityError()
        {
            var counter = new Counter();
            var host = _f.NewHost("InactiveHost", active: false);
            CoroutineFixture.ExpectWarning("JANITOR110.*'InactiveHost' is inactive");

            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(counter));

            Assert.That(registration.IsActive, Is.False);
            Assert.That(counter.Value, Is.Zero, "the routine must not take a single step");
            Assert.That(_f.Area.EntryCount, Is.Zero);
            LogAssert.NoUnexpectedReceived();

            host.gameObject.SetActive(true);
            yield return CoroutineFixture.Frames(3);
            Assert.That(counter.Value, Is.Zero, "activating the host later must not start the routine");
            Assert.That(_f.Area.EntryCount, Is.Zero);
        }

        // Guard: the check reads activeInHierarchy, not activeSelf.
        [Test]
        public void StartCoroutine_HostUnderAnInactiveParent_IsNotStartedWithJanitor110AndNoUnityError()
        {
            var counter = new Counter();
            var parent = _f.NewObject("Parent", active: false);
            var host = _f.NewHost("ChildHost", parent: parent.transform);
            Assert.That(host.gameObject.activeSelf, Is.True, "precondition: the host itself is active");
            CoroutineFixture.ExpectWarning("JANITOR110.*'ChildHost' is inactive");

            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(counter));

            Assert.That(registration.IsActive, Is.False);
            Assert.That(counter.Value, Is.Zero);
            Assert.That(_f.Area.EntryCount, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: Unity's == makes a destroyed host match; the warning names the reason and the host is never dereferenced.
        [Test]
        public void StartCoroutine_DestroyedHost_IsNotStartedWithAJanitor110Warning()
        {
            var counter = new Counter();
            var host = _f.NewHost();
            Object.DestroyImmediate(host.gameObject);
            CoroutineFixture.ExpectWarning("JANITOR110.*because its host was destroyed");

            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(counter));

            Assert.That(registration.IsActive, Is.False);
            Assert.That(counter.Value, Is.Zero);
            Assert.That(_f.Area.EntryCount, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: a C# null host does not throw; it is reported as not started.
        [Test]
        public void StartCoroutine_NullHost_IsNotStartedWithAJanitor110WarningAndNeverThrows()
        {
            var counter = new Counter();
            CoroutineTestHost host = null;
            CoroutineFixture.ExpectWarning("JANITOR110.*because its host is null");

            LifetimeRegistration registration = default;
            Assert.DoesNotThrow(() => registration = _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(counter)));

            Assert.That(registration.IsActive, Is.False);
            Assert.That(counter.Value, Is.Zero);
            Assert.That(_f.Area.EntryCount, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        // Lifetime state pre-check

        // Guard: Start returns default for a lifetime that is not Active, and logs nothing (the caller is inside an OnCancel action).
        [Test]
        public void StartCoroutine_WhileTheLifetimeIsCancelling_IsNotStartedAndLogsNothing()
        {
            var counter = new Counter();
            var host = _f.NewHost();
            LifetimeRegistration seen = default;
            _f.Area.OnCancel(() => seen = _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(counter)));

            _f.Area.Cancel();

            Assert.That(seen.IsActive, Is.False);
            Assert.That(counter.Value, Is.Zero);
            Assert.That(_f.Area.EntryCount, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: same, for a disposed lifetime.
        [Test]
        public void StartCoroutine_OnADisposedLifetime_IsNotStartedAndLogsNothing()
        {
            var counter = new Counter();
            var host = _f.NewHost();
            var child = _f.Area.CreateChild("child");
            child.Dispose();

            var registration = child.StartCoroutine(host, CoroutineRoutines.Forever(counter));

            Assert.That(registration.IsActive, Is.False);
            Assert.That(counter.Value, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: the state check comes first, so an inactive host on a disposed lifetime stays silent.
        [Test]
        public void StartCoroutine_OnADisposedLifetimeWithAnInactiveHost_LogsNothing()
        {
            var host = _f.NewHost("InactiveHost", active: false);
            var child = _f.Area.CreateChild("child");
            child.Dispose();

            var registration = child.StartCoroutine(host, CoroutineRoutines.Forever(new Counter()));

            Assert.That(registration.IsActive, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        // Active and component lifetimes

        // Guard: the host pre-check runs before Register, so JANITOR110 wins over JANITOR108 when the host is the trigger's own object.
        [Test]
        public void StartCoroutine_ActiveLifetimeWhoseHostIsInactive_ReportsJanitor110NotJanitor108()
        {
            var counter = new Counter();
            var host = _f.NewHost("ActiveHost");
            var active = host.GetActiveLifetime();
            host.gameObject.SetActive(false);
            Assert.That(active.State, Is.EqualTo(LifetimeState.Active), "deactivation opens a fresh generation");
            CoroutineFixture.ExpectWarning("JANITOR110.*'ActiveHost' is inactive");

            var registration = active.StartCoroutine(host, CoroutineRoutines.Forever(counter));

            Assert.That(registration.IsActive, Is.False);
            Assert.That(counter.Value, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: the active lifetime's trigger cancels on OnDisable, which stops the routine and removes the entry at once.
        [UnityTest]
        public IEnumerator ActiveLifetime_HostDeactivated_StopsTheRoutineAtOnceAndAReactivationStartsFresh()
        {
            var counter = new Counter();
            var host = _f.NewHost();
            var active = host.GetActiveLifetime();
            var registration = active.StartCoroutine(host, CoroutineRoutines.Forever(counter));
            yield return CoroutineFixture.Frames(2);
            Assert.That(active.EntryCount, Is.EqualTo(1));

            host.gameObject.SetActive(false);

            Assert.That(active.EntryCount, Is.Zero, "the trigger's OnDisable removes the entry synchronously");
            Assert.That(registration.IsActive, Is.False);
            var atDisable = counter.Value;
            yield return CoroutineFixture.Frames(3);
            host.gameObject.SetActive(true);
            yield return CoroutineFixture.Frames(3);
            Assert.That(counter.Value, Is.EqualTo(atDisable), "neither Unity nor the package restarts the old routine");

            var fresh = new Counter();
            var second = active.StartCoroutine(host, CoroutineRoutines.Forever(fresh));
            yield return CoroutineFixture.Frames(3);
            Assert.That(second.IsActive, Is.True);
            Assert.That(fresh.Value, Is.GreaterThan(1), "the new generation accepts and runs a routine");
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: a component lifetime disposes at destroy; Stop must cope with a host that is going away, without a Unity log.
        [UnityTest]
        public IEnumerator ComponentLifetime_HostGameObjectDestroyed_StopsTheRoutineAndLeavesNoEntryOrLog()
        {
            var counter = new Counter();
            var host = _f.NewHost();
            var lifetime = host.GetLifetime();
            lifetime.StartCoroutine(host, CoroutineRoutines.Forever(counter));
            yield return CoroutineFixture.Frames(2);
            Assert.That(counter.Value, Is.GreaterThan(1));

            Object.Destroy(host.gameObject);
            yield return CoroutineFixture.Frames(2);
            var afterDestroy = counter.Value;
            yield return CoroutineFixture.Frames(3);

            Assert.That(lifetime.IsDisposed, Is.True);
            Assert.That(lifetime.EntryCount, Is.Zero);
            Assert.That(counter.Value, Is.EqualTo(afterDestroy));
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: destroying only the host component (not its GameObject) disposes the component lifetime and stops the routine.
        [UnityTest]
        public IEnumerator ComponentLifetime_HostComponentDestroyed_StopsTheRoutineAndLeavesNoEntryOrLog()
        {
            var counter = new Counter();
            var host = _f.NewHost();
            var lifetime = host.GetLifetime();
            lifetime.StartCoroutine(host, CoroutineRoutines.Forever(counter));
            yield return CoroutineFixture.Frames(2);

            Object.DestroyImmediate(host);
            var afterDestroy = counter.Value;
            yield return CoroutineFixture.Frames(3);

            Assert.That(lifetime.IsDisposed, Is.True);
            Assert.That(lifetime.EntryCount, Is.Zero);
            Assert.That(counter.Value, Is.EqualTo(afterDestroy));
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: Tree.ReportError takes the owner object of the lifetime, so a component lifetime names its component.
        [UnityTest]
        public IEnumerator Exception_OnAComponentLifetime_NamesTheHostComponentAsTheOwner()
        {
            var counter = new Counter();
            var host = _f.NewHost();
            var lifetime = host.GetLifetime();
            lifetime.StartCoroutine(host, CoroutineRoutines.ThrowsAfter(counter, 1, new InvalidOperationException("owner boom")));

            yield return CoroutineFixture.Frames(3);

            Assert.That(_f.Errors.Count, Is.EqualTo(1));
            var context = _f.Errors[0].Context;
            Assert.That(context.Source, Is.EqualTo(LifetimeErrorSource.Coroutine));
            Assert.That(context.Owner, Is.SameAs(host));
            Assert.That(context.LifetimeName, Is.EqualTo(nameof(CoroutineTestHost)));
            Assert.That(context.Member, Is.EqualTo(nameof(Exception_OnAComponentLifetime_NamesTheHostComponentAsTheOwner)));
            _f.Errors.Clear();
        }
    }
}
