using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.Binding
{
    // The active lifetime under a hierarchy, a clone, a pool and a component that is only disabled.
    [TestFixture]
    public sealed class ActiveLifetimePoolingTests
    {
        private BindingSession _s;
        private CallLog _log;

        [SetUp]
        public void SetUp()
        {
            _s = BindingSession.Begin();
            _log = new CallLog();
        }

        [TearDown]
        public void TearDown()
        {
            _s.Complete();
        }

        // A parent deactivated with many children

        [UnityTest]
        public IEnumerator ParentDeactivated_WithManyChildrenHoldingActiveWork_CancelsEachOnce_AndReactivationRegistersOnce()
        {
            const int children = 100;
            var cancelled = new Counter();
            var started = new Counter();
            var delivered = new Counter();
            var evt = new OwnedEvent("children");
            Action handler = () => delivered.Value++;
            BindingProbe.EnableHook = enabled =>
            {
                var current = enabled.GetActiveLifetime();
                current.OnCancel(cancelled, static c => c.Value++);
                current.Run(started, static async (c, ct) =>
                {
                    c.Value++;
                    await UniTask.Delay(TimeSpan.FromSeconds(60), cancellationToken: ct);
                });
                evt.Subscribe(handler, current);
            };
            var parent = _s.NewObject("Parent");
            var lifetimes = new Lifetime[children];
            for (var i = 0; i < children; i++)
            {
                var child = _s.NewObject("Child" + i);
                child.transform.SetParent(parent.transform, false);
                lifetimes[i] = child.AddComponent<BindingProbe>().GetActiveLifetime();
            }

            Assert.That(started.Value, Is.EqualTo(children), "premise: every child registered in OnEnable");
            Assert.That(evt.SubscriberCount, Is.EqualTo(children));
            Assert.That(_s.Tree.Owners.Count, Is.EqualTo(children));
            for (var i = 0; i < children; i++)
            {
                Assert.That(lifetimes[i].EntryCount, Is.EqualTo(3), "child " + i);
                Assert.That(lifetimes[i].Generation, Is.Zero);
            }

            parent.SetActive(false);

            Assert.That(cancelled.Value, Is.EqualTo(children), "each child's work was cancelled once");
            Assert.That(evt.SubscriberCount, Is.Zero);
            for (var i = 0; i < children; i++)
            {
                Assert.That(lifetimes[i].Generation, Is.EqualTo(1), "child " + i + " advanced one generation");
                Assert.That(lifetimes[i].EntryCount, Is.Zero, "child " + i);
                Assert.That(lifetimes[i].IsDisposed, Is.False);
            }

            parent.SetActive(true);

            Assert.That(cancelled.Value, Is.EqualTo(children), "the reactivation cancelled nothing");
            Assert.That(started.Value, Is.EqualTo(2 * children));
            Assert.That(evt.SubscriberCount, Is.EqualTo(children), "one subscription per child, none doubled");
            for (var i = 0; i < children; i++)
            {
                Assert.That(lifetimes[i].EntryCount, Is.EqualTo(3), "child " + i + " registered once");
                Assert.That(lifetimes[i].Generation, Is.EqualTo(1));
            }

            evt.Invoke();
            Assert.That(delivered.Value, Is.EqualTo(children), "one delivery per child");
            Assert.That(_s.Tree.Owners.Count, Is.EqualTo(children));
            Assert.That(_s.Logs.Count("JANITOR105"), Is.Zero);

            // The cancelled delays end with an exception each in the next frame; nothing is routed.
            yield return BindingScenes.Frames(3);
            LogAssert.NoUnexpectedReceived();
        }

        // A clone of an object whose trigger is bound

        [Test]
        public void Instantiate_OfAnObjectWithABoundTrigger_GivesTheCloneItsOwnLifetimeAndAnUnboundTrigger()
        {
            var original = _s.NewObject("Original");
            var originalProbe = original.AddComponent<BindingProbe>();
            var originalActive = originalProbe.GetActiveLifetime();
            originalActive.Record(_log, "original");
            var clone = Object.Instantiate(original);
            try
            {
                var cloneTrigger = clone.GetComponent<ActiveLifetimeTrigger>();
                Assert.That(cloneTrigger, Is.Not.Null, "the trigger is a component, so it is cloned with the object");
                Assert.That(clone.GetComponents<ActiveLifetimeTrigger>().Length, Is.EqualTo(1));
                Assert.That(cloneTrigger.BoundLifetime, Is.Null, "its lifetime is not serialized, so the clone's trigger is unbound");
                Assert.That(_s.Tree.Owners.Count, Is.EqualTo(1), "the clone has no lifetime until it asks for one");

                var cloneActive = clone.GetComponent<BindingProbe>().GetActiveLifetime();

                Assert.That(cloneActive, Is.Not.SameAs(originalActive), "the clone's active lifetime is its own");
                Assert.That(cloneActive.OwnerObject, Is.SameAs(cloneTrigger));
                Assert.That(cloneTrigger.BoundLifetime, Is.SameAs(cloneActive));
                Assert.That(originalProbe.GetActiveLifetime(), Is.SameAs(originalActive));
                Assert.That(clone.GetComponents<ActiveLifetimeTrigger>().Length, Is.EqualTo(1), "no second trigger was added");
                Assert.That(_s.Tree.Owners.Count, Is.EqualTo(2));

                cloneActive.Record(_log, "clone");
                clone.SetActive(false);

                Assert.That(_log.ToArray(), Is.EqualTo(new[] { "clone" }), "only the clone's work ended");
                Assert.That(originalActive.Generation, Is.Zero);
                Assert.That(originalActive.EntryCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(clone);
            }
        }

        // A pooled object activated and deactivated thousands of times

        [UnityTest]
        public IEnumerator PooledObject_ThousandsOfActivationsWithATaskATimerAndASubscription_KeepsTheIndexAndTheEntriesFlat()
        {
            const int cycles = 2000;
            var cancelled = new Counter();
            var started = new Counter();
            var fired = new Counter();
            var delivered = new Counter();
            var evt = new OwnedEvent("pooled");
            Action handler = () => delivered.Value++;
            BindingProbe.EnableHook = enabled =>
            {
                var current = enabled.GetActiveLifetime();
                current.OnCancel(cancelled, static c => c.Value++);
                current.Run(started, static async (c, ct) =>
                {
                    c.Value++;
                    await UniTask.Delay(TimeSpan.FromSeconds(60), cancellationToken: ct);
                });
                current.After(60f, fired, static c => c.Value++);
                evt.Subscribe(handler, current);
            };
            var probe = _s.NewProbe();
            var lifetime = probe.GetActiveLifetime();
            Assert.That(lifetime.EntryCount, Is.EqualTo(4), "premise: OnCancel, task, timer and subscription");
            var highestEntries = 0;
            var highestOwners = 0;

            for (var i = 0; i < cycles; i++)
            {
                probe.gameObject.SetActive(false);
                probe.gameObject.SetActive(true);
                highestEntries = Math.Max(highestEntries, lifetime.EntryCount);
                highestOwners = Math.Max(highestOwners, _s.Tree.Owners.Count);
            }

            Assert.That(highestEntries, Is.EqualTo(4), "the entry count never grew");
            Assert.That(highestOwners, Is.EqualTo(1), "the owner index never grew");
            Assert.That(lifetime.Generation, Is.EqualTo(cycles));
            Assert.That(cancelled.Value, Is.EqualTo(cycles));
            Assert.That(started.Value, Is.EqualTo(cycles + 1));
            Assert.That(evt.SubscriberCount, Is.EqualTo(1));
            Assert.That(_s.Logs.Count("JANITOR105"), Is.Zero);
            Assert.That(fired.Value, Is.Zero, "no timer of an earlier use fired");

            // 2,000 cancelled delays and timers end with an exception each in the next frame; nothing is routed or logged.
            yield return BindingScenes.Frames(3);
            LogAssert.NoUnexpectedReceived();
            Assert.That(fired.Value, Is.Zero);
            Assert.That(lifetime.EntryCount, Is.EqualTo(4));
        }

        // Excluded on purpose: a Run task or an After timer reads the lifetime token, and the first read of a generation creates
        // one CancellationTokenSource (documented, one per use); a task or timer that is still pending when it is cancelled
        // also ends with an exception in the following frame. So this block registers only items that never read the token,
        // and measures the SetActive calls (Cancel, the trigger, OnEnable) and the three registrations.
        [Test]
        public void PooledObject_RepeatedActivationsWithTokenFreeRegistrations_AllocateNothing()
        {
            const int cycles = 100;
            var cancelled = new Counter();
            var delivered = new Counter();
            var disposable = new DisposeProbe();
            var evt = new OwnedEvent("pooled");
            Action handler = () => delivered.Value++;
            BindingProbe.EnableHook = enabled =>
            {
                var current = enabled.GetActiveLifetime();
                current.OnCancel(cancelled, static c => c.Value++);
                evt.Subscribe(handler, current);
                disposable.AddTo(current);
            };
            var pooled = _s.NewProbe();
            var active = pooled.GetActiveLifetime();
            TestDelegate cycle = () =>
            {
                for (var i = 0; i < cycles; i++)
                {
                    pooled.gameObject.SetActive(false);
                    pooled.gameObject.SetActive(true);
                }
            };

            // Mono allocates on the first run of an un-run lambda, so the block runs once before it is measured.
            cycle();

            Assert.That(cycle, Is.Not.AllocatingGCMemory());

            Assert.That(cancelled.Value, Is.EqualTo(2 * cycles), "the measured block really cancelled and registered again");
            Assert.That(disposable.DisposeCount, Is.EqualTo(2 * cycles));
            Assert.That(evt.SubscriberCount, Is.EqualTo(1));
            Assert.That(active.EntryCount, Is.EqualTo(3));
            Assert.That(active.Generation, Is.EqualTo(2 * cycles));
            Assert.That(_s.Tree.Owners.Count, Is.EqualTo(1));
        }

        // Disabling a user component is not a deactivation

        [Test]
        public void EnabledFalse_OnAUserComponent_LeavesTheActiveLifetimeAlone()
        {
            var probe = _s.NewProbe();
            var active = probe.GetActiveLifetime();
            active.Record(_log, "item");
            var trigger = probe.GetComponent<ActiveLifetimeTrigger>();
            var deactivations = trigger.Deactivations;

            probe.enabled = false;

            Assert.That(_log.Count, Is.Zero, "disabling the component cancels nothing");
            Assert.That(active.Generation, Is.Zero);
            Assert.That(trigger.Deactivations, Is.EqualTo(deactivations));
            Assert.That(trigger.enabled, Is.True);
            Assert.That(active.Record(_log, "while disabled").IsActive, Is.True, "the object is active, so work is accepted");

            probe.enabled = true;

            Assert.That(_log.Count, Is.Zero);

            probe.gameObject.SetActive(false);

            Assert.That(_log.ToArray(), Is.EquivalentTo(new[] { "item", "while disabled" }), "a real deactivation still cancels it");
            Assert.That(active.Generation, Is.EqualTo(1));
        }

        // Destroying only the trigger

        [UnityTest]
        public IEnumerator Destroy_OfTheTriggerAlone_CancelsOnceAndDisposesTheLifetime_AndTheNextAskStartsANewOne()
        {
            var probe = _s.NewProbe();
            var old = probe.GetActiveLifetime();
            old.Record(_log, "item");
            var trigger = probe.GetComponent<ActiveLifetimeTrigger>();

            Object.Destroy(trigger);
            yield return null;

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "item" }), "the work ended once");
            Assert.That(old.IsDisposed, Is.True, "the lifetime follows its trigger");
            Assert.That(_s.Tree.Owners.Count, Is.Zero);
            Assert.That(probe.TryGetComponent<ActiveLifetimeTrigger>(out _), Is.False);
            Assert.That(old.Record(_log, "late").IsActive, Is.False, "a disposed lifetime refuses work");

            var fresh = probe.GetActiveLifetime();

            Assert.That(fresh, Is.Not.SameAs(old));
            Assert.That(fresh.IsDisposed, Is.False);
            Assert.That(fresh.Generation, Is.Zero);
            Assert.That(probe.GetComponents<ActiveLifetimeTrigger>().Length, Is.EqualTo(1));
            Assert.That(_s.Tree.Owners.Count, Is.EqualTo(1));
            Assert.That(fresh.Record(_log, "again").IsActive, Is.True);

            probe.gameObject.SetActive(false);

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "item", "late", "again" }), "the new trigger is bound, so a deactivation cancels the new lifetime");
        }
    }
}
