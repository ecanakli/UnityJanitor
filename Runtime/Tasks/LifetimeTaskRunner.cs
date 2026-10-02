using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.Janitor
{
    // Run, After and Every bookkeeping. Arguments are validated by LifetimeTaskExtensions; a lifetime that is not
    // Active never starts anything. The pooled continuations own the entry, exception routing and the overrun hooks.
    internal static class LifetimeTaskRunner
    {
        // Lets After(Action) and Every(Action) share the typed timer without a closure.
        internal static readonly Action<Action> InvokeAction = InvokeActionCore;

        internal static void Run(Lifetime lifetime, Func<CancellationToken, UniTask> work, string member, int line)
        {
            var task = RunContinuation.Start(lifetime, member, line, out var token);
            if (task == null)
            {
                return;
            }

            UniTask running;
            try
            {
                running = work(token);
            }
            catch (Exception exception)
            {
                task.Fail(exception);
                return;
            }

            task.Observe(running);
        }

        internal static void Run<TState>(Lifetime lifetime, TState state, Func<TState, CancellationToken, UniTask> work, string member, int line)
        {
            var task = RunContinuation.Start(lifetime, member, line, out var token);
            if (task == null)
            {
                return;
            }

            UniTask running;
            try
            {
                running = work(state, token);
            }
            catch (Exception exception)
            {
                task.Fail(exception);
                return;
            }

            task.Observe(running);
        }

        internal static void After<TState>(Lifetime lifetime, float seconds, bool ignoreTimeScale, TState state, Action<TState> callback, string member, int line)
        {
            if (seconds > 0f)
            {
                TimerContinuation<TState>.Start(lifetime, seconds, ignoreTimeScale, false, state, callback, member, line);
                return;
            }

            // No delay at all: run in place, but only for a lifetime that is Active and, if gated, under an active GameObject.
            lifetime.Tree.Guard.EnsureMainThread("Lifetime.After");
            if (lifetime.State != LifetimeState.Active)
            {
                LifetimeDiagnostics.RegistrationRefused(lifetime, member, line);
                return;
            }

            if (lifetime.RefuseForInactiveObject(member, line))
            {
                return;
            }

            try
            {
                callback(state);
            }
            catch (Exception exception)
            {
                lifetime.Tree.ReportError(exception, LifetimeErrorSource.Timer, lifetime, member, line);
            }
        }

        internal static void Every<TState>(Lifetime lifetime, float seconds, bool ignoreTimeScale, TState state, Action<TState> callback, string member, int line)
        {
            TimerContinuation<TState>.Start(lifetime, seconds, ignoreTimeScale, true, state, callback, member, line);
        }

        private static void InvokeActionCore(Action action)
        {
            action();
        }
    }
}
