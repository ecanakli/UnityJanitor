using Ecanakli.Janitor.Tests.DiagnosticsTests;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests.Binding
{
    // The active lifetime follows the GameObject, not the enabled flag of a component: work added in OnEnable is added again by every
    // enable of that component that is not a deactivation of the object.
    [TestFixture]
    public sealed class ReEnableWhileActiveTests
    {
        private BindingSession _s;
#if UNITY_EDITOR
        private DiagnosticsRecordingKit _d;
#endif

        [SetUp]
        public void SetUp()
        {
            _s = BindingSession.Begin();
#if UNITY_EDITOR
            _d = new DiagnosticsRecordingKit();
#endif
        }

        [TearDown]
        public void TearDown()
        {
#if UNITY_EDITOR
            try
            {
                _s.Complete();
            }
            finally
            {
                _d.Dispose();
            }
#else
            _s.Complete();
#endif
        }

        [Test]
        public void ComponentDisabledAndEnabledAgain_WhileTheObjectStaysActive_AddsASecondEntryToTheActiveLifetimeAndWarnsOfNothing()
        {
            var go = _s.NewInactiveObject("Pooled");
            var behaviour = go.AddComponent<OnEnableOnActiveLifetime>();
            go.SetActive(true);
            var active = behaviour.GetActiveLifetime();
            var generation = active.Generation;
            Assert.That(active.EntryCount, Is.EqualTo(1));

            behaviour.enabled = false;

            Assert.That(active.EntryCount, Is.EqualTo(1), "disabling the component ends nothing");
            Assert.That(active.Generation, Is.EqualTo(generation));
#if UNITY_EDITOR
            _d.AdvanceFrames(1);
#endif

            behaviour.enabled = true;

            Assert.That(active.EntryCount, Is.EqualTo(2), "the enable added a second entry to the same generation");
            Assert.That(active.Generation, Is.EqualTo(generation), "no cancel ran in between");
#if UNITY_EDITOR
            Assert.That(_d.Count(DiagnosticIds.RepeatedOnEnable), Is.Zero, "the check looks at component and GameObject lifetimes only");
            Assert.That(_d.Count(DiagnosticIds.DuplicateSubscription), Is.Zero);
            Assert.That(_d.Total, Is.Zero, "nothing at all was recorded");
#endif
        }

        [Test]
        public void ComponentDisabledAndEnabledAgain_ThenTheObjectDeactivated_EndsBothEntriesAndTheNextActivationStartsWithOne()
        {
            var go = _s.NewInactiveObject("Pooled");
            var behaviour = go.AddComponent<OnEnableOnActiveLifetime>();
            go.SetActive(true);
            var active = behaviour.GetActiveLifetime();
            var generation = active.Generation;
            behaviour.enabled = false;
#if UNITY_EDITOR
            _d.AdvanceFrames(1);
#endif
            behaviour.enabled = true;
            Assert.That(active.EntryCount, Is.EqualTo(2));

            go.SetActive(false);

            Assert.That(active.EntryCount, Is.Zero, "a deactivation ends every entry of the generation");
            Assert.That(active.Generation, Is.GreaterThan(generation));

            go.SetActive(true);

            Assert.That(active.EntryCount, Is.EqualTo(1));
        }
    }
}
