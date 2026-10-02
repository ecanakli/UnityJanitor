using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests
{
    // The default delay provider (UniTask.Delay on the player loop) end to end. EditMode tests drive a manual clock instead.
    // Every wait is bounded by a real-time deadline, so a broken timer fails the test instead of hanging it.
    [TestFixture]
    public sealed class LifetimeTimerPlayModeTests
    {
        private const float DeadlineSeconds = 10f;

        private TestScope _t;
        private Lifetime _area;

        [SetUp]
        public void SetUp()
        {
            _t = new TestScope();
            _area = _t.App.CreateChild("area");
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            _t.Complete();
        }

        [UnityTest]
        public IEnumerator After_RealDelay_FiresOnceAfterTheDelay()
        {
            var fired = 0;
            _area.After(0.2f, () => fired++);
            Assert.That(fired, Is.Zero, "it must not fire in place");
            Assert.That(_area.EntryCount, Is.EqualTo(1));

            var deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (fired == 0 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(fired, Is.EqualTo(1), "the default provider must complete the delay");
            for (var i = 0; i < 10; i++)
            {
                yield return null;
            }

            Assert.That(fired, Is.EqualTo(1), "After fires once");
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator After_CancelledBeforeTheRealDelayElapses_NeverFires()
        {
            var fired = 0;
            _area.After(0.2f, () => fired++);
            yield return null;

            _area.Cancel();
            var until = Time.realtimeSinceStartup + 0.6f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            Assert.That(fired, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero);
            Assert.That(_t.Errors.Count, Is.Zero, "cancellation is silent");
        }

        [UnityTest]
        public IEnumerator Every_IgnoreTimeScaleWithATimeScaleOfZero_KeepsFiringUntilCancelled()
        {
            Time.timeScale = 0f;
            var scaled = 0;
            var unscaled = 0;
            _area.Every(0.05f, () => scaled++);
            _area.Every(0.05f, () => unscaled++, true);

            var deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (unscaled < 3 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(unscaled, Is.GreaterThanOrEqualTo(3), "an unscaled timer must run while the time scale is zero");
            Assert.That(scaled, Is.Zero, "a scaled timer must not run while the time scale is zero");

            _area.Cancel();
            yield return null;
            yield return null;
            var afterCancel = unscaled;
            for (var i = 0; i < 10; i++)
            {
                yield return null;
            }

            Assert.That(unscaled, Is.EqualTo(afterCancel), "a cancelled timer must stop firing");
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Run_RealAwaitOnTheLifetimeToken_StopsSilentlyAtTheNextAwaitWhenCancelled()
        {
            var stage = 0;
            _area.Run(async ct =>
            {
                stage = 1;
                await UniTask.Delay(TimeSpan.FromSeconds(60), cancellationToken: ct);
                stage = 2;
            });
            yield return null;
            Assert.That(stage, Is.EqualTo(1));
            Assert.That(_area.EntryCount, Is.EqualTo(1));

            _area.Cancel();
            Assert.That(_area.EntryCount, Is.Zero, "the drain removes the entry at once");
            yield return null;
            yield return null;

            Assert.That(stage, Is.EqualTo(1), "the code after a token-aware await must not run");
            Assert.That(_t.Errors.Count, Is.Zero, "the cancellation is silent");
        }
    }
}
