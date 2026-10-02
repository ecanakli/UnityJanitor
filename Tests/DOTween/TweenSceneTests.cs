using System.Collections;
using System.Text.RegularExpressions;
using DG.Tweening;
using NUnit.Framework;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.TweenBinding
{
    // A scene call ends the tweens of the scene's objects
    // before any OnDestroy runs. The scene is created additively, so the test runner's own scene is not involved.
    // Only [UnityTest] methods live here, so [UnityTearDown] is never paired with a plain [Test].
    [TestFixture]
    public sealed class TweenSceneTests
    {
        private TweenSession _s;

        [SetUp]
        public void SetUp()
        {
            _s = TweenSession.Begin();
        }

        [UnityTearDown]
        public IEnumerator TearDownAsync()
        {
            yield return _s.CompleteAsync();
        }

        [UnityTest]
        public IEnumerator SceneDispose_KillsTheTweenOfAnObjectInTheSceneWhileTheObjectIsStillAlive()
        {
            var scene = _s.NewScene();
            var host = _s.NewHost();
            SceneManager.MoveGameObjectToScene(host.gameObject, scene);
            var destroyed = false;
            host.DestroyProbe = () => destroyed = true;
            var tween = _s.NewTween(_s.Box(), 10f);
            tween.AddTo(host);

            SceneLifetimes.Dispose(scene);

            Assert.That(tween.IsActive(), Is.False, "the scene call ends the tween");
            Assert.That(destroyed, Is.False, "and the object has not been destroyed yet");
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisposeAll_KillsTheTweenOfAnObjectInTheSceneWhileTheObjectIsStillAlive()
        {
            var scene = _s.NewScene();
            var host = _s.NewHost();
            SceneManager.MoveGameObjectToScene(host.gameObject, scene);
            var destroyed = false;
            host.DestroyProbe = () => destroyed = true;
            var tween = _s.NewTween(_s.Box(), 10f);
            var gameObjectTween = _s.NewTween(_s.Box(), 10f);
            tween.AddTo(host);
            gameObjectTween.AddTo(host.gameObject);

            SceneLifetimes.DisposeAll();

            Assert.That(tween.IsActive(), Is.False);
            Assert.That(gameObjectTween.IsActive(), Is.False);
            Assert.That(destroyed, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SceneDispose_CompleteMode_LandsTheEndValueBeforeTheObjectIsDestroyed()
        {
            var scene = _s.NewScene();
            var host = _s.NewHost();
            SceneManager.MoveGameObjectToScene(host.gameObject, scene);
            var box = _s.Box();
            var completes = 0;
            var tween = _s.NewTween(box, 10f);
            tween.OnComplete(() => completes++);
            tween.AddTo(host, TweenCancelMode.Complete);
            _s.Step(0.4f);

            SceneLifetimes.Dispose(scene);

            Assert.That(tween.IsActive(), Is.False);
            Assert.That(box.Value, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(completes, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator AddTo_AnObjectOfADisposedScene_KillsTheTweenEvenInCompleteMode()
        {
            var scene = _s.NewScene();
            var host = _s.NewHost();
            SceneManager.MoveGameObjectToScene(host.gameObject, scene);
            SceneLifetimes.Dispose(scene);
            var completes = 0;
            var tween = _s.NewTween(_s.Box(), 10f);
            tween.OnComplete(() => completes++);
            LogAssert.Expect(UnityEngine.LogType.Warning, new Regex("JANITOR109"));

            tween.AddTo(host, TweenCancelMode.Complete);

            Assert.That(tween.IsActive(), Is.False, "the owner's scene lifetime is disposed, so the lifetime handed out is the disposed one");
            Assert.That(completes, Is.Zero);
            yield return null;
        }
    }
}
