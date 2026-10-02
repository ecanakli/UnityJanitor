using Cysharp.Threading.Tasks;
using DG.Tweening;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests.TweenBinding
{
    // The user's own onComplete or onKill ends the await from inside the callback. The continuation then starts another
    // await, which rents the promise that was just returned, before the first callback has returned. The first callback must
    // not settle the second await.
    [TestFixture]
    public sealed class AwaitCompletionReentrancyTests
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
        public void OnComplete_WhoseOriginalEndsTheAwaitThroughTheLifetime_DoesNotSettleTheNextAwaitOfTheRecycledPromise()
        {
            var firstArea = _s.Area("First");
            var secondArea = _s.Area("Second");
            var log = new CallLog();
            var first = _s.NewTween(_s.Box(), 10f, 1f);
            var second = _s.NewTween(_s.Box(), 20f, 5f);
            first.OnComplete(() => firstArea.Cancel());
            var waiting = TweenAwaits.AwaitThenAwait(first, firstArea, second, secondArea, log);

            _s.Step(1f);

            Assert.That(log.ToArray(), Is.EqualTo(new[] { "first canceled" }), "the second await is still running");
            TweenAwaits.AssertStatus(waiting, UniTaskStatus.Pending);

            _s.Step(5f);

            Assert.That(log.ToArray(), Is.EqualTo(new[] { "first canceled", "second completed" }));
            TweenAwaits.AssertStatus(waiting, UniTaskStatus.Succeeded);
            TweenAwaits.Consume(waiting);
        }

        [Test]
        public void OnComplete_WhoseOriginalEndsTheAwaitThroughTheToken_DoesNotSettleTheNextAwaitOfTheRecycledPromise()
        {
            var source = _s.NewSource();
            var secondArea = _s.Area("Second");
            var log = new CallLog();
            var first = _s.NewTween(_s.Box(), 10f, 1f);
            var second = _s.NewTween(_s.Box(), 20f, 5f);
            first.OnComplete(() => source.Cancel());
            var waiting = TweenAwaits.AwaitThenAwait(first, source.Token, second, secondArea, log);

            _s.Step(1f);

            Assert.That(log.ToArray(), Is.EqualTo(new[] { "first canceled" }), "the second await is still running");
            TweenAwaits.AssertStatus(waiting, UniTaskStatus.Pending);

            _s.Step(5f);

            Assert.That(log.ToArray(), Is.EqualTo(new[] { "first canceled", "second completed" }));
            TweenAwaits.AssertStatus(waiting, UniTaskStatus.Succeeded);
            TweenAwaits.Consume(waiting);
        }

        // A kill made inside an update loop only marks the tween; DOTween runs its onKill in that pass or the next, with the
        // tween already inactive. A tween updated earlier in the pass kills the first one that way, so no nested kill happens.
        [Test]
        public void OnKill_WhoseOriginalEndsTheAwaitThroughTheLifetime_DoesNotSettleTheNextAwaitOfTheRecycledPromise()
        {
            var firstArea = _s.Area("First");
            var secondArea = _s.Area("Second");
            var log = new CallLog();
            Tween first = null;
            var killer = KillerOf(() => first);
            first = _s.NewTween(_s.Box(), 10f, 100f);
            var second = _s.NewTween(_s.Box(), 20f, 5f);
            first.OnKill(() => firstArea.Cancel());
            var waiting = TweenAwaits.AwaitThenAwait(first, firstArea, second, secondArea, log);

            StepUntilKilled(log);

            Assert.That(killer.IsActive(), Is.True, "premise: the killer is still running");
            Assert.That(log.ToArray(), Is.EqualTo(new[] { "first canceled" }), "the second await is still running");
            TweenAwaits.AssertStatus(waiting, UniTaskStatus.Pending);

            _s.Step(5f);

            Assert.That(log.ToArray(), Is.EqualTo(new[] { "first canceled", "second completed" }));
            TweenAwaits.AssertStatus(waiting, UniTaskStatus.Succeeded);
            TweenAwaits.Consume(waiting);
        }

        [Test]
        public void OnKill_WhoseOriginalEndsTheAwaitThroughTheToken_DoesNotSettleTheNextAwaitOfTheRecycledPromise()
        {
            var source = _s.NewSource();
            var secondArea = _s.Area("Second");
            var log = new CallLog();
            Tween first = null;
            var killer = KillerOf(() => first);
            first = _s.NewTween(_s.Box(), 10f, 100f);
            var second = _s.NewTween(_s.Box(), 20f, 5f);
            first.OnKill(() => source.Cancel());
            var waiting = TweenAwaits.AwaitThenAwait(first, source.Token, second, secondArea, log);

            StepUntilKilled(log);

            Assert.That(killer.IsActive(), Is.True, "premise: the killer is still running");
            Assert.That(log.ToArray(), Is.EqualTo(new[] { "first canceled" }), "the second await is still running");
            TweenAwaits.AssertStatus(waiting, UniTaskStatus.Pending);

            _s.Step(5f);

            Assert.That(log.ToArray(), Is.EqualTo(new[] { "first canceled", "second completed" }));
            TweenAwaits.AssertStatus(waiting, UniTaskStatus.Succeeded);
            TweenAwaits.Consume(waiting);
        }

        // A long tween created before the one it kills, so the update loop reaches it first; it kills once, from its onUpdate.
        private Tween KillerOf(System.Func<Tween> victim)
        {
            var killed = false;
            var killer = _s.NewTween(_s.Box(), 10f, 1000f);
            killer.OnUpdate(() =>
            {
                if (killed)
                {
                    return;
                }

                killed = true;
                victim().Kill();
            });
            return killer;
        }

        // One pass, and a second one if DOTween despawns the marked tween only in the next pass.
        private void StepUntilKilled(CallLog log)
        {
            _s.Step(0.5f);
            if (log.Count == 0)
            {
                _s.Step(0.01f);
            }
        }
    }
}
