using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace Ecanakli.Janitor
{
    // Pooled source behind AwaitCompletionAsync; it chains onComplete and onKill, and a kill without a completion cancels.
    internal sealed class TweenCompletionPromise : IUniTaskSource, ITaskPoolNode<TweenCompletionPromise>
    {
        private static readonly Action<object> TokenCancelCallback = OnTokenCancelled;

        private static TaskPool<TweenCompletionPromise> _pool;

        // Lifetime awaits that are armed and not settled, so the lifetime entry of a tween can end an await whose callbacks were replaced.
        private static TweenCompletionPromise[] _armedAwaits = new TweenCompletionPromise[8];
        private static int _armedAwaitCount;

        private readonly TweenCallback _onComplete;
        private readonly TweenCallback _onKill;
        private readonly Action _cancelHop;

        private TweenCompletionPromise _nextNode;
        private UniTaskCompletionSourceCore<AsyncUnit> _core;
        private Tween _tween;
        private TweenCallback _originalComplete;
        private TweenCallback _originalKill;
        private LifetimeRegistration _registration;
        private bool _hasRegistration;
        private string _lifetimeName;
        private string _member;
        private int _line;
        private CancellationToken _token;
        private CancellationTokenRegistration _tokenRegistration;

        // Armed: the callbacks steer this await. Settled: the result is decided, maybe not consumed yet.
        private bool _armed;
        private bool _settled;
        private short _hopVersion;
        private int _armedAwaitIndex = -1;

        static TweenCompletionPromise()
        {
            TaskPool.RegisterSizeGetter(typeof(TweenCompletionPromise), () => _pool.Size);
        }

        private TweenCompletionPromise()
        {
            _onComplete = HandleTweenComplete;
            _onKill = HandleTweenKill;
            _cancelHop = CancelHop;
        }

        // Promises waiting in the pool; tests use it to prove a chained-on-top promise is not returned.
        internal static int PooledCount => _pool.Size;

        public ref TweenCompletionPromise NextNode => ref _nextNode;

        // The tween is already registered; the handle tells onComplete whether the generation is still current.
        internal static UniTask ForLifetime(Tween tween, LifetimeRegistration registration, string lifetimeName, string member, int line)
        {
            var promise = Acquire(tween);
            promise._registration = registration;
            promise._hasRegistration = true;
            promise._lifetimeName = lifetimeName;
            promise._member = member;
            promise._line = line;
            promise.AddArmedAwait();
            return new UniTask(promise, promise._core.Version);
        }

        // Armed lifetime awaits right now; tests read it to prove none is left behind.
        internal static int ArmedAwaitCount => _armedAwaitCount;

        // True while an await on this tween is armed and not settled. The sweep asks it for an inactive tween only.
        internal static bool HasArmedAwait(Tween tween)
        {
            for (var i = 0; i < _armedAwaitCount; i++)
            {
                var promise = _armedAwaits[i];
                if (ReferenceEquals(promise._tween, tween) && promise._armed && !promise._settled)
                {
                    return true;
                }
            }

            return false;
        }

        // Domain reload can be off, so nothing armed in a finished session may cross into the next one; an abandoned promise is dropped.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetSession()
        {
            for (var i = 0; i < _armedAwaitCount; i++)
            {
                var promise = _armedAwaits[i];
                if (promise != null)
                {
                    promise._armedAwaitIndex = -1;
                }
            }

            Array.Clear(_armedAwaits, 0, _armedAwaitCount);
            _armedAwaitCount = 0;
        }

        // Called by the lifetime entry of a tween right after it killed the tween: whatever the callbacks did, the await ends.
        internal static void CancelArmedAwaits(Tween tween)
        {
            for (var i = _armedAwaitCount - 1; i >= 0; i--)
            {
                // Ending an await runs its continuation, which can start or end other awaits.
                if (i >= _armedAwaitCount)
                {
                    continue;
                }

                var promise = _armedAwaits[i];
                if (ReferenceEquals(promise._tween, tween) && promise._armed && !promise._settled)
                {
                    promise.Settle(true);
                }
            }
        }

        internal static UniTask ForToken(Tween tween, CancellationToken token)
        {
            var promise = Acquire(tween);
            var version = promise._core.Version;
            promise._token = token;
            if (token.CanBeCanceled)
            {
                promise._tokenRegistration = token.RegisterWithoutCaptureExecutionContext(TokenCancelCallback, promise);
            }

            return new UniTask(promise, version);
        }

        public UniTaskStatus GetStatus(short token)
        {
            return _core.GetStatus(token);
        }

        public UniTaskStatus UnsafeGetStatus()
        {
            return _core.UnsafeGetStatus();
        }

        public void OnCompleted(Action<object> continuation, object state, short token)
        {
            _core.OnCompleted(continuation, state, token);
        }

        public void GetResult(short token)
        {
            // A stale token or a pending result throws without touching the pool.
            if (token != _core.Version || _core.UnsafeGetStatus() == UniTaskStatus.Pending)
            {
                _core.GetResult(token);
                return;
            }

            try
            {
                _core.GetResult(token);
            }
            finally
            {
                TryReturn();
            }
        }

        private static TweenCompletionPromise Acquire(Tween tween)
        {
            if (!_pool.TryPop(out var promise))
            {
                promise = new TweenCompletionPromise();
            }

            promise._tween = tween;
            promise._originalComplete = tween.onComplete;
            promise._originalKill = tween.onKill;
            if (ReferenceEquals(promise._originalComplete, promise._onComplete))
            {
                promise._originalComplete = null;
            }

            if (ReferenceEquals(promise._originalKill, promise._onKill))
            {
                promise._originalKill = null;
            }

            tween.onComplete = promise._onComplete;
            tween.onKill = promise._onKill;
            promise._armed = true;
            TaskTracker.TrackActiveTask(promise, 3);
            return promise;
        }

        private static void OnTokenCancelled(object state)
        {
            var promise = (TweenCompletionPromise)state;
            if (PlayerLoopHelper.IsMainThread)
            {
                promise.CancelOnMainThread();
                return;
            }

            // DOTween is not thread safe: the kill waits for the main thread.
            promise._hopVersion = promise._core.Version;
            PlayerLoopHelper.AddContinuation(PlayerLoopTiming.Update, promise._cancelHop);
        }

        private void CancelHop()
        {
            // A hop that outlived its await finds another version and does nothing.
            if (_hopVersion == _core.Version)
            {
                CancelOnMainThread();
            }
        }

        private void CancelOnMainThread()
        {
            if (!_armed || _settled)
            {
                return;
            }

            var version = _core.Version;
            var tween = _tween;
            var name = _lifetimeName;
            var member = _member;
            var line = _line;
            try
            {
                TweenTermination.KillNow(tween);
            }
            catch (Exception exception)
            {
                TweenTermination.Route(exception, LifetimeErrorSource.TokenCallback, name, member, line);
            }

            // onKill normally settled it; a tween that refuses the kill (nested in a Sequence) is cancelled here.
            if (version == _core.Version && _armed && !_settled)
            {
                Settle(true);
            }
        }

        private void HandleTweenComplete()
        {
            var version = _core.Version;
            RunOriginal(_originalComplete);

            // The original may have killed the tween, which settles and can recycle this promise.
            if (version != _core.Version || !_armed || _settled)
            {
                return;
            }

            var canceled = (_hasRegistration && !_registration.IsActive)
                || _token.IsCancellationRequested
                || ReferenceEquals(TweenTermination.Completing, _tween);
            Settle(canceled);
        }

        private void HandleTweenKill()
        {
            var version = _core.Version;
            RunOriginal(_originalKill);
            if (version != _core.Version || !_armed || _settled)
            {
                return;
            }

            Settle(true);
        }

        private void RunOriginal(TweenCallback original)
        {
            if (original == null)
            {
                return;
            }

            var name = _lifetimeName;
            var member = _member;
            var line = _line;
            try
            {
                original();
            }
            catch (Exception exception)
            {
                TweenTermination.Route(exception, LifetimeErrorSource.EventHandler, name, member, line);
            }
        }

        // Touch nothing after the result is set: the continuation runs inline and can recycle this promise.
        private void Settle(bool canceled)
        {
            _settled = true;
            RemoveArmedAwait();
            _tokenRegistration.Dispose();
            var name = _lifetimeName;
            var member = _member;
            var line = _line;
            var token = _token.IsCancellationRequested ? _token : default;
            try
            {
                if (canceled)
                {
                    _core.TrySetCanceled(token);
                }
                else
                {
                    _core.TrySetResult(AsyncUnit.Default);
                }
            }
            catch (Exception exception)
            {
                TweenTermination.Route(exception, LifetimeErrorSource.EventHandler, name, member, line);
            }
        }

        private void AddArmedAwait()
        {
            if (_armedAwaitCount == _armedAwaits.Length)
            {
                Array.Resize(ref _armedAwaits, _armedAwaitCount * 2);
            }

            _armedAwaitIndex = _armedAwaitCount;
            _armedAwaits[_armedAwaitCount++] = this;
        }

        // Swap-remove; the promise that moved into the hole learns its new index.
        private void RemoveArmedAwait()
        {
            var index = _armedAwaitIndex;
            if (index < 0)
            {
                return;
            }

            _armedAwaitIndex = -1;

            // A session reset can have dropped this promise; its old slot may belong to another one now.
            if (index >= _armedAwaitCount || !ReferenceEquals(_armedAwaits[index], this))
            {
                return;
            }

            var last = --_armedAwaitCount;
            var moved = _armedAwaits[last];
            _armedAwaits[last] = null;
            if (index != last)
            {
                _armedAwaits[index] = moved;
                moved._armedAwaitIndex = index;
            }
        }

        // Restores a field only if it still holds ours; if something sits on top, this promise stays a pass-through and is not pooled.
        private void TryReturn()
        {
            RemoveArmedAwait();
            TaskTracker.RemoveTracking(this);
            _tokenRegistration.Dispose();
            _tokenRegistration = default;

            var poolable = true;
            var tween = _tween;
            if (tween != null)
            {
                var current = tween.onComplete;
                if (ReferenceEquals(current, _onComplete))
                {
                    tween.onComplete = _originalComplete;
                }
                else if (current != null)
                {
                    poolable = false;
                }

                current = tween.onKill;
                if (ReferenceEquals(current, _onKill))
                {
                    tween.onKill = _originalKill;
                }
                else if (current != null)
                {
                    poolable = false;
                }
            }

            _core.Reset();
            _armed = false;
            _settled = false;
            _tween = null;
            _registration = default;
            _hasRegistration = false;
            _lifetimeName = null;
            _member = null;
            _line = 0;
            _token = default;

            if (poolable)
            {
                _originalComplete = null;
                _originalKill = null;
                _pool.TryPush(this);
            }
        }
    }
}
