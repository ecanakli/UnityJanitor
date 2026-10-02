using System;
using System.Runtime.CompilerServices;

namespace Ecanakli.Janitor
{
    /// <summary>Extension methods that bind subscriptions and disposables to a <see cref="Lifetime"/>.</summary>
    public static partial class LifetimeSubscriptionExtensions
    {
        /// <summary>
        /// Disposes <paramref name="disposable"/> when the current generation of <paramref name="lifetime"/> ends.
        /// A class instance allocates nothing; a struct is boxed once.
        /// </summary>
        /// <typeparam name="T">The disposable type.</typeparam>
        /// <param name="disposable">The item to dispose. It is disposed immediately when the lifetime is already ending, or when its GameObject is inactive.</param>
        /// <param name="lifetime">The owner.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that disposes this one item; default when it was disposed immediately.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="lifetime"/> is null (the item is disposed first) or <paramref name="disposable"/> is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public static LifetimeRegistration AddTo<T>(this T disposable, Lifetime lifetime, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
            where T : IDisposable
        {
            if (lifetime == null)
            {
                DisposeQuietly(disposable, member, line);
                throw new ArgumentNullException(nameof(lifetime));
            }

            if (disposable == null)
            {
                throw new ArgumentNullException(nameof(disposable));
            }

            return lifetime.Register(disposable, null, null, EntryInvoker.DisposeItem, null, null, member, line);
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> to an event the game does not own and removes it when the current
        /// generation of <paramref name="owner"/> ends. <paramref name="add"/> runs immediately when the owner is
        /// active; the package calls <paramref name="remove"/> with the same handler exactly once. The package
        /// allocates nothing; the caller's lambdas allocate once, or nothing when they are static. Calling it again
        /// with the same add, remove and handler (each the same target and method) on the same owner returns the
        /// existing registration, adds nothing and raises the development warning JANITOR105. Lambdas that capture a
        /// local variable are new objects on every call, so such a repeat is not recognised.
        /// </summary>
        /// <param name="owner">The owner that decides how long the subscription lives.</param>
        /// <param name="add">Adds the handler to the event, for example <c>h => source.Changed += h</c>.</param>
        /// <param name="remove">Removes the handler from the event, for example <c>h => source.Changed -= h</c>.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException">Any argument is null; <paramref name="add"/> is not called.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public static LifetimeRegistration Subscribe(this Lifetime owner, Action<Action> add, Action<Action> remove, Action handler, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            return SubscribePaired(owner, add, remove, handler, member, line);
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> to an event the game does not own and removes it when the current
        /// generation of <paramref name="owner"/> ends. <paramref name="add"/> runs immediately when the owner is
        /// active; the package calls <paramref name="remove"/> with the same handler exactly once. The package
        /// allocates nothing; the caller's lambdas allocate once, or nothing when they are static. The type
        /// argument must be spelled out, because it cannot be inferred from a lambda or a method group. A repeat of
        /// the same add, remove and handler on the same owner is ignored, as in the non-generic form.
        /// </summary>
        /// <typeparam name="T">The event argument type.</typeparam>
        /// <param name="owner">The owner that decides how long the subscription lives.</param>
        /// <param name="add">Adds the handler to the event.</param>
        /// <param name="remove">Removes the handler from the event.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException">Any argument is null; <paramref name="add"/> is not called.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public static LifetimeRegistration Subscribe<T>(this Lifetime owner, Action<Action<T>> add, Action<Action<T>> remove, Action<T> handler, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            return SubscribePaired(owner, add, remove, handler, member, line);
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> to an event the game does not own and removes it when the current
        /// generation of <paramref name="owner"/> ends. <paramref name="add"/> runs immediately when the owner is
        /// active; the package calls <paramref name="remove"/> with the same handler exactly once. The package
        /// allocates nothing; the caller's lambdas allocate once, or nothing when they are static. The type
        /// arguments must be spelled out, because they cannot be inferred from a lambda or a method group. A repeat
        /// of the same add, remove and handler on the same owner is ignored, as in the non-generic form.
        /// </summary>
        /// <typeparam name="T1">The first event argument type.</typeparam>
        /// <typeparam name="T2">The second event argument type.</typeparam>
        /// <param name="owner">The owner that decides how long the subscription lives.</param>
        /// <param name="add">Adds the handler to the event.</param>
        /// <param name="remove">Removes the handler from the event.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException">Any argument is null; <paramref name="add"/> is not called.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public static LifetimeRegistration Subscribe<T1, T2>(this Lifetime owner, Action<Action<T1, T2>> add, Action<Action<T1, T2>> remove, Action<T1, T2> handler, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            return SubscribePaired(owner, add, remove, handler, member, line);
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> to an event the game does not own and removes it when the current
        /// generation of <paramref name="owner"/> ends. <paramref name="add"/> runs immediately when the owner is
        /// active; the package calls <paramref name="remove"/> with the same handler exactly once. The package
        /// allocates nothing; the caller's lambdas allocate once, or nothing when they are static. The type
        /// arguments must be spelled out, because they cannot be inferred from a lambda or a method group. A repeat
        /// of the same add, remove and handler on the same owner is ignored, as in the non-generic form.
        /// </summary>
        /// <typeparam name="T1">The first event argument type.</typeparam>
        /// <typeparam name="T2">The second event argument type.</typeparam>
        /// <typeparam name="T3">The third event argument type.</typeparam>
        /// <param name="owner">The owner that decides how long the subscription lives.</param>
        /// <param name="add">Adds the handler to the event.</param>
        /// <param name="remove">Removes the handler from the event.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException">Any argument is null; <paramref name="add"/> is not called.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public static LifetimeRegistration Subscribe<T1, T2, T3>(this Lifetime owner, Action<Action<T1, T2, T3>> add, Action<Action<T1, T2, T3>> remove, Action<T1, T2, T3> handler, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            return SubscribePaired(owner, add, remove, handler, member, line);
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> to an event the game does not own, for any delegate type such as a
        /// custom callback delegate. <paramref name="add"/> runs immediately when the owner is active; the package
        /// calls <paramref name="remove"/> with the same handler exactly once, when the current generation of
        /// <paramref name="owner"/> ends. The delegate type must be spelled out, because it cannot be inferred from a lambda.
        /// A repeat of the same add, remove and handler on the same owner is ignored, as in the non-generic form.
        /// </summary>
        /// <typeparam name="TDelegate">The handler delegate type.</typeparam>
        /// <param name="owner">The owner that decides how long the subscription lives.</param>
        /// <param name="add">Adds the handler to the event.</param>
        /// <param name="remove">Removes the handler from the event.</param>
        /// <param name="handler">The handler.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that removes this one subscription; default when nothing was added.</returns>
        /// <exception cref="ArgumentNullException">Any argument is null; <paramref name="add"/> is not called.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public static LifetimeRegistration Subscribe<TDelegate>(this Lifetime owner, Action<TDelegate> add, Action<TDelegate> remove, TDelegate handler, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
            where TDelegate : Delegate
        {
            return SubscribePaired(owner, add, remove, handler, member, line);
        }

        // One path for all five forms. The entry stores the handler, the add and the remove delegate; the shared
        // EntryInvoker<THandler>.PairedTerminate calls remove(handler) exactly once.
        private static LifetimeRegistration SubscribePaired<THandler>(Lifetime owner, Action<THandler> add, Action<THandler> remove, THandler handler, string member, int line)
            where THandler : Delegate
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (add == null)
            {
                throw new ArgumentNullException(nameof(add));
            }

            if (remove == null)
            {
                throw new ArgumentNullException(nameof(remove));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            owner.Tree.Guard.EnsureMainThread("Lifetime.Subscribe");
            if (owner.State != LifetimeState.Active)
            {
                LifetimeDiagnostics.RegistrationRefused(owner, member, line);
                return default;
            }

            if (TryFindPaired(owner, add, remove, handler, out var existing))
            {
                return existing;
            }

            var generation = owner.Generation;
            try
            {
                add(handler);
            }
            catch (Exception exception)
            {
                owner.Tree.ReportError(exception, LifetimeErrorSource.EventHandler, owner, member, line);
                return default;
            }

            // A generation that ended inside add would leave Register tying the handler to the next one.
            if (owner.Generation != generation)
            {
                RemoveQuietly(remove, handler, owner, member, line);
                return default;
            }

            return owner.Register(handler, add, remove, EntryInvoker<THandler>.PairedTerminate, null, null, member, line, EntryAux.Paired);
        }

        // A live entry with the same add, remove and handler (same target and method each) makes this call a repeat.
        private static bool TryFindPaired<THandler>(Lifetime owner, Action<THandler> add, Action<THandler> remove, THandler handler, out LifetimeRegistration existing)
            where THandler : Delegate
        {
            var terminate = EntryInvoker<THandler>.PairedTerminate;
            var id = owner.NewestEntryId;
            while (id != 0)
            {
                ref var entry = ref owner.EntryAt(id);
                if (ReferenceEquals(entry.Terminate, terminate)
                    && SameDelegate((Delegate)entry.A, handler)
                    && SameDelegate((Delegate)entry.B, add)
                    && SameDelegate(entry.Fn, remove))
                {
                    DevWarnings.DuplicateSubscription("Paired Subscribe", owner);
                    existing = new LifetimeRegistration(owner, owner.Generation, id, entry.Version);
                    return true;
                }

                id = entry.Next;
            }

            existing = default;
            return false;
        }

        // Equal means same target and same method. Target is compared first because it is cheap.
        private static bool SameDelegate(Delegate existing, Delegate candidate)
        {
            if (ReferenceEquals(existing, candidate))
            {
                return true;
            }

            return ReferenceEquals(existing.Target, candidate.Target) && existing.Equals(candidate);
        }

        // Same failure handling as a terminate action: routed, never thrown.
        private static void RemoveQuietly<THandler>(Action<THandler> remove, THandler handler, Lifetime owner, string member, int line)
            where THandler : Delegate
        {
            try
            {
                remove(handler);
            }
            catch (Exception exception)
            {
                owner.Tree.ReportError(exception, LifetimeErrorSource.CancelAction, owner, member, line);
            }
        }

        // The item must not outlive a missing owner; a failing Dispose is routed like any terminate action.
        private static void DisposeQuietly<T>(T disposable, string member, int line)
            where T : IDisposable
        {
            if (disposable == null)
            {
                return;
            }

            try
            {
                disposable.Dispose();
            }
            catch (Exception exception)
            {
                var context = new LifetimeErrorContext(LifetimeErrorSource.CancelAction, null, null, member, line);
                LifetimeErrors.Dispatch(exception, in context);
            }
        }
    }
}
