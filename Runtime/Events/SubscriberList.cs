using System;

namespace Ecanakli.Janitor
{
    internal enum InvokeEntry : byte
    {
        Empty,
        Entered,
        TooDeep,
    }

    // A null Handler marks a removed slot; Serial stays so the array remains sorted for the binary search.
    internal struct SubscriberSlot<THandler> where THandler : Delegate
    {
        internal long Serial;
        internal THandler Handler;
        internal Lifetime Owner;
        internal string Member;
        internal int Generation;
        internal int EntryId;
        internal int EntryVersion;
        internal int Line;
    }

    // Ordered subscriber slots of one event. Slots are appended in subscription order and each gets an ever-growing
    // serial, so the owner's entry can find its slot by binary search even after a compaction moved it.
    // While any Invoke runs no slot moves: removals blank the slot in place and compaction waits for the outermost
    // Invoke. Never copy this struct: always call it through the owning field.
    internal struct SubscriberList<THandler> where THandler : Delegate
    {
        private const int InitialCapacity = 4;

        // Lists up to this size compact on every removal; larger lists wait until a quarter of the slots are holes.
        private const int EagerCompactSize = 16;

        private SubscriberSlot<THandler>[] _slots;
        private LifetimeTree _tree;
        private long _nextSerial;
        private int _used;
        private int _removed;
        private int _depth;

        // Live subscriptions.
        internal int Count => _used - _removed;

        // Slots in use, holes included. Outside Invoke it equals Count for up to EagerCompactSize slots.
        internal int SlotCount => _used;

        // Invokes currently on the stack.
        internal int Depth => _depth;

        internal LifetimeRegistration Subscribe(THandler handler, Lifetime owner, string member, int line, IOwnedEventNode node)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            owner.Tree.Guard.EnsureMainThread("OwnedEvent.Subscribe");
            if (owner.State != LifetimeState.Active)
            {
                LifetimeDiagnostics.RegistrationRefused(owner, member, line);
                return default;
            }

            var duplicate = FindDuplicate(handler, owner);
            if (duplicate >= 0)
            {
                DevWarnings.DuplicateSubscription(node.Label, owner);
                ref var existing = ref _slots[duplicate];
                return new LifetimeRegistration(owner, existing.Generation, existing.EntryId, existing.EntryVersion);
            }

            // The slot exists before the entry, so a teardown during registration still finds it.
            _tree = owner.Tree;
            var serial = Append(handler, owner, member, line);
            var registration = owner.Register(node, null, null, OwnedEventEntry.Terminate, null, null, member, line, serial);
            SetEntry(serial, in registration);
            return registration;
        }

        // Called by the owner's terminate action. Unknown or repeated serials are no-ops.
        internal void RemoveBySerial(long serial)
        {
            var index = Find(serial);
            if (index < 0)
            {
                return;
            }

            ref var slot = ref _slots[index];
            if (IsRemoved(in slot))
            {
                return;
            }

            slot.Handler = null;
            slot.Owner = null;
            slot.Member = null;
            _removed++;
            if (_depth == 0)
            {
                CompactIfNeeded();
            }
        }

        // end is the slot count at entry: slots appended during this Invoke are delivered by the next one.
        internal InvokeEntry TryEnter(out int end)
        {
            end = _used;
            if (_used == 0)
            {
                return InvokeEntry.Empty;
            }

            if (_depth >= OwnedEventEntry.MaxInvokeDepth)
            {
                return InvokeEntry.TooDeep;
            }

            _depth++;
            return InvokeEntry.Entered;
        }

        // False for a removed slot and for a subscriber whose owner ended or moved to another generation.
        internal bool TryGetDeliverable(int index, out THandler handler, out Lifetime owner, out string member, out int line)
        {
            handler = null;
            owner = null;
            member = null;
            line = 0;
            if (index >= _used)
            {
                return false;
            }

            ref var slot = ref _slots[index];
            if (IsRemoved(in slot))
            {
                return false;
            }

            var slotOwner = slot.Owner;
            if (slotOwner.State != LifetimeState.Active || slotOwner.Generation != slot.Generation)
            {
                return false;
            }

            handler = slot.Handler;
            owner = slotOwner;
            member = slot.Member;
            line = slot.Line;
            return true;
        }

        internal void Exit()
        {
            _depth--;
            if (_depth == 0 && _removed != 0)
            {
                CompactIfNeeded();
            }
        }

        // Only a list that has subscribers knows a tree; an empty event has nothing to guard.
        internal void EnsureInvokeThread()
        {
            if (_tree != null)
            {
                _tree.Guard.EnsureMainThread("OwnedEvent.Invoke");
            }
        }

        private long Append(THandler handler, Lifetime owner, string member, int line)
        {
            if (_slots == null)
            {
                _slots = new SubscriberSlot<THandler>[InitialCapacity];
            }
            else if (_used == _slots.Length)
            {
                Array.Resize(ref _slots, _used * 2);
            }

            var serial = ++_nextSerial;
            ref var slot = ref _slots[_used++];
            slot.Serial = serial;
            slot.Handler = handler;
            slot.Owner = owner;
            slot.Member = member;
            slot.Generation = owner.Generation;
            slot.EntryId = 0;
            slot.EntryVersion = 0;
            slot.Line = line;
            return serial;
        }

        private void SetEntry(long serial, in LifetimeRegistration registration)
        {
            var index = Find(serial);
            if (index < 0)
            {
                return;
            }

            ref var slot = ref _slots[index];
            if (IsRemoved(in slot))
            {
                return;
            }

            slot.EntryId = registration.EntryId;
            slot.EntryVersion = registration.EntryVersion;
        }

        // Same handler and same live owner generation. Delegates are compared only for slots of that owner.
        private int FindDuplicate(THandler handler, Lifetime owner)
        {
            var generation = owner.Generation;
            for (var i = 0; i < _used; i++)
            {
                ref var slot = ref _slots[i];
                if (!IsRemoved(in slot)
                    && ReferenceEquals(slot.Owner, owner)
                    && slot.Generation == generation
                    && SameHandler(slot.Handler, handler))
                {
                    return i;
                }
            }

            return -1;
        }

        // Reference comparison on purpose: == on a Delegate-constrained type would call the virtual Equals.
        private static bool IsRemoved(in SubscriberSlot<THandler> slot)
        {
            return ReferenceEquals(slot.Handler, null);
        }

        // Equal means same target and same method. Target is compared first because it is cheap.
        private static bool SameHandler(THandler existing, THandler candidate)
        {
            if (ReferenceEquals(existing, candidate))
            {
                return true;
            }

            return ReferenceEquals(existing.Target, candidate.Target) && existing.Equals(candidate);
        }

        private int Find(long serial)
        {
            var low = 0;
            var high = _used - 1;
            while (low <= high)
            {
                var middle = low + ((high - low) >> 1);
                var value = _slots[middle].Serial;
                if (value == serial)
                {
                    return middle;
                }

                if (value < serial)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return -1;
        }

        // Runs only when no Invoke is on the stack. Trailing holes are trimmed at once.
        private void CompactIfNeeded()
        {
            while (_used > 0 && IsRemoved(in _slots[_used - 1]))
            {
                _slots[--_used] = default;
                _removed--;
            }

            if (_removed != 0 && (_used <= EagerCompactSize || _removed * 4 >= _used))
            {
                Compact();
            }
        }

        // Order-preserving: live slots slide down over the holes.
        private void Compact()
        {
            var write = 0;
            for (var read = 0; read < _used; read++)
            {
                if (IsRemoved(in _slots[read]))
                {
                    continue;
                }

                if (write != read)
                {
                    _slots[write] = _slots[read];
                }

                write++;
            }

            Array.Clear(_slots, write, _used - write);
            _used = write;
            _removed = 0;
        }
    }
}
