#if UNITY_EDITOR
using Ecanakli.Janitor.Tests.Binding;
using Ecanakli.Janitor.Tests.Coroutines;
using NUnit.Framework;
using UnityEngine;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // JANITOR116 with real components: work registered in OnEnable on the component or GameObject lifetime is added again by every
    // later activation, and the active lifetime is the fix. Frames are driven by the kit, so a re-enable is always a later frame.
    [TestFixture]
    public sealed class RepeatedOnEnablePlayTests
    {
        private BindingSession _s;
        private DiagnosticsRecordingKit _d;

        [SetUp]
        public void SetUp()
        {
            _s = BindingSession.Begin();
            _d = new DiagnosticsRecordingKit();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                _s.Complete();
            }
            finally
            {
                _d.Dispose();
            }
        }

        [Test]
        public void ReEnable_AComponentThatRegistersInOnEnableOnItsComponentLifetime_RecordsJanitor116()
        {
            var go = _s.NewInactiveObject("Pooled");
            var behaviour = go.AddComponent<OnEnableOnComponentLifetime>();
            go.SetActive(true);
            Assert.That(_d.Count(DiagnosticIds.RepeatedOnEnable), Is.Zero, "enabled once");

            go.SetActive(false);
            _d.AdvanceFrames(1);
            go.SetActive(true);

            var lifetime = behaviour.GetLifetime();
            var warning = _d.Single(DiagnosticIds.RepeatedOnEnable);
            Assert.That(lifetime.EntryCount, Is.EqualTo(2), "premise: the registration doubled");
            Assert.That(warning.Lifetime, Is.SameAs(lifetime));
            Assert.That(warning.Member, Is.EqualTo("OnEnable"));
            Assert.That(warning.Line, Is.GreaterThan(0));
            Assert.That(warning.Context, Is.SameAs(behaviour), "the window pings the component");
            Assert.That(warning.Message, Does.Contain("OnEnable:" + warning.Line).And.Contain("GetActiveLifetime()"));
        }

        [Test]
        public void ReEnable_AComponentThatRegistersInOnEnableOnItsGameObjectLifetime_RecordsJanitor116()
        {
            var go = _s.NewInactiveObject("Pooled");
            go.AddComponent<OnEnableOnGameObjectLifetime>();
            go.SetActive(true);

            go.SetActive(false);
            _d.AdvanceFrames(1);
            go.SetActive(true);

            var warning = _d.Single(DiagnosticIds.RepeatedOnEnable);
            Assert.That(warning.Lifetime, Is.SameAs(go.GetLifetime()));
            Assert.That(warning.Member, Is.EqualTo("OnEnable"));
        }

        [Test]
        public void ReEnable_AComponentThatRegistersInOnEnableOnItsActiveLifetime_RecordsNothingAndKeepsOneEntry()
        {
            var go = _s.NewInactiveObject("Pooled");
            var behaviour = go.AddComponent<OnEnableOnActiveLifetime>();
            go.SetActive(true);

            for (var i = 0; i < 3; i++)
            {
                go.SetActive(false);
                _d.AdvanceFrames(1);
                go.SetActive(true);
            }

            Assert.That(behaviour.GetActiveLifetime().EntryCount, Is.EqualTo(1), "the active lifetime was cancelled on every deactivation");
            Assert.That(_d.Count(DiagnosticIds.RepeatedOnEnable), Is.Zero);
        }

        [Test]
        public void StartCoroutine_FromOnEnableAfterTheObjectWasReEnabled_RecordsNothingBecauseUnityStoppedTheEarlierCoroutine()
        {
            var probe = _s.NewProbe();
            var component = probe.GetLifetime();
            var counter = new Counter();
            component.StartCoroutine(probe, CoroutineRoutines.Forever(counter), "OnEnable", 40);
            _d.AdvanceFrames(1);

            probe.gameObject.SetActive(false);
            probe.gameObject.SetActive(true);
            component.StartCoroutine(probe, CoroutineRoutines.Forever(counter), "OnEnable", 40);

            Assert.That(_d.Count(DiagnosticIds.RepeatedOnEnable), Is.Zero, "a stopped coroutine is not live, so nothing doubled");
        }

        [Test]
        public void StartCoroutine_FromOnEnableWhileTheEarlierCoroutineIsStillRunning_RecordsJanitor116()
        {
            var probe = _s.NewProbe();
            var component = probe.GetLifetime();
            var counter = new Counter();
            component.StartCoroutine(probe, CoroutineRoutines.Forever(counter), "OnEnable", 41);
            _d.AdvanceFrames(1);

            component.StartCoroutine(probe, CoroutineRoutines.Forever(counter), "OnEnable", 41);

            Assert.That(_d.Single(DiagnosticIds.RepeatedOnEnable).Line, Is.EqualTo(41));
        }
    }
}
#endif
