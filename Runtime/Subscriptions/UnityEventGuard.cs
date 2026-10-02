using System;
using UnityEngine.Events;

namespace Ecanakli.Janitor
{
    // UnityEvent dispatches over a snapshot, so a listener removed during Invoke still runs in that Invoke.
    // The guard is the listener that gets added instead of the raw handler: it calls the handler only while its
    // registration is live, and termination removes exactly this guard. The owner's entry stores the guard.
    // Allocation: the guard plus its listener delegate, once per Subscribe; nothing per Invoke.
    internal abstract class UnityEventGuardBase
    {
        internal static readonly EntryTerminate Terminate = TerminateCore;

        private readonly Lifetime _owner;

        // Unity skips a listener whose target Object was destroyed; the guard keeps that behaviour for the wrapped handler.
        private readonly UnityEngine.Object _unityTarget;
        private readonly bool _hasUnityTarget;

        private int _generation;
        private bool _live;
        private bool _ended;

        protected UnityEventGuardBase(Lifetime owner, Delegate handler)
        {
            _owner = owner;
            _unityTarget = handler.Target as UnityEngine.Object;
            _hasUnityTarget = !ReferenceEquals(_unityTarget, null);
        }

        // Display name of the event, built on demand (duplicate warnings and diagnostics only).
        internal abstract string Label { get; }

        // True when this guard wraps the same event and an equal handler (same target and method).
        internal abstract bool IsSame(object source, Delegate handler);

        protected abstract void RemoveListener();

        // Shared front half of every Subscribe form. False means the call is finished and result is what to return.
        internal static bool TryBegin(object source, Delegate handler, Lifetime owner, string member, int line, out LifetimeRegistration result)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            owner.Tree.Guard.EnsureMainThread("UnityEvent.Subscribe");
            result = default;

            // A Cancelling or Disposed owner adds nothing.
            if (owner.State != LifetimeState.Active)
            {
                LifetimeDiagnostics.RegistrationRefused(owner, member, line);
                return false;
            }

            return !TryFindDuplicate(owner, source, handler, out result);
        }

        // Adds the guard to the owner and arms it. Call after the guard's listener was added to the event.
        internal LifetimeRegistration Attach(string member, int line)
        {
            var generation = _owner.Generation;
            var registration = _owner.Register(this, null, null, Terminate, null, null, member, line);

            // A refusal, or a sweep probe that ended the generation, has already run Terminate and removed the listener.
            if (!_ended)
            {
                _generation = generation;
                _live = true;
            }

            return registration;
        }

        // Same handler, same event, same owner: the owner's own entries are the only place a duplicate can live.
        private static bool TryFindDuplicate(Lifetime owner, object source, Delegate handler, out LifetimeRegistration existing)
        {
            var id = owner.NewestEntryId;
            while (id != 0)
            {
                ref var entry = ref owner.EntryAt(id);
                if (ReferenceEquals(entry.Terminate, Terminate))
                {
                    var guard = (UnityEventGuardBase)entry.A;
                    if (guard.IsSame(source, handler))
                    {
                        DevWarnings.DuplicateSubscription(guard.Label, owner);
                        existing = new LifetimeRegistration(owner, owner.Generation, id, entry.Version);
                        return true;
                    }
                }

                id = entry.Next;
            }

            existing = default;
            return false;
        }

        // Equal means same target and same method. Target is compared first because it is cheap.
        protected static bool Same(Delegate existing, Delegate candidate)
        {
            if (ReferenceEquals(existing, candidate))
            {
                return true;
            }

            return ReferenceEquals(existing.Target, candidate.Target) && existing.Equals(candidate);
        }

        // True while the subscription is live, its owner is Active in the same generation, and the handler's target still exists.
        protected bool CanDeliver()
        {
            if (!_live || _owner.State != LifetimeState.Active || _owner.Generation != _generation)
            {
                return false;
            }

            return !_hasUnityTarget || _unityTarget != null;
        }

        private void End()
        {
            if (_ended)
            {
                return;
            }

            _ended = true;
            _live = false;
            RemoveListener();
        }

        private static void TerminateCore(object first, object second, Delegate fn, long aux)
        {
            ((UnityEventGuardBase)first).End();
        }
    }

    internal sealed class UnityEventGuard : UnityEventGuardBase
    {
        internal readonly UnityAction Listener;

        private readonly UnityEvent _event;
        private readonly UnityAction _handler;

        internal UnityEventGuard(UnityEvent source, UnityAction handler, Lifetime owner)
            : base(owner, handler)
        {
            _event = source;
            _handler = handler;
            Listener = Invoke;
        }

        internal override string Label => "UnityEvent";

        internal override bool IsSame(object source, Delegate handler)
        {
            return ReferenceEquals(_event, source) && Same(_handler, handler);
        }

        protected override void RemoveListener()
        {
            _event.RemoveListener(Listener);
        }

        private void Invoke()
        {
            if (CanDeliver())
            {
                _handler();
            }
        }
    }

    internal sealed class UnityEventGuard<T0> : UnityEventGuardBase
    {
        internal readonly UnityAction<T0> Listener;

        private readonly UnityEvent<T0> _event;
        private readonly UnityAction<T0> _handler;

        internal UnityEventGuard(UnityEvent<T0> source, UnityAction<T0> handler, Lifetime owner)
            : base(owner, handler)
        {
            _event = source;
            _handler = handler;
            Listener = Invoke;
        }

        internal override string Label => "UnityEvent<" + typeof(T0).Name + ">";

        internal override bool IsSame(object source, Delegate handler)
        {
            return ReferenceEquals(_event, source) && Same(_handler, handler);
        }

        protected override void RemoveListener()
        {
            _event.RemoveListener(Listener);
        }

        private void Invoke(T0 arg0)
        {
            if (CanDeliver())
            {
                _handler(arg0);
            }
        }
    }

    internal sealed class UnityEventGuard<T0, T1> : UnityEventGuardBase
    {
        internal readonly UnityAction<T0, T1> Listener;

        private readonly UnityEvent<T0, T1> _event;
        private readonly UnityAction<T0, T1> _handler;

        internal UnityEventGuard(UnityEvent<T0, T1> source, UnityAction<T0, T1> handler, Lifetime owner)
            : base(owner, handler)
        {
            _event = source;
            _handler = handler;
            Listener = Invoke;
        }

        internal override string Label => "UnityEvent<" + typeof(T0).Name + ", " + typeof(T1).Name + ">";

        internal override bool IsSame(object source, Delegate handler)
        {
            return ReferenceEquals(_event, source) && Same(_handler, handler);
        }

        protected override void RemoveListener()
        {
            _event.RemoveListener(Listener);
        }

        private void Invoke(T0 arg0, T1 arg1)
        {
            if (CanDeliver())
            {
                _handler(arg0, arg1);
            }
        }
    }

    internal sealed class UnityEventGuard<T0, T1, T2> : UnityEventGuardBase
    {
        internal readonly UnityAction<T0, T1, T2> Listener;

        private readonly UnityEvent<T0, T1, T2> _event;
        private readonly UnityAction<T0, T1, T2> _handler;

        internal UnityEventGuard(UnityEvent<T0, T1, T2> source, UnityAction<T0, T1, T2> handler, Lifetime owner)
            : base(owner, handler)
        {
            _event = source;
            _handler = handler;
            Listener = Invoke;
        }

        internal override string Label => "UnityEvent<" + typeof(T0).Name + ", " + typeof(T1).Name + ", " + typeof(T2).Name + ">";

        internal override bool IsSame(object source, Delegate handler)
        {
            return ReferenceEquals(_event, source) && Same(_handler, handler);
        }

        protected override void RemoveListener()
        {
            _event.RemoveListener(Listener);
        }

        private void Invoke(T0 arg0, T1 arg1, T2 arg2)
        {
            if (CanDeliver())
            {
                _handler(arg0, arg1, arg2);
            }
        }
    }

    internal sealed class UnityEventGuard<T0, T1, T2, T3> : UnityEventGuardBase
    {
        internal readonly UnityAction<T0, T1, T2, T3> Listener;

        private readonly UnityEvent<T0, T1, T2, T3> _event;
        private readonly UnityAction<T0, T1, T2, T3> _handler;

        internal UnityEventGuard(UnityEvent<T0, T1, T2, T3> source, UnityAction<T0, T1, T2, T3> handler, Lifetime owner)
            : base(owner, handler)
        {
            _event = source;
            _handler = handler;
            Listener = Invoke;
        }

        internal override string Label => "UnityEvent<" + typeof(T0).Name + ", " + typeof(T1).Name + ", " + typeof(T2).Name + ", " + typeof(T3).Name + ">";

        internal override bool IsSame(object source, Delegate handler)
        {
            return ReferenceEquals(_event, source) && Same(_handler, handler);
        }

        protected override void RemoveListener()
        {
            _event.RemoveListener(Listener);
        }

        private void Invoke(T0 arg0, T1 arg1, T2 arg2, T3 arg3)
        {
            if (CanDeliver())
            {
                _handler(arg0, arg1, arg2, arg3);
            }
        }
    }
}
