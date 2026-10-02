using System;
using System.Collections;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks.Triggers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.Binding
{
    // The component and GameObject lifetimes.
    [TestFixture]
    public sealed class ComponentLifetimeTests
    {
        private BindingSession _s;
        private CallLog _log;
        private CallLog _lifecycle;

        [SetUp]
        public void SetUp()
        {
            _s = BindingSession.Begin();
            _log = new CallLog();
            _lifecycle = new CallLog();
        }

        [TearDown]
        public void TearDown()
        {
            _s.Complete();
        }

        // Guard: ComponentLifetimes.BindDestroy registers DisposeFromOwner on destroyCancellationToken, read at creation.
        [UnityTest]
        public IEnumerator Destroy_DisposesTheComponentLifetime_BetweenOnDisableAndOnDestroy()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var lifetime = probe.GetLifetime();
            lifetime.Record(_log, "item");
            lifetime.RecordToken(_log, "token");
            BindingProbe.Log = _log;

            Object.Destroy(go);
            yield return null;

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "OnDisable", "token", "item", "OnDestroy" }));
            Assert.That(lifetime.IsDisposed, Is.True);
            Assert.That(_s.Tree.Owners.Count, Is.Zero, "a disposed component lifetime leaves the owner index");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void DestroyImmediate_DisposesTheComponentLifetime_InTheSameOrder()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var lifetime = probe.GetLifetime();
            lifetime.Record(_log, "item");
            BindingProbe.Log = _log;

            Object.DestroyImmediate(go);

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "OnDisable", "item", "OnDestroy" }));
            Assert.That(lifetime.IsDisposed, Is.True);
        }

        // Guard: Lifetime.OwnerTokenRegistration is released only by FinishDispose, never by FinishCancel.
        [UnityTest]
        public IEnumerator Destroy_AfterAManualCancel_DisposesTheLifetimeAndRunsOnlyTheNewGenerationsItems()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var lifetime = probe.GetLifetime();
            lifetime.Record(_log, "gen0");

            lifetime.Cancel();

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "gen0" }));
            Assert.That(lifetime.IsDisposed, Is.False);
            Assert.That(_s.Tree.Owners.Count, Is.EqualTo(1), "a Cancel keeps the owner index entry");
            lifetime.Record(_log, "gen1");

            Object.Destroy(go);
            yield return null;

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "gen0", "gen1" }));
            Assert.That(lifetime.IsDisposed, Is.True, "the destroy registration survived the Cancel");
            Assert.That(_s.Tree.Owners.Count, Is.Zero);
        }

        // Guard: ComponentLifetimes.ForBehaviour returns the sentinel for a Unity-null owner.
        [UnityTest]
        public IEnumerator GetLifetime_OnADestroyedComponent_ReturnsTheDisposedSentinel()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var original = probe.GetLifetime();
            Object.Destroy(go);
            yield return null;

            var afterwards = probe.GetLifetime();

            Assert.That(original.IsDisposed, Is.True);
            Assert.That(afterwards, Is.SameAs(_s.Tree.Sentinel));
            Assert.That(afterwards.IsDisposed, Is.True);
            var ran = 0;
            afterwards.OnCancel(() => ran++);
            Assert.That(ran, Is.EqualTo(1), "a registration on the sentinel is terminated at once");
            var disposable = new DisposeProbe();
            disposable.AddTo(probe);
            Assert.That(disposable.DisposeCount, Is.EqualTo(1), "an item added to a destroyed owner is disposed at once");
            Assert.That(_s.Tree.Owners.Count, Is.Zero, "the sentinel is never indexed");
        }

        [UnityTest]
        public IEnumerator GetLifetime_OnADestroyedGameObject_ReturnsTheDisposedSentinel()
        {
            var go = _s.NewObject();
            var original = go.GetLifetime();
            Object.Destroy(go);
            yield return null;

            var afterwards = go.GetLifetime();

            Assert.That(original.IsDisposed, Is.True);
            Assert.That(afterwards, Is.SameAs(_s.Tree.Sentinel));
            Assert.That(_s.Tree.Owners.Count, Is.Zero);
        }

        // Guard: the inactive branch of ForBehaviour uses UniTask's destroy trigger, whose AwakeMonitor catches never-activated objects.
        [UnityTest]
        public IEnumerator GetLifetime_FirstAccessWhileInactive_OnAnObjectDestroyedWithoutActivating_IsDisposedWithinOneFrame()
        {
            BindingProbe.Log = _lifecycle;
            var go = _s.NewInactiveObject();
            var probe = go.AddComponent<BindingProbe>();
            var lifetime = probe.GetLifetime();
            lifetime.Record(_log, "item");
            Assert.That(go.TryGetComponent<AsyncDestroyTrigger>(out _), Is.True, "the inactive path watches the object through UniTask's destroy trigger");
            Assert.That(lifetime.IsDisposed, Is.False);

            Object.Destroy(go);
            yield return null;
            var disposedAfterOneFrame = lifetime.IsDisposed;
            if (!disposedAfterOneFrame)
            {
                yield return null;
            }

            Assert.That(disposedAfterOneFrame, Is.True, "not disposed after one frame; disposed after a second frame: " + lifetime.IsDisposed);
            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "item" }));
            Assert.That(_lifecycle.ToArray(), Is.Empty, "a never activated object runs neither Awake nor OnDestroy, so only the trigger can dispose the lifetime");
        }

        [UnityTest]
        public IEnumerator GetLifetime_FirstAccessWhileInactive_ThenActivatedAndDestroyed_IsDisposedWithinOneFrame()
        {
            var go = _s.NewInactiveObject();
            var probe = go.AddComponent<BindingProbe>();
            var lifetime = probe.GetLifetime();
            lifetime.Record(_log, "item");
            go.SetActive(true);

            Object.Destroy(go);
            yield return null;

            Assert.That(lifetime.IsDisposed, Is.True);
            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "item" }));
        }

        [Test]
        public void GetLifetime_GameObjectAndMonoBehaviour_AreSeparateLifetimesOfTheSameScene()
        {
            var go = _s.NewObject("Owner");
            var probe = go.AddComponent<BindingProbe>();

            var componentLifetime = probe.GetLifetime();
            Assert.That(go.TryGetComponent<AsyncDestroyTrigger>(out _), Is.False, "an active component needs no extra component");
            var objectLifetime = go.GetLifetime();

            var scene = SceneLifetimes.Get(go.scene);
            Assert.That(objectLifetime, Is.Not.SameAs(componentLifetime));
            Assert.That(componentLifetime.Kind, Is.EqualTo(LifetimeKind.Component));
            Assert.That(objectLifetime.Kind, Is.EqualTo(LifetimeKind.GameObject));
            Assert.That(componentLifetime.Parent, Is.SameAs(scene));
            Assert.That(objectLifetime.Parent, Is.SameAs(scene));
            Assert.That(componentLifetime.Name, Is.EqualTo(nameof(BindingProbe)));
            Assert.That(objectLifetime.Name, Is.EqualTo("Owner"));
            Assert.That(componentLifetime.OwnerObject, Is.SameAs(probe));
            Assert.That(objectLifetime.OwnerObject, Is.SameAs(go));
            Assert.That(componentLifetime.Placed, Is.False);
            Assert.That(go.TryGetComponent<AsyncDestroyTrigger>(out _), Is.True, "a GameObject lifetime watches the object through UniTask's destroy trigger");
            Assert.That(_s.Tree.Owners.Count, Is.EqualTo(2));
        }

        [Test]
        public void GetLifetime_TwoComponentsOnOneObject_HaveTheirOwnLifetimes()
        {
            var go = _s.NewObject();
            var first = go.AddComponent<BindingProbe>();
            var second = go.AddComponent<BindingProbe>();

            Assert.That(first.GetLifetime(), Is.Not.SameAs(second.GetLifetime()));
            Assert.That(first.GetLifetime(), Is.SameAs(first.GetLifetime()));
            Assert.That(_s.Tree.Owners.Count, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator Destroy_OfTheComponentAlone_DisposesOnlyTheComponentLifetime()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var componentLifetime = probe.GetLifetime();
            var objectLifetime = go.GetLifetime();
            componentLifetime.Record(_log, "component");
            objectLifetime.Record(_log, "object");

            Object.Destroy(probe);
            yield return null;

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "component" }));
            Assert.That(componentLifetime.IsDisposed, Is.True);
            Assert.That(objectLifetime.IsDisposed, Is.False, "destroying one component does not dispose the GameObject lifetime");

            Object.Destroy(go);
            yield return null;

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "component", "object" }));
            Assert.That(objectLifetime.IsDisposed, Is.True);
        }

        // A component that never ran Awake does not cancel its destroy token, so only the GameObject's destruction is seen.
        [UnityTest]
        public IEnumerator Destroy_OfTheComponentAlone_OnTheInactivePath_DoesNotDisposeTheLifetime()
        {
            var go = _s.NewInactiveObject();
            var probe = go.AddComponent<BindingProbe>();
            var lifetime = probe.GetLifetime();
            lifetime.Record(_log, "item");

            Object.Destroy(probe);
            yield return BindingScenes.Frames(3);

            Assert.That(lifetime.IsDisposed, Is.False, "Destroy(component) alone does not dispose a lifetime of a component that never ran Awake");
            Assert.That(_log.Count, Is.Zero);

            Object.Destroy(go);
            yield return BindingScenes.Frames(2);

            Assert.That(lifetime.IsDisposed, Is.True, "destroying the GameObject disposes it");
            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "item" }));
        }

        // First access while inactive, then activated: the component's own destroy token ends the lifetime.
        [UnityTest]
        public IEnumerator Destroy_OfTheComponentAlone_AfterAFirstAccessWhileInactiveAndAnActivation_DisposesTheLifetime()
        {
            var go = _s.NewInactiveObject();
            var probe = go.AddComponent<BindingProbe>();
            var lifetime = probe.GetLifetime();
            lifetime.Record(_log, "item");
            go.SetActive(true);

            Object.Destroy(probe);
            yield return BindingScenes.Frames(2);

            Assert.That(lifetime.IsDisposed, Is.True, "the component is gone, so its lifetime is");
            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "item" }));
            Assert.That(_s.Tree.Owners.Count, Is.Zero);

            Object.Destroy(go);
            yield return BindingScenes.Frames(2);

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "item" }), "the GameObject's destroy signal finds nothing left to end");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Destroy_OfTheGameObject_AfterAFirstAccessWhileInactiveAndAnActivation_DisposesTheLifetimeBetweenOnDisableAndOnDestroy()
        {
            var go = _s.NewInactiveObject();
            var probe = go.AddComponent<BindingProbe>();
            var lifetime = probe.GetLifetime();
            lifetime.Record(_lifecycle, "item");
            lifetime.RecordToken(_lifecycle, "token");
            go.SetActive(true);
            BindingProbe.Log = _lifecycle;

            Object.Destroy(go);
            yield return BindingScenes.Frames(2);

            Assert.That(_lifecycle.ToArray(), Is.EqualTo(new[] { "OnDisable", "token", "item", "OnDestroy" }));
            Assert.That(lifetime.IsDisposed, Is.True);
            Assert.That(_s.Tree.Owners.Count, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Destroy_OfTheGameObject_AfterAFirstAccessWhileInactiveWithoutAnActivation_DisposesTheLifetimeOnce()
        {
            var go = _s.NewInactiveObject();
            var probe = go.AddComponent<BindingProbe>();
            var lifetime = probe.GetLifetime();
            lifetime.Record(_log, "item");

            Object.Destroy(go);
            yield return BindingScenes.Frames(3);

            Assert.That(lifetime.IsDisposed, Is.True);
            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "item" }));
            Assert.That(_s.Tree.Owners.Count, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Destroy_OfTheComponentAlone_AfterAManualCancel_OnTheInactivePath_StillDisposesTheLifetime()
        {
            var go = _s.NewInactiveObject();
            var probe = go.AddComponent<BindingProbe>();
            var lifetime = probe.GetLifetime();
            lifetime.Record(_log, "gen0");
            go.SetActive(true);

            lifetime.Cancel();
            lifetime.Record(_log, "gen1");
            Object.Destroy(probe);
            yield return BindingScenes.Frames(2);

            Assert.That(lifetime.IsDisposed, Is.True, "a Cancel keeps both destroy registrations");
            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "gen0", "gen1" }));
        }

        [UnityTest]
        public IEnumerator Destroy_ChildAreaOfTheComponentLifetime_IsDisposedWithIt()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var lifetime = probe.GetLifetime();
            var area = lifetime.CreateChild("area");
            area.Record(_log, "area");
            lifetime.Record(_log, "component");

            Object.Destroy(go);
            yield return null;

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "area", "component" }), "descendants first");
            Assert.That(area.IsDisposed, Is.True);
        }

        [Test]
        public void GetLifetime_RepeatedCalls_ReturnTheSameLifetimeAndIndexItOnce()
        {
            var probe = _s.NewProbe();

            var first = probe.GetLifetime();
            for (var i = 0; i < 10; i++)
            {
                Assert.That(probe.GetLifetime(), Is.SameAs(first));
            }

            Assert.That(_s.Tree.Owners.Count, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_OnAComponentLifetime_IsIgnoredWithJanitor107()
        {
            var probe = _s.NewProbe();
            var lifetime = probe.GetLifetime();
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR107"));

            lifetime.Dispose();

            Assert.That(lifetime.IsDisposed, Is.False, "an object lifetime is disposed by its owner, not by the game");
            Assert.That(lifetime.Record(_log, "still").IsActive, Is.True);
        }

        // Guard: every public entry point checks the tree and the thread before it touches a Unity object.
        [Test]
        public void EveryEntryPoint_OffTheMainThread_ThrowsBeforeTouchingUnity()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var disposable = new DisposeProbe();

            var failures = new[]
            {
                ThreadRunner.Run(() => probe.GetLifetime()),
                ThreadRunner.Run(() => go.GetLifetime()),
                ThreadRunner.Run(() => probe.GetActiveLifetime()),
                ThreadRunner.Run(() => disposable.AddTo(probe)),
                ThreadRunner.Run(() => SceneLifetimes.DisposeAll()),
            };

            for (var i = 0; i < failures.Length; i++)
            {
                Assert.That(failures[i], Is.TypeOf<InvalidOperationException>(), "entry point " + i);
                Assert.That(failures[i].Message, Does.Contain("main thread"), "entry point " + i);
            }

            Assert.That(_s.Tree.Owners.Count, Is.Zero, "a rejected call creates nothing");
            Assert.That(disposable.DisposeCount, Is.Zero);
        }

        [Test]
        public void GetLifetime_NullArguments_Throw()
        {
            var probe = _s.NewProbe();

            var behaviour = Assert.Throws<ArgumentNullException>(() => ((MonoBehaviour)null).GetLifetime());
            var gameObject = Assert.Throws<ArgumentNullException>(() => ((GameObject)null).GetLifetime());
            var parent = Assert.Throws<ArgumentNullException>(() => probe.GetLifetime((Lifetime)null));
            var active = Assert.Throws<ArgumentNullException>(() => ((Component)null).GetActiveLifetime());
            var activeParent = Assert.Throws<ArgumentNullException>(() => probe.GetActiveLifetime((Lifetime)null));

            Assert.That(behaviour.ParamName, Is.EqualTo("behaviour"));
            Assert.That(gameObject.ParamName, Is.EqualTo("gameObject"));
            Assert.That(parent.ParamName, Is.EqualTo("parent"));
            Assert.That(active.ParamName, Is.EqualTo("component"));
            Assert.That(activeParent.ParamName, Is.EqualTo("parent"));
            Assert.That(_s.Tree.Owners.Count, Is.Zero, "a rejected call creates nothing");
        }
    }
}
