using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.Janitor
{
    // The pooled continuation behind After and Every. One instance owns the whole timer: Every re-arms the same
    // object after each tick, so a running timer allocates nothing beyond the delay provider's own promise.
    // The state is typed, so a struct state is never boxed. Every field is touched on the main thread only.
    internal sealed class TimerContinuation<TState> : LifetimeTaskEntry
    {
        // Guards a delay provider that completes synchronously from turning Every into an endless loop.
        private const int MaxSynchronousTicks = 256;

        // Not capped: the pool keeps the peak number of concurrent timers, so a burst allocates once and never again.
        private static readonly Stack<TimerContinuation<TState>> Pool = new Stack<TimerContinuation<TState>>(8);

        private readonly Action _onDelayed;
        private readonly Action _resume;
        private UniTask.Awaiter _awaiter;
        private TState _state;
        private Action<TState> _callback;
        private CancellationToken _token;
        private float _seconds;
        private bool _ignoreTimeScale;
        private bool _repeat;

        private TimerContinuation()
        {
            _onDelayed = OnDelayed;
            _resume = Resume;
        }

        // Does nothing when the lifetime is not Active. Seconds must already be validated.
        internal static void Start(Lifetime lifetime, float seconds, bool ignoreTimeScale, bool repeat, TState state, Action<TState> callback, string member, int line)
        {
            lifetime.Tree.Guard.EnsureMainThread(repeat ? "Lifetime.Every" : "Lifetime.After");
            if (lifetime.State != LifetimeState.Active)
            {
                LifetimeDiagnostics.RegistrationRefused(lifetime, member, line);
                return;
            }

            var token = lifetime.TokenForRegistration();
            var timer = Pool.Count > 0 ? Pool.Pop() : new TimerContinuation<TState>();
            timer._state = state;
            timer._callback = callback;
            timer._token = token;
            timer._seconds = seconds < 0f ? 0f : seconds;
            timer._ignoreTimeScale = ignoreTimeScale;
            timer._repeat = repeat;
            timer.Bind(lifetime, repeat ? LifetimeTaskKind.Every : LifetimeTaskKind.After, member, line);
            if (!timer.TryRegister())
            {
                timer.Recycle();
                return;
            }

            timer.Arm();
        }

        // Starts the next delay. A delay that is already complete is treated as elapsed, bounded for Every.
        private void Arm()
        {
            for (var completedInARow = 0; ; completedInARow++)
            {
                UniTask.Awaiter awaiter;
                try
                {
                    awaiter = Owner.Tree.Delay(_seconds, _ignoreTimeScale, _token).GetAwaiter();
                    if (!awaiter.IsCompleted)
                    {
                        _awaiter = awaiter;
                        awaiter.UnsafeOnCompleted(_onDelayed);
                        return;
                    }
                }
                catch (Exception exception)
                {
                    Report(exception, LifetimeErrorSource.Timer);
                    Stop();
                    return;
                }

                if (!Tick(awaiter))
                {
                    return;
                }

                // completedInARow counts finished ticks minus one, so this stops right after tick number MaxSynchronousTicks.
                if (completedInARow + 1 >= MaxSynchronousTicks)
                {
                    Report(new InvalidOperationException("Janitor: the delay completed synchronously " + MaxSynchronousTicks + " times in a row; the timer was stopped."), LifetimeErrorSource.Timer);
                    Stop();
                    return;
                }
            }
        }

        private void OnDelayed()
        {
            var tree = Owner.Tree;
            if (tree.Guard.IsMainThread)
            {
                Resume();
            }
            else
            {
                tree.Marshal.PostAction(_resume);
            }
        }

        private void Resume()
        {
            var awaiter = _awaiter;
            _awaiter = default;
            if (Tick(awaiter))
            {
                Arm();
            }
        }

        // One elapsed delay. True means the timer keeps running; false means it ended and was recycled.
        private bool Tick(UniTask.Awaiter awaiter)
        {
            try
            {
                awaiter.GetResult();
            }
            catch (Exception exception)
            {
                // A cancelled delay is the normal end of a generation; the dispatcher drops it.
                Report(exception, LifetimeErrorSource.Timer);
                Stop();
                return false;
            }

            // Never fire for a generation that ended, even when the delay completed normally in the same frame.
            if (!IsCurrent())
            {
                Stop();
                return false;
            }

            try
            {
                _callback(_state);
            }
            catch (Exception exception)
            {
                Report(exception, LifetimeErrorSource.Timer);
                Stop();
                return false;
            }

            // The callback may have cancelled its own lifetime.
            if (!_repeat || !IsCurrent())
            {
                Stop();
                return false;
            }

            return true;
        }

        private bool IsCurrent()
        {
            return Owner.State == LifetimeState.Active && Owner.IsRegistrationLive(Generation, Slot, Version);
        }

        private void Stop()
        {
            EndEntry();
            Recycle();
        }

        private void Recycle()
        {
            _awaiter = default;
            _state = default;
            _callback = null;
            _token = default;
            ClearIdentity();
            Pool.Push(this);
        }
    }
}
