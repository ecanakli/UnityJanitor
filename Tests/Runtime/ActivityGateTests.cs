using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.Binding
{
    // An active lifetime, and every area created below it, accepts work only while its GameObject is active.
    [TestFixture]
    public sealed class ActivityGateTests
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

        [Test]
        public void ChildArea_WhileTheObjectIsInactive_TerminatesRegistrationsWithJanitor108()
        {
            var probe = _s.NewProbe();
            var area = probe.GetActiveLifetime().CreateChild("area");
            probe.gameObject.SetActive(false);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR108"));
            var ran = 0;

            var registration = area.OnCancel(() => ran++);

            Assert.That(ran, Is.EqualTo(1), "the item is terminated at once");
            Assert.That(registration.IsActive, Is.False);
            Assert.That(area.EntryCount, Is.Zero);
            Assert.That(_s.Logs.Count("JANITOR108"), Is.EqualTo(1));
#if UNITY_EDITOR
            Assert.That(area.DiagRefusedCount, Is.EqualTo(1), "the refusal is counted on the area that was asked");
#endif
        }

        [Test]
        public void AreaBelowAnArea_WhileTheObjectIsInactive_RefusesToo()
        {
            var probe = _s.NewProbe();
            var deeper = probe.GetActiveLifetime().CreateChild("area").CreateChild("deeper");
            probe.gameObject.SetActive(false);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR108"));

            var registration = deeper.Record(_log, "deeper");

            Assert.That(registration.IsActive, Is.False);
            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "deeper" }), "terminated at once");
        }

        [Test]
        public void ChildArea_AfterTheObjectIsActiveAgain_AcceptsWorkWithoutAWarning()
        {
            var probe = _s.NewProbe();
            var area = probe.GetActiveLifetime().CreateChild("area");
            probe.gameObject.SetActive(false);
            probe.gameObject.SetActive(true);

            var registration = area.Record(_log, "item");

            Assert.That(registration.IsActive, Is.True);
            Assert.That(area.EntryCount, Is.EqualTo(1));
            Assert.That(_s.Logs.Count("JANITOR108"), Is.Zero);
        }

        [Test]
        public void ChildArea_Run_WhileTheObjectIsInactive_StartsNothing()
        {
            var probe = _s.NewProbe();
            var area = probe.GetActiveLifetime().CreateChild("area");
            probe.gameObject.SetActive(false);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR108"));
            var started = 0;

            area.Run(ct =>
            {
                started++;
                return UniTask.CompletedTask;
            });

            Assert.That(started, Is.Zero);
            Assert.That(area.EntryCount, Is.Zero);
        }

        [Test]
        public void RunAfterAndEvery_OnTheActiveLifetime_WhileTheObjectIsInactive_StartNothing()
        {
            var probe = _s.NewProbe();
            var active = probe.GetActiveLifetime();
            probe.gameObject.SetActive(false);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR108"));
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR108"));
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR108"));
            var started = 0;

            active.Run(ct =>
            {
                started++;
                return UniTask.CompletedTask;
            });
            active.After(1f, () => started++);
            active.Every(1f, () => started++);

            Assert.That(started, Is.Zero);
            Assert.That(active.EntryCount, Is.Zero);
            Assert.That(_s.Logs.Count("JANITOR108"), Is.EqualTo(3));
        }

        [Test]
        public void ChildArea_OwnedEventSubscribe_WhileTheObjectIsInactive_AddsNothing()
        {
            var probe = _s.NewProbe();
            var area = probe.GetActiveLifetime().CreateChild("area");
            probe.gameObject.SetActive(false);
            var evt = new OwnedEvent("inactive");
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR108"));

            var registration = evt.Subscribe(() => { }, area);

            Assert.That(registration.IsActive, Is.False);
            Assert.That(evt.SubscriberCount, Is.Zero, "the terminated entry removes its slot");
        }

        [Test]
        public void Token_WhileTheObjectIsInactive_IsCancelledForTheActiveLifetimeAndItsAreas()
        {
            var probe = _s.NewProbe();
            var active = probe.GetActiveLifetime();
            var area = active.CreateChild("area");
            var deeper = area.CreateChild("deeper");
            probe.gameObject.SetActive(false);

            Assert.That(active.Token.IsCancellationRequested, Is.True);
            Assert.That(area.Token.IsCancellationRequested, Is.True);
            Assert.That(deeper.Token.IsCancellationRequested, Is.True);

            probe.gameObject.SetActive(true);

            Assert.That(active.Token.IsCancellationRequested, Is.False, "an active object gets a live token again");
            Assert.That(area.Token.IsCancellationRequested, Is.False);
            Assert.That(deeper.Token.IsCancellationRequested, Is.False);
            Assert.That(_s.Logs.Count("JANITOR108"), Is.Zero, "reading a token is not a registration");
        }

        [Test]
        public void After_WithoutADelay_WhileTheObjectIsInactive_IsRefusedLikeADelayAboveZero()
        {
            var probe = _s.NewProbe();
            var active = probe.GetActiveLifetime();
            var area = active.CreateChild("area");
            probe.gameObject.SetActive(false);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR108"));
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR108"));
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR108"));
            var fired = 0;

            active.After(0f, () => fired++);
            active.After(0.01f, () => fired++);
            area.After(0f, () => fired++);

            Assert.That(fired, Is.Zero, "no callback runs, whatever the delay");
            Assert.That(_s.Logs.Count("JANITOR108"), Is.EqualTo(3), "each refusal raises the warning");
#if UNITY_EDITOR
            Assert.That(active.DiagRefusedCount, Is.EqualTo(2));
            Assert.That(area.DiagRefusedCount, Is.EqualTo(1));
#endif
        }

        [Test]
        public void After_WithoutADelay_WhileTheObjectIsActive_StillRunsInPlace()
        {
            var probe = _s.NewProbe();
            var active = probe.GetActiveLifetime();
            var area = active.CreateChild("area");
            var fired = 0;

            active.After(0f, () => fired++);
            area.After(0f, () => fired++);

            Assert.That(fired, Is.EqualTo(2));
            Assert.That(_s.Logs.Count("JANITOR108"), Is.Zero);
        }

        [Test]
        public void AreasThatAreNotBelowAnActiveLifetime_AcceptWorkWhileTheObjectIsInactive()
        {
            var probe = _s.NewProbe();
            var componentArea = probe.GetLifetime().CreateChild("componentArea");
            var appArea = Lifetime.App.CreateChild("appArea");
            probe.GetActiveLifetime();
            probe.gameObject.SetActive(false);

            var onComponent = componentArea.Record(_log, "component");
            var onApp = appArea.Record(_log, "app");

            Assert.That(onComponent.IsActive, Is.True);
            Assert.That(onApp.IsActive, Is.True);
            Assert.That(componentArea.Token.IsCancellationRequested, Is.False);
            Assert.That(appArea.Token.IsCancellationRequested, Is.False);
            Assert.That(_s.Logs.Count("JANITOR108"), Is.Zero);
            Assert.That(_log.Count, Is.Zero);
        }
    }
}
