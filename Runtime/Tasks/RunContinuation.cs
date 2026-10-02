using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.Janitor
{
    // The pooled continuation behind Run. It awaits through UnsafeOnCompleted with a delegate cached per instance,
    // so it allocates nothing in steady state (an async state machine would allocate in debug builds).
    // Every field is touched on the main thread only; a completion on another thread hops back before Finish.
    internal sealed class RunContinuation : LifetimeTaskEntry
    {
        // Not capped: the pool keeps the peak number of concurrent tasks, so a burst allocates once and never again.
        private static readonly Stack<RunContinuation> Pool = new Stack<RunContinuation>(16);

        private readonly Action _onCompleted;
        private readonly Action _finish;
        private UniTask.Awaiter _awaiter;

        private RunContinuation()
        {
            _onCompleted = OnCompleted;
            _finish = Finish;
        }

        // Null when the task must not start: the lifetime is not Active, or its generation ended while registering.
        internal static RunContinuation Start(Lifetime lifetime, string member, int line, out CancellationToken token)
        {
            lifetime.Tree.Guard.EnsureMainThread("Lifetime.Run");
            token = default;
            if (lifetime.State != LifetimeState.Active)
            {
                LifetimeDiagnostics.RegistrationRefused(lifetime, member, line);
                return null;
            }

            token = lifetime.TokenForRegistration();
            var task = Pool.Count > 0 ? Pool.Pop() : new RunContinuation();
            task.Bind(lifetime, LifetimeTaskKind.Run, member, line);
            if (!task.TryRegister())
            {
                task.Recycle();
                return null;
            }

            return task;
        }

        // The work threw before its first await: route it and end the entry.
        internal void Fail(Exception exception)
        {
            Report(exception, LifetimeErrorSource.Task);
            EndEntry();
            Recycle();
        }

        // A synchronous completion ends the entry at once; otherwise the pooled continuation takes over.
        internal void Observe(UniTask work)
        {
            var awaiter = work.GetAwaiter();
            try
            {
                if (!awaiter.IsCompleted)
                {
                    _awaiter = awaiter;
                    awaiter.UnsafeOnCompleted(_onCompleted);
                    return;
                }
            }
            catch (Exception exception)
            {
                // A task that was already awaited elsewhere cannot be observed again.
                Fail(exception);
                return;
            }

            Complete(awaiter);
        }

        private void OnCompleted()
        {
            var tree = Owner.Tree;
            if (tree.Guard.IsMainThread)
            {
                Finish();
            }
            else
            {
                tree.Marshal.PostAction(_finish);
            }
        }

        private void Finish()
        {
            var awaiter = _awaiter;
            _awaiter = default;
            Complete(awaiter);
        }

        // A fault is routed even when the generation already ended: it is a real bug.
        private void Complete(UniTask.Awaiter awaiter)
        {
            try
            {
                awaiter.GetResult();
            }
            catch (Exception exception)
            {
                Report(exception, LifetimeErrorSource.Task);
            }

            EndEntry();
            Recycle();
        }

        private void Recycle()
        {
            _awaiter = default;
            ClearIdentity();
            Pool.Push(this);
        }
    }
}
