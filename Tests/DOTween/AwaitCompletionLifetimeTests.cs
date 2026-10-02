using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using NUnit.Framework;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.TweenBinding
{
    // AwaitCompletionAsync(lifetime) registers the tween, then awaits it.
    // Anything that ends the tween early (Cancel, Dispose, an external Kill, SetLink plus destroy) cancels the await.
    [TestFixture]
    public sealed class AwaitCompletionLifetimeTests
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

        // Completion

        [Test]
        public void Await_TweenCompletes_TaskSucceedsAndTheValueLands()
        {
            var area = _s.Area();
            var box = _s.Box();
            var tween = _s.NewTween(box, 10f);

            var task = tween.AwaitCompletionAsync(area);
            TweenAwaits.AssertStatus(task, UniTaskStatus.Pending);
            _s.Step(0.5f);
            TweenAwaits.AssertStatus(task, UniTaskStatus.Pending);
            Assert.That(box.Value, Is.LessThan(10f), "premise: still midway");
            _s.Step(0.5f);

            TweenAwaits.AssertStatus(task, UniTaskStatus.Succeeded);
            Assert.That(box.Value, Is.EqualTo(10f).Within(0.0001f));
            TweenAwaits.Consume(task);
        }

        [Test]
        public void Await_AsyncWaiter_ResumesInlineWhenTheTweenCompletes()
        {
            var area = _s.Area();
            var log = new CallLog();
            var tween = _s.NewTween(_s.Box(), 10f);
            var waiting = TweenAwaits.AwaitLogged(tween, area, log);
            Assert.That(log.Count, Is.Zero, "premise: the waiter is parked on the tween");

            _s.Step(1f);

            Assert.That(log.ToArray(), Is.EqualTo(new[] { "completed" }));
            TweenAwaits.AssertStatus(waiting, UniTaskStatus.Succeeded);
            TweenAwaits.Consume(waiting);
        }

        [Test]
        public void Await_ARootSequence_CompletesWhenTheWholeSequenceDoes()
        {
            var area = _s.Area();
            var first = _s.Box();
            var second = _s.Box();
            var sequence = _s.NewSequence(_s.NewNestedTween(first, 10f, 1f), _s.NewNestedTween(second, 20f, 1f));

            var task = sequence.AwaitCompletionAsync(area);
            _s.Step(1f);
            TweenAwaits.AssertStatus(task, UniTaskStatus.Pending);
            _s.Step(1.1f);

            TweenAwaits.AssertStatus(task, UniTaskStatus.Succeeded);
            Assert.That(second.Value, Is.EqualTo(20f).Within(0.0001f));
            TweenAwaits.Consume(task);
        }

        [Test]
        public void Await_ExternalKillWithComplete_CountsAsACompletion()
        {
            var area = _s.Area();
            var box = _s.Box();
            var tween = _s.NewTween(box, 10f);
            var task = tween.AwaitCompletionAsync(area);

            tween.Kill(true);

            TweenAwaits.AssertStatus(task, UniTaskStatus.Succeeded);
            Assert.That(box.Value, Is.EqualTo(10f).Within(0.0001f));
            TweenAwaits.Consume(task);
        }

        // Cancellation

        [Test]
        public void Await_LifetimeCancel_KillsTheTweenAndCancelsTheTask()
        {
            var area = _s.Area();
            var box = _s.Box();
            var tween = _s.NewTween(box, 10f);
            var task = tween.AwaitCompletionAsync(area);
            _s.Step(0.4f);
            var reached = box.Value;

            area.Cancel();

            Assert.That(tween.IsActive(), Is.False);
            Assert.That(box.Value, Is.EqualTo(reached));
            TweenAwaits.AssertCancelled(task);
        }

        [Test]
        public void Await_LifetimeDispose_CancelsTheTask()
        {
            var area = _s.Area();
            var tween = _s.NewTween(_s.Box(), 10f);
            var task = tween.AwaitCompletionAsync(area);

            area.Dispose();

            Assert.That(tween.IsActive(), Is.False);
            TweenAwaits.AssertCancelled(task);
        }

        [Test]
        public void Await_CancelOnTheParent_CancelsTheTaskOfAChildArea()
        {
            var parent = _s.Area("Parent");
            var child = parent.CreateChild("Child");
            var tween = _s.NewTween(_s.Box(), 10f);
            var task = tween.AwaitCompletionAsync(child);

            parent.Cancel();

            TweenAwaits.AssertCancelled(task);
        }

        [Test]
        public void Await_AsyncWaiter_CancelThrowsAtTheAwaitAndNothingAfterItRuns()
        {
            var area = _s.Area();
            var log = new CallLog();
            var tween = _s.NewTween(_s.Box(), 10f);
            var waiting = TweenAwaits.AwaitLogged(tween, area, log);

            area.Cancel();

            Assert.That(log.ToArray(), Is.EqualTo(new[] { "canceled" }), "the continuation gets the cancellation, never a completion");
            TweenAwaits.AssertStatus(waiting, UniTaskStatus.Succeeded);
            TweenAwaits.Consume(waiting);
        }

        [Test]
        public void Await_ExternalKill_CancelsTheTask()
        {
            var area = _s.Area();
            var tween = _s.NewTween(_s.Box(), 10f);
            var task = tween.AwaitCompletionAsync(area);

            tween.Kill();

            TweenAwaits.AssertCancelled(task);
        }

        [UnityTest]
        public IEnumerator Await_SetLinkAndDestroy_CancelsTheTask()
        {
            var area = _s.Area();
            var go = _s.NewObject();
            var linked = _s.NewLinkedTween(go);
            var task = linked.AwaitCompletionAsync(area);

            Object.Destroy(go);
            for (var i = 0; i < TweenSession.MaxFrames && task.Status == UniTaskStatus.Pending; i++)
            {
                yield return null;
            }

            TweenAwaits.AssertCancelled(task);
            Assert.That(linked.IsActive(), Is.False);
        }

        [UnityTest]
        public IEnumerator Await_OwnerComponentDestroyed_CancelsTheTask()
        {
            var host = _s.NewHost();
            var tween = _s.NewTween(_s.Box(), 10f);
            var task = tween.AwaitCompletionAsync(host.GetLifetime());

            Object.Destroy(host.gameObject);
            yield return TweenSession.Frames(2);

            TweenAwaits.AssertCancelled(task);
        }

        // Loops

        [Test]
        public void Await_InfiniteLoop_NeverCompletesByItselfAndACancelEndsItWithACancellation()
        {
            var area = _s.Area();
            var completes = 0;
            var tween = _s.NewLoop(_s.Box(), 10f, 1f);
            tween.OnComplete(() => completes++);
            var task = tween.AwaitCompletionAsync(area);

            for (var i = 0; i < 20; i++)
            {
                _s.Step(1f);
            }

            TweenAwaits.AssertStatus(task, UniTaskStatus.Pending);
            Assert.That(completes, Is.Zero, "an infinite loop has no completion");

            area.Cancel();

            TweenAwaits.AssertCancelled(task);
        }

        [Test]
        public void Await_InfiniteLoop_AnExternalKillEndsItWithACancellation()
        {
            var area = _s.Area();
            var tween = _s.NewLoop(_s.Box(), 10f, 1f);
            var task = tween.AwaitCompletionAsync(area);
            _s.Step(3f);

            tween.Kill();

            TweenAwaits.AssertCancelled(task);
        }

        // A tween DOTween refuses to kill

        [Test]
        public void Await_ANestedTween_CancelIsIgnoredByDOTween_SoTheTaskIsCancelledAtOnceAndTheTweenKeepsRunning()
        {
            var area = _s.Area();
            var first = _s.NewNestedTween(_s.Box(), 10f, 1f);
            var second = _s.NewNestedTween(_s.Box(), 20f, 1f);
            _s.NewSequence(first, second);
            var task = first.AwaitCompletionAsync(area);

            area.Cancel();

            Assert.That(first.IsActive(), Is.True, "premise: DOTween ignores Kill on a nested tween");
            TweenAwaits.AssertCancelled(task);

            _s.Step(1.2f);

            Assert.That(_s.Errors.Count, Is.Zero, "the nested tween finishing later touches nothing of the ended await");
        }

        // An owner that has already ended, and a tween that has already ended

        [Test]
        public void Await_ADisposedLifetime_KillsTheTweenAndReturnsACancelledTask()
        {
            var area = _s.Area();
            var tween = _s.NewTween(_s.Box(), 10f);
            area.Dispose();

            var task = tween.AwaitCompletionAsync(area);

            Assert.That(tween.IsActive(), Is.False);
            TweenAwaits.AssertCancelled(task);
            Assert.That(area.EntryCount, Is.Zero);
        }

        [Test]
        public void Await_ALifetimeThatIsCancelling_ReturnsACancelledTaskAndKillsTheTween()
        {
            var area = _s.Area();
            var tween = _s.NewTween(_s.Box(), 10f);
            var status = UniTaskStatus.Pending;
            var activeInside = true;
            area.OnCancel(() =>
            {
                status = tween.AwaitCompletionAsync(area).Status;
                activeInside = tween.IsActive();
            });

            area.Cancel();

            Assert.That(status, Is.EqualTo(UniTaskStatus.Canceled));
            Assert.That(activeInside, Is.False);
        }

        [Test]
        public void Await_AnInactiveTween_Completes()
        {
            var area = _s.Area();
            var tween = _s.NewTween(_s.Box(), 10f);
            tween.Kill();

            var task = tween.AwaitCompletionAsync(area);

            TweenAwaits.AssertStatus(task, UniTaskStatus.Succeeded);
            Assert.That(area.EntryCount, Is.Zero, "nothing is registered for a tween that is gone");
            TweenAwaits.Consume(task);
        }

        [Test]
        public void Await_AnInactiveTweenOnADisposedLifetime_IsCancelledNotCompleted()
        {
            var area = _s.Area();
            var tween = _s.NewTween(_s.Box(), 10f);
            tween.Kill();
            area.Dispose();

            var task = tween.AwaitCompletionAsync(area);

            TweenAwaits.AssertCancelled(task);
        }

        // Complete mode beside an await

        [Test]
        public void Await_ThenAddToComplete_CancelLandsTheEndValueAndStillCancelsTheAwait()
        {
            var area = _s.Area();
            var box = _s.Box();
            var completes = 0;
            var tween = _s.NewTween(box, 10f);
            tween.OnComplete(() => completes++);
            var task = tween.AwaitCompletionAsync(area);
            tween.AddTo(area, TweenCancelMode.Complete);
            _s.Step(0.4f);

            area.Cancel();

            Assert.That(box.Value, Is.EqualTo(10f).Within(0.0001f), "the newer Complete entry ended first and landed the visual");
            Assert.That(completes, Is.EqualTo(1));
            TweenAwaits.AssertCancelled(task);
        }

        [Test]
        public void Await_AfterAddToComplete_TheNewerAwaitEntryKillsFirstSoTheValueDoesNotLand()
        {
            var area = _s.Area();
            var box = _s.Box();
            var completes = 0;
            var tween = _s.NewTween(box, 10f);
            tween.OnComplete(() => completes++);
            tween.AddTo(area, TweenCancelMode.Complete);
            var task = tween.AwaitCompletionAsync(area);
            _s.Step(0.4f);

            area.Cancel();

            Assert.That(box.Value, Is.LessThan(10f), "items end newest first, so the await's kill came first");
            Assert.That(completes, Is.Zero);
            TweenAwaits.AssertCancelled(task);
        }

        // The user's callbacks

        [Test]
        public void Await_UsersCallbacks_RunExactlyOnceAndAreRestoredAfterTheCompletion()
        {
            var area = _s.Area();
            var completes = 0;
            var kills = 0;
            TweenCallback onComplete = () => completes++;
            TweenCallback onKill = () => kills++;
            var tween = _s.NewTween(_s.Box(), 10f);
            tween.SetAutoKill(false);
            tween.OnComplete(onComplete);
            tween.OnKill(onKill);

            var task = tween.AwaitCompletionAsync(area);
            Assert.That(tween.onComplete, Is.Not.SameAs(onComplete), "premise: the await chains onComplete");
            Assert.That(tween.onKill, Is.Not.SameAs(onKill), "premise: and onKill");

            _s.Step(1f);
            TweenAwaits.Consume(task);

            Assert.That(completes, Is.EqualTo(1));
            Assert.That(kills, Is.Zero, "the tween is still alive");
            Assert.That(tween.onComplete, Is.SameAs(onComplete), "both fields are restored once the await is consumed");
            Assert.That(tween.onKill, Is.SameAs(onKill));

            tween.Kill();

            Assert.That(kills, Is.EqualTo(1), "after the restore the user's onKill runs directly, once");
            Assert.That(completes, Is.EqualTo(1));
        }

        [Test]
        public void Await_UsersCallbacks_AfterACancel_OnKillRanOnceAndOnCompleteNever()
        {
            var area = _s.Area();
            var completes = 0;
            var kills = 0;
            var tween = _s.NewTween(_s.Box(), 10f);
            tween.OnComplete(() => completes++);
            tween.OnKill(() => kills++);
            var task = tween.AwaitCompletionAsync(area);

            area.Cancel();

            Assert.That(kills, Is.EqualTo(1));
            Assert.That(completes, Is.Zero);
            TweenAwaits.AssertCancelled(task);
            Assert.That(kills, Is.EqualTo(1), "consuming the task runs nothing again");
        }

        [Test]
        public void Await_AsyncWaiter_UsersCallbacksAreRestoredByTheTimeTheWaiterHasResumed()
        {
            var area = _s.Area();
            var completes = 0;
            var kills = 0;
            TweenCallback onComplete = () => completes++;
            TweenCallback onKill = () => kills++;
            var log = new CallLog();
            var tween = _s.NewTween(_s.Box(), 10f);
            tween.SetAutoKill(false);
            tween.OnComplete(onComplete);
            tween.OnKill(onKill);
            var waiting = TweenAwaits.AwaitLogged(tween, area, log);

            _s.Step(1f);

            Assert.That(log.ToArray(), Is.EqualTo(new[] { "completed" }));
            Assert.That(tween.onComplete, Is.SameAs(onComplete), "the inline continuation consumed the await, which restored the fields");
            Assert.That(tween.onKill, Is.SameAs(onKill));
            Assert.That(completes, Is.EqualTo(1));
            TweenAwaits.Consume(waiting);
        }

        [Test]
        public void Await_AUsersOnCompleteThatThrows_IsRoutedAndTheAwaitStillCompletes()
        {
            var area = _s.Area();
            var failure = new InvalidOperationException("user onComplete");
            var tween = _s.NewTween(_s.Box(), 10f);
            tween.OnComplete(() => throw failure);
            var task = tween.AwaitCompletionAsync(area);

            _s.Step(1f);

            TweenAwaits.AssertStatus(task, UniTaskStatus.Succeeded);
            TweenAwaits.Consume(task);
            Assert.That(_s.Errors.Count, Is.EqualTo(1));
            var captured = _s.Errors[0];
            Assert.That(captured.Exception, Is.SameAs(failure));
            Assert.That(captured.Context.Source, Is.EqualTo(LifetimeErrorSource.EventHandler));
            Assert.That(captured.Context.LifetimeName, Is.EqualTo("TweenArea"));
            Assert.That(captured.Context.Member, Is.EqualTo(nameof(Await_AUsersOnCompleteThatThrows_IsRoutedAndTheAwaitStillCompletes)));
            Assert.That(captured.Context.Line, Is.GreaterThan(0));
            _s.Errors.Clear();
        }

        [Test]
        public void Await_AUsersOnKillThatThrows_IsRoutedOnceAndTheAwaitIsStillCancelled()
        {
            var area = _s.Area();
            var failure = new InvalidOperationException("user onKill");
            var tween = _s.NewTween(_s.Box(), 10f);
            tween.OnKill(() => throw failure);
            var task = tween.AwaitCompletionAsync(area);

            area.Cancel();

            TweenAwaits.AssertCancelled(task);
            Assert.That(_s.Errors.Count, Is.EqualTo(1), "the failure is routed once, by the await, not again by the lifetime");
            Assert.That(_s.Errors[0].Exception, Is.SameAs(failure));
            Assert.That(_s.Errors[0].Context.Source, Is.EqualTo(LifetimeErrorSource.EventHandler));
            _s.Errors.Clear();
        }

        // Something chained on top of the await's callbacks

        [Test]
        public void Await_NothingChained_ThePromiseGoesBackToThePool()
        {
            var area = _s.Area();
            TweenAwaits.WarmPool(_s, area);
            var log = new CallLog();
            var tween = _s.NewTween(_s.Box(), 10f);
            var before = TweenCompletionPromise.PooledCount;

            var waiting = TweenAwaits.AwaitLogged(tween, area, log);
            Assert.That(TweenCompletionPromise.PooledCount, Is.EqualTo(before - 1), "premise: the await rented the pooled promise");
            area.Cancel();

            Assert.That(log.ToArray(), Is.EqualTo(new[] { "canceled" }));
            Assert.That(TweenCompletionPromise.PooledCount, Is.EqualTo(before), "the consumed promise is returned");
            TweenAwaits.Consume(waiting);
        }

        [Test]
        public void Await_ChainedOnTopOfOnKill_ThePromiseIsNotPooledAndTheForwardingRuns()
        {
            var area = _s.Area();
            TweenAwaits.WarmPool(_s, area);
            var kills = 0;
            var top = 0;
            var log = new CallLog();
            var tween = _s.NewTween(_s.Box(), 10f);
            tween.OnKill(() => kills++);
            var before = TweenCompletionPromise.PooledCount;
            var waiting = TweenAwaits.AwaitLogged(tween, area, log);
            tween.onKill += () => top++;

            area.Cancel();

            Assert.That(log.ToArray(), Is.EqualTo(new[] { "canceled" }), "the await still ends in a cancellation");
            Assert.That(kills, Is.EqualTo(1), "the user's original onKill ran once, through the chain");
            Assert.That(top, Is.EqualTo(1), "what was chained on top ran once");
            Assert.That(TweenCompletionPromise.PooledCount, Is.EqualTo(before - 1), "a promise with a foreign delegate on top is never returned");
            TweenAwaits.Consume(waiting);
        }

        [Test]
        public void Await_ChainedOnTopOfOnComplete_TheDroppedPromiseKeepsForwardingTheOriginal()
        {
            var area = _s.Area();
            TweenAwaits.WarmPool(_s, area);
            var originals = 0;
            var top = 0;
            var log = new CallLog();
            var tween = _s.NewTween(_s.Box(), 10f);
            tween.SetAutoKill(false);
            tween.OnComplete(() => originals++);
            var before = TweenCompletionPromise.PooledCount;
            var waiting = TweenAwaits.AwaitLogged(tween, area, log);
            tween.onComplete += () => top++;

            _s.Step(1f);

            Assert.That(log.ToArray(), Is.EqualTo(new[] { "completed" }));
            Assert.That(originals, Is.EqualTo(1));
            Assert.That(top, Is.EqualTo(1));
            Assert.That(TweenCompletionPromise.PooledCount, Is.EqualTo(before - 1), "not pooled");

            tween.Restart();
            _s.Step(1f);

            Assert.That(originals, Is.EqualTo(2), "the dropped promise still forwards to the saved original");
            Assert.That(top, Is.EqualTo(2));
            Assert.That(log.Count, Is.EqualTo(1), "and it settles nothing a second time");
            TweenAwaits.Consume(waiting);
        }

        // The pooled promise

        [Test]
        public void Await_ThePooledPromise_IsRentedForTheAwaitAndReturnedWhenItIsConsumed()
        {
            var area = _s.Area();
            TweenAwaits.WarmPool(_s, area);
            var before = TweenCompletionPromise.PooledCount;
            var tween = _s.NewTween(_s.Box(), 10f);

            var task = tween.AwaitCompletionAsync(area);
            Assert.That(TweenCompletionPromise.PooledCount, Is.EqualTo(before - 1));
            _s.Step(1f);
            Assert.That(TweenCompletionPromise.PooledCount, Is.EqualTo(before - 1), "settled, but not returned before it is consumed");
            TweenAwaits.Consume(task);

            Assert.That(TweenCompletionPromise.PooledCount, Is.EqualTo(before));
        }

        [Test]
        public void GetResult_TwiceOnTheSameTask_ThrowsAndDoesNotReturnThePromiseAgain()
        {
            var area = _s.Area();
            TweenAwaits.WarmPool(_s, area);
            var tween = _s.NewTween(_s.Box(), 10f);
            var task = tween.AwaitCompletionAsync(area);
            _s.Step(1f);
            TweenAwaits.Consume(task);
            var pooled = TweenCompletionPromise.PooledCount;

            Assert.That(() => TweenAwaits.Consume(task), Throws.InstanceOf<InvalidOperationException>());

            Assert.That(TweenCompletionPromise.PooledCount, Is.EqualTo(pooled), "a stale token never touches the pool");
        }

        [Test]
        public void GetResult_WhileTheAwaitIsPending_ThrowsAndKeepsThePromiseOutOfThePool()
        {
            var area = _s.Area();
            TweenAwaits.WarmPool(_s, area);
            var tween = _s.NewTween(_s.Box(), 10f);
            var task = tween.AwaitCompletionAsync(area);
            var rented = TweenCompletionPromise.PooledCount;

            Assert.That(() => TweenAwaits.Consume(task), Throws.InstanceOf<InvalidOperationException>());

            Assert.That(TweenCompletionPromise.PooledCount, Is.EqualTo(rented));
            area.Cancel();
            TweenAwaits.AssertCancelled(task);
            Assert.That(TweenCompletionPromise.PooledCount, Is.EqualTo(rented + 1));
        }

        // Arguments and threads

        [Test]
        public void Await_NullTween_Throws()
        {
            var area = _s.Area();
            Tween none = null;

            var failure = Assert.Throws<ArgumentNullException>(() => none.AwaitCompletionAsync(area));

            Assert.That(failure.ParamName, Is.EqualTo("tween"));
        }

        [Test]
        public void Await_NullLifetime_KillsTheTweenThenThrows()
        {
            var tween = _s.NewTween(_s.Box(), 10f);

            var failure = Assert.Throws<ArgumentNullException>(() => tween.AwaitCompletionAsync((Lifetime)null));

            Assert.That(failure.ParamName, Is.EqualTo("lifetime"));
            Assert.That(tween.IsActive(), Is.False);
        }

        [Test]
        public void Await_OffTheMainThread_ThrowsAndLeavesTheTweenUntouched()
        {
            var area = _s.Area();
            var tween = _s.NewTween(_s.Box(), 10f);

            var failure = ThreadRunner.Run(() => tween.AwaitCompletionAsync(area));

            Assert.That(failure, Is.InstanceOf<InvalidOperationException>());
            Assert.That(tween.IsActive(), Is.True);
            Assert.That(area.EntryCount, Is.Zero);
            Assert.That(tween.onComplete, Is.Null, "no callback was chained");
        }
    }
}
