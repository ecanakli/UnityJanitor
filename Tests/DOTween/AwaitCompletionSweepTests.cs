using Cysharp.Threading.Tasks;
using DG.Tweening;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests.TweenBinding
{
    // An await whose callbacks were replaced is ended by the lifetime's entry of its tween. That entry must survive the sweep
    // of the lifetime's list while the await is armed, even though the tween is inactive; otherwise the lifetime's end finds
    // nothing to settle. Fifteen more items bring the entry list to the sweep threshold of 16.
    [TestFixture]
    public sealed class AwaitCompletionSweepTests
    {
        private TweenSession _s;

        [SetUp]
        public void SetUp()
        {
            _s = TweenSession.Begin();

            // Anything armed by an earlier test is gone, as at the start of a session.
            TweenCompletionPromise.ResetSession();
        }

        [TearDown]
        public void TearDown()
        {
            _s.Complete();
        }

        [Test]
        public void ExternalKillWithAReplacedOnKill_ThenASweep_ThenTheLifetimeEnds_CancelsTheAwait()
        {
            var area = _s.Area();
            var kills = 0;
            var tween = _s.NewTween(_s.Box(), 10f);
            var task = tween.AwaitCompletionAsync(area);
            tween.OnKill(() => kills++);

            tween.Kill();

            Assert.That(kills, Is.EqualTo(1));
            TweenAwaits.AssertStatus(task, UniTaskStatus.Pending);
            Assert.That(TweenCompletionPromise.ArmedAwaitCount, Is.EqualTo(1));

            Sweep(area);

            Assert.That(area.EntryCount, Is.EqualTo(16), "the sweep kept the entry of the tween that has a pending await");
            TweenAwaits.AssertStatus(task, UniTaskStatus.Pending);

            area.Cancel();

            TweenAwaits.AssertCancelled(task);
            Assert.That(TweenCompletionPromise.ArmedAwaitCount, Is.Zero, "nothing armed is left behind");
            Assert.That(_s.Errors.Count, Is.Zero);
        }

        [Test]
        public void CompletionWithBothCallbacksReplaced_ThenASweep_ThenTheLifetimeEnds_CancelsTheAwait()
        {
            var area = _s.Area();
            var completes = 0;
            var kills = 0;
            var tween = _s.NewTween(_s.Box(), 10f, 1f);
            var task = tween.AwaitCompletionAsync(area);
            tween.OnComplete(() => completes++);
            tween.OnKill(() => kills++);

            _s.Step(1f);

            Assert.That(completes, Is.EqualTo(1));
            Assert.That(kills, Is.EqualTo(1), "the tween completed and was auto-killed");
            TweenAwaits.AssertStatus(task, UniTaskStatus.Pending);

            Sweep(area);

            Assert.That(area.EntryCount, Is.EqualTo(16));

            area.Dispose();

            TweenAwaits.AssertCancelled(task);
            Assert.That(TweenCompletionPromise.ArmedAwaitCount, Is.Zero);
            Assert.That(_s.Errors.Count, Is.Zero);
        }

        [Test]
        public void KillAllWithAReplacedOnKill_ThenASweep_ThenTheLifetimeEnds_CancelsTheAwait()
        {
            var area = _s.Area();
            var tween = _s.NewTween(_s.Box(), 10f);
            var task = tween.AwaitCompletionAsync(area);
            tween.OnKill(() => { });

            DOTween.KillAll();

            TweenAwaits.AssertStatus(task, UniTaskStatus.Pending);

            Sweep(area);
            area.Cancel();

            TweenAwaits.AssertCancelled(task);
            Assert.That(TweenCompletionPromise.ArmedAwaitCount, Is.Zero);
            Assert.That(_s.Errors.Count, Is.Zero);
        }

        // Guards: the other entries are reclaimed as before.

        [Test]
        public void AddTo_ATweenThatEndsOutside_IsStillReclaimedBySweeps()
        {
            var area = _s.Area();
            var tween = _s.NewTween(_s.Box(), 10f);
            tween.AddTo(area);
            tween.Kill();

            Sweep(area);

            Assert.That(area.EntryCount, Is.EqualTo(15), "the entry of the dead tween was dropped");
        }

        [Test]
        public void Await_ThatCompletedNormally_IsStillReclaimedBySweeps()
        {
            var area = _s.Area();
            var tween = _s.NewTween(_s.Box(), 10f, 1f);
            var task = tween.AwaitCompletionAsync(area);
            _s.Step(1f);
            TweenAwaits.AssertStatus(task, UniTaskStatus.Succeeded);
            TweenAwaits.Consume(task);
            Assert.That(TweenCompletionPromise.ArmedAwaitCount, Is.Zero);

            Sweep(area);

            Assert.That(area.EntryCount, Is.EqualTo(15), "nothing is armed, so the entry of the finished tween was dropped");
        }

        [Test]
        public void Sweep_KeepsOnlyTheEntryWithAPendingAwait_AmongDeadTweenEntries()
        {
            var area = _s.Area();
            var hung = _s.NewTween(_s.Box(), 10f);
            var hungTask = hung.AwaitCompletionAsync(area);
            hung.OnKill(() => { });
            hung.Kill();
            var registered = _s.NewTween(_s.Box(), 10f);
            registered.AddTo(area);
            registered.Kill();
            var finished = _s.NewTween(_s.Box(), 10f, 1f);
            var finishedTask = finished.AwaitCompletionAsync(area);
            _s.Step(1f);
            TweenAwaits.AssertStatus(finishedTask, UniTaskStatus.Succeeded);
            TweenAwaits.Consume(finishedTask);

            // Three tween entries and thirteen items reach the threshold.
            for (var i = 0; i < 13; i++)
            {
                area.OnCancel(() => { });
            }

            Assert.That(area.EntryCount, Is.EqualTo(14), "only the pending await's entry survived next to the 13 items");

            area.Cancel();

            TweenAwaits.AssertCancelled(hungTask);
            Assert.That(TweenCompletionPromise.ArmedAwaitCount, Is.Zero);
        }

        // The session start

        [Test]
        public void ResetSession_DropsAwaitsArmedInAnEarlierSession_AndTheirLateSettlingTouchesNothingOfTheNewOnes()
        {
            var oldArea = _s.Area("Old");
            var newArea = _s.Area("New");
            var oldTween = _s.NewTween(_s.Box(), 10f, 1f);
            var oldTask = oldTween.AwaitCompletionAsync(oldArea);
            Assert.That(TweenCompletionPromise.ArmedAwaitCount, Is.EqualTo(1));

            TweenCompletionPromise.ResetSession();

            Assert.That(TweenCompletionPromise.ArmedAwaitCount, Is.Zero, "the await armed before the session start is dropped");
            var newTween = _s.NewTween(_s.Box(), 10f, 5f);
            var newTask = newTween.AwaitCompletionAsync(newArea);
            Assert.That(TweenCompletionPromise.ArmedAwaitCount, Is.EqualTo(1));

            _s.Step(1f);

            TweenAwaits.AssertStatus(oldTask, UniTaskStatus.Succeeded);
            TweenAwaits.Consume(oldTask);
            Assert.That(TweenCompletionPromise.ArmedAwaitCount, Is.EqualTo(1), "the dropped promise did not remove the new one when it settled");
            TweenAwaits.AssertStatus(newTask, UniTaskStatus.Pending);

            newArea.Cancel();

            TweenAwaits.AssertCancelled(newTask);
            Assert.That(TweenCompletionPromise.ArmedAwaitCount, Is.Zero);
            Assert.That(_s.Errors.Count, Is.Zero);
        }

        private static void Sweep(Lifetime area)
        {
            for (var i = 0; i < 15; i++)
            {
                area.OnCancel(() => { });
            }
        }
    }
}
