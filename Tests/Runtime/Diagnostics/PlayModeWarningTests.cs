#if UNITY_EDITOR
using System;
using System.Collections;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using Ecanakli.Janitor.Tests.Binding;
using Ecanakli.Janitor.Tests.Coroutines;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // The warning rules that need Unity objects: 101 (real player loop), 103, 104, 105 (UnityEvent), 106 (scene exemption),
    // 107, 108, 109, 110, 113 and 114. Each is recorded once per cause with the right lifetime, and still logged where it logged before.
    [TestFixture]
    public sealed class PlayModeWarningTests
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

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
            {
                yield return _s.CompleteAsync();
            }
            finally
            {
                _d.Dispose();
            }
        }

        // 101, with the real frame counter and the real player loop

        [UnityTest]
        public IEnumerator Cancel_AnAwaitWithoutTheToken_IsReportedOnceAfterTheThreshold()
        {
            _d.Dispose();
            _d = new DiagnosticsRecordingKit(manualFrames: false);
            var area = Lifetime.App.CreateChild("area");
            var release = new Counter();
            area.Run(release, static async (r, ct) => { await UniTask.WaitUntil(() => r.Value > 0); }, "TokenlessSite", 7);

            area.Cancel();
            yield return BindingScenes.Frames(8);

            var warning = _d.Single(DiagnosticIds.TaskOverrun);
            Assert.That(warning.Lifetime, Is.SameAs(area));
            Assert.That(warning.Member, Is.EqualTo("TokenlessSite"));
            Assert.That(warning.Line, Is.EqualTo(7));
            Assert.That(warning.Count, Is.EqualTo(1));

            yield return BindingScenes.Frames(10);
            Assert.That(_d.Single(DiagnosticIds.TaskOverrun).Count, Is.EqualTo(1), "once, not once per frame");

            release.Value = 1;
            yield return BindingScenes.Frames(3);
            Assert.That(TaskOverrunTracker.Count, Is.Zero, "the task finished, so its record is gone");
        }

        [UnityTest]
        public IEnumerator Cancel_AnAwaitThatHonoursTheToken_IsNeverReported()
        {
            _d.Dispose();
            _d = new DiagnosticsRecordingKit(manualFrames: false);
            var area = Lifetime.App.CreateChild("area");
            area.Run(async ct => { await UniTask.Delay(TimeSpan.FromSeconds(30), cancellationToken: ct); });

            area.Cancel();
            yield return BindingScenes.Frames(15);

            Assert.That(_d.Count(DiagnosticIds.TaskOverrun), Is.Zero);
            Assert.That(TaskOverrunTracker.Count, Is.Zero);
        }

        // 103

        [UnityTest]
        public IEnumerator Capture_ADestroyedComponentOnTheInactivePath_RecordsJanitor103OnceWithTheRightLifetime()
        {
            var go = _s.NewInactiveObject();
            var probe = go.AddComponent<BindingProbe>();
            var lifetime = probe.GetLifetime();
            Object.Destroy(probe);
            yield return BindingScenes.Frames(3);
            Assert.That(lifetime.IsDisposed, Is.False, "premise: Destroy(component) alone does not end a lifetime made on the inactive path");

            LifetimeDiagnostics.CaptureDefault(_snapshot);

            var node = SnapshotQueries.NodeOf(_snapshot, lifetime);
            Assert.That(node.OwnerDestroyed, Is.True);
            Assert.That(_d.Count(DiagnosticIds.OrphanLifetime), Is.Zero, "the first snapshot only notes the destroyed owner");

            _d.AdvanceFrames(1);
            LifetimeDiagnostics.CaptureDefault(_snapshot);

            var warning = _d.Single(DiagnosticIds.OrphanLifetime);
            Assert.That(warning.Lifetime, Is.SameAs(lifetime));
            Assert.That(warning.LifetimeLabel, Is.EqualTo(nameof(BindingProbe)));
            Assert.That(warning.Message, Does.Contain("was destroyed").And.Contain("still alive"));
            Assert.That(warning.Message, Does.Contain("first used while its GameObject was inactive").And.Not.Contain("Destroy alone does not end it"));

            for (var refresh = 0; refresh < 4; refresh++)
            {
                yield return BindingScenes.Frames(2);
                LifetimeDiagnostics.CaptureDefault(_snapshot);
            }

            Assert.That(_d.Single(DiagnosticIds.OrphanLifetime).Count, Is.EqualTo(1), "recorded once, not once per refresh");

            Object.Destroy(go);
            yield return BindingScenes.Frames(2);
            LifetimeDiagnostics.CaptureDefault(_snapshot);
            Assert.That(lifetime.IsDisposed, Is.True, "destroying the GameObject ends it");
            Assert.That(SnapshotQueries.Contains(_snapshot, lifetime), Is.False);
        }

        // UniTask ends the lifetime of a never-activated object in its next update after the destroy, so a snapshot taken in between
        // sees a destroyed owner and a live lifetime. DestroyImmediate makes that state deterministic.
        [UnityTest]
        public IEnumerator Capture_ADestroyedNeverActivatedObjectBeforeUniTaskNoticesIt_RecordsNothing()
        {
            var go = _s.NewInactiveObject();
            var lifetime = go.AddComponent<BindingProbe>().GetLifetime();
            Object.DestroyImmediate(go);
            Assert.That(lifetime.IsDisposed, Is.False, "premise: the destroy of a never-activated object is only noticed by UniTask's next update");

            LifetimeDiagnostics.CaptureDefault(_snapshot);
            yield return BindingScenes.Frames(3);
            _d.AdvanceFrames(1);
            LifetimeDiagnostics.CaptureDefault(_snapshot);

            Assert.That(lifetime.IsDisposed, Is.True, "the lifetime ends on its own once UniTask has polled");
            Assert.That(_d.Count(DiagnosticIds.OrphanLifetime), Is.Zero, "a correct teardown is not reported as a leak");
        }

        // 104

        [UnityTest]
        public IEnumerator SceneUnloaded_WithoutDispose_RecordsJanitor104OnceAttachedToTheSceneLifetime()
        {
            var scene = _s.NewScene("Skipped");
            var sceneName = scene.name;
            var probe = _s.NewProbeIn(scene);
            var sceneLifetime = SceneLifetimes.Get(scene);
            probe.GetLifetime();
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR104"));

            yield return BindingScenes.Unload(scene);

            var warning = _d.Single(DiagnosticIds.SceneDisposedLate);
            Assert.That(warning.Lifetime, Is.SameAs(sceneLifetime));
            Assert.That(warning.LifetimeLabel, Is.EqualTo(sceneName));
            Assert.That(warning.Message, Does.Contain("'" + sceneName + "'").And.Contain("was unloaded without SceneLifetimes.Dispose"));
            Assert.That(warning.Count, Is.EqualTo(1));
            Assert.That(_s.Logs.Count("JANITOR104"), Is.EqualTo(1), "the console and the record agree");

            PlayModeBootstrap.HandleSceneUnloaded(scene);
            Assert.That(_d.Single(DiagnosticIds.SceneDisposedLate).Count, Is.EqualTo(1), "once per scene");
        }

        [UnityTest]
        public IEnumerator SceneUnloaded_AfterTheOneLineCall_RecordsNothing()
        {
            var scene = _s.NewScene("Called");
            _s.NewProbeIn(scene).GetLifetime();

            SceneLifetimes.Dispose(scene);
            yield return BindingScenes.Unload(scene);

            Assert.That(_d.Count(DiagnosticIds.SceneDisposedLate), Is.Zero);
        }

        // 105 for UnityEvent (OwnedEvent and SignalBus are in the EditMode and Zenject suites)

        private void OnInt(int value)
        {
        }

        private void OnNothing()
        {
        }

        [Test]
        public void Subscribe_ADuplicateOnAUnityEvent_RecordsJanitor105AttachedToTheOwner()
        {
            var owner = Lifetime.App.CreateChild("owner");
            var evt = new UnityEvent<int>();
            UnityAction<int> handler = OnInt;
            evt.Subscribe(handler, owner);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));

            evt.Subscribe(handler, owner);

            var warning = _d.Single(DiagnosticIds.DuplicateSubscription);
            Assert.That(warning.Lifetime, Is.SameAs(owner));
            Assert.That(warning.Message, Does.Contain("UnityEvent<Int32>").And.Contain("'owner'"));
            Assert.That(warning.Count, Is.EqualTo(1));
            Assert.That(_s.Logs.Count("JANITOR105"), Is.EqualTo(1), "the console warning is kept");
        }

        [Test]
        public void Subscribe_ADuplicateOnAUnityEventAgain_RaisesTheCounterOfOneEntry()
        {
            var owner = Lifetime.App.CreateChild("owner");
            var evt = new UnityEvent();
            UnityAction handler = OnNothing;
            evt.Subscribe(handler, owner);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));

            evt.Subscribe(handler, owner);
            evt.Subscribe(handler, owner);

            var warning = _d.Single(DiagnosticIds.DuplicateSubscription);
            Assert.That(warning.Count, Is.EqualTo(2));
            Assert.That(warning.Message, Does.Contain("UnityEvent already has this handler"));
        }

        [Test]
        public void Subscribe_TheSameUnityEventHandlerUnderAnotherOwner_RecordsNothing()
        {
            var first = Lifetime.App.CreateChild("first");
            var second = Lifetime.App.CreateChild("second");
            var evt = new UnityEvent();
            UnityAction handler = OnNothing;

            evt.Subscribe(handler, first);
            evt.Subscribe(handler, second);

            Assert.That(_d.Count(DiagnosticIds.DuplicateSubscription), Is.Zero);
        }

        // 106: only child areas count, under every parent; object lifetimes are children by design

        [Test]
        public void CreateComponentLifetimes_PastTheChildLimitUnderASceneLifetime_IsExempt()
        {
            Lifetime scene = null;
            for (var i = 0; i < 70; i++)
            {
                var probe = _s.NewProbe("probe" + i);
                probe.GetLifetime();
                scene = SceneLifetimes.Get(probe.gameObject.scene);
            }

            Assert.That(scene.ChildCount, Is.GreaterThan(LifetimeDiagnostics.GrowthChildLimit), "premise: the scene lifetime is past the limit");
            Assert.That(_d.Count(DiagnosticIds.Growth), Is.Zero, "every object lifetime of a scene is its child by design");
        }

        [Test]
        public void CreateComponentLifetimes_100UnderASceneLifetime_RecordNothing()
        {
            Lifetime scene = null;
            for (var i = 0; i < 100; i++)
            {
                var probe = _s.NewProbe("probe" + i);
                probe.GetLifetime();
                scene = SceneLifetimes.Get(probe.gameObject.scene);
            }

            Assert.That(scene.ChildCount, Is.GreaterThanOrEqualTo(100), "premise: 100 component lifetimes are children of the scene lifetime");
            Assert.That(_d.Count(DiagnosticIds.Growth), Is.Zero);
        }

        [Test]
        public void CreateChild_PastTheChildLimitUnderASceneLifetime_RecordsJanitor106ForTheScene()
        {
            var probe = _s.NewProbe();
            probe.GetLifetime();
            var scene = SceneLifetimes.Get(probe.gameObject.scene);

            for (var i = 0; i <= LifetimeDiagnostics.GrowthChildLimit; i++)
            {
                scene.CreateChild();
            }

            var warning = _d.Single(DiagnosticIds.Growth);
            Assert.That(warning.Lifetime, Is.SameAs(scene));
            Assert.That(warning.Message, Does.Contain("65 live children that are areas"));
        }

        [Test]
        public void GetLifetime_100ObjectsPlacedInOneCategory_RecordsNothing()
        {
            var combat = Lifetime.App.CreateChild("Combat");

            for (var i = 0; i < 100; i++)
            {
                _s.NewProbe("unit" + i).GetLifetime(combat);
            }

            Assert.That(combat.ChildCount, Is.EqualTo(100), "premise: the category holds 100 placed object lifetimes");
            Assert.That(_d.Count(DiagnosticIds.Growth), Is.Zero, "placed objects are legitimate children, not undisposed areas");
        }

        [Test]
        public void CreateChild_PastTheChildLimitOnAComponentLifetime_RecordsJanitor106ForThatLifetime()
        {
            var component = _s.NewProbe().GetLifetime();

            for (var i = 0; i <= LifetimeDiagnostics.GrowthChildLimit; i++)
            {
                component.CreateChild();
            }

            var warning = _d.Single(DiagnosticIds.Growth);
            Assert.That(warning.Lifetime, Is.SameAs(component));
            Assert.That(warning.Message, Does.Contain("65 live children"));
        }

        // 107

        [Test]
        public void Dispose_OnAComponentLifetime_RecordsJanitor107AttachedToIt()
        {
            var lifetime = _s.NewProbe().GetLifetime();
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR107"));

            lifetime.Dispose();

            var warning = _d.Single(DiagnosticIds.DisposeIgnored);
            Assert.That(warning.Lifetime, Is.SameAs(lifetime));
            Assert.That(warning.Message, Does.Contain("'" + nameof(BindingProbe) + "'"));
            Assert.That(_s.Logs.Count("JANITOR107"), Is.EqualTo(1));
        }

        // 108

        [Test]
        public void Register_OnAnActiveLifetimeWhileTheObjectIsInactive_RecordsJanitor108WithTheOwnerAsContext()
        {
            var probe = _s.NewProbe();
            var lifetime = probe.GetActiveLifetime();
            probe.gameObject.SetActive(false);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR108"));
            var ran = 0;

            lifetime.OnCancel(() => ran++, "InactiveSite", 11);

            Assert.That(ran, Is.EqualTo(1), "the item is terminated at once");
            var warning = _d.Single(DiagnosticIds.RegistrationOnInactiveObject);
            Assert.That(warning.Lifetime, Is.SameAs(lifetime));
            Assert.That(warning.Context, Is.SameAs(lifetime.OwnerObject), "the window can ping the object");
            Assert.That(warning.Message, Does.Contain("terminated immediately because its GameObject is inactive"));
            Assert.That(warning.Count, Is.EqualTo(1));
            Assert.That(lifetime.DiagRefusedCount, Is.EqualTo(1));
            Assert.That(lifetime.DiagFirstRefusedMember, Is.EqualTo("InactiveSite"));
            Assert.That(lifetime.DiagFirstRefusedLine, Is.EqualTo(11));
        }

        [Test]
        public void Register_OnAnInactiveActiveLifetimeAgain_RaisesTheCounterOfOneEntry()
        {
            var probe = _s.NewProbe();
            var lifetime = probe.GetActiveLifetime();
            probe.gameObject.SetActive(false);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR108"));
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR108"));

            lifetime.OnCancel(() => { }, "First", 1);
            lifetime.OnCancel(() => { }, "Second", 2);

            Assert.That(_d.Single(DiagnosticIds.RegistrationOnInactiveObject).Count, Is.EqualTo(2));
            Assert.That(lifetime.DiagFirstRefusedMember, Is.EqualTo("First"), "the first call site is the one kept");
        }

        // 109

        [UnityTest]
        public IEnumerator Register_OnADisposedSceneLifetimeWhileTheSceneIsLoaded_RecordsJanitor109AttachedToIt()
        {
            var scene = _s.NewScene("Disposed");
            var sceneLifetime = SceneLifetimes.Get(scene);
            SceneLifetimes.Dispose(scene);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR109"));

            sceneLifetime.OnCancel(() => { }, "SceneSite", 5);

            var warning = _d.Single(DiagnosticIds.RegistrationOnDisposedScene);
            Assert.That(warning.Lifetime, Is.SameAs(sceneLifetime));
            Assert.That(warning.Message, Does.Contain("disposed scene lifetime"));
            Assert.That(sceneLifetime.DiagRefusedCount, Is.EqualTo(1));
            Assert.That(sceneLifetime.DiagFirstRefusedMember, Is.EqualTo("SceneSite"));
            yield break;
        }

        [UnityTest]
        public IEnumerator GetLifetime_OnAnObjectOfADisposedScene_RecordsJanitor109AttachedToTheSceneLifetime()
        {
            var scene = _s.NewScene("Disposed");
            var sceneLifetime = SceneLifetimes.Get(scene);
            SceneLifetimes.Dispose(scene);
            var probe = _s.NewProbeIn(scene);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR109"));

            probe.GetLifetime();

            Assert.That(_d.Single(DiagnosticIds.RegistrationOnDisposedScene).Lifetime, Is.SameAs(sceneLifetime));
            yield break;
        }

        [UnityTest]
        public IEnumerator Register_OnADisposedSceneLifetimeAfterTheUnload_RecordsNothing()
        {
            var scene = _s.NewScene("Gone");
            var sceneLifetime = SceneLifetimes.Get(scene);
            SceneLifetimes.Dispose(scene);
            yield return BindingScenes.Unload(scene);

            sceneLifetime.OnCancel(() => { });

            Assert.That(_d.Count(DiagnosticIds.RegistrationOnDisposedScene), Is.Zero, "a registration after the unload is expected teardown noise");
        }

        // 110

        [Test]
        public void StartCoroutine_OnAnInactiveHost_RecordsJanitor110WithTheCallSiteAndTheHost()
        {
            var host = _s.NewProbe("Host");
            host.gameObject.SetActive(false);
            var area = Lifetime.App.CreateChild("area");
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR110.*'Host' is inactive"));

            area.StartCoroutine(host, CoroutineRoutines.Forever(new Counter()), "CoroutineSite", 321);

            var warning = _d.Single(DiagnosticIds.CoroutineNotStarted);
            Assert.That(warning.Lifetime, Is.SameAs(area));
            Assert.That(warning.Member, Is.EqualTo("CoroutineSite"));
            Assert.That(warning.Line, Is.EqualTo(321));
            Assert.That(warning.Context, Is.SameAs(host));
            Assert.That(warning.Message, Does.Contain("CoroutineSite:321").And.Contain("'Host' is inactive"));
            Assert.That(_s.Logs.Count("JANITOR110"), Is.EqualTo(1));
        }

        [Test]
        public void StartCoroutine_OnANullHost_RecordsJanitor110WithoutAContextObject()
        {
            var area = Lifetime.App.CreateChild("area");
            MonoBehaviour host = null;
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR110.*because its host is null"));

            area.StartCoroutine(host, CoroutineRoutines.Forever(new Counter()), "NullSite", 9);

            var warning = _d.Single(DiagnosticIds.CoroutineNotStarted);
            Assert.That(warning.Lifetime, Is.SameAs(area));
            Assert.That(warning.Context, Is.Null);
            Assert.That(warning.Member, Is.EqualTo("NullSite"));
        }

        [Test]
        public void StartCoroutine_OnADestroyedHost_RecordsJanitor110WithoutAContextObject()
        {
            var host = _s.NewProbe("Host");
            Object.DestroyImmediate(host.gameObject);
            var area = Lifetime.App.CreateChild("area");
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR110.*because its host was destroyed"));

            area.StartCoroutine(host, CoroutineRoutines.Forever(new Counter()), "DestroyedSite", 3);

            Assert.That(_d.Single(DiagnosticIds.CoroutineNotStarted).Context, Is.Null);
        }

        [Test]
        public void StartCoroutine_WhileTheLifetimeIsCancelling_RecordsNothing()
        {
            var host = _s.NewProbe("Host");
            var area = Lifetime.App.CreateChild("area");
            area.OnCancel(() => area.StartCoroutine(host, CoroutineRoutines.Forever(new Counter())));

            area.Cancel();

            Assert.That(_d.Count(DiagnosticIds.CoroutineNotStarted), Is.Zero, "no warning is logged for a lifetime that is ending, so none is recorded");
        }

        // 113

        [Test]
        public void GetLifetime_ALaterMismatch_RecordsJanitor113AttachedToTheExistingLifetime()
        {
            var probe = _s.NewProbe();
            var first = Lifetime.App.CreateChild("First");
            var second = Lifetime.App.CreateChild("Second");
            var placed = probe.GetLifetime(first);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR113"));
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR113"));

            probe.GetLifetime(second);
            probe.GetLifetime(second);

            var warning = _d.Single(DiagnosticIds.ParentMismatch);
            Assert.That(warning.Lifetime, Is.SameAs(placed));
            Assert.That(warning.Context, Is.SameAs(probe));
            Assert.That(warning.Message, Does.Contain("'First'").And.Contain("'Second'"));
            Assert.That(warning.Count, Is.EqualTo(2), "the same mismatch repeated is one entry with a counter");
            Assert.That(_s.Logs.Count("JANITOR113"), Is.EqualTo(2));
        }

        [Test]
        public void GetLifetime_WithADisposedCategory_RecordsJanitor113OnTheObjectOnly()
        {
            var probe = _s.NewProbe();
            var gone = Lifetime.App.CreateChild("Gone");
            gone.Dispose();
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR113"));

            probe.GetLifetime(gone);

            var warning = _d.Single(DiagnosticIds.ParentMismatch);
            Assert.That(warning.Lifetime, Is.Null, "the object's lifetime did not exist yet");
            Assert.That(warning.Context, Is.SameAs(probe));
            Assert.That(warning.Message, Does.Contain("'Gone'").And.Contain("already disposed"));
        }

        [Test]
        public void GetLifetime_AfterTheCategoryWasDisposed_RecordsJanitor113OnceAndSaysItCannotBePlacedAgain()
        {
            var probe = _s.NewProbe();
            var category = Lifetime.App.CreateChild("Popups");
            var other = Lifetime.App.CreateChild("Other");
            probe.GetLifetime(category);
            category.Dispose();
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR113.*cannot be placed again"));

            probe.GetLifetime(other);
            probe.GetLifetime(other);
            probe.GetLifetime(other);

            var warning = _d.Single(DiagnosticIds.ParentMismatch);
            Assert.That(warning.Count, Is.EqualTo(1));
            Assert.That(warning.Message, Does.Contain("cannot be placed again"));
            Assert.That(_s.Logs.Count("JANITOR113"), Is.EqualTo(1));
        }

        // 114

        [Test]
        public void Dispose_OnACategory_RecordsJanitor114AsInformationPerRehomedLifetimeAndLogsNothing()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var category = Lifetime.App.CreateChild("Popups");
            var component = probe.GetLifetime(category);
            var active = probe.GetActiveLifetime(category);

            category.Dispose();

            var rows = _d.Warnings(DiagnosticIds.Rehomed);
            Assert.That(rows.Count, Is.EqualTo(2), "one per re-homed lifetime");
            Assert.That(SnapshotQueries.HasFor(rows, component), Is.True);
            Assert.That(SnapshotQueries.HasFor(rows, active), Is.True);
            for (var i = 0; i < rows.Count; i++)
            {
                Assert.That(rows[i].IsInfo, Is.True, "a re-home is information, not a warning");
                Assert.That(rows[i].Message, Does.Contain("was re-homed under"));
                Assert.That(rows[i].Context, Is.SameAs(rows[i].Lifetime.OwnerObject));
            }

            Assert.That(_s.Tree.RehomeCount, Is.EqualTo(2));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Dispose_OnACategoryWithNothingPlaced_RecordsNoRehome()
        {
            var category = Lifetime.App.CreateChild("Empty");

            category.Dispose();

            Assert.That(_d.Count(DiagnosticIds.Rehomed), Is.Zero);
        }
    }
}
#endif
