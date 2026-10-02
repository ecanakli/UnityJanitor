#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Ecanakli.Janitor.Tests.Binding;
using Ecanakli.Janitor.Tests.Coroutines;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // What the editor records for the Unity-bound lifetimes: kind, label and owner of component, GameObject, active,
    // scene and App lifetimes; coroutine and UnityEvent entries; the disposal of an object's lifetime; the play session boundary.
    [TestFixture]
    public sealed class PlayModeRecordingTests
    {
        private BindingSession _s;
        private DiagnosticsRecordingKit _d;
        private DiagnosticsSnapshot _snapshot;

        [SetUp]
        public void SetUp()
        {
            _s = BindingSession.Begin();
            _d = new DiagnosticsRecordingKit();
            _snapshot = new DiagnosticsSnapshot();
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

        private void OnInt(int value)
        {
        }

        [Test]
        public void Capture_ComponentGameObjectActiveSceneAndAppLifetimes_ReportKindLabelAndOwner()
        {
            var probe = _s.NewProbe("Owner");
            var component = probe.GetLifetime();
            var objectLifetime = probe.gameObject.GetLifetime();
            var active = probe.GetActiveLifetime();
            var scene = SceneLifetimes.Get(probe.gameObject.scene);

            LifetimeDiagnostics.CaptureDefault(_snapshot);

            var appNode = SnapshotQueries.NodeOf(_snapshot, Lifetime.App);
            Assert.That(appNode.Kind, Is.EqualTo(LifetimeKind.App));
            Assert.That(appNode.Label, Is.EqualTo("App"));

            var sceneNode = SnapshotQueries.NodeOf(_snapshot, scene);
            Assert.That(sceneNode.Kind, Is.EqualTo(LifetimeKind.Scene));
            Assert.That(sceneNode.Label, Is.EqualTo(probe.gameObject.scene.name));
            Assert.That(sceneNode.Owner, Is.Null);
            Assert.That(sceneNode.ParentId, Is.EqualTo(appNode.Id));

            var componentNode = SnapshotQueries.NodeOf(_snapshot, component);
            Assert.That(componentNode.Kind, Is.EqualTo(LifetimeKind.Component));
            Assert.That(componentNode.Label, Is.EqualTo(nameof(BindingProbe)));
            Assert.That(componentNode.Owner, Is.SameAs(probe));
            Assert.That(componentNode.OwnerDestroyed, Is.False);
            Assert.That(componentNode.ParentId, Is.EqualTo(sceneNode.Id));
            Assert.That(componentNode.Depth, Is.EqualTo(2));

            var objectNode = SnapshotQueries.NodeOf(_snapshot, objectLifetime);
            Assert.That(objectNode.Kind, Is.EqualTo(LifetimeKind.GameObject));
            Assert.That(objectNode.Label, Is.EqualTo("Owner"));
            Assert.That(objectNode.Owner, Is.SameAs(probe.gameObject));

            var activeNode = SnapshotQueries.NodeOf(_snapshot, active);
            Assert.That(activeNode.Kind, Is.EqualTo(LifetimeKind.Active));
            Assert.That(activeNode.Label, Is.EqualTo("Owner"));
            Assert.That(activeNode.Owner, Is.InstanceOf<ActiveLifetimeTrigger>(), "an active lifetime is owned by its hidden trigger");
        }

        [Test]
        public void Capture_APlacedLifetime_ReportsItsCategoryAsParentAndThePlacedFlag()
        {
            var probe = _s.NewProbe();
            var category = Lifetime.App.CreateChild("Popups");
            var placed = probe.GetLifetime(category);

            LifetimeDiagnostics.CaptureDefault(_snapshot);

            var node = SnapshotQueries.NodeOf(_snapshot, placed);
            Assert.That(node.Placed, Is.True);
            Assert.That(node.ParentId, Is.EqualTo(category.DiagId));
            Assert.That(node.Depth, Is.EqualTo(2));
        }

        [Test]
        public void Capture_ACoroutineEntry_IsCountedAsACoroutineAndLabelled()
        {
            var host = _s.NewProbe("Host");
            var area = Lifetime.App.CreateChild("area");
            var counter = new Counter();

            var registration = area.StartCoroutine(host, CoroutineRoutines.Forever(counter), "CoroutineSite", 17);
            LifetimeDiagnostics.CaptureDefault(_snapshot);
            var entries = new List<EntryView>();
            LifetimeDiagnostics.CaptureEntries(area, entries);

            Assert.That(registration.IsActive, Is.True);
            var node = SnapshotQueries.NodeOf(_snapshot, area);
            Assert.That(node.Coroutines, Is.EqualTo(1));
            Assert.That(node.EntryCount, Is.EqualTo(1));
            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].Kind, Is.EqualTo(EntryKind.Coroutine));
            Assert.That(entries[0].FormatLabel(), Is.EqualTo("Coroutine"));
            Assert.That(entries[0].Member, Is.EqualTo("CoroutineSite"));
            Assert.That(entries[0].Line, Is.EqualTo(17));
        }

        [Test]
        public void Capture_AUnityEventSubscription_IsASubscriptionLabelledWithTheEventType()
        {
            var area = Lifetime.App.CreateChild("area");
            var evt = new UnityEvent<int>();
            UnityAction<int> handler = OnInt;

            evt.Subscribe(handler, area);
            LifetimeDiagnostics.CaptureDefault(_snapshot);
            var entries = new List<EntryView>();
            LifetimeDiagnostics.CaptureEntries(area, entries);

            Assert.That(SnapshotQueries.NodeOf(_snapshot, area).Subscriptions, Is.EqualTo(1));
            Assert.That(entries[0].Kind, Is.EqualTo(EntryKind.Subscription));
            Assert.That(entries[0].FormatLabel(), Is.EqualTo("UnityEvent<Int32>"));
        }

        [Test]
        public void Capture_AnActiveLifetimeEntryRegisteredWhileActive_IsCountedLikeAnyOther()
        {
            var probe = _s.NewProbe();
            var active = probe.GetActiveLifetime();
            var counter = new Counter();

            active.OnCancel(counter, static c => c.Value++);
            LifetimeDiagnostics.CaptureDefault(_snapshot);

            var node = SnapshotQueries.NodeOf(_snapshot, active);
            Assert.That(node.Others, Is.EqualTo(1));
            Assert.That(node.TotalRegistered, Is.EqualTo(1));
        }

        // Disposal of an object's lifetime

        [UnityTest]
        public IEnumerator Destroy_OfAComponent_RecordsItsLifetimeInTheRecentlyDisposedListWithKindAndLabel()
        {
            var go = _s.NewObject("Doomed");
            var probe = go.AddComponent<BindingProbe>();
            var lifetime = probe.GetLifetime();
            lifetime.OnCancel(() => { });
            _d.Frame = 150;

            Object.Destroy(go);
            yield return null;

            var recent = new List<RecentLifetime>();
            LifetimeDiagnostics.CopyRecent(recent);
            Assert.That(lifetime.IsDisposed, Is.True);
            Assert.That(recent.Count, Is.EqualTo(1));
            Assert.That(recent[0].Kind, Is.EqualTo(LifetimeKind.Component));
            Assert.That(recent[0].FormatLabel(), Is.EqualTo(nameof(BindingProbe)));
            Assert.That(recent[0].TotalRegistered, Is.EqualTo(1));
            Assert.That(recent[0].DisposedFrame, Is.EqualTo(150));
        }

        [UnityTest]
        public IEnumerator Capture_AfterTheOwnerIsDestroyed_TheLifetimeIsGoneAndNoOrphanWasRecorded()
        {
            var go = _s.NewObject("Doomed");
            var probe = go.AddComponent<BindingProbe>();
            var lifetime = probe.GetLifetime();
            LifetimeDiagnostics.CaptureDefault(_snapshot);
            Assert.That(SnapshotQueries.Contains(_snapshot, lifetime), Is.True);

            Object.Destroy(go);
            yield return null;
            LifetimeDiagnostics.CaptureDefault(_snapshot);

            Assert.That(SnapshotQueries.Contains(_snapshot, lifetime), Is.False);
            Assert.That(_d.Count(DiagnosticIds.OrphanLifetime), Is.Zero, "a normal destroy disposes the lifetime with its owner");
        }

        // The play session boundary

        [Test]
        public void Restart_KeepsTheRecentlyDisposedBufferAndClearsTheWarnings()
        {
            Lifetime.App.CreateChild("old").Dispose();
            LifetimeDiagnostics.Report(null, "TEST100", "from the first session");
            Assert.That(LifetimeDiagnostics.RecentCount, Is.EqualTo(1));
            Assert.That(_d.Total, Is.EqualTo(1));
            var sessionBefore = LifetimeDiagnostics.Session;

            _s.Restart();

            Assert.That(LifetimeDiagnostics.Session, Is.EqualTo(sessionBefore + 1));
            Assert.That(_d.Total, Is.Zero, "warnings belong to one play session");
            Assert.That(LifetimeDiagnostics.RecentCount, Is.EqualTo(1), "the history survives it");
            Lifetime.App.CreateChild("new").Dispose();
            var recent = new List<RecentLifetime>();
            LifetimeDiagnostics.CopyRecent(recent);
            Assert.That(recent[0].FormatLabel(), Is.EqualTo("new"));
            Assert.That(recent[0].Session, Is.EqualTo(sessionBefore + 1));
            Assert.That(recent[1].FormatLabel(), Is.EqualTo("old"));
            Assert.That(recent[1].Session, Is.EqualTo(sessionBefore));
        }

        [Test]
        public void Restart_TheOldTreeShuttingDown_DoesNotFloodTheRecentlyDisposedBuffer()
        {
            var probe = _s.NewProbe();
            probe.GetLifetime();
            for (var i = 0; i < 25; i++)
            {
                Lifetime.App.CreateChild("area" + i);
            }

            _s.Restart();

            Assert.That(LifetimeDiagnostics.RecentCount, Is.Zero, "the old session's tree was shut down, not disposed lifetime by lifetime");
        }

        [Test]
        public void Exit_TheApplicationEndingTheTree_DoesNotFloodTheRecentlyDisposedBuffer()
        {
            Lifetime.App.CreateChild("before").Dispose();
            _s.NewProbe().GetLifetime();
            for (var i = 0; i < 25; i++)
            {
                Lifetime.App.CreateChild("area" + i);
            }

            _s.Exit.Cancel();

            var recent = new List<RecentLifetime>();
            LifetimeDiagnostics.CopyRecent(recent);
            Assert.That(recent.Count, Is.EqualTo(1), "only the disposal before the exit is kept");
            Assert.That(recent[0].FormatLabel(), Is.EqualTo("before"));
        }

        [Test]
        public void Restart_ANewTreeCapturesAsAFreshRoot()
        {
            _s.NewProbe().GetLifetime();
            _s.Restart();

            LifetimeDiagnostics.CaptureDefault(_snapshot);

            Assert.That(_snapshot.HasTree, Is.True);
            Assert.That(_snapshot.Nodes.Count, Is.EqualTo(1), "only App: the old tree is gone");
            Assert.That(_snapshot.Nodes[0].Lifetime, Is.SameAs(Lifetime.App));
        }
    }
}
#endif
