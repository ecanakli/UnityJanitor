using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Ecanakli.Janitor
{
    /// <summary>
    /// Component owners for the subscribe-only <see cref="IOwnedEvent"/> views, so a publisher that exposes a view
    /// can still be subscribed to with <c>view.Subscribe(handler, this)</c>. On the event classes themselves the
    /// instance methods are used instead. A <see cref="MonoBehaviour"/> owner resolves to its component lifetime, any
    /// other component to the lifetime of its GameObject.
    /// </summary>
    public static class OwnedEventViewExtensions
    {
        /// <summary>Subscribes <paramref name="handler"/> to the event behind the view, owned by a component.</summary>
        /// <param name="view">The subscribe-only view.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The owning component.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="view"/>, <paramref name="owner"/> or <paramref name="handler"/> is null; nothing is added.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static LifetimeRegistration Subscribe(this IOwnedEvent view, Action handler, Component owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            return view.Subscribe(handler, LifetimeComponentExtensions.ResolveOwnerOrNull(owner), member, line);
        }

        /// <summary>Subscribes <paramref name="handler"/> to the event behind the view, owned by a component.</summary>
        /// <typeparam name="T">The argument type.</typeparam>
        /// <param name="view">The subscribe-only view.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The owning component.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="view"/>, <paramref name="owner"/> or <paramref name="handler"/> is null; nothing is added.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static LifetimeRegistration Subscribe<T>(this IOwnedEvent<T> view, Action<T> handler, Component owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            return view.Subscribe(handler, LifetimeComponentExtensions.ResolveOwnerOrNull(owner), member, line);
        }

        /// <summary>Subscribes <paramref name="handler"/> to the event behind the view, owned by a component.</summary>
        /// <typeparam name="T1">The first argument type.</typeparam>
        /// <typeparam name="T2">The second argument type.</typeparam>
        /// <param name="view">The subscribe-only view.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The owning component.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="view"/>, <paramref name="owner"/> or <paramref name="handler"/> is null; nothing is added.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static LifetimeRegistration Subscribe<T1, T2>(this IOwnedEvent<T1, T2> view, Action<T1, T2> handler, Component owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            return view.Subscribe(handler, LifetimeComponentExtensions.ResolveOwnerOrNull(owner), member, line);
        }

        /// <summary>Subscribes <paramref name="handler"/> to the event behind the view, owned by a component.</summary>
        /// <typeparam name="T1">The first argument type.</typeparam>
        /// <typeparam name="T2">The second argument type.</typeparam>
        /// <typeparam name="T3">The third argument type.</typeparam>
        /// <param name="view">The subscribe-only view.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The owning component.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="view"/>, <paramref name="owner"/> or <paramref name="handler"/> is null; nothing is added.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static LifetimeRegistration Subscribe<T1, T2, T3>(this IOwnedEvent<T1, T2, T3> view, Action<T1, T2, T3> handler, Component owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            return view.Subscribe(handler, LifetimeComponentExtensions.ResolveOwnerOrNull(owner), member, line);
        }
    }
}
