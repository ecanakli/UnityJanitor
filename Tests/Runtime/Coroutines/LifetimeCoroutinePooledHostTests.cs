using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.Coroutines
{
    // Unity never disposes a stopped coroutine, so the host's deactivation counter is what lets the sweep reclaim entries of a pooled host.
    [TestFixture]
    public sealed class LifetimeCoroutinePooledHostTests
    {
        private const int Cycles = 240;

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

        // The pooled host

        // Guard: IsFinishedCore compares the trigger's deactivation counter; without it every dead entry looks alive once the host is active again.
        [UnityTest]
        public IEnumerator PooledHost_CycledOnAPlainLifetime_KeepsTheEntryListBoundedAndReclaimsDeadRoutines()
        {
            var host = _f.NewHost("PooledHost");
            yield return PooledHostScenario(_f.Area, host);
        }

        // Guard: same, on the host's own component lifetime, which a pooled object never ends.
        [UnityTest]
        public IEnumerator PooledHost_CycledOnItsOwnComponentLifetime_KeepsTheEntryListBoundedAndReclaimsDeadRoutines()
        {
            var host = _f.NewHost("PooledHost");
            yield return PooledHostScenario(host.GetLifetime(), host);
        }

        // The deactivation counter and the probe

        // Guard: OnDisable counts whether or not an active lifetime is bound.
        [Test]
        public void Trigger_DeactivationCounter_CountsEveryOnDisableBoundOrNot()
        {
            var host = _f.NewHost();
            _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(new Counter()));
            var hasTrigger = host.TryGetComponent(out ActiveLifetimeTrigger trigger);
            Assert.That(hasTrigger, Is.True, "starting a coroutine adds the trigger");
            var atStart = trigger.Deactivations;

            for (var i = 0; i < 3; i++)
            {
                host.gameObject.SetActive(false);
                host.gameObject.SetActive(true);
            }

            Assert.That(trigger.Deactivations, Is.EqualTo(atStart + 3), "counted without a bound lifetime");

            host.GetActiveLifetime();
            for (var i = 0; i < 2; i++)
            {
                host.gameObject.SetActive(false);
                host.gameObject.SetActive(true);
            }

            Assert.That(trigger.Deactivations, Is.EqualTo(atStart + 5), "and with one");
        }

        // Guard: the probe reports a host that was deactivated and is active again.
        [Test]
        public void IsFinishedProbe_HostDeactivatedAndReactivated_ReportsFinished()
        {
            var host = _f.NewHost();
            _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(new Counter()));
            var wrapper = CoroutineFixture.WrapperOf(_f.Area);
            Assert.That(LifetimeCoroutine.IsFinishedProbe(wrapper), Is.False, "precondition: running");

            host.gameObject.SetActive(false);
            host.gameObject.SetActive(true);

            Assert.That(host.gameObject.activeInHierarchy, Is.True, "precondition: the host is active again");
            Assert.That(LifetimeCoroutine.IsFinishedProbe(wrapper), Is.True);
        }

        // Guard: a deactivation of an ancestor reaches the trigger's OnDisable too.
        [Test]
        public void IsFinishedProbe_ParentDeactivatedAndReactivated_ReportsFinished()
        {
            var parent = _f.NewObject("Parent");
            var host = _f.NewHost("ChildHost", parent: parent.transform);
            _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(new Counter()));
            var wrapper = CoroutineFixture.WrapperOf(_f.Area);

            parent.SetActive(false);
            parent.SetActive(true);

            Assert.That(LifetimeCoroutine.IsFinishedProbe(wrapper), Is.True);
        }

        // Guard: the counter is recorded at start, so earlier deactivations do not end a new routine.
        [UnityTest]
        public IEnumerator IsFinishedProbe_RoutineStartedAfterEarlierDeactivations_ReportsNotFinishedAndKeepsRunning()
        {
            var counter = new Counter();
            var host = _f.NewHost();
            _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(new Counter()));
            for (var i = 0; i < 3; i++)
            {
                host.gameObject.SetActive(false);
                host.gameObject.SetActive(true);
            }

            _f.Area.Cancel();
            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(counter));
            var wrapper = CoroutineFixture.WrapperOf(_f.Area);
            yield return CoroutineFixture.Frames(3);

            Assert.That(LifetimeCoroutine.IsFinishedProbe(wrapper), Is.False);
            Assert.That(registration.IsActive, Is.True);
            Assert.That(counter.Value, Is.GreaterThan(1));
        }

        // The hidden trigger

        // Guard: GetOrAdd adds one unbound trigger per GameObject and never creates an active lifetime.
        [Test]
        public void Trigger_AddedForACoroutine_IsOneUnboundTriggerAndCreatesNoActiveLifetime()
        {
            var host = _f.NewHost();
            var owners = LifetimeTree.Default.Owners;
            var ownersBefore = owners.Count;

            _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(new Counter()));
            _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(new Counter()));
            _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(new Counter()));

            var hasTrigger = host.TryGetComponent(out ActiveLifetimeTrigger trigger);
            Assert.That(hasTrigger, Is.True);
            Assert.That(host.GetComponents<ActiveLifetimeTrigger>().Length, Is.EqualTo(1), "three coroutines share one trigger");
            Assert.That(trigger.BoundLifetime, Is.Null, "no active lifetime is bound");
            Assert.That(owners.Count, Is.EqualTo(ownersBefore), "and none is created or indexed");
            Assert.That(owners.TryGet(trigger.GetInstanceID(), out _), Is.False);
        }

        // Guard: Start adds the trigger only after the host pre-check, so a coroutine that is not started leaves the host untouched.
        [Test]
        public void Trigger_CoroutineNotStartedOnAnInactiveHost_AddsNothing()
        {
            var host = _f.NewHost("InactiveHost", active: false);
            CoroutineFixture.ExpectWarning("JANITOR110");

            _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(new Counter()));

            Assert.That(host.TryGetComponent<ActiveLifetimeTrigger>(out _), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: ForActive takes the unbound trigger through ResolveParent, so the category is honoured and CheckParent never runs.
        [Test]
        public void GetActiveLifetime_AfterACoroutineAddedTheTrigger_HonoursTheCategoryWithoutJanitor113()
        {
            var category = _f.Area.CreateChild("category");
            var host = _f.NewHost();
            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(new Counter()));
            var hasTrigger = host.TryGetComponent(out ActiveLifetimeTrigger trigger);
            Assert.That(hasTrigger, Is.True);

            var active = host.GetActiveLifetime(category);

            Assert.That(active.Kind, Is.EqualTo(LifetimeKind.Active));
            Assert.That(active.Parent, Is.SameAs(category), "the category of the first GetActiveLifetime call decides");
            Assert.That(active.Placed, Is.True);
            Assert.That(trigger.BoundLifetime, Is.SameAs(active), "the existing trigger was bound, not replaced");
            Assert.That(host.GetComponents<ActiveLifetimeTrigger>().Length, Is.EqualTo(1));
            Assert.That(host.GetActiveLifetime(category), Is.SameAs(active), "and the same category again is not a mismatch");
            Assert.That(registration.IsActive, Is.True, "binding does not disturb the running coroutine");
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: Bind sets the owner GameObject of a trigger that was added earlier, so the inactive-object refusal works on it.
        [Test]
        public void GetActiveLifetime_AfterACoroutineAddedTheTrigger_BindsItAndCancelsOnDeactivation()
        {
            var host = _f.NewHost("BoundLater");
            _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(new Counter()));
            var hasTrigger = host.TryGetComponent(out ActiveLifetimeTrigger trigger);
            Assert.That(hasTrigger, Is.True);

            var active = host.GetActiveLifetime();
            var generation = active.Generation;
            host.gameObject.SetActive(false);

            Assert.That(active.OwnerObject, Is.SameAs(trigger));
            Assert.That(active.Generation, Is.EqualTo(generation + 1), "deactivation cancels the lifetime that was bound late");
            CoroutineFixture.ExpectWarning("JANITOR108");
            var refused = active.OnCancel(() => { });
            Assert.That(refused.IsActive, Is.False, "and an inactive object refuses registrations");
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: GetOrAdd finds a trigger that an active lifetime already bound, so a coroutine adds no second one.
        [UnityTest]
        public IEnumerator Trigger_ActiveLifetimeFirstThenACoroutine_ReusesTheBoundTrigger()
        {
            var counter = new Counter();
            var host = _f.NewHost();
            var active = host.GetActiveLifetime();
            var trigger = host.GetComponent<ActiveLifetimeTrigger>();

            var registration = active.StartCoroutine(host, CoroutineRoutines.Forever(counter));
            yield return CoroutineFixture.Frames(2);

            Assert.That(host.GetComponents<ActiveLifetimeTrigger>().Length, Is.EqualTo(1));
            Assert.That(trigger.BoundLifetime, Is.SameAs(active));
            Assert.That(registration.IsActive, Is.True);
            Assert.That(counter.Value, Is.GreaterThan(1));
        }

        // 240 cycles on one pooled host, plus a control routine on a host that stays active.
        private IEnumerator PooledHostScenario(Lifetime longLived, CoroutineTestHost host)
        {
            var steps = new Counter();
            var control = new Counter();
            var controlHost = _f.NewHost("ControlHost");
            longLived.StartCoroutine(controlHost, CoroutineRoutines.Forever(control));
            var live = 2;
            var maxEntries = 0;

            for (var cycle = 0; cycle < Cycles; cycle++)
            {
                host.gameObject.SetActive(true);
                longLived.StartCoroutine(host, CoroutineRoutines.Forever(steps));
                maxEntries = Math.Max(maxEntries, longLived.EntryCount);
                host.gameObject.SetActive(false);
                if (cycle % 40 == 39)
                {
                    yield return CoroutineFixture.Frames(1);
                }
            }

            Assert.That(steps.Value, Is.EqualTo(Cycles), "every routine took its first step and was stopped with its host, none resumed");
            Assert.That(maxEntries, Is.LessThanOrEqualTo((2 * live) + 16), "entries stay within 2 x live + 16 (the dead routines of earlier cycles are swept)");

            // A sweep with the host active again: the dead routines must go, the control routine must stay.
            host.gameObject.SetActive(true);
            for (var i = 0; i < 20 && CoroutineFixture.CountCoroutineEntries(longLived) > 1; i++)
            {
                longLived.OnCancel(() => { });
            }

            Assert.That(CoroutineFixture.CountCoroutineEntries(longLived), Is.EqualTo(1), "only the control routine is left");
            var before = control.Value;
            yield return CoroutineFixture.Frames(3);
            Assert.That(control.Value, Is.GreaterThan(before), "the sweep must not touch a live routine");

            longLived.Cancel();
            var atCancel = control.Value;
            yield return CoroutineFixture.Frames(3);
            Assert.That(control.Value, Is.EqualTo(atCancel));
            Assert.That(longLived.EntryCount, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
