using System;
using System.Runtime.CompilerServices;

namespace Ecanakli.Janitor
{
    /// <summary>
    /// The subscribe-only view of an <see cref="OwnedEvent"/>. A publisher keeps the event private and exposes
    /// this view, so outsiders can subscribe and only the owner can invoke.
    /// </summary>
    public interface IOwnedEvent
    {
        /// <summary>
        /// Subscribes <paramref name="handler"/> for as long as the current generation of <paramref name="owner"/>
        /// lasts; the package removes it when that generation ends or when the returned handle is cancelled.
        /// Nothing is added when the owner is not active. Subscribing an equal handler for the same owner again
        /// returns the existing registration and raises the development warning JANITOR105; another owner gets
        /// a separate subscription.
        /// </summary>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The owner that decides how long the subscription lives.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="owner"/> or <paramref name="handler"/> is null; nothing is added.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        LifetimeRegistration Subscribe(Action handler, Lifetime owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0);
    }

    /// <summary>The subscribe-only view of an <see cref="OwnedEvent{T}"/>.</summary>
    /// <typeparam name="T">The argument type.</typeparam>
    public interface IOwnedEvent<T>
    {
        /// <summary>
        /// Subscribes <paramref name="handler"/> for as long as the current generation of <paramref name="owner"/>
        /// lasts; the package removes it when that generation ends or when the returned handle is cancelled.
        /// Nothing is added when the owner is not active. Subscribing an equal handler for the same owner again
        /// returns the existing registration and raises the development warning JANITOR105; another owner gets
        /// a separate subscription.
        /// </summary>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The owner that decides how long the subscription lives.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="owner"/> or <paramref name="handler"/> is null; nothing is added.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        LifetimeRegistration Subscribe(Action<T> handler, Lifetime owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0);
    }

    /// <summary>The subscribe-only view of an <see cref="OwnedEvent{T1, T2}"/>.</summary>
    /// <typeparam name="T1">The first argument type.</typeparam>
    /// <typeparam name="T2">The second argument type.</typeparam>
    public interface IOwnedEvent<T1, T2>
    {
        /// <summary>
        /// Subscribes <paramref name="handler"/> for as long as the current generation of <paramref name="owner"/>
        /// lasts; the package removes it when that generation ends or when the returned handle is cancelled.
        /// Nothing is added when the owner is not active. Subscribing an equal handler for the same owner again
        /// returns the existing registration and raises the development warning JANITOR105; another owner gets
        /// a separate subscription.
        /// </summary>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The owner that decides how long the subscription lives.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="owner"/> or <paramref name="handler"/> is null; nothing is added.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        LifetimeRegistration Subscribe(Action<T1, T2> handler, Lifetime owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0);
    }

    /// <summary>The subscribe-only view of an <see cref="OwnedEvent{T1, T2, T3}"/>.</summary>
    /// <typeparam name="T1">The first argument type.</typeparam>
    /// <typeparam name="T2">The second argument type.</typeparam>
    /// <typeparam name="T3">The third argument type.</typeparam>
    public interface IOwnedEvent<T1, T2, T3>
    {
        /// <summary>
        /// Subscribes <paramref name="handler"/> for as long as the current generation of <paramref name="owner"/>
        /// lasts; the package removes it when that generation ends or when the returned handle is cancelled.
        /// Nothing is added when the owner is not active. Subscribing an equal handler for the same owner again
        /// returns the existing registration and raises the development warning JANITOR105; another owner gets
        /// a separate subscription.
        /// </summary>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The owner that decides how long the subscription lives.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="owner"/> or <paramref name="handler"/> is null; nothing is added.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        LifetimeRegistration Subscribe(Action<T1, T2, T3> handler, Lifetime owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0);
    }
}
