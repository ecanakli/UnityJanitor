using System;
using System.Collections;
using System.Text.RegularExpressions;
using DG.Tweening;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.TweenBinding
{
    // AddTo(Component) and AddTo(GameObject) resolve their owner exactly like the core AddTo(IDisposable),
    // and a tween bound to a component dies with it, synchronously, before OnDestroy.
    [TestFixture]
    public sealed class TweenOwnerTests
    {
        private TweenSession _s;

        [SetUp]
        public void SetUp()
        {
            _s = TweenSession.Begin();
        }

        [TearDown]
        public void TearDown()
        {
            _s.Complete();
        }

        [Test]
        public void AddTo_MonoBehaviourOwner_RegistersOnTheComponentLifetimeAndDiesWithTheComponent()
        {
            var host = _s.NewHost();
            var component = host.GetLifetime();
            var objectLifetime = host.gameObject.GetLifetime();
            var tween = _s.NewTween(_s.Box());

            tween.AddTo(host);

            Assert.That(component.EntryCount, Is.EqualTo(1), "a MonoBehaviour owner uses its component lifetime");
            Assert.That(objectLifetime.EntryCount, Is.Zero);

            Object.DestroyImmediate(host.gameObject);

            Assert.That(tween.IsActive(), Is.False);
        }

        [Test]
        public void AddTo_TransformOwner_RegistersOnTheGameObjectLifetimeAndSurvivesADestroyOfAComponent()
        {
            var host = _s.NewHost();
            var go = host.gameObject;
            var component = host.GetLifetime();
            var objectLifetime = go.GetLifetime();
            var tween = _s.NewTween(_s.Box());

            tween.AddTo(host.transform);

            Assert.That(objectLifetime.EntryCount, Is.EqualTo(1), "any other component uses the lifetime of its GameObject");
            Assert.That(component.EntryCount, Is.Zero);

            Object.DestroyImmediate(host);
            Assert.That(tween.IsActive(), Is.True, "destroying one component does not dispose the GameObject lifetime");

            Object.DestroyImmediate(go);
            Assert.That(tween.IsActive(), Is.False);
        }

        [Test]
        public void AddTo_GameObjectOwner_RegistersOnTheGameObjectLifetimeAndDiesWithTheObject()
        {
            var host = _s.NewHost();
            var component = host.GetLifetime();
            var objectLifetime = host.gameObject.GetLifetime();
            var tween = _s.NewTween(_s.Box());

            tween.AddTo(host.gameObject);

            Assert.That(objectLifetime.EntryCount, Is.EqualTo(1));
            Assert.That(component.EntryCount, Is.Zero);

            Object.DestroyImmediate(host.gameObject);

            Assert.That(tween.IsActive(), Is.False);
        }

        [Test]
        public void AddTo_DisposableAndTweenOnTheSameOwner_ResolveToTheirOwnOverloadsAndTheSameLifetime()
        {
            var host = _s.NewHost();
            var component = host.GetLifetime();
            var objectLifetime = host.gameObject.GetLifetime();
            var disposable = new DisposeProbe();
            var other = new DisposeProbe();
            var tween = _s.NewTween(_s.Box());
            var viaTransform = _s.NewTween(_s.Box());

            disposable.AddTo(host);
            tween.AddTo(host);
            other.AddTo(host.transform);
            viaTransform.AddTo(host.transform);

            Assert.That(component.EntryCount, Is.EqualTo(2), "the core AddTo and the tween AddTo share the component lifetime");
            Assert.That(objectLifetime.EntryCount, Is.EqualTo(2), "and the GameObject lifetime for any other component");

            Object.DestroyImmediate(host.gameObject);

            Assert.That(disposable.DisposeCount, Is.EqualTo(1));
            Assert.That(other.DisposeCount, Is.EqualTo(1));
            Assert.That(tween.IsActive(), Is.False);
            Assert.That(viaTransform.IsActive(), Is.False);
        }

        [Test]
        public void Destroy_CompleteMode_LandsTheEndValueAndRunsOnCompleteOnce()
        {
            var host = _s.NewHost();
            var box = _s.Box();
            var completes = 0;
            var tween = _s.NewTween(box, 10f);
            tween.OnComplete(() => completes++);
            tween.AddTo(host, TweenCancelMode.Complete);
            _s.Step(0.4f);
            Assert.That(box.Value, Is.LessThan(10f), "premise: the tween is midway");

            Object.DestroyImmediate(host.gameObject);

            Assert.That(tween.IsActive(), Is.False);
            Assert.That(box.Value, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(completes, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Destroy_OfTheOwner_KillsTheTweenBeforeOnDestroyRuns()
        {
            var host = _s.NewHost();
            var tween = _s.NewTween(_s.Box());
            tween.AddTo(host);
            var activeInOnDestroy = true;
            host.DestroyProbe = () => activeInOnDestroy = tween.IsActive();

            Object.Destroy(host.gameObject);
            yield return TweenSession.Frames(2);

            Assert.That(activeInOnDestroy, Is.False, "teardown runs between OnDisable and OnDestroy");
            Assert.That(tween.IsActive(), Is.False);
        }

        [UnityTest]
        public IEnumerator DestroyImmediate_AddToKillsAtOnce_WhereSetLinkWaitsForDOTween()
        {
            var go = _s.NewObject();
            var bound = _s.NewTween(_s.Box(), 10f, 30f);
            var linked = _s.NewLinkedTween(go);
            bound.AddTo(go);

            Object.DestroyImmediate(go);

            Assert.That(bound.IsActive(), Is.False, "AddTo kills synchronously during teardown");
            for (var i = 0; i < TweenSession.MaxFrames && linked.IsActive(); i++)
            {
                yield return null;
            }

            Assert.That(linked.IsActive(), Is.False, "SetLink kills on a later DOTween update");
        }

        [Test]
        public void AddTo_ADestroyedMonoBehaviour_KillsEvenInCompleteMode()
        {
            var host = _s.NewHost();
            Object.DestroyImmediate(host.gameObject);
            var box = _s.Box();
            var completes = 0;
            var tween = _s.NewTween(box, 10f);
            tween.OnComplete(() => completes++);

            tween.AddTo(host, TweenCancelMode.Complete);

            Assert.That(tween.IsActive(), Is.False);
            Assert.That(completes, Is.Zero);
            Assert.That(box.Value, Is.Zero);
        }

        [Test]
        public void AddTo_ADestroyedGameObject_KillsEvenInCompleteMode()
        {
            var go = _s.NewObject();
            Object.DestroyImmediate(go);
            var completes = 0;
            var tween = _s.NewTween(_s.Box(), 10f);
            tween.OnComplete(() => completes++);

            tween.AddTo(go, TweenCancelMode.Complete);

            Assert.That(tween.IsActive(), Is.False);
            Assert.That(completes, Is.Zero);
        }

        [Test]
        public void AddTo_ADestroyedTransform_KillsEvenInCompleteMode()
        {
            var go = _s.NewObject();
            var transform = go.transform;
            Object.DestroyImmediate(go);
            var completes = 0;
            var tween = _s.NewTween(_s.Box(), 10f);
            tween.OnComplete(() => completes++);

            tween.AddTo(transform, TweenCancelMode.Complete);

            Assert.That(tween.IsActive(), Is.False, "a destroyed component that has no GameObject left to ask still ends the tween");
            Assert.That(completes, Is.Zero);
        }

        [Test]
        public void AddTo_NullComponentAndNullGameObject_KillTheTweenThenThrow()
        {
            var first = _s.NewTween(_s.Box());
            var second = _s.NewTween(_s.Box());

            var componentFailure = Assert.Throws<ArgumentNullException>(() => first.AddTo((Component)null));
            var objectFailure = Assert.Throws<ArgumentNullException>(() => second.AddTo((GameObject)null));

            Assert.That(componentFailure.ParamName, Is.EqualTo("owner"));
            Assert.That(objectFailure.ParamName, Is.EqualTo("owner"));
            Assert.That(first.IsActive(), Is.False);
            Assert.That(second.IsActive(), Is.False);
        }

        [Test]
        public void AddTo_ComponentOwnerOffTheMainThread_ThrowsAndLeavesTheTweenUntouched()
        {
            var host = _s.NewHost();
            var tween = _s.NewTween(_s.Box());

            var failure = ThreadRunner.Run(() => tween.AddTo(host));

            Assert.That(failure, Is.InstanceOf<InvalidOperationException>());
            Assert.That(tween.IsActive(), Is.True);
            Assert.That(host.GetLifetime().EntryCount, Is.Zero);
        }

        [Test]
        public void AddTo_ActiveLifetime_KillsOnDeactivationAndTheNextActivationStartsFresh()
        {
            var host = _s.NewHost();
            var active = host.GetActiveLifetime();
            var first = _s.NewTween(_s.Box());
            first.AddTo(active);
            Assert.That(active.EntryCount, Is.EqualTo(1));

            host.gameObject.SetActive(false);

            Assert.That(first.IsActive(), Is.False, "deactivation cancels the active lifetime, which kills the tween");

            host.gameObject.SetActive(true);
            var second = _s.NewTween(_s.Box());
            second.AddTo(active);

            Assert.That(second.IsActive(), Is.True, "the reactivated object starts a fresh generation");
            Assert.That(active.EntryCount, Is.EqualTo(1));

            host.gameObject.SetActive(false);
            Assert.That(second.IsActive(), Is.False);
        }

        [Test]
        public void AddTo_ActiveLifetimeOfAnInactiveObject_KillsEvenInCompleteMode()
        {
            var host = _s.NewHost();
            var active = host.GetActiveLifetime();
            host.gameObject.SetActive(false);
            var box = _s.Box();
            var completes = 0;
            var tween = _s.NewTween(box, 10f);
            tween.OnComplete(() => completes++);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR108"));

            tween.AddTo(active, TweenCancelMode.Complete);

            Assert.That(tween.IsActive(), Is.False, "the active lifetime refuses work while its object is inactive");
            Assert.That(completes, Is.Zero);
            Assert.That(box.Value, Is.Zero);
        }
    }
}
