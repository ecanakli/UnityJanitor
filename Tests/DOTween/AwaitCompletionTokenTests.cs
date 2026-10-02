using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using NUnit.Framework;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.TweenBinding
{
    // AwaitCompletionAsync(token) awaits the tween and kills it when the caller's token is cancelled.
    // The caller owns the token; the await owns one registration on it, released when the await ends.
    [TestFixture]
    public sealed class AwaitCompletionTokenTests
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
        public void Await_Token_TweenCompletes_TaskSucceedsAndTheValueLands()
        {
            var source = _s.NewSource();
            var box = _s.Box();
            var tween = _s.NewTween(box, 10f);

            var task = tween.AwaitCompletionAsync(source.Token);
            TweenAwaits.AssertStatus(task, UniTaskStatus.Pending);
            _s.Step(0.5f);
            TweenAwaits.AssertStatus(task, UniTaskStatus.Pending);
            _s.Step(0.5f);

            TweenAwaits.AssertStatus(task, UniTaskStatus.Succeeded);
            Assert.That(box.Value, Is.EqualTo(10f).Within(0.0001f));
            TweenAwaits.Consume(task);
        }

        [Test]
        public void Await_Token_AsyncWaiter_ResumesInlineWhenTheTweenCompletes()
        {
            var source = _s.NewSource();
            var log = new CallLog();
            var tween = _s.NewTween(_s.Box(), 10f);
            var waiting = TweenAwaits.AwaitLogged(tween, source.Token, log);

            _s.Step(1f);

            Assert.That(log.ToArray(), Is.EqualTo(new[] { "completed" }));
            TweenAwaits.Consume(waiting);
        }

        [Test]
        public void Await_Token_NoneToken_RegistersNothingAndFollowsTheTween()
        {
            var tween = _s.NewTween(_s.Box(), 10f);

            var task = tween.AwaitCompletionAsync(CancellationToken.None);
            TweenAwaits.AssertStatus(task, UniTaskStatus.Pending);
            _s.Step(1f);

            TweenAwaits.AssertStatus(task, UniTaskStatus.Succeeded);
            TweenAwaits.Consume(task);
        }

        [Test]
        public void Await_Token_UsersCallbacks_RunExactlyOnceAndAreRestoredAfterTheCompletion()
        {
            var source = _s.NewSource();
            var completes = 0;
            var kills = 0;
            TweenCallback onComplete = () => completes++;
            TweenCallback onKill = () => kills++;
            var tween = _s.NewTween(_s.Box(), 10f);
            tween.SetAutoKill(false);
            tween.OnComplete(onComplete);
            tween.OnKill(onKill);

            var task = tween.AwaitCompletionAsync(source.Token);
            Assert.That(tween.onComplete, Is.Not.SameAs(onComplete), "premise: the await chains onComplete");
            _s.Step(1f);
            TweenAwaits.Consume(task);

            Assert.That(completes, Is.EqualTo(1));
            Assert.That(tween.onComplete, Is.SameAs(onComplete));
            Assert.That(tween.onKill, Is.SameAs(onKill));
            tween.Kill();
            Assert.That(kills, Is.EqualTo(1));
        }

        // Cancellation

        [Test]
        public void Await_Token_TokenCancel_KillsTheTweenAndCancelsTheTaskWithThatToken()
        {
            var source = _s.NewSource();
            var box = _s.Box();
            var kills = 0;
            var completes = 0;
            var tween = _s.NewTween(box, 10f);
            tween.OnKill(() => kills++);
            tween.OnComplete(() => completes++);
            var task = tween.AwaitCompletionAsync(source.Token);
            _s.Step(0.4f);
            var reached = box.Value;

            source.Cancel();

            Assert.That(tween.IsActive(), Is.False);
            Assert.That(box.Value, Is.EqualTo(reached));
            Assert.That(kills, Is.EqualTo(1));
            Assert.That(completes, Is.Zero);
            TweenAwaits.AssertStatus(task, UniTaskStatus.Canceled);
            var cancelled = Assert.Catch<OperationCanceledException>(() => TweenAwaits.Consume(task));
            Assert.That(cancelled.CancellationToken, Is.EqualTo(source.Token), "the exception carries the caller's token");
        }

        [Test]
        public void Await_Token_AsyncWaiter_TokenCancelThrowsAtTheAwait()
        {
            var source = _s.NewSource();
            var log = new CallLog();
            var tween = _s.NewTween(_s.Box(), 10f);
            var waiting = TweenAwaits.AwaitLogged(tween, source.Token, log);

            source.Cancel();

            Assert.That(log.ToArray(), Is.EqualTo(new[] { "canceled" }));
            TweenAwaits.Consume(waiting);
        }

        [Test]
        public void Await_Token_ExternalKill_CancelsTheTaskWithoutACancelledToken()
        {
            var source = _s.NewSource();
            var tween = _s.NewTween(_s.Box(), 10f);
            var task = tween.AwaitCompletionAsync(source.Token);

            tween.Kill();

            TweenAwaits.AssertStatus(task, UniTaskStatus.Canceled);
            var cancelled = Assert.Catch<OperationCanceledException>(() => TweenAwaits.Consume(task));
            Assert.That(cancelled.CancellationToken.IsCancellationRequested, Is.False, "the token was not what ended the await");
        }

        [UnityTest]
        public IEnumerator Await_Token_SetLinkAndDestroy_CancelsTheTask()
        {
            var source = _s.NewSource();
            var go = _s.NewObject();
            var linked = _s.NewLinkedTween(go);
            var task = linked.AwaitCompletionAsync(source.Token);

            Object.Destroy(go);
            for (var i = 0; i < TweenSession.MaxFrames && task.Status == UniTaskStatus.Pending; i++)
            {
                yield return null;
            }

            TweenAwaits.AssertCancelled(task);
            Assert.That(linked.IsActive(), Is.False);
        }

        [UnityTest]
        public IEnumerator Await_Token_DestroyCancellationToken_KillsTheTweenWhenTheComponentIsDestroyed()
        {
            var host = _s.NewHost();
            var tween = _s.NewTween(_s.Box(), 10f);
            var task = tween.AwaitCompletionAsync(host.destroyCancellationToken);

            Object.Destroy(host.gameObject);
            yield return TweenSession.Frames(2);

            Assert.That(tween.IsActive(), Is.False);
            TweenAwaits.AssertCancelled(task);
        }

        [Test]
        public void Await_Token_InfiniteLoop_NeverCompletesByItselfAndATokenCancelEndsItWithACancellation()
        {
            var source = _s.NewSource();
            var completes = 0;
            var tween = _s.NewLoop(_s.Box(), 10f, 1f);
            tween.OnComplete(() => completes++);
            var task = tween.AwaitCompletionAsync(source.Token);

            for (var i = 0; i < 20; i++)
            {
                _s.Step(1f);
            }

            TweenAwaits.AssertStatus(task, UniTaskStatus.Pending);
            Assert.That(completes, Is.Zero);

            source.Cancel();

            TweenAwaits.AssertCancelled(task);
        }

        [Test]
        public void Await_Token_InfiniteLoop_AnExternalKillEndsItWithACancellation()
        {
            var source = _s.NewSource();
            var tween = _s.NewLoop(_s.Box(), 10f, 1f);
            var task = tween.AwaitCompletionAsync(source.Token);
            _s.Step(3f);

            tween.Kill();

            TweenAwaits.AssertCancelled(task);
        }

        // A token that is already cancelled, and a tween that is already gone

        [Test]
        public void Await_Token_AlreadyCancelled_KillsTheTweenAndReturnsACancelledTask()
        {
            var source = _s.NewSource();
            source.Cancel();
            var kills = 0;
            var tween = _s.NewTween(_s.Box(), 10f);
            tween.OnKill(() => kills++);

            var task = tween.AwaitCompletionAsync(source.Token);

            Assert.That(tween.IsActive(), Is.False);
            Assert.That(kills, Is.EqualTo(1));
            TweenAwaits.AssertStatus(task, UniTaskStatus.Canceled);
            var cancelled = Assert.Catch<OperationCanceledException>(() => TweenAwaits.Consume(task));
            Assert.That(cancelled.CancellationToken, Is.EqualTo(source.Token));
        }

        [Test]
        public void Await_Token_AlreadyCancelledAndTheTweenIsInactive_IsCancelledNotCompleted()
        {
            var source = _s.NewSource();
            var tween = _s.NewTween(_s.Box(), 10f);
            tween.Kill();
            source.Cancel();

            var task = tween.AwaitCompletionAsync(source.Token);

            TweenAwaits.AssertCancelled(task);
        }

        [Test]
        public void Await_Token_AnInactiveTween_Completes()
        {
            var source = _s.NewSource();
            var tween = _s.NewTween(_s.Box(), 10f);
            tween.Kill();

            var task = tween.AwaitCompletionAsync(source.Token);

            TweenAwaits.AssertStatus(task, UniTaskStatus.Succeeded);
            TweenAwaits.Consume(task);
        }

        // The registration on the caller's token

        [Test]
        public void Await_Token_CancelAfterTheTweenCompleted_DoesNotKillTheFinishedTween()
        {
            var source = _s.NewSource();
            var tween = _s.NewTween(_s.Box(), 10f);
            tween.SetAutoKill(false);
            var task = tween.AwaitCompletionAsync(source.Token);
            _s.Step(1f);
            TweenAwaits.AssertStatus(task, UniTaskStatus.Succeeded);

            source.Cancel();

            Assert.That(tween.IsActive(), Is.True, "a finished await no longer listens to its token");
            TweenAwaits.Consume(task);
        }

        [Test]
        public void Await_Token_CancelAfterTheAwaitEnded_DoesNotTouchThePromiseRentedByAnotherAwait()
        {
            var first = _s.NewSource();
            var second = _s.NewSource();
            var firstTween = _s.NewTween(_s.Box(), 10f);
            var firstTask = firstTween.AwaitCompletionAsync(first.Token);
            _s.Step(1f);
            TweenAwaits.Consume(firstTask);
            var secondTween = _s.NewTween(_s.Box(), 10f);
            var secondTask = secondTween.AwaitCompletionAsync(second.Token);

            first.Cancel();

            Assert.That(secondTween.IsActive(), Is.True, "the first await's token must not reach the promise now used by the second");
            TweenAwaits.AssertStatus(secondTask, UniTaskStatus.Pending);

            second.Cancel();

            Assert.That(secondTween.IsActive(), Is.False);
            TweenAwaits.AssertCancelled(secondTask);
        }

        [UnityTest]
        public IEnumerator Await_Token_CancelledOnAWorkerThread_KillsTheTweenOnTheMainThread()
        {
            var mainThread = Thread.CurrentThread.ManagedThreadId;
            var killThread = 0;
            var source = _s.NewSource();
            var tween = _s.NewTween(_s.Box(), 10f);
            tween.OnKill(() => killThread = Thread.CurrentThread.ManagedThreadId);
            var task = tween.AwaitCompletionAsync(source.Token);

            var failure = ThreadRunner.Run(() => source.Cancel());

            Assert.That(failure, Is.Null);
            Assert.That(tween.IsActive(), Is.True, "nothing is killed on the worker thread");
            TweenAwaits.AssertStatus(task, UniTaskStatus.Pending);
            for (var i = 0; i < TweenSession.MaxFrames && task.Status == UniTaskStatus.Pending; i++)
            {
                yield return null;
            }

            TweenAwaits.AssertCancelled(task);
            Assert.That(tween.IsActive(), Is.False);
            Assert.That(killThread, Is.EqualTo(mainThread), "the kill ran on the main thread");
        }

        // A tween DOTween refuses to kill

        [Test]
        public void Await_Token_ANestedTween_TheKillIsIgnoredButTheTaskIsStillCancelled()
        {
            var source = _s.NewSource();
            var first = _s.NewNestedTween(_s.Box(), 10f, 1f);
            var second = _s.NewNestedTween(_s.Box(), 20f, 1f);
            _s.NewSequence(first, second);
            var task = first.AwaitCompletionAsync(source.Token);

            source.Cancel();

            Assert.That(first.IsActive(), Is.True, "premise: DOTween ignores Kill on a nested tween");
            TweenAwaits.AssertCancelled(task);
        }

        // Arguments and threads

        [Test]
        public void Await_Token_NullTween_Throws()
        {
            var source = _s.NewSource();
            Tween none = null;

            var failure = Assert.Throws<ArgumentNullException>(() => none.AwaitCompletionAsync(source.Token));

            Assert.That(failure.ParamName, Is.EqualTo("tween"));
        }

        [Test]
        public void Await_Token_OffTheMainThread_ThrowsAndLeavesTheTweenUntouched()
        {
            var source = _s.NewSource();
            var tween = _s.NewTween(_s.Box(), 10f);

            var failure = ThreadRunner.Run(() => tween.AwaitCompletionAsync(source.Token));

            Assert.That(failure, Is.InstanceOf<InvalidOperationException>());
            Assert.That(tween.IsActive(), Is.True);
            Assert.That(tween.onComplete, Is.Null, "no callback was chained");
        }
    }
}
