using System.Collections;
using DG.Tweening;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.TweenBinding
{
    // The flaw to avoid is a handle that trusts IsActive() under DOTween recycling. A killed
    // recyclable tween is reused for the next one, and a stale reference then points at a stranger. AddTo makes the
    // tween non-recyclable, so the tween is its own handle. These tests run with DOTween.defaultRecyclable = true.
    [TestFixture]
    public sealed class TweenRecyclingTests
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

        [UnityTest]
        public IEnumerator Control_AnUnregisteredRecyclableTween_IsReusedAfterItsKill()
        {
            _s.EnableRecycling();
            var first = _s.NewTween(_s.Box());
            first.Kill();
            yield return null;

            var second = _s.NewTween(_s.Box());

            Assert.That(ReferenceEquals(first, second), Is.True, "premise: DOTween hands the killed object to the next tween");
            Assert.That(first.IsActive(), Is.True, "the stale reference reports the stranger's state as its own");
        }

        [UnityTest]
        public IEnumerator AddTo_AKilledRegisteredTween_IsNeverReusedByANewTween()
        {
            _s.EnableRecycling();
            var area = _s.Area();
            var registered = _s.NewTween(_s.Box());
            registered.AddTo(area);
            registered.Kill();
            yield return null;

            var next = _s.NewTween(_s.Box());
            var afterThat = _s.NewTween(_s.Box());

            Assert.That(ReferenceEquals(registered, next), Is.False);
            Assert.That(ReferenceEquals(registered, afterThat), Is.False);
            Assert.That(registered.IsActive(), Is.False, "the held reference stays dead");
            Assert.That(next.IsActive(), Is.True);
        }

        [UnityTest]
        public IEnumerator Cancel_OverAKilledRegisteredTween_DoesNotKillATweenCreatedAfterwards()
        {
            _s.EnableRecycling();
            var area = _s.Area();
            var registered = _s.NewTween(_s.Box());
            registered.AddTo(area);
            registered.Kill();
            yield return null;
            var box = _s.Box();
            var stranger = _s.NewTween(box, 10f);

            area.Cancel();

            Assert.That(stranger.IsActive(), Is.True, "cancelling the old owner must not touch a tween that took the slot");
            _s.Step(1f);
            Assert.That(box.Value, Is.EqualTo(10f).Within(0.0001f), "and it still runs to its end");
        }

        [UnityTest]
        public IEnumerator Cancel_OverARegisteredTweenThatCompleted_DoesNotKillATweenCreatedAfterwards()
        {
            _s.EnableRecycling();
            var area = _s.Area();
            var registered = _s.NewTween(_s.Box(), 10f, 1f);
            registered.AddTo(area);
            _s.Step(1f);
            yield return null;
            var stranger = _s.NewTween(_s.Box(), 10f);

            area.Cancel();

            Assert.That(ReferenceEquals(registered, stranger), Is.False);
            Assert.That(stranger.IsActive(), Is.True);
        }

        [UnityTest]
        public IEnumerator Cancel_OnOneLifetime_DoesNotKillATweenRegisteredOnAnotherAfterTheFirstTweenDied()
        {
            _s.EnableRecycling();
            var first = _s.Area("First");
            var second = _s.Area("Second");
            var dead = _s.NewTween(_s.Box());
            dead.AddTo(first);
            dead.Kill();
            yield return null;
            var living = _s.NewTween(_s.Box());
            living.AddTo(second);

            first.Cancel();

            Assert.That(living.IsActive(), Is.True);
            second.Cancel();
            Assert.That(living.IsActive(), Is.False);
        }

        [UnityTest]
        public IEnumerator AwaitCompletionAsync_Lifetime_MakesTheTweenNonRecyclable()
        {
            _s.EnableRecycling();
            var area = _s.Area();
            var awaited = _s.NewTween(_s.Box());
            var task = awaited.AwaitCompletionAsync(area);
            awaited.Kill();
            yield return null;

            var next = _s.NewTween(_s.Box());

            Assert.That(ReferenceEquals(awaited, next), Is.False);
            Assert.That(awaited.IsActive(), Is.False);
            TweenAwaits.AssertCancelled(task);
        }

        [UnityTest]
        public IEnumerator AwaitCompletionAsync_Token_MakesTheTweenNonRecyclable()
        {
            _s.EnableRecycling();
            var source = _s.NewSource();
            var awaited = _s.NewTween(_s.Box());
            var task = awaited.AwaitCompletionAsync(source.Token);
            awaited.Kill();
            yield return null;

            var next = _s.NewTween(_s.Box());

            Assert.That(ReferenceEquals(awaited, next), Is.False);
            Assert.That(awaited.IsActive(), Is.False);
            TweenAwaits.AssertCancelled(task);
        }
    }
}
