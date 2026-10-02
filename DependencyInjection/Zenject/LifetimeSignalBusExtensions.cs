using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using Zenject;
using Ecanakli.Janitor.DependencyInjection;

namespace Ecanakli.Janitor
{
    /// <summary>
    /// Owner-bound SignalBus subscriptions: <c>bus.Subscribe&lt;TSignal&gt;(handler, owner)</c> subscribes now and the package
    /// calls <c>TryUnsubscribe</c> when the owner's generation ends, so user code never writes the unsubscribe. The signal type
    /// argument must be written out. The signal has to be declared, exactly as with <c>SignalBus.Subscribe</c>. No SignalBus
    /// binding is added by the package; the game installs SignalBus itself. Main thread only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Duplicates: SignalBus keeps one subscription per signal and handler. Subscribing the same handler to the same signal
    /// with the same owner again returns the existing registration and raises the development warning JANITOR105. The same
    /// handler under a different owner cannot be held by SignalBus and throws <see cref="InvalidOperationException"/>.
    /// Only subscriptions made through these methods are known to the package; a handler subscribed directly with
    /// <c>SignalBus.Subscribe</c> is not tracked.
    /// </para>
    /// <para>
    /// Allocation: nothing per call from the package once warm (static delegates, reused entries); SignalBus allocates
    /// its own subscription wrapper. A handler that is a fresh delegate each call allocates in the caller, as with <c>+=</c>.
    /// </para>
    /// </remarks>
    public static class LifetimeSignalBusExtensions
    {
        /// <summary>
        /// Subscribes <paramref name="handler"/> to <typeparamref name="TSignal"/> on <paramref name="bus"/> and unsubscribes it when the
        /// current generation of <paramref name="owner"/> ends or the returned registration is cancelled. Nothing is subscribed when
        /// <paramref name="owner"/> is not active (cancelling or disposed). See <see cref="LifetimeSignalBusExtensions"/> for the duplicate rule.
        /// </summary>
        /// <typeparam name="TSignal">The signal type; it has to be declared on the bus.</typeparam>
        /// <param name="bus">The signal bus.</param>
        /// <param name="handler">The handler, called with the fired signal.</param>
        /// <param name="owner">The lifetime that decides how long the subscription lives.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was subscribed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="bus"/>, <paramref name="handler"/> or <paramref name="owner"/> is null; nothing is subscribed.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread, or the handler is already subscribed to this signal on this bus for another owner.</exception>
        /// <remarks>An error from <c>SignalBus.Subscribe</c>, such as an undeclared signal, is thrown after the owner entry was undone, so nothing is left behind.</remarks>
        public static LifetimeRegistration Subscribe<TSignal>(this SignalBus bus, Action<TSignal> handler, Lifetime owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            Require(bus, owner, handler);
            return SignalSubscriptions.Subscribe<TSignal, Action<TSignal>>(bus, handler, owner, SignalOps<TSignal>.SubscribeTyped, SignalOps<TSignal>.TerminateTyped, member, line);
        }

        /// <summary>
        /// Subscribes a handler that takes no argument to <typeparamref name="TSignal"/> on <paramref name="bus"/> and unsubscribes it when the
        /// current generation of <paramref name="owner"/> ends or the returned registration is cancelled; see
        /// <see cref="Subscribe{TSignal}(SignalBus, Action{TSignal}, Lifetime, string, int)"/> for the rules.
        /// </summary>
        /// <typeparam name="TSignal">The signal type; it has to be declared on the bus.</typeparam>
        /// <param name="bus">The signal bus.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The lifetime that decides how long the subscription lives.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was subscribed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="bus"/>, <paramref name="handler"/> or <paramref name="owner"/> is null; nothing is subscribed.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread, or the handler is already subscribed to this signal on this bus for another owner.</exception>
        public static LifetimeRegistration Subscribe<TSignal>(this SignalBus bus, Action handler, Lifetime owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            Require(bus, owner, handler);
            return SignalSubscriptions.Subscribe<TSignal, Action>(bus, handler, owner, SignalOps<TSignal>.SubscribePlain, SignalOps<TSignal>.TerminatePlain, member, line);
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> to <typeparamref name="TSignal"/> on <paramref name="bus"/> for as long as the current
        /// generation of the owner's lifetime lasts. A <see cref="MonoBehaviour"/> owner uses its component lifetime; any other component
        /// uses the lifetime of its GameObject. A destroyed owner subscribes nothing. See
        /// <see cref="Subscribe{TSignal}(SignalBus, Action{TSignal}, Lifetime, string, int)"/> for the rules.
        /// </summary>
        /// <typeparam name="TSignal">The signal type; it has to be declared on the bus.</typeparam>
        /// <param name="bus">The signal bus.</param>
        /// <param name="handler">The handler, called with the fired signal.</param>
        /// <param name="owner">The owning component.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was subscribed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="bus"/>, <paramref name="handler"/> or <paramref name="owner"/> is null; nothing is subscribed.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread, or the handler is already subscribed to this signal on this bus for another owner.</exception>
        public static LifetimeRegistration Subscribe<TSignal>(this SignalBus bus, Action<TSignal> handler, Component owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            Require(bus, owner, handler);
            ZenjectDiagnostics.EnsureMainThread("SignalBus.Subscribe");
            var lifetime = ResolveLifetime(owner);
            if (lifetime == null)
            {
                // The owner is already destroyed.
                return default;
            }

            return SignalSubscriptions.Subscribe<TSignal, Action<TSignal>>(bus, handler, lifetime, SignalOps<TSignal>.SubscribeTyped, SignalOps<TSignal>.TerminateTyped, member, line);
        }

        /// <summary>
        /// Subscribes a handler that takes no argument to <typeparamref name="TSignal"/> on <paramref name="bus"/> for as long as the
        /// current generation of the owner's lifetime lasts; the owner resolves as in
        /// <see cref="Subscribe{TSignal}(SignalBus, Action{TSignal}, Component, string, int)"/>.
        /// </summary>
        /// <typeparam name="TSignal">The signal type; it has to be declared on the bus.</typeparam>
        /// <param name="bus">The signal bus.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="owner">The owning component.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was subscribed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="bus"/>, <paramref name="handler"/> or <paramref name="owner"/> is null; nothing is subscribed.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread, or the handler is already subscribed to this signal on this bus for another owner.</exception>
        public static LifetimeRegistration Subscribe<TSignal>(this SignalBus bus, Action handler, Component owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            Require(bus, owner, handler);
            ZenjectDiagnostics.EnsureMainThread("SignalBus.Subscribe");
            var lifetime = ResolveLifetime(owner);
            if (lifetime == null)
            {
                // The owner is already destroyed.
                return default;
            }

            return SignalSubscriptions.Subscribe<TSignal, Action>(bus, handler, lifetime, SignalOps<TSignal>.SubscribePlain, SignalOps<TSignal>.TerminatePlain, member, line);
        }

        // C# null checks only: a destroyed Unity owner is not an argument error, it just subscribes nothing.
        private static void Require(SignalBus bus, object owner, Delegate handler)
        {
            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            if (ReferenceEquals(owner, null))
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
        }

        // A MonoBehaviour uses its component lifetime (a destroyed one gets a disposed lifetime), any other component its GameObject's; null for a destroyed non-MonoBehaviour.
        private static Lifetime ResolveLifetime(Component owner)
        {
            var behaviour = owner as MonoBehaviour;
            if (!ReferenceEquals(behaviour, null))
            {
                return behaviour.GetLifetime();
            }

            return owner == null ? null : owner.gameObject.GetLifetime();
        }
    }
}
