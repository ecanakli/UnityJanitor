using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ecanakli.Janitor
{
    // The MonoBehaviour forms run on the component lifetime (this.GetLifetime()), so they end when the component is destroyed.
    public static partial class LifetimeTaskExtensions
    {
        /// <summary>
        /// Runs <paramref name="work"/> on the lifetime of this component; see
        /// <see cref="Run(Lifetime, Func{CancellationToken, UniTask}, string, int)"/>. It ends when the component is
        /// destroyed, and on any <c>Cancel()</c> of the component lifetime or of the category it was placed in.
        /// </summary>
        /// <param name="behaviour">The owning component.</param>
        /// <param name="work">The work. Pass the token to every awaited call so that it stops on cancel.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <exception cref="ArgumentNullException"><paramref name="behaviour"/> or <paramref name="work"/> is null; nothing is started.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static void Run(this MonoBehaviour behaviour, Func<CancellationToken, UniTask> work, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            Run(behaviour.GetLifetime(), work, member, line);
        }

        /// <summary>
        /// Like <see cref="Run(MonoBehaviour, Func{CancellationToken, UniTask}, string, int)"/>, with a state value
        /// passed to the work. A static work delegate allocates nothing.
        /// </summary>
        /// <typeparam name="TState">The state type.</typeparam>
        /// <param name="behaviour">The owning component.</param>
        /// <param name="state">Passed to the work.</param>
        /// <param name="work">The work. Pass the token to every awaited call so that it stops on cancel.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <exception cref="ArgumentNullException"><paramref name="behaviour"/> or <paramref name="work"/> is null; nothing is started.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static void Run<TState>(this MonoBehaviour behaviour, TState state, Func<TState, CancellationToken, UniTask> work, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            Run(behaviour.GetLifetime(), state, work, member, line);
        }

        /// <summary>
        /// Runs <paramref name="action"/> once after <paramref name="seconds"/> on the lifetime of this component; see
        /// <see cref="After(Lifetime, float, Action, bool, string, int)"/>.
        /// </summary>
        /// <param name="behaviour">The owning component.</param>
        /// <param name="seconds">The delay in seconds. Values at or below zero run in place.</param>
        /// <param name="action">The callback.</param>
        /// <param name="ignoreTimeScale">True to measure the delay in unscaled time.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <exception cref="ArgumentNullException"><paramref name="behaviour"/> or <paramref name="action"/> is null; nothing is started.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="seconds"/> is NaN, infinite or larger than 900000000000.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static void After(this MonoBehaviour behaviour, float seconds, Action action, bool ignoreTimeScale = false, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            After(behaviour.GetLifetime(), seconds, action, ignoreTimeScale, member, line);
        }

        /// <summary>
        /// Like <see cref="After(MonoBehaviour, float, Action, bool, string, int)"/>, with a state value passed to the
        /// callback. A static callback allocates nothing, and a struct state is not boxed.
        /// </summary>
        /// <typeparam name="TState">The state type.</typeparam>
        /// <param name="behaviour">The owning component.</param>
        /// <param name="seconds">The delay in seconds. Values at or below zero run in place.</param>
        /// <param name="state">Passed to the callback.</param>
        /// <param name="action">The callback.</param>
        /// <param name="ignoreTimeScale">True to measure the delay in unscaled time.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <exception cref="ArgumentNullException"><paramref name="behaviour"/> or <paramref name="action"/> is null; nothing is started.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="seconds"/> is NaN, infinite or larger than 900000000000.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static void After<TState>(this MonoBehaviour behaviour, float seconds, TState state, Action<TState> action, bool ignoreTimeScale = false, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            After(behaviour.GetLifetime(), seconds, state, action, ignoreTimeScale, member, line);
        }

        /// <summary>
        /// Runs <paramref name="action"/> repeatedly on the lifetime of this component; see
        /// <see cref="Every(Lifetime, float, Action, bool, string, int)"/>.
        /// </summary>
        /// <param name="behaviour">The owning component.</param>
        /// <param name="seconds">The interval in seconds.</param>
        /// <param name="action">The callback.</param>
        /// <param name="ignoreTimeScale">True to measure the interval in unscaled time.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <exception cref="ArgumentNullException"><paramref name="behaviour"/> or <paramref name="action"/> is null; nothing is started.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="seconds"/> is NaN, infinite or larger than 900000000000.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static void Every(this MonoBehaviour behaviour, float seconds, Action action, bool ignoreTimeScale = false, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            Every(behaviour.GetLifetime(), seconds, action, ignoreTimeScale, member, line);
        }

        /// <summary>
        /// Like <see cref="Every(MonoBehaviour, float, Action, bool, string, int)"/>, with a state value passed to the
        /// callback. A static callback allocates nothing, and a struct state is not boxed.
        /// </summary>
        /// <typeparam name="TState">The state type.</typeparam>
        /// <param name="behaviour">The owning component.</param>
        /// <param name="seconds">The interval in seconds.</param>
        /// <param name="state">Passed to the callback.</param>
        /// <param name="action">The callback.</param>
        /// <param name="ignoreTimeScale">True to measure the interval in unscaled time.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <exception cref="ArgumentNullException"><paramref name="behaviour"/> or <paramref name="action"/> is null; nothing is started.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="seconds"/> is NaN, infinite or larger than 900000000000.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static void Every<TState>(this MonoBehaviour behaviour, float seconds, TState state, Action<TState> action, bool ignoreTimeScale = false, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            Every(behaviour.GetLifetime(), seconds, state, action, ignoreTimeScale, member, line);
        }
    }
}
