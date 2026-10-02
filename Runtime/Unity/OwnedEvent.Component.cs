using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Ecanakli.Janitor
{
    // The Component owner of every OwnedEvent arity. A MonoBehaviour owner uses its component lifetime; any other
    // Component uses the lifetime of its GameObject. Argument checks and the duplicate rule are the Lifetime form's.
    public sealed partial class OwnedEvent
    {
        /// <summary>
        /// Subscribes <paramref name="handler"/> for as long as the current generation of the owner's lifetime lasts,
        /// exactly like the <see cref="Lifetime"/> form. A <see cref="MonoBehaviour"/> owner resolves to its component
        /// lifetime, any other component to the lifetime of its GameObject. A destroyed owner adds nothing.
        /// </summary>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The owning component.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="owner"/> or <paramref name="handler"/> is null; nothing is added.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public LifetimeRegistration Subscribe(Action handler, Component owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            return _list.Subscribe(handler, LifetimeComponentExtensions.ResolveOwnerOrNull(owner), member, line, this);
        }
    }

    public sealed partial class OwnedEvent<T>
    {
        /// <summary>
        /// Subscribes <paramref name="handler"/> for as long as the current generation of the owner's lifetime lasts,
        /// exactly like the <see cref="Lifetime"/> form. A <see cref="MonoBehaviour"/> owner resolves to its component
        /// lifetime, any other component to the lifetime of its GameObject. A destroyed owner adds nothing.
        /// </summary>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The owning component.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="owner"/> or <paramref name="handler"/> is null; nothing is added.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public LifetimeRegistration Subscribe(Action<T> handler, Component owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            return _list.Subscribe(handler, LifetimeComponentExtensions.ResolveOwnerOrNull(owner), member, line, this);
        }
    }

    public sealed partial class OwnedEvent<T1, T2>
    {
        /// <summary>
        /// Subscribes <paramref name="handler"/> for as long as the current generation of the owner's lifetime lasts,
        /// exactly like the <see cref="Lifetime"/> form. A <see cref="MonoBehaviour"/> owner resolves to its component
        /// lifetime, any other component to the lifetime of its GameObject. A destroyed owner adds nothing.
        /// </summary>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The owning component.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="owner"/> or <paramref name="handler"/> is null; nothing is added.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public LifetimeRegistration Subscribe(Action<T1, T2> handler, Component owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            return _list.Subscribe(handler, LifetimeComponentExtensions.ResolveOwnerOrNull(owner), member, line, this);
        }
    }

    public sealed partial class OwnedEvent<T1, T2, T3>
    {
        /// <summary>
        /// Subscribes <paramref name="handler"/> for as long as the current generation of the owner's lifetime lasts,
        /// exactly like the <see cref="Lifetime"/> form. A <see cref="MonoBehaviour"/> owner resolves to its component
        /// lifetime, any other component to the lifetime of its GameObject. A destroyed owner adds nothing.
        /// </summary>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The owning component.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="owner"/> or <paramref name="handler"/> is null; nothing is added.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public LifetimeRegistration Subscribe(Action<T1, T2, T3> handler, Component owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            return _list.Subscribe(handler, LifetimeComponentExtensions.ResolveOwnerOrNull(owner), member, line, this);
        }
    }
}
