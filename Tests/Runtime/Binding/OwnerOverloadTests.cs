using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.Binding
{
    // The MonoBehaviour and Component overloads.
    // Each form must bind to the right lifetime and end with it. A MonoBehaviour owner uses its component lifetime;
    // any other Component uses the lifetime of its GameObject.
    [TestFixture]
    public sealed class OwnerOverloadTests
    {
        private BindingSession _s;

        [SetUp]
        public void SetUp()
        {
            _s = BindingSession.Begin();
        }

        [TearDown]
        public void TearDown()
        {
            _s.Complete();
        }

        // this.Run, After, Every

        [UnityTest]
        public IEnumerator Run_OnAMonoBehaviour_BindsToTheComponentLifetimeAndEndsWithIt()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var component = probe.GetLifetime();
            var gameObjectLifetime = go.GetLifetime();
            var token = default(CancellationToken);
            var stage = 0;

            probe.Run(async ct =>
            {
                token = ct;
                stage = 1;
                await UniTask.Delay(TimeSpan.FromSeconds(60), cancellationToken: ct);
                stage = 2;
            });

            Assert.That(stage, Is.EqualTo(1));
            Assert.That(component.EntryCount, Is.EqualTo(1), "the task is an entry of the component lifetime");
            Assert.That(gameObjectLifetime.EntryCount, Is.Zero);
            Assert.That(token, Is.EqualTo(component.Token), "the work receives the component lifetime's token");

            Object.Destroy(go);
            yield return BindingScenes.Frames(2);

            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(component.EntryCount, Is.Zero);
            Assert.That(stage, Is.EqualTo(1), "the code after the token-aware await never ran");
        }

        [UnityTest]
        public IEnumerator Run_WithStateOnAMonoBehaviour_PassesTheStateAndEndsWithTheComponent()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var counter = new Counter();
            var token = default(CancellationToken);

            probe.Run(counter, (c, ct) =>
            {
                c.Value++;
                token = ct;
                return UniTask.Delay(TimeSpan.FromSeconds(60), cancellationToken: ct);
            });

            Assert.That(counter.Value, Is.EqualTo(1));
            Assert.That(probe.GetLifetime().EntryCount, Is.EqualTo(1));

            Object.Destroy(go);
            yield return BindingScenes.Frames(2);

            Assert.That(token.IsCancellationRequested, Is.True);
        }

        [UnityTest]
        public IEnumerator After_OnAMonoBehaviour_FiresOnceAndNeverAfterTheComponentIsDestroyed()
        {
            var clock = _s.UseManualClock();
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var component = probe.GetLifetime();
            var fired = 0;
            var neverFired = 0;
            var state = new Counter();
            var neverState = new Counter();
            probe.After(1f, () => fired++);
            probe.After(1f, state, static c => c.Value++);
            probe.After(5f, () => neverFired++);
            probe.After(5f, neverState, static c => c.Value++);
            Assert.That(component.EntryCount, Is.EqualTo(4), "each timer is an entry of the component lifetime");
            Assert.That(go.GetLifetime().EntryCount, Is.Zero);

            clock.Advance(1f);

            Assert.That(fired, Is.EqualTo(1));
            Assert.That(state.Value, Is.EqualTo(1));
            Assert.That(neverFired, Is.Zero);
            Assert.That(neverState.Value, Is.Zero);
            Assert.That(component.EntryCount, Is.EqualTo(2), "a timer that fired left its entry");

            Object.Destroy(go);
            yield return null;
            clock.Advance(10f);

            Assert.That(fired, Is.EqualTo(1));
            Assert.That(state.Value, Is.EqualTo(1));
            Assert.That(neverFired, Is.Zero, "a timer of a destroyed component never fires");
            Assert.That(neverState.Value, Is.Zero);
            Assert.That(clock.PendingCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Every_OnAMonoBehaviour_TicksUntilTheComponentIsDestroyed()
        {
            var clock = _s.UseManualClock();
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var ticks = 0;
            var counter = new Counter();
            probe.Every(1f, () => ticks++);
            probe.Every(1f, counter, static c => c.Value++);

            clock.Advance(1f);
            clock.Advance(1f);
            clock.Advance(1f);

            Assert.That(ticks, Is.EqualTo(3));
            Assert.That(counter.Value, Is.EqualTo(3));

            Object.Destroy(go);
            yield return null;
            clock.Advance(1f);
            clock.Advance(1f);

            Assert.That(ticks, Is.EqualTo(3), "no tick after the component is destroyed");
            Assert.That(counter.Value, Is.EqualTo(3));
            Assert.That(clock.PendingCount, Is.Zero, "the cancelled delays were completed and not re-armed");
        }

        // OnCancel

        [UnityTest]
        public IEnumerator OnCancel_AllFourFormsOnAMonoBehaviour_RunOnTheComponentLifetimeWhenItEnds()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var plain = 0;
            var state = new Counter();
            var probed = new Counter();
            var first = new Counter();
            var second = new Counter();

            probe.OnCancel(() => plain++);
            probe.OnCancel(state, static c => c.Value++);
            probe.OnCancel(probed, static c => c.Value++, static c => false);
            probe.OnCancel(first, second, static (a, b) =>
            {
                a.Value++;
                b.Value++;
            });

            Assert.That(probe.GetLifetime().EntryCount, Is.EqualTo(4));
            Assert.That(go.GetLifetime().EntryCount, Is.Zero);
            probe.GetLifetime().Cancel();

            Assert.That(plain, Is.EqualTo(1), "a Cancel of the component lifetime runs them");
            Assert.That(state.Value, Is.EqualTo(1));
            Assert.That(probed.Value, Is.EqualTo(1));
            Assert.That(first.Value + second.Value, Is.EqualTo(2));

            probe.OnCancel(() => plain++);
            Object.Destroy(go);
            yield return null;

            Assert.That(plain, Is.EqualTo(2), "destroying the component runs the new generation's item once");
        }

        // AddTo

        [UnityTest]
        public IEnumerator AddTo_OnAMonoBehaviour_BindsToTheComponentLifetime()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var gameObjectLifetime = go.GetLifetime();
            var disposable = new DisposeProbe();

            var registration = disposable.AddTo(probe);

            Assert.That(registration.IsActive, Is.True);
            Assert.That(probe.GetLifetime().EntryCount, Is.EqualTo(1));
            Assert.That(gameObjectLifetime.EntryCount, Is.Zero);

            Object.Destroy(go);
            yield return null;

            Assert.That(disposable.DisposeCount, Is.EqualTo(1));
            Assert.That(registration.IsActive, Is.False);
        }

        [UnityTest]
        public IEnumerator AddTo_OnANonMonoBehaviourComponent_BindsToTheGameObjectLifetime()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var componentLifetime = probe.GetLifetime();
            var disposable = new DisposeProbe();

            disposable.AddTo(go.transform);

            var gameObjectLifetime = go.GetLifetime();
            Assert.That(gameObjectLifetime.EntryCount, Is.EqualTo(1), "a Transform owner resolves to the GameObject lifetime");
            Assert.That(componentLifetime.EntryCount, Is.Zero);

            Object.Destroy(probe);
            yield return null;
            Assert.That(disposable.DisposeCount, Is.Zero, "destroying a component does not end the GameObject lifetime");

            Object.Destroy(go);
            yield return null;
            Assert.That(disposable.DisposeCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator AddTo_OnAGameObject_BindsToTheGameObjectLifetime()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var disposable = new DisposeProbe();

            disposable.AddTo(go);

            Assert.That(go.GetLifetime().EntryCount, Is.EqualTo(1));
            Assert.That(probe.GetLifetime().EntryCount, Is.Zero);

            Object.Destroy(go);
            yield return null;

            Assert.That(disposable.DisposeCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator AddTo_OnADestroyedOwnerOrANullOwner_DisposesTheItem()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            Object.Destroy(go);
            yield return null;
            var afterDestroy = new DisposeProbe();
            var afterDestroyGameObject = new DisposeProbe();
            var nullComponent = new DisposeProbe();
            var nullGameObject = new DisposeProbe();

            afterDestroy.AddTo(probe);
            afterDestroyGameObject.AddTo(go);
            var componentFailure = Assert.Throws<ArgumentNullException>(() => nullComponent.AddTo((Component)null));
            var gameObjectFailure = Assert.Throws<ArgumentNullException>(() => nullGameObject.AddTo((GameObject)null));

            Assert.That(afterDestroy.DisposeCount, Is.EqualTo(1), "a destroyed owner disposes at once");
            Assert.That(afterDestroyGameObject.DisposeCount, Is.EqualTo(1));
            Assert.That(nullComponent.DisposeCount, Is.EqualTo(1), "a null owner disposes the item, then throws");
            Assert.That(nullGameObject.DisposeCount, Is.EqualTo(1));
            Assert.That(componentFailure.ParamName, Is.EqualTo("owner"));
            Assert.That(gameObjectFailure.ParamName, Is.EqualTo("owner"));
        }

        // OwnedEvent with a Component owner

        [UnityTest]
        public IEnumerator OwnedEventSubscribe_AllFourArities_BindToTheComponentLifetimeAndEndWithIt()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var e0 = new OwnedEvent("e0");
            var e1 = new OwnedEvent<int>("e1");
            var e2 = new OwnedEvent<int, int>("e2");
            var e3 = new OwnedEvent<int, int, int>("e3");
            var hits = 0;

            e0.Subscribe(() => hits++, probe);
            e1.Subscribe(a => hits += a, probe);
            e2.Subscribe((a, b) => hits += a + b, probe);
            e3.Subscribe((a, b, c) => hits += a + b + c, probe);

            e0.Invoke();
            e1.Invoke(1);
            e2.Invoke(1, 1);
            e3.Invoke(1, 1, 1);
            Assert.That(hits, Is.EqualTo(1 + 1 + 2 + 3));
            Assert.That(probe.GetLifetime().EntryCount, Is.EqualTo(4));
            Assert.That(go.GetLifetime().EntryCount, Is.Zero);

            Object.Destroy(go);
            yield return null;

            e0.Invoke();
            e1.Invoke(1);
            e2.Invoke(1, 1);
            e3.Invoke(1, 1, 1);
            Assert.That(hits, Is.EqualTo(7), "nothing is delivered after the owner's destroy");
            Assert.That(e0.SubscriberCount + e1.SubscriberCount + e2.SubscriberCount + e3.SubscriberCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator OwnedEventSubscribe_OnANonMonoBehaviourComponent_BindsToTheGameObjectLifetime()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var evt = new OwnedEvent("transform");
            var hits = 0;

            evt.Subscribe(() => hits++, go.transform);

            Assert.That(go.GetLifetime().EntryCount, Is.EqualTo(1));
            Assert.That(probe.GetLifetime().EntryCount, Is.Zero);

            Object.Destroy(probe);
            yield return null;
            evt.Invoke();
            Assert.That(hits, Is.EqualTo(1), "the GameObject lifetime outlives the component");

            Object.Destroy(go);
            yield return null;
            evt.Invoke();
            Assert.That(hits, Is.EqualTo(1));
            Assert.That(evt.SubscriberCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator OwnedEventSubscribe_OnADestroyedOrNullComponent_AddsNothing()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            Object.Destroy(go);
            yield return null;
            var evt = new OwnedEvent("gone");
            Action handler = () => { };

            var registration = evt.Subscribe(handler, probe);
            var failure = Assert.Throws<ArgumentNullException>(() => evt.Subscribe(handler, (Component)null));

            Assert.That(registration.IsActive, Is.False);
            Assert.That(evt.SubscriberCount, Is.Zero, "a destroyed owner adds nothing");
            Assert.That(failure.ParamName, Is.EqualTo("owner"));
        }

        [UnityTest]
        public IEnumerator OwnedEventSubscribe_ThroughTheSubscribeOnlyView_AcceptsAComponentOwner()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var evt = new OwnedEvent<int>("view");
            IOwnedEvent<int> view = evt;
            var total = 0;

            var registration = view.Subscribe(a => total += a, probe);
            evt.Invoke(2);

            Assert.That(registration.IsActive, Is.True);
            Assert.That(total, Is.EqualTo(2));

            Object.Destroy(go);
            yield return null;
            evt.Invoke(2);

            Assert.That(total, Is.EqualTo(2));
            Assert.That(evt.SubscriberCount, Is.Zero);
        }

        // Paired Subscribe on a MonoBehaviour

        [UnityTest]
        public IEnumerator Subscribe_PairedActionFormOnAMonoBehaviour_BindsToTheComponentLifetime()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var source = new BindingEventSource();
            var hits = 0;
            Action handler = () => hits++;

            var registration = probe.Subscribe(h => source.Zero += h, h => source.Zero -= h, handler);
            source.RaiseZero();

            Assert.That(registration.IsActive, Is.True);
            Assert.That(source.Adds, Is.EqualTo(1));
            Assert.That(hits, Is.EqualTo(1));
            Assert.That(probe.GetLifetime().EntryCount, Is.EqualTo(1));
            Assert.That(go.GetLifetime().EntryCount, Is.Zero);

            Object.Destroy(go);
            yield return null;
            source.RaiseZero();

            Assert.That(source.Removes, Is.EqualTo(1), "remove ran exactly once, at the component's destroy");
            Assert.That(source.ZeroListeners, Is.Zero);
            Assert.That(hits, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Subscribe_PairedGenericFormsOnAMonoBehaviour_RemoveOnDestroy()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var source = new BindingEventSource();
            var total = 0;
            Action<int> one = a => total += a;
            Action<int, int> two = (a, b) => total += a + b;
            Action<int, int, int> three = (a, b, c) => total += a + b + c;
            BindingEventSource.CustomHandler custom = (name, value) => total += value;

            probe.Subscribe<int>(h => source.One += h, h => source.One -= h, one);
            probe.Subscribe<int, int>(h => source.Two += h, h => source.Two -= h, two);
            probe.Subscribe<int, int, int>(h => source.Three += h, h => source.Three -= h, three);
            probe.Subscribe<BindingEventSource.CustomHandler>(h => source.Custom += h, h => source.Custom -= h, custom);
            source.RaiseOne(1);
            source.RaiseTwo(1, 1);
            source.RaiseThree(1, 1, 1);
            source.RaiseCustom("n", 1);

            Assert.That(total, Is.EqualTo(1 + 2 + 3 + 1));
            Assert.That(source.Adds, Is.EqualTo(4));
            Assert.That(probe.GetLifetime().EntryCount, Is.EqualTo(4));

            Object.Destroy(go);
            yield return null;
            source.RaiseOne(1);
            source.RaiseTwo(1, 1);
            source.RaiseThree(1, 1, 1);
            source.RaiseCustom("n", 1);

            Assert.That(source.Removes, Is.EqualTo(4));
            Assert.That(source.OneListeners + source.TwoListeners + source.ThreeListeners + source.CustomListeners, Is.Zero);
            Assert.That(total, Is.EqualTo(7), "nothing is delivered after the destroy");
        }

        [UnityTest]
        public IEnumerator Subscribe_PairedFormOnADestroyedMonoBehaviour_AddsNothing()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            Object.Destroy(go);
            yield return null;
            var source = new BindingEventSource();

            var registration = probe.Subscribe(h => source.Zero += h, h => source.Zero -= h, () => { });

            Assert.That(registration.IsActive, Is.False);
            Assert.That(source.ZeroListeners, Is.Zero, "the sentinel is disposed, so the package never calls add");
        }
    }
}
