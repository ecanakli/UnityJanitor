using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Events;

namespace Ecanakli.Janitor
{
    // Component owners for the UnityEvent forms. The owner resolves exactly as AddTo(Component) does: a MonoBehaviour
    // uses its component lifetime, any other Component the lifetime of its GameObject, a destroyed owner adds nothing.
    public static partial class LifetimeSubscriptionExtensions
    {
        /// <summary>
        /// Subscribes <paramref name="handler"/> to <paramref name="evt"/> for as long as the current generation of the
        /// owner's lifetime lasts, exactly like the <see cref="Lifetime"/> form (see
        /// <see cref="Subscribe(UnityEvent, UnityAction, Lifetime, string, int)"/>). A <see cref="MonoBehaviour"/> owner
        /// resolves to its component lifetime, any other component to the lifetime of its GameObject. A destroyed
        /// owner adds nothing.
        /// </summary>
        /// <param name="evt">The event.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The owning component.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="evt"/>, <paramref name="handler"/> or <paramref name="owner"/> is null; nothing is added.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static LifetimeRegistration Subscribe(this UnityEvent evt, UnityAction handler, Component owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            return Subscribe(evt, handler, LifetimeComponentExtensions.ResolveOwnerOrNull(owner), member, line);
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> to <paramref name="evt"/> for as long as the current generation of the
        /// owner's lifetime lasts, exactly like the <see cref="Lifetime"/> form. A <see cref="MonoBehaviour"/> owner
        /// resolves to its component lifetime, any other component to the lifetime of its GameObject. A destroyed
        /// owner adds nothing.
        /// </summary>
        /// <typeparam name="T0">The first event argument type.</typeparam>
        /// <param name="evt">The event.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The owning component.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="evt"/>, <paramref name="handler"/> or <paramref name="owner"/> is null; nothing is added.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static LifetimeRegistration Subscribe<T0>(this UnityEvent<T0> evt, UnityAction<T0> handler, Component owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            return Subscribe(evt, handler, LifetimeComponentExtensions.ResolveOwnerOrNull(owner), member, line);
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> to <paramref name="evt"/> for as long as the current generation of the
        /// owner's lifetime lasts, exactly like the <see cref="Lifetime"/> form. A <see cref="MonoBehaviour"/> owner
        /// resolves to its component lifetime, any other component to the lifetime of its GameObject. A destroyed
        /// owner adds nothing.
        /// </summary>
        /// <typeparam name="T0">The first event argument type.</typeparam>
        /// <typeparam name="T1">The second event argument type.</typeparam>
        /// <param name="evt">The event.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The owning component.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="evt"/>, <paramref name="handler"/> or <paramref name="owner"/> is null; nothing is added.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static LifetimeRegistration Subscribe<T0, T1>(this UnityEvent<T0, T1> evt, UnityAction<T0, T1> handler, Component owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            return Subscribe(evt, handler, LifetimeComponentExtensions.ResolveOwnerOrNull(owner), member, line);
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> to <paramref name="evt"/> for as long as the current generation of the
        /// owner's lifetime lasts, exactly like the <see cref="Lifetime"/> form. A <see cref="MonoBehaviour"/> owner
        /// resolves to its component lifetime, any other component to the lifetime of its GameObject. A destroyed
        /// owner adds nothing.
        /// </summary>
        /// <typeparam name="T0">The first event argument type.</typeparam>
        /// <typeparam name="T1">The second event argument type.</typeparam>
        /// <typeparam name="T2">The third event argument type.</typeparam>
        /// <param name="evt">The event.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The owning component.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="evt"/>, <paramref name="handler"/> or <paramref name="owner"/> is null; nothing is added.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static LifetimeRegistration Subscribe<T0, T1, T2>(this UnityEvent<T0, T1, T2> evt, UnityAction<T0, T1, T2> handler, Component owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            return Subscribe(evt, handler, LifetimeComponentExtensions.ResolveOwnerOrNull(owner), member, line);
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> to <paramref name="evt"/> for as long as the current generation of the
        /// owner's lifetime lasts, exactly like the <see cref="Lifetime"/> form. A <see cref="MonoBehaviour"/> owner
        /// resolves to its component lifetime, any other component to the lifetime of its GameObject. A destroyed
        /// owner adds nothing.
        /// </summary>
        /// <typeparam name="T0">The first event argument type.</typeparam>
        /// <typeparam name="T1">The second event argument type.</typeparam>
        /// <typeparam name="T2">The third event argument type.</typeparam>
        /// <typeparam name="T3">The fourth event argument type.</typeparam>
        /// <param name="evt">The event.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The owning component.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="evt"/>, <paramref name="handler"/> or <paramref name="owner"/> is null; nothing is added.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static LifetimeRegistration Subscribe<T0, T1, T2, T3>(this UnityEvent<T0, T1, T2, T3> evt, UnityAction<T0, T1, T2, T3> handler, Component owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            return Subscribe(evt, handler, LifetimeComponentExtensions.ResolveOwnerOrNull(owner), member, line);
        }
    }
}
