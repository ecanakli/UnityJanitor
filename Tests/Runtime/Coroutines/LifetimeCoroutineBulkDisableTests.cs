using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.Coroutines
{
    // Disabling components, the trigger included, is not a deactivation: Unity keeps running the coroutines of an active object.
    [TestFixture]
    public sealed class LifetimeCoroutineBulkDisableTests
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

        [UnityTest]
        public IEnumerator BulkDisable_OfEveryBehaviourOnTheHost_KeepsAComponentBoundEntryLive_AndCancelStillStopsTheRoutine()
        {
            var counter = new Counter();
            var host = _f.NewHost();
            var lifetime = host.GetLifetime();
            lifetime.StartCoroutine(host, CoroutineRoutines.Forever(counter));
            var wrapper = CoroutineFixture.WrapperOf(lifetime);
            yield return CoroutineFixture.Frames(2);

            var behaviours = host.GetComponentsInChildren<Behaviour>();
            Assert.That(behaviours.Length, Is.EqualTo(2), "the host and the trigger");
            for (var i = 0; i < behaviours.Length; i++)
            {
                behaviours[i].enabled = false;
            }

            Assert.That(LifetimeCoroutine.IsFinishedProbe(wrapper), Is.False, "Unity still runs the routine, so its entry is not finished");
            var running = counter.Value;
            yield return CoroutineFixture.Frames(2);
            Assert.That(counter.Value, Is.GreaterThan(running), "disabling a component stops none of its coroutines");

            // The first sweep runs at 16 entries; a wrongly finished entry would be dropped there.
            for (var i = 0; i < 20; i++)
            {
                lifetime.OnCancel(() => { });
            }

            Assert.That(CoroutineFixture.CountCoroutineEntries(lifetime), Is.EqualTo(1), "the sweep keeps the live entry");

            lifetime.Cancel();
            var atCancel = counter.Value;
            yield return CoroutineFixture.Frames(3);

            Assert.That(counter.Value, Is.EqualTo(atCancel), "Cancel still stops the routine");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Trigger_SelfDisable_IsNeverCounted_AndARealDeactivationAlwaysIs()
        {
            var host = _f.NewHost();
            _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(new Counter()));
            var trigger = host.GetComponent<ActiveLifetimeTrigger>();
            var atStart = trigger.Deactivations;

            trigger.enabled = false;

            Assert.That(trigger.Deactivations, Is.EqualTo(atStart), "a disabled trigger counts nothing");

            _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(new Counter()));

            Assert.That(trigger.enabled, Is.True, "starting a coroutine enables the trigger again");
            Assert.That(trigger.Deactivations, Is.EqualTo(atStart));

            host.gameObject.SetActive(false);
            host.gameObject.SetActive(true);

            Assert.That(trigger.Deactivations, Is.EqualTo(atStart + 1), "a real deactivation still counts");
        }

        [Test]
        public void Trigger_AddedForACoroutine_IsHiddenInTheInspector()
        {
            var host = _f.NewHost();

            _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(new Counter()));

            var trigger = host.GetComponent<ActiveLifetimeTrigger>();
            Assert.That(trigger.hideFlags, Is.EqualTo(HideFlags.HideInInspector));
            Assert.That(host.hideFlags, Is.EqualTo(HideFlags.None), "the user's own component is left alone");
            Assert.That(host.gameObject.hideFlags, Is.EqualTo(HideFlags.None));
        }
    }
}
