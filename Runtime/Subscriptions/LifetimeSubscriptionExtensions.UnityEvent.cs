using System;
using System.Runtime.CompilerServices;
using UnityEngine.Events;

namespace Ecanakli.Janitor
{
    // UnityEvent forms, event first: evt.Subscribe(handler, owner). The event never sees the raw handler: it gets a
    // guard listener (see UnityEventGuard), so a subscription removed during a dispatch is not delivered in it.
    public static partial class LifetimeSubscriptionExtensions
    {
        /// <summary>
        /// Subscribes <paramref name="handler"/> to <paramref name="evt"/> and removes it when the current generation
        /// of <paramref name="owner"/> ends or the returned registration is cancelled. The event gets a small guard
        /// listener instead of the handler, and the guard calls the handler only while the subscription is active. A
        /// UnityEvent dispatches over a snapshot, so without the guard a handler removed during a dispatch would still
        /// run once; with it, a subscription ended during a dispatch is not delivered in that dispatch. Subscribing the
        /// same handler (same target and method) to the same event with the same owner again returns the existing
        /// registration and raises the development warning JANITOR105. The package allocates the guard and its
        /// listener delegate once per call and nothing per invoke.
        /// </summary>
        /// <param name="evt">The event.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The owner that decides how long the subscription lives.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="evt"/>, <paramref name="handler"/> or <paramref name="owner"/> is null; nothing is added.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public static LifetimeRegistration Subscribe(this UnityEvent evt, UnityAction handler, Lifetime owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            if (evt == null)
            {
                throw new ArgumentNullException(nameof(evt));
            }

            if (!UnityEventGuardBase.TryBegin(evt, handler, owner, member, line, out var finished))
            {
                return finished;
            }

            var guard = new UnityEventGuard(evt, handler, owner);
            evt.AddListener(guard.Listener);
            return guard.Attach(member, line);
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> to <paramref name="evt"/> and removes it when the current generation
        /// of <paramref name="owner"/> ends or the returned registration is cancelled; see
        /// <see cref="Subscribe(UnityEvent, UnityAction, Lifetime, string, int)"/> for the guard and the duplicate rule.
        /// </summary>
        /// <typeparam name="T0">The first event argument type.</typeparam>
        /// <param name="evt">The event.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The owner that decides how long the subscription lives.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="evt"/>, <paramref name="handler"/> or <paramref name="owner"/> is null; nothing is added.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public static LifetimeRegistration Subscribe<T0>(this UnityEvent<T0> evt, UnityAction<T0> handler, Lifetime owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            if (evt == null)
            {
                throw new ArgumentNullException(nameof(evt));
            }

            if (!UnityEventGuardBase.TryBegin(evt, handler, owner, member, line, out var finished))
            {
                return finished;
            }

            var guard = new UnityEventGuard<T0>(evt, handler, owner);
            evt.AddListener(guard.Listener);
            return guard.Attach(member, line);
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> to <paramref name="evt"/> and removes it when the current generation
        /// of <paramref name="owner"/> ends or the returned registration is cancelled; see
        /// <see cref="Subscribe(UnityEvent, UnityAction, Lifetime, string, int)"/> for the guard and the duplicate rule.
        /// </summary>
        /// <typeparam name="T0">The first event argument type.</typeparam>
        /// <typeparam name="T1">The second event argument type.</typeparam>
        /// <param name="evt">The event.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The owner that decides how long the subscription lives.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="evt"/>, <paramref name="handler"/> or <paramref name="owner"/> is null; nothing is added.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public static LifetimeRegistration Subscribe<T0, T1>(this UnityEvent<T0, T1> evt, UnityAction<T0, T1> handler, Lifetime owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            if (evt == null)
            {
                throw new ArgumentNullException(nameof(evt));
            }

            if (!UnityEventGuardBase.TryBegin(evt, handler, owner, member, line, out var finished))
            {
                return finished;
            }

            var guard = new UnityEventGuard<T0, T1>(evt, handler, owner);
            evt.AddListener(guard.Listener);
            return guard.Attach(member, line);
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> to <paramref name="evt"/> and removes it when the current generation
        /// of <paramref name="owner"/> ends or the returned registration is cancelled; see
        /// <see cref="Subscribe(UnityEvent, UnityAction, Lifetime, string, int)"/> for the guard and the duplicate rule.
        /// </summary>
        /// <typeparam name="T0">The first event argument type.</typeparam>
        /// <typeparam name="T1">The second event argument type.</typeparam>
        /// <typeparam name="T2">The third event argument type.</typeparam>
        /// <param name="evt">The event.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The owner that decides how long the subscription lives.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="evt"/>, <paramref name="handler"/> or <paramref name="owner"/> is null; nothing is added.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public static LifetimeRegistration Subscribe<T0, T1, T2>(this UnityEvent<T0, T1, T2> evt, UnityAction<T0, T1, T2> handler, Lifetime owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            if (evt == null)
            {
                throw new ArgumentNullException(nameof(evt));
            }

            if (!UnityEventGuardBase.TryBegin(evt, handler, owner, member, line, out var finished))
            {
                return finished;
            }

            var guard = new UnityEventGuard<T0, T1, T2>(evt, handler, owner);
            evt.AddListener(guard.Listener);
            return guard.Attach(member, line);
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> to <paramref name="evt"/> and removes it when the current generation
        /// of <paramref name="owner"/> ends or the returned registration is cancelled; see
        /// <see cref="Subscribe(UnityEvent, UnityAction, Lifetime, string, int)"/> for the guard and the duplicate rule.
        /// </summary>
        /// <typeparam name="T0">The first event argument type.</typeparam>
        /// <typeparam name="T1">The second event argument type.</typeparam>
        /// <typeparam name="T2">The third event argument type.</typeparam>
        /// <typeparam name="T3">The fourth event argument type.</typeparam>
        /// <param name="evt">The event.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The owner that decides how long the subscription lives.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="evt"/>, <paramref name="handler"/> or <paramref name="owner"/> is null; nothing is added.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public static LifetimeRegistration Subscribe<T0, T1, T2, T3>(this UnityEvent<T0, T1, T2, T3> evt, UnityAction<T0, T1, T2, T3> handler, Lifetime owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            if (evt == null)
            {
                throw new ArgumentNullException(nameof(evt));
            }

            if (!UnityEventGuardBase.TryBegin(evt, handler, owner, member, line, out var finished))
            {
                return finished;
            }

            var guard = new UnityEventGuard<T0, T1, T2, T3>(evt, handler, owner);
            evt.AddListener(guard.Listener);
            return guard.Attach(member, line);
        }
    }
}
