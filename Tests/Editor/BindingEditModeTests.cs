using System;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Triggers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests
{
    // Outside Play Mode no lifetime tree exists, so every
    // Unity-facing entry point throws InvalidOperationException mentioning Play Mode, and touches nothing.
    // ActiveLifetimeTrigger is the MonoBehaviour under test only because it is public and needs no other assembly.
    [TestFixture]
    public sealed class BindingEditModeTests
    {
        private TestScope _t;
        private LifetimeTree _previousDefault;
        private GameObject _go;
        private ActiveLifetimeTrigger _behaviour;

        [SetUp]
        public void SetUp()
        {
            _t = new TestScope();
            _previousDefault = LifetimeTree.Default;
            LifetimeTree.Default = null;
            _go = new GameObject("BindingEditMode");
            _behaviour = _go.AddComponent<ActiveLifetimeTrigger>();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (_go != null)
                {
                    Object.DestroyImmediate(_go);
                }

                _t.Complete();
            }
            finally
            {
                LifetimeTree.Default = _previousDefault;
            }
        }

        [Test]
        public void GetLifetime_OnAMonoBehaviour_ThrowsOutsidePlayMode()
        {
            AssertPlayModeOnly(() => _behaviour.GetLifetime());
            AssertPlayModeOnly(() => _behaviour.GetLifetime(_t.App.CreateChild("category")));
        }

        [Test]
        public void GetLifetime_OnAGameObject_ThrowsOutsidePlayModeAndAddsNoComponent()
        {
            var plain = new GameObject("Plain");
            try
            {
                AssertPlayModeOnly(() => plain.GetLifetime());

                Assert.That(plain.TryGetComponent<AsyncDestroyTrigger>(out _), Is.False, "a rejected call must not touch the object");
            }
            finally
            {
                Object.DestroyImmediate(plain);
            }
        }

        [Test]
        public void GetActiveLifetime_ThrowsOutsidePlayModeAndAddsNoTrigger()
        {
            var plain = new GameObject("Plain");
            try
            {
                AssertPlayModeOnly(() => plain.transform.GetActiveLifetime());
                AssertPlayModeOnly(() => plain.transform.GetActiveLifetime(_t.App.CreateChild("category")));

                Assert.That(plain.TryGetComponent<ActiveLifetimeTrigger>(out _), Is.False, "a rejected call must not add the hidden trigger");
            }
            finally
            {
                Object.DestroyImmediate(plain);
            }
        }

        [Test]
        public void SceneLifetimes_ThrowOutsidePlayMode_EvenForAnInvalidScene()
        {
            AssertPlayModeOnly(() => SceneLifetimes.Get(default(Scene)));
            AssertPlayModeOnly(() => SceneLifetimes.Dispose(default(Scene)));
            AssertPlayModeOnly(() => SceneLifetimes.DisposeAll());
        }

        [Test]
        public void SceneLifetime_OfASceneThatWasNeverSaved_IsNamedUntitled()
        {
            Assert.That(SceneBinding.LifetimeNameFor(string.Empty), Is.EqualTo("Untitled"), "an unsaved scene has an empty name");
            Assert.That(SceneBinding.LifetimeNameFor(null), Is.EqualTo("Untitled"));
            Assert.That(SceneBinding.LifetimeNameFor("Main"), Is.EqualTo("Main"));
        }

        [Test]
        public void TheMonoBehaviourTaskForms_ThrowOutsidePlayModeAndStartNothing()
        {
            var ran = 0;

            AssertPlayModeOnly(() => _behaviour.Run(ct =>
            {
                ran++;
                return UniTask.CompletedTask;
            }));
            AssertPlayModeOnly(() => _behaviour.Run(new Counter(), (c, ct) => UniTask.CompletedTask));
            AssertPlayModeOnly(() => _behaviour.After(1f, () => ran++));
            AssertPlayModeOnly(() => _behaviour.After(1f, new Counter(), c => c.Value++));
            AssertPlayModeOnly(() => _behaviour.Every(1f, () => ran++));
            AssertPlayModeOnly(() => _behaviour.Every(1f, new Counter(), c => c.Value++));

            Assert.That(ran, Is.Zero);
        }

        [Test]
        public void TheMonoBehaviourSubscriptionForms_ThrowOutsidePlayMode_AndNeverCallAdd()
        {
            var adds = 0;
            var cancelled = 0;

            AssertPlayModeOnly(() => _behaviour.OnCancel(() => cancelled++));
            AssertPlayModeOnly(() => _behaviour.OnCancel(new Counter(), c => c.Value++));
            AssertPlayModeOnly(() => _behaviour.Subscribe(h => adds++, h => { }, () => { }));

            Assert.That(adds, Is.Zero);
            Assert.That(cancelled, Is.Zero);
        }

        [Test]
        public void AddTo_WithAComponentOrGameObjectOwner_ThrowsOutsidePlayModeAndLeavesTheItemUntouched()
        {
            var component = new DisposeProbe();
            var gameObject = new DisposeProbe();

            AssertPlayModeOnly(() => component.AddTo(_behaviour));
            AssertPlayModeOnly(() => gameObject.AddTo(_go));

            Assert.That(component.DisposeCount, Is.Zero);
            Assert.That(gameObject.DisposeCount, Is.Zero);
        }

        [Test]
        public void OwnedEventSubscribe_WithAComponentOwner_ThrowsOutsidePlayModeAndAddsNothing()
        {
            var evt = new OwnedEvent("edit");
            var typed = new OwnedEvent<int>("edit");

            AssertPlayModeOnly(() => evt.Subscribe(() => { }, _behaviour));
            AssertPlayModeOnly(() => typed.Subscribe(a => { }, _behaviour));

            Assert.That(evt.SubscriberCount, Is.Zero);
            Assert.That(typed.SubscriberCount, Is.Zero);
        }

        [Test]
        public void NullArguments_ThrowArgumentNullExceptionBeforeThePlayModeCheck()
        {
            Assert.Throws<ArgumentNullException>(() => ((MonoBehaviour)null).GetLifetime());
            Assert.Throws<ArgumentNullException>(() => ((GameObject)null).GetLifetime());
            Assert.Throws<ArgumentNullException>(() => ((Component)null).GetActiveLifetime());
            Assert.Throws<ArgumentNullException>(() => _behaviour.GetLifetime((Lifetime)null));
            Assert.Throws<ArgumentNullException>(() => ((MonoBehaviour)null).Run(ct => UniTask.CompletedTask));
        }

        private static void AssertPlayModeOnly(TestDelegate call)
        {
            var exception = Assert.Throws<InvalidOperationException>(call);

            Assert.That(exception.Message, Does.Contain("Play Mode"));
        }
    }
}
