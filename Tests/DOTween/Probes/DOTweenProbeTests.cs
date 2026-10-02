using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.Probes
{
    // Pins DOTween behaviour the DOTween assembly relies on.
    public sealed class DOTweenProbeTests
    {
        private readonly List<string> _logs = new List<string>();
        private GameObject _target;

        [SetUp]
        public void SetUp()
        {
            _logs.Clear();
            Application.logMessageReceived += Capture;
            _target = new GameObject("TweenTarget");
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= Capture;
            if (_target != null)
            {
                DOTween.Kill(_target.transform);
                Object.Destroy(_target);
            }
        }

        [UnityTest]
        public IEnumerator EngineProbe_09_SetLink_KillsOnDOTweenUpdateAfterDestroy()
        {
            var linked = new GameObject("Linked");
            Tween tween = linked.transform.DOMoveX(10f, 5f).SetLink(linked);
            yield return null;

            Object.Destroy(linked);
            bool sameFrame = tween.IsActive();
            yield return null;
            bool afterOneFrame = tween.IsActive();
            yield return null;
            bool afterTwoFrames = tween.IsActive();

            Assert.That(sameFrame, Is.True, "still active in the Destroy frame");
            Assert.That(afterOneFrame, Is.False, "dead one frame later");
            Assert.That(afterTwoFrames, Is.False);
        }

        [UnityTest]
        public IEnumerator EngineProbe_10_KillOnNestedTween()
        {
            Tween inner = _target.transform.DOMoveX(10f, 5f);
            Sequence sequence = DOTween.Sequence().Append(inner);
            yield return null;

            inner.Kill();
            bool innerActive = inner.IsActive();
            yield return null;
            bool sequenceActive = sequence.IsActive();
            sequence.Kill();

            Assert.That(innerActive, Is.True, "Kill on a nested tween is ignored: the nested tween stays active");
            Assert.That(sequenceActive, Is.True, "and the parent Sequence keeps playing");
            Assert.That(_logs, Is.Empty, "DOTween says nothing about the ignored Kill: " + string.Join(" | ", _logs));
        }

        [UnityTest]
        public IEnumerator EngineProbe_11_Recycling_ReusesKilledTween_SetRecyclableFalsePrevents()
        {
            bool previous = DOTween.defaultRecyclable;
            DOTween.defaultRecyclable = true;

            // Cached tweens left by other tests would make the reuse order depend on them.
            DOTween.ClearCachedTweens();
            try
            {
                Tween recyclable = _target.transform.DOMoveX(1f, 5f);
                recyclable.Kill();
                yield return null;
                Tween next = _target.transform.DOMoveY(1f, 5f);
                bool reused = ReferenceEquals(recyclable, next);
                bool staleActive = recyclable.IsActive();

                Tween nonRecyclable = _target.transform.DOMoveZ(1f, 5f).SetRecyclable(false);
                nonRecyclable.Kill();
                yield return null;
                Tween after = _target.transform.DOMoveX(2f, 5f);
                bool nonRecyclableReused = ReferenceEquals(nonRecyclable, after);

                Assert.That(reused, Is.True, "a killed recyclable tween is handed out again");
                Assert.That(staleActive, Is.True, "and a stale reference to it reports IsActive() == true");
                Assert.That(nonRecyclableReused, Is.False, "SetRecyclable(false) prevents the reuse");
            }
            finally
            {
                DOTween.defaultRecyclable = previous;
                DOTween.ClearCachedTweens();
            }
        }

        private void Capture(string condition, string stackTrace, LogType type)
        {
            if (condition.Contains("DOTween"))
            {
                _logs.Add(type + ": " + condition);
            }
        }
    }
}
