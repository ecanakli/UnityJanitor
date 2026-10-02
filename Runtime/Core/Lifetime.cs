using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Ecanakli.Janitor
{
    /// <summary>
    /// Owns cleanup work. <see cref="Cancel"/> stops everything registered in this lifetime and its descendants
    /// and keeps the lifetime usable; <see cref="Dispose"/> ends an area for good. Main thread only, except
    /// <see cref="Cancel"/> and <see cref="Dispose"/>, which are marshalled to the main thread.
    /// </summary>
    public sealed partial class Lifetime : IDisposable
    {
        /// <summary>The app-wide root lifetime. It is package-owned: <see cref="Dispose"/> is ignored on it.</summary>
        /// <exception cref="InvalidOperationException">No lifetime tree exists, which is the case outside Play Mode.</exception>
        public static Lifetime App
        {
            get { return LifetimeTree.RequireDefault("Lifetime.App").App; }
        }

        /// <summary>A display name for diagnostics. It may be null and is never used for lookup.</summary>
        public string Name => _name;

        /// <summary>True once this lifetime has been disposed. Off the main thread the value may be stale.</summary>
        public bool IsDisposed => _state == LifetimeState.Disposed;

        /// <summary>
        /// The token of the current generation. It is cancelled by the next <see cref="Cancel"/> or
        /// <see cref="Dispose"/> and stays cancelled forever; after a <see cref="Cancel"/> this property returns a
        /// different token. While the lifetime is ending or disposed it returns an already cancelled token. So does
        /// an active lifetime, or an area created below one, while its GameObject is inactive.
        /// </summary>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public CancellationToken Token
        {
            get
            {
                Tree.Guard.EnsureMainThread("Lifetime.Token");
                if (_state != LifetimeState.Active)
                {
                    return new CancellationToken(true);
                }

                var gate = ActivityGate;
                if (gate != null && !gate.IsOwnerActive())
                {
                    return new CancellationToken(true);
                }

                return CurrentToken();
            }
        }

        /// <summary>
        /// Creates an area under this lifetime. Cancelling this lifetime cancels the area; disposing it disposes the area.
        /// An area below an active lifetime accepts work only while the GameObject of that active lifetime is active.
        /// </summary>
        /// <param name="name">An optional display name. Unnamed areas are labelled by call site.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>The new area. When this lifetime is already disposed, the area is born disposed.</returns>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public Lifetime CreateChild(string name = null, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            Tree.Guard.EnsureMainThread("Lifetime.CreateChild");
            return CreateChildCore(name, LifetimeKind.Area, false, null, member, line);
        }

        /// <summary>
        /// Ends the current generation of this lifetime and of every descendant, then opens fresh generations.
        /// All tokens are cancelled first, then items are terminated deepest first and newest first. That order holds
        /// within one operation: a Cancel or Dispose that a cancel action starts runs to its end at once, before the
        /// remaining items of the operation that is running. The lifetimes stay attached and usable. Does nothing
        /// when this lifetime is already ending. Never throws; failures are routed to
        /// <see cref="LifetimeErrors.Handler"/>. Off the main thread it is marshalled.
        /// </summary>
        public void Cancel()
        {
            if (!Tree.Guard.IsMainThread)
            {
                Tree.Marshal.Enqueue(MarshalOp.Cancel, this);
                DevWarnings.Marshalled("Lifetime.Cancel()", this);
                return;
            }

            CancelOnMainThread();
        }

        /// <summary>
        /// Ends this lifetime and every descendant area for good. An object lifetime that was placed here through
        /// <c>GetLifetime(parent)</c> is cancelled and moved under its scene lifetime instead, because this lifetime
        /// does not own the object. Ignored, with a development warning, on package-owned lifetimes such as
        /// <see cref="App"/>. Idempotent. Never throws; failures are routed to <see cref="LifetimeErrors.Handler"/>.
        /// Off the main thread it is marshalled.
        /// </summary>
        public void Dispose()
        {
            if (!Tree.Guard.IsMainThread)
            {
                Tree.Marshal.Enqueue(MarshalOp.Dispose, this);
                DevWarnings.Marshalled("Lifetime.Dispose()", this);
                return;
            }

            DisposeOnMainThread();
        }

        /// <summary>Runs <paramref name="action"/> when the current generation ends. Custom cleanup only; it is never the way to unsubscribe.</summary>
        /// <param name="action">The cleanup. It runs immediately when this lifetime is already ending, or when its GameObject is inactive.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that cancels this one item; default when the action already ran.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public LifetimeRegistration OnCancel(Action action, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            return Register(null, null, action, EntryInvoker.InvokeAction, null, null, member, line);
        }

        /// <summary>Runs <paramref name="action"/> with <paramref name="state"/> when the current generation ends. A static action allocates nothing.</summary>
        /// <typeparam name="TState">The state type.</typeparam>
        /// <param name="state">Passed to the action.</param>
        /// <param name="action">The cleanup. It runs immediately when this lifetime is already ending, or when its GameObject is inactive.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that cancels this one item; default when the action already ran.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public LifetimeRegistration OnCancel<TState>(TState state, Action<TState> action, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
            where TState : class
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            return Register(state, null, action, EntryInvoker<TState>.Terminate, null, null, member, line);
        }

        /// <summary>
        /// Like <see cref="OnCancel{TState}(TState, Action{TState}, string, int)"/>, with a probe. When a sweep
        /// finds the probe true the item is dropped without running the action, so finished one-shot items do not
        /// pile up. A <see cref="Cancel"/> or <see cref="Dispose"/> before the next sweep still runs the action, so
        /// it must tolerate an item that already finished.
        /// </summary>
        /// <typeparam name="TState">The state type.</typeparam>
        /// <param name="state">Passed to the action and to the probe.</param>
        /// <param name="action">The cleanup. It runs immediately when this lifetime is already ending, or when its GameObject is inactive.</param>
        /// <param name="isFinished">Returns true when the item no longer needs cleanup. It runs during amortized sweeps only.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that cancels this one item; default when the action already ran.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> or <paramref name="isFinished"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public LifetimeRegistration OnCancel<TState>(TState state, Action<TState> action, Func<TState, bool> isFinished, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
            where TState : class
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (isFinished == null)
            {
                throw new ArgumentNullException(nameof(isFinished));
            }

            return Register(state, null, action, EntryInvoker<TState>.Terminate, isFinished, EntryInvoker<TState>.IsFinished, member, line);
        }

        /// <summary>Runs <paramref name="action"/> with two state values when the current generation ends. A static action allocates nothing.</summary>
        /// <typeparam name="T1">The first state type.</typeparam>
        /// <typeparam name="T2">The second state type.</typeparam>
        /// <param name="first">Passed to the action.</param>
        /// <param name="second">Passed to the action.</param>
        /// <param name="action">The cleanup. It runs immediately when this lifetime is already ending, or when its GameObject is inactive.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that cancels this one item; default when the action already ran.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public LifetimeRegistration OnCancel<T1, T2>(T1 first, T2 second, Action<T1, T2> action, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
            where T1 : class
            where T2 : class
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            return Register(first, second, action, EntryInvoker<T1, T2>.Terminate, null, null, member, line);
        }
    }
}
