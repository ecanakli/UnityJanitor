using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // JANITOR116: a registration whose call site is OnEnable, on a component or GameObject lifetime that already holds a live entry from
    // the same line registered in an earlier frame. That is the doubling of README rule 4. Frames are driven by the kit.
    [TestFixture]
    public sealed class RepeatedOnEnableTests
    {
        private DiagnosticsRecordingKit _d;
        private TestScope _t;
        private ManualClock _clock;
        private Lifetime _component;
        private Lifetime _gameObject;

        [SetUp]
        public void SetUp()
        {
            _d = new DiagnosticsRecordingKit();
            _t = new TestScope();
            _t.Tree.MakeDefault();
            _clock = _t.Tree.UseManualClock();
            _component = _t.App.CreateChildCore("Popup", LifetimeKind.Component, true, null, null, 0);
            _gameObject = _t.App.CreateChildCore("Pooled", LifetimeKind.GameObject, true, null, null, 0);
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

        private static void Noop()
        {
        }

        [Test]
        public void OnCancel_FromOnEnableAgainInALaterFrame_OnAComponentLifetime_RecordsJanitor116WithTheCallSite()
        {
            _component.OnCancel(Noop, "OnEnable", 12);
            Assert.That(_d.Count(DiagnosticIds.RepeatedOnEnable), Is.Zero, "enabled once");
            _d.AdvanceFrames(1);

            _component.OnCancel(Noop, "OnEnable", 12);

            var warning = _d.Single(DiagnosticIds.RepeatedOnEnable);
            Assert.That(warning.Lifetime, Is.SameAs(_component));
            Assert.That(warning.LifetimeLabel, Is.EqualTo("Popup"));
            Assert.That(warning.Member, Is.EqualTo("OnEnable"));
            Assert.That(warning.Line, Is.EqualTo(12));
            Assert.That(warning.Message, Does.Contain("OnEnable:12").And.Contain("'Popup'").And.Contain("GetActiveLifetime()"));
            Assert.That(warning.Count, Is.EqualTo(1));
            Assert.That(warning.IsInfo, Is.False);
        }

        [Test]
        public void OnCancel_FromOnEnableAgainInALaterFrame_OnAGameObjectLifetime_RecordsJanitor116()
        {
            _gameObject.OnCancel(Noop, "OnEnable", 30);
            _d.AdvanceFrames(1);

            _gameObject.OnCancel(Noop, "OnEnable", 30);

            Assert.That(_d.Single(DiagnosticIds.RepeatedOnEnable).Lifetime, Is.SameAs(_gameObject));
        }

        [Test]
        public void OnCancel_FromOnEnableOnce_RecordsNothing()
        {
            _component.OnCancel(Noop, "OnEnable", 12);
            _component.OnCancel(Noop, "OnEnable", 13);

            Assert.That(_d.Count(DiagnosticIds.RepeatedOnEnable), Is.Zero, "two lines of one OnEnable are two pieces of work, not a repeat");
        }

        [Test]
        public void OnCancel_ManyTimesFromOneLineInOneFrame_RecordsNothing()
        {
            for (var i = 0; i < 5; i++)
            {
                _component.OnCancel(Noop, "OnEnable", 12);
            }

            Assert.That(_component.EntryCount, Is.EqualTo(5), "premise: five live entries from one line");
            Assert.That(_d.Count(DiagnosticIds.RepeatedOnEnable), Is.Zero, "a loop inside one OnEnable is not a re-enable");
        }

        [Test]
        public void OnCancel_FromAnotherMemberAgainInALaterFrame_RecordsNothing()
        {
            _component.OnCancel(Noop, "Start", 12);
            _d.AdvanceFrames(1);

            _component.OnCancel(Noop, "Start", 12);
            _component.OnCancel(Noop, "OnEnabled", 12);
            _component.OnCancel(Noop, null, 12);

            Assert.That(_d.Count(DiagnosticIds.RepeatedOnEnable), Is.Zero);
        }

        [Test]
        public void OnCancel_FromOnEnableOnAnAreaSceneOrActiveLifetime_RecordsNothing()
        {
            var area = _t.App.CreateChild("area");
            var scene = _t.App.CreateChildCore("Scene", LifetimeKind.Scene, true, null, null, 0);
            var active = _t.App.CreateChildCore("Active", LifetimeKind.Active, true, null, null, 0);
            area.OnCancel(Noop, "OnEnable", 12);
            scene.OnCancel(Noop, "OnEnable", 12);
            active.OnCancel(Noop, "OnEnable", 12);
            _d.AdvanceFrames(1);

            area.OnCancel(Noop, "OnEnable", 12);
            scene.OnCancel(Noop, "OnEnable", 12);
            active.OnCancel(Noop, "OnEnable", 12);

            Assert.That(_d.Count(DiagnosticIds.RepeatedOnEnable), Is.Zero, "only a lifetime that does not follow activation can double");
        }

        [Test]
        public void OnCancel_FromTwoOnEnableLines_ReportsEachSiteOncePerLifetime()
        {
            _component.OnCancel(Noop, "OnEnable", 10);
            _component.OnCancel(Noop, "OnEnable", 11);
            _d.AdvanceFrames(1);
            _component.OnCancel(Noop, "OnEnable", 10);
            _component.OnCancel(Noop, "OnEnable", 11);
            _d.AdvanceFrames(1);

            _component.OnCancel(Noop, "OnEnable", 10);
            _component.OnCancel(Noop, "OnEnable", 11);
            _gameObject.OnCancel(Noop, "OnEnable", 10);
            _d.AdvanceFrames(1);
            _gameObject.OnCancel(Noop, "OnEnable", 10);

            var rows = _d.Warnings(DiagnosticIds.RepeatedOnEnable);
            Assert.That(rows.Count, Is.EqualTo(3), "lines 10 and 11 of the component lifetime, line 10 of the GameObject lifetime");
            Assert.That(rows[0].Line, Is.EqualTo(10));
            Assert.That(rows[1].Line, Is.EqualTo(11));
            Assert.That(rows[2].Lifetime, Is.SameAs(_gameObject));
            Assert.That(_d.Occurrences(DiagnosticIds.RepeatedOnEnable), Is.EqualTo(3), "later activations do not raise the counter: a site is reported once");
        }

        [Test]
        public void OnCancel_FromOnEnableAfterTheGenerationWasCancelled_RecordsNothing()
        {
            _component.OnCancel(Noop, "OnEnable", 12);
            _d.AdvanceFrames(1);

            _component.Cancel();
            _component.OnCancel(Noop, "OnEnable", 12);

            Assert.That(_component.EntryCount, Is.EqualTo(1), "premise: the cancel emptied the lifetime");
            Assert.That(_d.Count(DiagnosticIds.RepeatedOnEnable), Is.Zero, "nothing doubled, because the earlier work was stopped");
        }

        [Test]
        public void After_FromOnEnableWhoseEarlierTimerAlreadyFired_RecordsNothing()
        {
            _component.After(1f, Noop, false, "OnEnable", 20);
            _clock.Advance(1f);
            _d.AdvanceFrames(1);

            _component.After(1f, Noop, false, "OnEnable", 20);

            Assert.That(_d.Count(DiagnosticIds.RepeatedOnEnable), Is.Zero, "a finished timer left no entry, so nothing is live twice");
        }

        [Test]
        public void After_FromOnEnableWhoseEarlierTimerIsPending_RecordsJanitor116()
        {
            _component.After(30f, Noop, false, "OnEnable", 21);
            _d.AdvanceFrames(1);

            _component.After(30f, Noop, false, "OnEnable", 21);

            var warning = _d.Single(DiagnosticIds.RepeatedOnEnable);
            Assert.That(warning.Line, Is.EqualTo(21));
            Assert.That(_clock.PendingCount, Is.EqualTo(2), "premise: two timers are pending, the work doubled");
        }

        [Test]
        public void OnCancel_FromOnEnableAgainWhileTrackingIsOff_RecordsNothing()
        {
            LifetimeDiagnostics.TrackingEnabled = false;
            _component.OnCancel(Noop, "OnEnable", 12);
            _d.AdvanceFrames(1);

            _component.OnCancel(Noop, "OnEnable", 12);

            Assert.That(_d.Total, Is.Zero);
        }

        [Test]
        public void OnCancel_FromOnEnableAfterTheSiteWasReported_AllocatesNothing()
        {
            var counter = new Counter();
            _component.OnCancel(counter, static c => c.Value++, "OnEnable", 30);
            _d.AdvanceFrames(1);
            _component.OnCancel(counter, static c => c.Value++, "OnEnable", 30);
            Assert.That(_d.Count(DiagnosticIds.RepeatedOnEnable), Is.EqualTo(1), "premise: the site is reported");
            TestDelegate measured = () =>
            {
                for (var i = 0; i < 32; i++)
                {
                    _component.OnCancel(counter, static c => c.Value++, "OnEnable", 30);
                }
            };
            measured();
            _component.Cancel();

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(_component.EntryCount, Is.EqualTo(32), "the measured block must really have registered");
        }
    }
}
