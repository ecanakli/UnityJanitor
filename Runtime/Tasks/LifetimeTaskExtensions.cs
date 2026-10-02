using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.Janitor
{
    /// <summary>
    /// Starts tasks and timers that end with a <see cref="Lifetime"/>. They share the lifetime's generation token,
    /// so <see cref="Lifetime.Cancel"/> stops them all; none of them has a per-item handle. Give a task its own
    /// child area to cancel it alone. All members must be called on the main thread and return nothing.
    /// </summary>
    public static partial class LifetimeTaskExtensions
    {
        /// <summary>
        /// Runs <paramref name="work"/> with the current generation token. It replaces <c>async void</c> and
        /// <c>Forget()</c>. The work is not started when the lifetime is not active. A synchronous throw and a fault
        /// are routed to <see cref="LifetimeErrors.Handler"/> with source <see cref="LifetimeErrorSource.Task"/>,
        /// also after the generation ended. An <see cref="OperationCanceledException"/> is silent.
        /// </summary>
        /// <param name="lifetime">The owner. Its current generation token is passed to the work.</param>
        /// <param name="work">The work. Pass the token to every awaited call so that it stops on cancel.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <exception cref="ArgumentNullException"><paramref name="lifetime"/> or <paramref name="work"/> is null; nothing is started.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public static void Run(this Lifetime lifetime, Func<CancellationToken, UniTask> work, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            if (lifetime == null)
            {
                throw new ArgumentNullException(nameof(lifetime));
            }

            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            LifetimeTaskRunner.Run(lifetime, work, member, line);
        }

        /// <summary>
        /// Like <see cref="Run(Lifetime, Func{CancellationToken, UniTask}, string, int)"/>, with a state value
        /// passed to the work. A static work delegate allocates nothing.
        /// </summary>
        /// <typeparam name="TState">The state type.</typeparam>
        /// <param name="lifetime">The owner. Its current generation token is passed to the work.</param>
        /// <param name="state">Passed to the work.</param>
        /// <param name="work">The work. Pass the token to every awaited call so that it stops on cancel.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <exception cref="ArgumentNullException"><paramref name="lifetime"/> or <paramref name="work"/> is null; nothing is started.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public static void Run<TState>(this Lifetime lifetime, TState state, Func<TState, CancellationToken, UniTask> work, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            if (lifetime == null)
            {
                throw new ArgumentNullException(nameof(lifetime));
            }

            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            LifetimeTaskRunner.Run(lifetime, state, work, member, line);
        }

        /// <summary>
        /// Runs <paramref name="action"/> once after <paramref name="seconds"/>. With <paramref name="seconds"/> at or
        /// below zero it runs immediately, but only when the lifetime accepts work: it is active, and an active
        /// lifetime (or an area below one) has an active GameObject, exactly as for a delay above zero. It never runs
        /// when the generation ends first. An exception is routed with source <see cref="LifetimeErrorSource.Timer"/>.
        /// </summary>
        /// <param name="lifetime">The owner. The delay uses its current generation token.</param>
        /// <param name="seconds">The delay in seconds. Values at or below zero run in place.</param>
        /// <param name="action">The callback.</param>
        /// <param name="ignoreTimeScale">True to measure the delay in unscaled time.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <exception cref="ArgumentNullException"><paramref name="lifetime"/> or <paramref name="action"/> is null; nothing is started.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="seconds"/> is NaN, infinite or larger than 900000000000.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public static void After(this Lifetime lifetime, float seconds, Action action, bool ignoreTimeScale = false, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            if (lifetime == null)
            {
                throw new ArgumentNullException(nameof(lifetime));
            }

            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            ValidateSeconds(seconds);
            LifetimeTaskRunner.After(lifetime, seconds, ignoreTimeScale, action, LifetimeTaskRunner.InvokeAction, member, line);
        }

        /// <summary>
        /// Like <see cref="After(Lifetime, float, Action, bool, string, int)"/>, with a state value passed to the
        /// callback. A static callback allocates nothing, and a struct state is not boxed.
        /// </summary>
        /// <typeparam name="TState">The state type.</typeparam>
        /// <param name="lifetime">The owner. The delay uses its current generation token.</param>
        /// <param name="seconds">The delay in seconds. Values at or below zero run in place.</param>
        /// <param name="state">Passed to the callback.</param>
        /// <param name="action">The callback.</param>
        /// <param name="ignoreTimeScale">True to measure the delay in unscaled time.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <exception cref="ArgumentNullException"><paramref name="lifetime"/> or <paramref name="action"/> is null; nothing is started.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="seconds"/> is NaN, infinite or larger than 900000000000.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public static void After<TState>(this Lifetime lifetime, float seconds, TState state, Action<TState> action, bool ignoreTimeScale = false, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            if (lifetime == null)
            {
                throw new ArgumentNullException(nameof(lifetime));
            }

            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            ValidateSeconds(seconds);
            LifetimeTaskRunner.After(lifetime, seconds, ignoreTimeScale, state, action, member, line);
        }

        /// <summary>
        /// Runs <paramref name="action"/> repeatedly, once per <paramref name="seconds"/>, until the generation
        /// ends. The interval restarts after each callback. Values at or below zero fire once per frame. An
        /// exception stops that timer and is routed once with source <see cref="LifetimeErrorSource.Timer"/>.
        /// </summary>
        /// <param name="lifetime">The owner. The timer uses its current generation token.</param>
        /// <param name="seconds">The interval in seconds.</param>
        /// <param name="action">The callback.</param>
        /// <param name="ignoreTimeScale">True to measure the interval in unscaled time.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <exception cref="ArgumentNullException"><paramref name="lifetime"/> or <paramref name="action"/> is null; nothing is started.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="seconds"/> is NaN, infinite or larger than 900000000000.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public static void Every(this Lifetime lifetime, float seconds, Action action, bool ignoreTimeScale = false, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            if (lifetime == null)
            {
                throw new ArgumentNullException(nameof(lifetime));
            }

            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            ValidateSeconds(seconds);
            LifetimeTaskRunner.Every(lifetime, seconds, ignoreTimeScale, action, LifetimeTaskRunner.InvokeAction, member, line);
        }

        /// <summary>
        /// Like <see cref="Every(Lifetime, float, Action, bool, string, int)"/>, with a state value passed to the
        /// callback. A static callback allocates nothing, and a struct state is not boxed.
        /// </summary>
        /// <typeparam name="TState">The state type.</typeparam>
        /// <param name="lifetime">The owner. The timer uses its current generation token.</param>
        /// <param name="seconds">The interval in seconds.</param>
        /// <param name="state">Passed to the callback.</param>
        /// <param name="action">The callback.</param>
        /// <param name="ignoreTimeScale">True to measure the interval in unscaled time.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <exception cref="ArgumentNullException"><paramref name="lifetime"/> or <paramref name="action"/> is null; nothing is started.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="seconds"/> is NaN, infinite or larger than 900000000000.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public static void Every<TState>(this Lifetime lifetime, float seconds, TState state, Action<TState> action, bool ignoreTimeScale = false, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            if (lifetime == null)
            {
                throw new ArgumentNullException(nameof(lifetime));
            }

            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            ValidateSeconds(seconds);
            LifetimeTaskRunner.Every(lifetime, seconds, ignoreTimeScale, state, action, member, line);
        }

        // Below TimeSpan.MaxValue (about 9.2e11 s), so the delay provider never overflows.
        private const float MaxSeconds = 900000000000f;

        private static void ValidateSeconds(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds > MaxSeconds)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds), "Seconds must be a finite number no larger than 900000000000.");
            }
        }
    }
}
