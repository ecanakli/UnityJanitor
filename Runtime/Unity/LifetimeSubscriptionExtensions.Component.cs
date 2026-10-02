using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Ecanakli.Janitor
{
    // Component and GameObject owners. A MonoBehaviour owner resolves to its component lifetime; any other
    // Component resolves to the lifetime of its GameObject.
    public static partial class LifetimeSubscriptionExtensions
    {
        /// <summary>
        /// Disposes <paramref name="disposable"/> when the current generation of the owner's lifetime ends. A
        /// <see cref="MonoBehaviour"/> owner uses its component lifetime; any other component uses the lifetime of
        /// its GameObject. A destroyed owner disposes the item at once.
        /// </summary>
        /// <typeparam name="T">The disposable type.</typeparam>
        /// <param name="disposable">The item to dispose.</param>
        /// <param name="owner">The owning component.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that disposes this one item; default when it was disposed immediately.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="owner"/> is null (the item is disposed first) or <paramref name="disposable"/> is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static LifetimeRegistration AddTo<T>(this T disposable, Component owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
            where T : IDisposable
        {
            if (ReferenceEquals(owner, null))
            {
                DisposeQuietly(disposable, member, line);
                throw new ArgumentNullException(nameof(owner));
            }

            if (disposable == null)
            {
                throw new ArgumentNullException(nameof(disposable));
            }

            return AddTo(disposable, LifetimeComponentExtensions.ResolveOwner(owner), member, line);
        }

        /// <summary>
        /// Disposes <paramref name="disposable"/> when the current generation of the GameObject's lifetime ends. A
        /// destroyed GameObject disposes the item at once.
        /// </summary>
        /// <typeparam name="T">The disposable type.</typeparam>
        /// <param name="disposable">The item to dispose.</param>
        /// <param name="owner">The owning GameObject.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that disposes this one item; default when it was disposed immediately.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="owner"/> is null (the item is disposed first) or <paramref name="disposable"/> is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static LifetimeRegistration AddTo<T>(this T disposable, GameObject owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
            where T : IDisposable
        {
            if (ReferenceEquals(owner, null))
            {
                DisposeQuietly(disposable, member, line);
                throw new ArgumentNullException(nameof(owner));
            }

            if (disposable == null)
            {
                throw new ArgumentNullException(nameof(disposable));
            }

            return AddTo(disposable, LifetimeComponentExtensions.ResolveOwner(owner), member, line);
        }

        /// <summary>
        /// Runs <paramref name="action"/> when the current generation of this component's lifetime ends. Custom
        /// cleanup only; it is never the way to unsubscribe.
        /// </summary>
        /// <param name="behaviour">The owning component.</param>
        /// <param name="action">The cleanup. It runs immediately when the lifetime is already ending.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that cancels this one item; default when the action already ran.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="behaviour"/> or <paramref name="action"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static LifetimeRegistration OnCancel(this MonoBehaviour behaviour, Action action, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            return behaviour.GetLifetime().OnCancel(action, member, line);
        }

        /// <summary>Runs <paramref name="action"/> with <paramref name="state"/> when the current generation of this component's lifetime ends.</summary>
        /// <typeparam name="TState">The state type.</typeparam>
        /// <param name="behaviour">The owning component.</param>
        /// <param name="state">Passed to the action.</param>
        /// <param name="action">The cleanup. It runs immediately when the lifetime is already ending.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that cancels this one item; default when the action already ran.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="behaviour"/> or <paramref name="action"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static LifetimeRegistration OnCancel<TState>(this MonoBehaviour behaviour, TState state, Action<TState> action, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
            where TState : class
        {
            return behaviour.GetLifetime().OnCancel(state, action, member, line);
        }

        /// <summary>
        /// Like <see cref="OnCancel{TState}(MonoBehaviour, TState, Action{TState}, string, int)"/>, with a probe. When
        /// a sweep finds the probe true the item is dropped without running the action. A cancel before the next
        /// sweep still runs the action, so it must tolerate an item that already finished.
        /// </summary>
        /// <typeparam name="TState">The state type.</typeparam>
        /// <param name="behaviour">The owning component.</param>
        /// <param name="state">Passed to the action and to the probe.</param>
        /// <param name="action">The cleanup. It runs immediately when the lifetime is already ending.</param>
        /// <param name="isFinished">Returns true when the item no longer needs cleanup. It runs during amortized sweeps only.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that cancels this one item; default when the action already ran.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="behaviour"/>, <paramref name="action"/> or <paramref name="isFinished"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static LifetimeRegistration OnCancel<TState>(this MonoBehaviour behaviour, TState state, Action<TState> action, Func<TState, bool> isFinished, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
            where TState : class
        {
            return behaviour.GetLifetime().OnCancel(state, action, isFinished, member, line);
        }

        /// <summary>Runs <paramref name="action"/> with two state values when the current generation of this component's lifetime ends.</summary>
        /// <typeparam name="T1">The first state type.</typeparam>
        /// <typeparam name="T2">The second state type.</typeparam>
        /// <param name="behaviour">The owning component.</param>
        /// <param name="first">Passed to the action.</param>
        /// <param name="second">Passed to the action.</param>
        /// <param name="action">The cleanup. It runs immediately when the lifetime is already ending.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that cancels this one item; default when the action already ran.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="behaviour"/> or <paramref name="action"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static LifetimeRegistration OnCancel<T1, T2>(this MonoBehaviour behaviour, T1 first, T2 second, Action<T1, T2> action, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
            where T1 : class
            where T2 : class
        {
            return behaviour.GetLifetime().OnCancel(first, second, action, member, line);
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> to an event the game does not own and removes it when the current
        /// generation of this component's lifetime ends; see the <see cref="Lifetime"/> form for the exact rules.
        /// </summary>
        /// <param name="owner">The owning component.</param>
        /// <param name="add">Adds the handler to the event, for example <c>h => source.Changed += h</c>.</param>
        /// <param name="remove">Removes the handler from the event, for example <c>h => source.Changed -= h</c>.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException">Any argument is null; <paramref name="add"/> is not called.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static LifetimeRegistration Subscribe(this MonoBehaviour owner, Action<Action> add, Action<Action> remove, Action handler, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            return SubscribePaired(owner.GetLifetime(), add, remove, handler, member, line);
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> to an event the game does not own and removes it when the current
        /// generation of this component's lifetime ends. The type argument must be spelled out.
        /// </summary>
        /// <typeparam name="T">The event argument type.</typeparam>
        /// <param name="owner">The owning component.</param>
        /// <param name="add">Adds the handler to the event.</param>
        /// <param name="remove">Removes the handler from the event.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException">Any argument is null; <paramref name="add"/> is not called.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static LifetimeRegistration Subscribe<T>(this MonoBehaviour owner, Action<Action<T>> add, Action<Action<T>> remove, Action<T> handler, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            return SubscribePaired(owner.GetLifetime(), add, remove, handler, member, line);
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> to an event the game does not own and removes it when the current
        /// generation of this component's lifetime ends. The type arguments must be spelled out.
        /// </summary>
        /// <typeparam name="T1">The first event argument type.</typeparam>
        /// <typeparam name="T2">The second event argument type.</typeparam>
        /// <param name="owner">The owning component.</param>
        /// <param name="add">Adds the handler to the event.</param>
        /// <param name="remove">Removes the handler from the event.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException">Any argument is null; <paramref name="add"/> is not called.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static LifetimeRegistration Subscribe<T1, T2>(this MonoBehaviour owner, Action<Action<T1, T2>> add, Action<Action<T1, T2>> remove, Action<T1, T2> handler, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            return SubscribePaired(owner.GetLifetime(), add, remove, handler, member, line);
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> to an event the game does not own and removes it when the current
        /// generation of this component's lifetime ends. The type arguments must be spelled out.
        /// </summary>
        /// <typeparam name="T1">The first event argument type.</typeparam>
        /// <typeparam name="T2">The second event argument type.</typeparam>
        /// <typeparam name="T3">The third event argument type.</typeparam>
        /// <param name="owner">The owning component.</param>
        /// <param name="add">Adds the handler to the event.</param>
        /// <param name="remove">Removes the handler from the event.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException">Any argument is null; <paramref name="add"/> is not called.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static LifetimeRegistration Subscribe<T1, T2, T3>(this MonoBehaviour owner, Action<Action<T1, T2, T3>> add, Action<Action<T1, T2, T3>> remove, Action<T1, T2, T3> handler, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            return SubscribePaired(owner.GetLifetime(), add, remove, handler, member, line);
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> to an event the game does not own, for any delegate type, and removes
        /// it when the current generation of this component's lifetime ends. The delegate type must be spelled out.
        /// </summary>
        /// <typeparam name="TDelegate">The handler delegate type.</typeparam>
        /// <param name="owner">The owning component.</param>
        /// <param name="add">Adds the handler to the event.</param>
        /// <param name="remove">Removes the handler from the event.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException">Any argument is null; <paramref name="add"/> is not called.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static LifetimeRegistration Subscribe<TDelegate>(this MonoBehaviour owner, Action<TDelegate> add, Action<TDelegate> remove, TDelegate handler, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
            where TDelegate : Delegate
        {
            return SubscribePaired(owner.GetLifetime(), add, remove, handler, member, line);
        }
    }
}
