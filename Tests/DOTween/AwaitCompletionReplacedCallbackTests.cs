using DG.Tweening;
using NUnit.Framework;
using Cysharp.Threading.Tasks;

namespace Ecanakli.Janitor.Tests.TweenBinding
{
    // The await chains onComplete and onKill. A callback the user sets after the call replaces the await's own,
    // and the await must still end when its lifetime does.
    [TestFixture]
    public sealed class AwaitCompletionReplacedCallbackTests
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

        // onKill replaced after the call

        [Test]
        public void Await_OnKillReplacedAfterTheCall_ALifetimeCancelStillCancelsTheTask()
        {
            var area = _s.Area();
            var kills = 0;
            var tween = _s.NewTween(_s.Box(), 10f);
            var task = tween.AwaitCompletionAsync(area);
            tween.OnKill(() => kills++);
            TweenAwaits.AssertStatus(task, UniTaskStatus.Pending);

            area.Cancel();

            Assert.That(tween.IsActive(), Is.False);
            Assert.That(kills, Is.EqualTo(1), "the user's onKill ran once");
            TweenAwaits.AssertCancelled(task);
        }

        [Test]
        public void Await_OnKillReplacedAfterTheCall_ALifetimeDisposeStillCancelsTheTask()
        {
            var area = _s.Area();
            var tween = _s.NewTween(_s.Box(), 10f);
            var task = tween.AwaitCompletionAsync(area);
            tween.OnKill(() => { });

            area.Dispose();

            TweenAwaits.AssertCancelled(task);
        }

        [Test]
        public void Await_OnKillReplacedAfterTheCall_AnAsyncWaiterGetsTheCancellation()
        {
            var area = _s.Area();
            var log = new CallLog();
            var tween = _s.NewTween(_s.Box(), 10f);
            var waiting = TweenAwaits.AwaitLogged(tween, area, log);
            tween.OnKill(() => { });

            area.Cancel();

            Assert.That(log.ToArray(), Is.EqualTo(new[] { "canceled" }), "the waiter is not left parked forever");
            TweenAwaits.AssertStatus(waiting, UniTaskStatus.Succeeded);
            TweenAwaits.Consume(waiting);
        }

        [Test]
        public void Await_OnKillReplacedAfterTheCall_AnExternalKillLeavesTheTaskPendingUntilTheLifetimeEnds()
        {
            var area = _s.Area();
            var kills = 0;
            var tween = _s.NewTween(_s.Box(), 10f);
            var task = tween.AwaitCompletionAsync(area);
            tween.OnKill(() => kills++);

            tween.Kill();

            Assert.That(kills, Is.EqualTo(1));
            TweenAwaits.AssertStatus(task, UniTaskStatus.Pending);

            area.Cancel();

            TweenAwaits.AssertCancelled(task);
        }

        // onComplete replaced after the call

        // The documented contract: callbacks go before the call. An onComplete set afterwards replaces the await's, so the
        // completion is seen only through the auto kill, which ends the await as cancelled.
        [Test]
        public void Await_OnCompleteReplacedAfterTheCall_ANormalCompletionEndsTheTaskAsCancelled()
        {
            var area = _s.Area();
            var completes = 0;
            var tween = _s.NewTween(_s.Box(), 10f, 1f);
            var task = tween.AwaitCompletionAsync(area);
            tween.OnComplete(() => completes++);

            _s.Step(1f);

            Assert.That(completes, Is.EqualTo(1), "the user's callback ran");
            TweenAwaits.AssertCancelled(task);
            Assert.That(_s.Errors.Count, Is.Zero);
        }

        [Test]
        public void Await_OnCompleteReplacedAfterTheCall_AKillBeforeTheCompletionStillCancelsTheTask()
        {
            var area = _s.Area();
            var tween = _s.NewTween(_s.Box(), 10f, 1f);
            var task = tween.AwaitCompletionAsync(area);
            tween.OnComplete(() => { });
            _s.Step(0.4f);

            tween.Kill();

            TweenAwaits.AssertCancelled(task);
        }

        [Test]
        public void Await_OnCompleteReplacedAfterTheCall_ALifetimeCancelAfterTheCompletionCancelsTheTask()
        {
            var area = _s.Area();
            var tween = _s.NewTween(_s.Box(), 10f, 1f);
            tween.SetAutoKill(false);
            var task = tween.AwaitCompletionAsync(area);
            tween.OnComplete(() => { });
            _s.Step(1f);
            TweenAwaits.AssertStatus(task, UniTaskStatus.Pending);

            area.Cancel();

            TweenAwaits.AssertCancelled(task);
        }

        // The engine fact behind the contract above (measured on 6000.3): by the time onKill runs, a finished and auto-killed
        // tween is already inactive and no longer reports itself complete, so a completion cannot be told from a kill there.
        [Test]
        public void OnKill_OfATweenThatFinishedAndWasAutoKilled_SeesItInactiveAndNotComplete()
        {
            var seen = string.Empty;
            var tween = _s.NewTween(_s.Box(), 10f, 1f);
            tween.OnKill(() => seen = tween.IsActive() + "/" + tween.IsComplete());

            _s.Step(1f);

            Assert.That(seen, Is.EqualTo("False/False"));
        }
    }
}
