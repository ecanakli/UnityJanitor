using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.Events;

namespace Ecanakli.Janitor.Tests
{
    // Work offered to an ended lifetime is counted as refused with its call site, whichever API offered it.
    [TestFixture]
    public sealed class RefusedRegistrationCountTests
    {
        private DiagnosticsRecordingKit _d;
        private TestScope _t;
        private Lifetime _ended;

        [SetUp]
        public void SetUp()
        {
            _d = new DiagnosticsRecordingKit();
            _t = new TestScope();
            _ended = _t.App.CreateChild("ended");
            _ended.Dispose();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                _t.Complete();
            }
            finally
            {
                _d.Dispose();
            }
        }

        [Test]
        public void Run_OnAnEndedLifetime_IsCountedWithItsCallSite()
        {
            _ended.Run(static ct => UniTask.CompletedTask, "RunSite", 11);

            AssertRefusedOnce("RunSite", 11);
        }

        [Test]
        public void RunWithState_OnAnEndedLifetime_IsCountedWithItsCallSite()
        {
            _ended.Run(new Counter(), static (c, ct) => UniTask.CompletedTask, "RunStateSite", 12);

            AssertRefusedOnce("RunStateSite", 12);
        }

        [Test]
        public void After_OnAnEndedLifetime_IsCountedWithItsCallSite()
        {
            _ended.After(1f, static () => { }, false, "AfterSite", 21);

            AssertRefusedOnce("AfterSite", 21);
        }

        [Test]
        public void AfterWithState_OnAnEndedLifetime_IsCountedWithItsCallSite()
        {
            _ended.After(1f, new Counter(), static c => c.Value++, false, "AfterStateSite", 22);

            AssertRefusedOnce("AfterStateSite", 22);
        }

        [Test]
        public void AfterWithoutADelay_OnAnEndedLifetime_IsCountedAndDoesNotRun()
        {
            var counter = new Counter();

            _ended.After(0f, counter, static c => c.Value++, false, "InPlaceSite", 23);

            AssertRefusedOnce("InPlaceSite", 23);
            Assert.That(counter.Value, Is.Zero);
        }

        [Test]
        public void Every_OnAnEndedLifetime_IsCountedWithItsCallSite()
        {
            _ended.Every(1f, static () => { }, false, "EverySite", 24);

            AssertRefusedOnce("EverySite", 24);
        }

        [Test]
        public void OwnedEventSubscribe_OnAnEndedOwner_IsCountedWithItsCallSite()
        {
            var evt = new OwnedEvent("Alloc");

            evt.Subscribe(static () => { }, _ended, "OwnedSite", 31);

            AssertRefusedOnce("OwnedSite", 31);
            Assert.That(evt.SubscriberCount, Is.Zero);
        }

        [Test]
        public void OwnedEventWithAnArgument_OnAnEndedOwner_IsCountedWithItsCallSite()
        {
            var evt = new OwnedEvent<int>("Alloc");

            evt.Subscribe(static value => { }, _ended, "OwnedIntSite", 32);

            AssertRefusedOnce("OwnedIntSite", 32);
        }

        [Test]
        public void PairedSubscribe_OnAnEndedOwner_IsCountedAndCallsNothing()
        {
            var adds = 0;

            _ended.Subscribe(h => adds++, h => { }, new Action(() => { }), "PairedSite", 41);

            AssertRefusedOnce("PairedSite", 41);
            Assert.That(adds, Is.Zero, "add is not called for an ended owner");
        }

        [Test]
        public void UnityEventSubscribe_OnAnEndedOwner_IsCountedWithItsCallSite()
        {
            var evt = new UnityEvent();
            var hits = 0;

            evt.Subscribe(() => hits++, _ended, "UnityEventSite", 51);
            evt.Invoke();

            AssertRefusedOnce("UnityEventSite", 51);
            Assert.That(hits, Is.Zero);
        }

        [Test]
        public void UnityEventWithAnArgument_OnAnEndedOwner_IsCountedWithItsCallSite()
        {
            var evt = new UnityEvent<int>();

            evt.Subscribe(value => { }, _ended, "UnityEventIntSite", 52);

            AssertRefusedOnce("UnityEventIntSite", 52);
        }

        [Test]
        public void StartCoroutine_OnAnEndedLifetime_IsCountedWithItsCallSite()
        {
            var registration = _ended.StartCoroutine(null, Routine(), "CoroutineSite", 61);

            AssertRefusedOnce("CoroutineSite", 61);
            Assert.That(registration.IsActive, Is.False);
        }

        [Test]
        public void SeveralRefusals_AreCounted_AndOnlyTheFirstCallSiteIsKept()
        {
            _ended.Run(static ct => UniTask.CompletedTask, "First", 1);
            _ended.After(1f, static () => { }, false, "Second", 2);
            _ended.Every(1f, static () => { }, false, "Third", 3);

            Assert.That(_ended.DiagRefusedCount, Is.EqualTo(3));
            Assert.That(_ended.DiagFirstRefusedMember, Is.EqualTo("First"));
            Assert.That(_ended.DiagFirstRefusedLine, Is.EqualTo(1));
        }

        [Test]
        public void RefusalsThroughRegister_AreStillCountedOnce()
        {
            _ended.OnCancel(static () => { }, "OnCancelSite", 71);

            AssertRefusedOnce("OnCancelSite", 71);
        }

        private void AssertRefusedOnce(string member, int line)
        {
            Assert.That(_ended.DiagRefusedCount, Is.EqualTo(1), "the early return must be counted exactly once");
            Assert.That(_ended.DiagFirstRefusedMember, Is.EqualTo(member));
            Assert.That(_ended.DiagFirstRefusedLine, Is.EqualTo(line));
        }

        private static IEnumerator Routine()
        {
            yield break;
        }
    }
}
