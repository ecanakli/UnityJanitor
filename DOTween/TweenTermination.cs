using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace Ecanakli.Janitor
{
    // Cached terminate actions, probe and registration helper; built on the public OnCancel overload only.
    internal static class TweenTermination
    {
        internal static readonly Action<Tween> KillAction = KillTween;
        internal static readonly Action<Tween> CompleteAction = CompleteTween;
        internal static readonly Action<Tween> AwaitKillAction = KillTweenAndEndAwait;
        internal static readonly Func<Tween, bool> IsFinishedProbe = IsFinished;
        internal static readonly Func<Tween, bool> AwaitIsFinishedProbe = IsAwaitFinished;

#if UNITY_EDITOR
        // Tweens already reported as unkillable, so one tween is reported once. Weak: it never keeps a tween alive.
        private static readonly ConditionalWeakTable<Tween, object> UnkillableReported = new ConditionalWeakTable<Tween, object>();
        private static readonly object ReportedMarker = new object();
#endif

        // True only while OnCancel runs: a terminate action seen then is an immediate one.
        [ThreadStatic]
        private static bool _registering;

        // The tween a Complete-mode teardown is completing, so an await on it can tell why it completed.
        [ThreadStatic]
        private static Tween _completing;

        internal static Tween Completing => _completing;

        // A lifetime that is not Active runs the action at once and returns a dead handle.
        internal static LifetimeRegistration Register(Tween tween, Lifetime lifetime, TweenCancelMode mode, string member, int line)
        {
            return Register(tween, lifetime, mode == TweenCancelMode.Complete ? CompleteAction : KillAction, IsFinishedProbe, member, line);
        }

        // The entry of an awaited tween: it kills like AddTo, then ends the await if the tween's callbacks no longer do.
        // Its probe keeps the entry while an await is still armed on an inactive tween, so the lifetime's end can settle it.
        internal static LifetimeRegistration RegisterAwait(Tween tween, Lifetime lifetime, string member, int line)
        {
            return Register(tween, lifetime, AwaitKillAction, AwaitIsFinishedProbe, member, line);
        }

        private static LifetimeRegistration Register(Tween tween, Lifetime lifetime, Action<Tween> action, Func<Tween, bool> probe, string member, int line)
        {
            var previous = _registering;
            _registering = true;
            try
            {
                return lifetime.OnCancel<Tween>(tween, action, probe, member, line);
            }
            finally
            {
                _registering = previous;
            }
        }

        // Non-recyclable, then killed; for a null or gone owner. Never throws.
        internal static void KillQuietly(Tween tween, string member, int line)
        {
            if (tween == null)
            {
                return;
            }

            try
            {
                tween.SetRecyclable(false);
                KillTween(tween);
            }
            catch (Exception exception)
            {
                Route(exception, LifetimeErrorSource.CancelAction, null, member, line);
            }
        }

        // May throw if a DOTween callback throws outside safe mode.
        internal static void KillNow(Tween tween)
        {
            KillTween(tween);
        }

        internal static void EnsureMainThread(string operation)
        {
            var mainThreadId = PlayerLoopHelper.MainThreadId;
            if (mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                throw new InvalidOperationException("Janitor: " + operation + " must be called on the main thread.");
            }
        }

        // Mirrors the core dispatch: cancellation is dropped, a failing handler is logged, nothing throws.
        internal static void Route(Exception exception, LifetimeErrorSource source, string lifetimeName, string member, int line)
        {
            if (exception == null || exception is OperationCanceledException)
            {
                return;
            }

            try
            {
                var context = new LifetimeErrorContext(source, null, lifetimeName, member, line);
                LifetimeErrors.Handler(exception, in context);
            }
            catch (Exception handlerFailure)
            {
                try
                {
                    Debug.LogException(handlerFailure);
                    Debug.LogException(exception);
                }
                catch
                {
                    // Logging itself failed; nothing left to report to.
                }
            }
        }

        private static bool IsFinished(Tween tween)
        {
            return !tween.IsActive();
        }

        // The armed-await scan runs only for an inactive tween, and only in a sweep.
        private static bool IsAwaitFinished(Tween tween)
        {
            return !tween.IsActive() && !TweenCompletionPromise.HasArmedAwait(tween);
        }

        // The flag is cleared meanwhile, so a teardown nested in a callback is not taken for an immediate one.
        private static void KillTween(Tween tween)
        {
            if (!tween.IsActive())
            {
                return;
            }

            var saved = _registering;
            _registering = false;
            try
            {
                tween.Kill(false);
            }
            finally
            {
                _registering = saved;
            }

            ReportIfStillActive(tween);
        }

        // An await armed on this tween is cancelled even when a callback set after the call replaced the await's own.
        private static void KillTweenAndEndAwait(Tween tween)
        {
            try
            {
                KillTween(tween);
            }
            finally
            {
                TweenCompletionPromise.CancelArmedAwaits(tween);
            }
        }

        private static void CompleteTween(Tween tween)
        {
            if (_registering)
            {
                // The owner had already ended: the tween never ran, so it is killed.
                KillTween(tween);
                return;
            }

            if (!tween.IsActive())
            {
                return;
            }

            var previous = _completing;
            _completing = tween;
            try
            {
                tween.Kill(true);
            }
            finally
            {
                _completing = previous;
            }

            ReportIfStillActive(tween);
        }

        // JANITOR102: DOTween ignores a kill on a tween nested in a Sequence, so the tween is still active right after the kill.
        // Editor only: the call, and with it the IsActive check, is removed from players. Reported once per tween, never throws.
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private static void ReportIfStillActive(Tween tween)
        {
#if UNITY_EDITOR
            try
            {
                if (!tween.IsActive() || UnkillableReported.TryGetValue(tween, out _))
                {
                    return;
                }

                UnkillableReported.Add(tween, ReportedMarker);
                var target = tween.target as UnityEngine.Object;
                var targetName = tween.target == null ? "none" : target != null ? target.name : tween.target.GetType().Name;
                var message = "A " + tween.GetType().Name + " (target: " + targetName + ") is still active right after it was killed. "
                    + "DOTween ignores a kill on a tween that is nested in a Sequence; register the root Sequence with AddTo instead.";

                // Called from inside the item's cancel action, so the core attributes it to that lifetime and call site.
                LifetimeDiagnostics.Report(null, DiagnosticIds.UnkillableTween, message, target);
            }
            catch (Exception)
            {
                // A diagnostic must never break a kill.
            }
#endif
        }
    }
}
