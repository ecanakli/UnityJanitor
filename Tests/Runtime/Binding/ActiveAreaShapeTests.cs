using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.Binding
{
    // An area below the active lifetime: a load that is cancelled and run again, an area created while the object is inactive,
    // and an area of an object that was never active.
    [TestFixture]
    public sealed class ActiveAreaShapeTests
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

        [UnityTest]
        public IEnumerator Restart_CancellingTheAreaThenRunningAgain_ReplacesTheLoadAndLeavesTheOwnersEntryAlone()
        {
            var go = _s.NewInactiveObject("Panel");
            var loader = go.AddComponent<RestartableLoader>();
            go.SetActive(true);
            var shown = loader.GetActiveLifetime();
            var area = loader.Load;
            Assert.That(loader.Started, Is.EqualTo(1));
            Assert.That(area.Parent, Is.SameAs(shown), "the load runs on a child area of the active lifetime");
            Assert.That(area.EntryCount, Is.EqualTo(1), "the running load");
            Assert.That(shown.EntryCount, Is.EqualTo(1), "the listener-like entry");

            loader.Restart();

            Assert.That(loader.Started, Is.EqualTo(2), "a second load started");
            Assert.That(loader.Stopped, Is.EqualTo(1), "the first load was stopped");
            Assert.That(loader.Tokens[0].IsCancellationRequested, Is.True);
            Assert.That(loader.Tokens[1].IsCancellationRequested, Is.False);
            Assert.That(loader.Load, Is.SameAs(area));
            Assert.That(area.IsDisposed, Is.False);
            Assert.That(area.EntryCount, Is.EqualTo(1), "one running load, not two");
            Assert.That(shown.EntryCount, Is.EqualTo(1), "the entry on the active lifetime was left alone");
            Assert.That(shown.Generation, Is.Zero, "the active lifetime did not end");
            Assert.That(loader.Changed.SubscriberCount, Is.EqualTo(1));

            go.SetActive(false);

            Assert.That(loader.Stopped, Is.EqualTo(2), "a deactivation stops the running load");
            Assert.That(loader.Tokens[1].IsCancellationRequested, Is.True);
            Assert.That(area.EntryCount, Is.Zero);
            Assert.That(shown.EntryCount, Is.Zero, "and the entry on the active lifetime");
            Assert.That(loader.Changed.SubscriberCount, Is.Zero);

            go.SetActive(true);

            Assert.That(loader.Started, Is.EqualTo(3), "exactly one load started again");
            Assert.That(loader.Load, Is.SameAs(area), "on the same area instance");
            Assert.That(area.IsDisposed, Is.False, "the area was cancelled, not disposed");
            Assert.That(area.EntryCount, Is.EqualTo(1));
            Assert.That(shown.EntryCount, Is.EqualTo(1));
            Assert.That(loader.Changed.SubscriberCount, Is.EqualTo(1));
            Assert.That(loader.Tokens[2].IsCancellationRequested, Is.False);
            Assert.That(_s.Logs.Count("JANITOR105"), Is.Zero);

            // The cancelled delays end with an exception each in the following frames; nothing is routed or logged.
            yield return BindingScenes.Frames(3);
            Assert.That(_s.Errors.Count, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void CreateChild_OnTheActiveLifetime_WhileTheObjectIsInactive_IsAllowedWithoutAWarning_AndTheAreaAcceptsWorkOnceTheObjectIsActive()
        {
            var probe = _s.NewProbe();
            var active = probe.GetActiveLifetime();
            probe.gameObject.SetActive(false);

            var area = active.CreateChild("area");

            Assert.That(area.IsDisposed, Is.False);
            Assert.That(area.Parent, Is.SameAs(active));
            Assert.That(_s.Logs.Count("JANITOR108"), Is.Zero, "creating an area is not a registration");
            LogAssert.NoUnexpectedReceived();

            LogAssert.Expect(LogType.Warning, new Regex("JANITOR108"));
            var refused = area.Record(_log, "while inactive");

            Assert.That(refused.IsActive, Is.False);
            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "while inactive" }), "the area refuses work while the object is inactive");

            probe.gameObject.SetActive(true);
            var accepted = area.Record(_log, "after activation");

            Assert.That(accepted.IsActive, Is.True);
            Assert.That(area.EntryCount, Is.EqualTo(1));
            Assert.That(_s.Logs.Count("JANITOR108"), Is.EqualTo(1), "only the refusal was reported");
        }

        [Test]
        public void CreateChild_OnTheActiveLifetimeOfAnObjectThatWasNeverActive_GivesAnAreaThatSurvivesEveryCycle()
        {
            var go = _s.NewInactiveObject("NeverActive");
            var probe = go.AddComponent<BindingProbe>();
            var active = probe.GetActiveLifetime();

            var area = active.CreateChild("area");

            Assert.That(area.IsDisposed, Is.False);
            Assert.That(_s.Logs.Count("JANITOR108"), Is.Zero);
            LogAssert.NoUnexpectedReceived();

            go.SetActive(true);
            var first = area.Record(_log, "first");

            Assert.That(first.IsActive, Is.True, "the area accepts work after the first activation");

            go.SetActive(false);

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "first" }), "a deactivation ends the work of the area");
            Assert.That(area.IsDisposed, Is.False, "the area was cancelled, not disposed");

            go.SetActive(true);
            var second = area.Record(_log, "second");

            Assert.That(second.IsActive, Is.True, "and again after a deactivate and activate cycle");
            Assert.That(area.EntryCount, Is.EqualTo(1));

            go.SetActive(false);

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "first", "second" }));
            Assert.That(area.IsDisposed, Is.False);
            Assert.That(_s.Logs.Count("JANITOR108"), Is.Zero);
        }
    }
}
