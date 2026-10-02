using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests.TweenBinding
{
    // Real async waiters and task asserts for the AwaitCompletionAsync tests.
    internal static class TweenAwaits
    {
        // Awaits the tween and logs how the await ended; the continuation runs inline inside the DOTween callback.
        internal static async UniTask AwaitLogged(Tween tween, Lifetime lifetime, CallLog log)
        {
            try
            {
                await tween.AwaitCompletionAsync(lifetime);
                log.Add("completed");
            }
            catch (OperationCanceledException)
            {
                log.Add("canceled");
            }
        }

        internal static async UniTask AwaitLogged(Tween tween, CancellationToken token, CallLog log)
        {
            try
            {
                await tween.AwaitCompletionAsync(token);
                log.Add("completed");
            }
            catch (OperationCanceledException)
            {
                log.Add("canceled");
            }
        }

        // Awaits first on its lifetime; however that ends, it awaits second on another lifetime inside the same call chain,
        // so the second await rents the promise the first one just returned.
        internal static async UniTask AwaitThenAwait(Tween first, Lifetime firstLifetime, Tween second, Lifetime secondLifetime, CallLog log)
        {
            try
            {
                await first.AwaitCompletionAsync(firstLifetime);
                log.Add("first completed");
            }
            catch (OperationCanceledException)
            {
                log.Add("first canceled");
            }

            await AwaitSecond(second, secondLifetime, log);
        }

        internal static async UniTask AwaitThenAwait(Tween first, CancellationToken firstToken, Tween second, Lifetime secondLifetime, CallLog log)
        {
            try
            {
                await first.AwaitCompletionAsync(firstToken);
                log.Add("first completed");
            }
            catch (OperationCanceledException)
            {
                log.Add("first canceled");
            }

            await AwaitSecond(second, secondLifetime, log);
        }

        private static async UniTask AwaitSecond(Tween second, Lifetime lifetime, CallLog log)
        {
            try
            {
                await second.AwaitCompletionAsync(lifetime);
                log.Add("second completed");
            }
            catch (OperationCanceledException)
            {
                log.Add("second canceled");
            }
        }

        // Observes the result the way an await does, which also returns a pooled promise.
        internal static void Consume(UniTask task)
        {
            task.GetAwaiter().GetResult();
        }

        internal static void AssertStatus(UniTask task, UniTaskStatus expected)
        {
            Assert.That(task.Status, Is.EqualTo(expected));
        }

        // The task is cancelled and an await on it throws OperationCanceledException.
        internal static void AssertCancelled(UniTask task)
        {
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Canceled));
            Assert.That(() => task.GetAwaiter().GetResult(), Throws.InstanceOf<OperationCanceledException>());
        }

        // Puts at least one promise in the pool, so pool arithmetic in a test starts from a known floor.
        internal static void WarmPool(TweenSession session, Lifetime lifetime)
        {
            var tween = session.NewTween(session.Box(), 10f);
            var task = tween.AwaitCompletionAsync(lifetime);
            session.Step(2f);
            Consume(task);
            Assert.That(TweenCompletionPromise.PooledCount, Is.GreaterThanOrEqualTo(1), "premise: the pool holds a promise");
        }
    }
}
