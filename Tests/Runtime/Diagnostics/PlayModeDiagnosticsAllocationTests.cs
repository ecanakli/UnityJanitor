#if UNITY_EDITOR
using System.Collections.Generic;
using Ecanakli.Janitor.Tests.Binding;
using Ecanakli.Janitor.Tests.Coroutines;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // The window refreshes at 4 Hz over a real session tree: component, GameObject, active and scene lifetimes with Unity owners,
    // coroutine and UnityEvent entries. A refresh of an unchanged tree allocates nothing beyond its reusable buffers, and a
    // registration on a component lifetime stays at 0 B with the diagnostics compiled in.
    [TestFixture]
    public sealed class PlayModeDiagnosticsAllocationTests
    {
        private BindingSession _s;
        private DiagnosticsRecordingKit _d;

        [SetUp]
        public void SetUp()
        {
            _s = BindingSession.Begin();
            _d = new DiagnosticsRecordingKit(manualFrames: false);
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

        private void BuildPlayModeTree()
        {
            var counter = new Counter();
            for (var i = 0; i < 4; i++)
            {
                var probe = _s.NewProbe("Probe" + i);
                var component = probe.GetLifetime();
                var active = probe.GetActiveLifetime();
                probe.gameObject.GetLifetime();
                component.OnCancel(counter, static c => c.Value++);
                active.OnCancel(counter, static c => c.Value++);
                component.StartCoroutine(probe, CoroutineRoutines.Forever(counter));
                component.CreateChild();
            }

            LifetimeDiagnostics.Report(Lifetime.App, "TEST100", "a recorded warning");
        }

        [Test]
        public void Capture_OfAnUnchangedPlayModeTree_AllocatesNothingBeyondTheReusableBuffers()
        {
            BuildPlayModeTree();
            var snapshot = new DiagnosticsSnapshot();
            TestDelegate measured = () => LifetimeDiagnostics.CaptureDefault(snapshot);
            measured();
            measured();

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(snapshot.HasTree, Is.True);
            Assert.That(snapshot.Nodes.Count, Is.GreaterThan(4 * 4), "App, the scene, and four objects with four lifetimes and a child each");
            Assert.That(snapshot.Warnings.Count, Is.EqualTo(1));
        }

        [Test]
        public void CaptureEntries_OfAComponentLifetimeWithACoroutine_AllocatesNothingBeyondTheReusableList()
        {
            var probe = _s.NewProbe();
            var component = probe.GetLifetime();
            var counter = new Counter();
            component.OnCancel(counter, static c => c.Value++);
            component.StartCoroutine(probe, CoroutineRoutines.Forever(counter));
            var entries = new List<EntryView>();
            TestDelegate measured = () => LifetimeDiagnostics.CaptureEntries(component, entries);
            measured();
            measured();

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(entries.Count, Is.EqualTo(2));
        }

        [Test]
        public void OnCancel_OnAComponentLifetime_WithTrackingOnAndStackTracesOff_AllocatesNothing()
        {
            var probe = _s.NewProbe();
            var component = probe.GetLifetime();
            var counter = new Counter();
            TestDelegate measured = () =>
            {
                for (var i = 0; i < 32; i++)
                {
                    component.OnCancel(counter, static c => c.Value++);
                }
            };
            measured();
            component.Cancel();

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(component.EntryCount, Is.EqualTo(32));
        }
    }
}
#endif
