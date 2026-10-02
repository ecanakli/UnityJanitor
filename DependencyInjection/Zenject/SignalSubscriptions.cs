using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Zenject;

namespace Ecanakli.Janitor.DependencyInjection
{
    // The live SignalBus subscriptions made through LifetimeSignalBusExtensions, one table per bus, keyed by signal type and handler.
    // SignalBus keeps exactly one subscription per (signal, handler), and in a player build a second one is not rejected
    // cleanly (its assert is editor-only), so a repeat must be caught before it reaches the bus. The core finds duplicates by
    // scanning the owner's internal entry list; that is not reachable from here, hence this table.
    // The tables hang off the bus weakly: a bus that is gone takes its table with it, so this class never keeps a bus
    // (or the container that owns it) alive. Every termination path removes its record; see Forget.
    internal static class SignalSubscriptions
    {
        private static ConditionalWeakTable<SignalBus, BusTable> _tables = new ConditionalWeakTable<SignalBus, BusTable>();

        // Records over all tables; a test hook and the leak check of the test sessions.
        private static int _liveCount;

        // The handler being armed or undone on this thread. A terminate that sees it is the core refusing a registration
        // (owner not Active) or this class undoing one; nothing was subscribed, so it must not touch the bus.
        [ThreadStatic]
        private static object _arming;

        internal static int LiveCount => _liveCount;

        // Domain reload may be off: a new session starts with no subscriptions (the old tree was disposed already).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetSession()
        {
            _tables = new ConditionalWeakTable<SignalBus, BusTable>();
            _liveCount = 0;
            _arming = null;
        }

        internal static bool IsArming(object handler)
        {
            return ReferenceEquals(_arming, handler);
        }

        // Removes the record of a terminated subscription. At most one live record exists per key, so the key identifies it.
        // Called first thing in every terminate; an owner ending, a registration cancel and a tree shutdown all end there.
        internal static void Forget(SignalBus bus, Type signal, Delegate handler)
        {
            if (_tables.TryGetValue(bus, out var table) && table.Records.Remove(new SignalKey(signal, handler)))
            {
                _liveCount--;
            }
        }

        // Order: repeat check, register on the owner (an owner that is not Active refuses it), then subscribe to the bus.
        // Registering first means a refused owner costs nothing on the bus and the bus is never subscribed without an owner entry.
        internal static LifetimeRegistration Subscribe<TSignal, THandler>(
            SignalBus bus,
            THandler handler,
            Lifetime owner,
            Action<SignalBus, THandler> subscribe,
            Action<SignalBus, THandler> terminate,
            string member,
            int line)
            where THandler : Delegate
        {
            ZenjectDiagnostics.EnsureMainThread("SignalBus.Subscribe");

            var key = new SignalKey(typeof(TSignal), handler);
            _tables.TryGetValue(bus, out var table);
            if (table != null && table.Records.TryGetValue(key, out var existing))
            {
                if (existing.Registration.IsActive)
                {
                    if (!ReferenceEquals(existing.Owner, owner))
                    {
                        throw new InvalidOperationException(
                            "Janitor: this handler is already subscribed to Signal<" + typeof(TSignal).Name + "> on this SignalBus for the lifetime '"
                            + (existing.Owner.Name ?? "unnamed") + "'. SignalBus keeps one subscription per handler, so it cannot also belong to '"
                            + (owner.Name ?? "unnamed") + "'. Subscribe a different handler instance, or use the same owner.");
                    }

                    ZenjectDiagnostics.DuplicateSignalSubscription(typeof(TSignal), owner);
                    return existing.Registration;
                }

                // A record whose registration ended without its terminate running; cannot be live.
                table.Records.Remove(key);
                _liveCount--;
            }

            var registration = Arm(owner, bus, handler, terminate, member, line);
            if (!registration.IsActive)
            {
                // The owner is not Active: the core already ran the (disarmed) terminate, and nothing was subscribed.
                return default;
            }

            try
            {
                subscribe(bus, handler);
            }
            catch
            {
                // Undeclared signal or a handler SignalBus already holds: undo the owner entry without touching the bus, then let the caller see it.
                Disarm(registration, handler);
                throw;
            }

            // Ended during the bus call: the terminate ran (and unsubscribed), so there is nothing to record.
            if (!registration.IsActive)
            {
                return registration;
            }

            if (table == null)
            {
                table = new BusTable();
                _tables.Add(bus, table);
            }

            table.Records[key] = new SignalRecord(owner, registration);
            _liveCount++;
            return registration;
        }

        // The core runs the terminate inside this call when the owner is not Active; the arming marker turns that run into a no-op.
        private static LifetimeRegistration Arm<THandler>(Lifetime owner, SignalBus bus, THandler handler, Action<SignalBus, THandler> terminate, string member, int line)
            where THandler : Delegate
        {
            var previous = _arming;
            _arming = handler;
            try
            {
                return owner.OnCancel<SignalBus, THandler>(bus, handler, terminate, member, line);
            }
            finally
            {
                _arming = previous;
            }
        }

        private static void Disarm(LifetimeRegistration registration, object handler)
        {
            var previous = _arming;
            _arming = handler;
            try
            {
                registration.Cancel();
            }
            finally
            {
                _arming = previous;
            }
        }

        private sealed class BusTable
        {
            internal readonly Dictionary<SignalKey, SignalRecord> Records = new Dictionary<SignalKey, SignalRecord>();
        }

        private readonly struct SignalKey : IEquatable<SignalKey>
        {
            private readonly Type _signal;
            private readonly Delegate _handler;

            internal SignalKey(Type signal, Delegate handler)
            {
                _signal = signal;
                _handler = handler;
            }

            public bool Equals(SignalKey other)
            {
                return _signal == other._signal && SameHandler(_handler, other._handler);
            }

            public override bool Equals(object obj)
            {
                return obj is SignalKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (_signal.GetHashCode() * 31) + _handler.GetHashCode();
                }
            }

            // The same rule SignalBus uses for its own duplicate check: an equal delegate (same target and method). Target first, it is cheap.
            private static bool SameHandler(Delegate first, Delegate second)
            {
                if (ReferenceEquals(first, second))
                {
                    return true;
                }

                return ReferenceEquals(first.Target, second.Target) && first.Equals(second);
            }
        }

        private readonly struct SignalRecord
        {
            internal readonly Lifetime Owner;
            internal readonly LifetimeRegistration Registration;

            internal SignalRecord(Lifetime owner, LifetimeRegistration registration)
            {
                Owner = owner;
                Registration = registration;
            }
        }
    }
}
