using System;

namespace Ecanakli.Janitor
{
    // One registered item. Ids are 1-based; 0 means none.
    internal struct EntrySlot
    {
        internal object A;
        internal object B;
        internal Delegate Fn;
        internal Delegate Probe;
        internal EntryTerminate Terminate;
        internal EntryProbe ProbeInvoker;
        internal string Member;
        internal long Aux;
        internal int Line;
        internal int Prev;
        internal int Next;
        internal int Version;
        internal int Flags;
#if UNITY_EDITOR
        // Editor-only diagnostics: the registration frame plus one (0 means not stamped), and the opt-in stack trace.
        internal int DiagFrame;
        internal object DiagTrace;
#endif
    }

    // Array-backed doubly linked list with a free list. Head is the newest entry (LIFO drain).
    // Never copy this struct: always call it through the owning field.
    internal struct EntryList
    {
        internal const int MinSweepThreshold = 16;

        private const int InitialCapacity = 4;
        private const int InUseFlag = 1;

        private EntrySlot[] _slots;
        private int _head;
        private int _free;
        private int _highWater;
        private int _count;
        private int _probed;
        private int _sweepAt;

        internal int Count => _count;

        internal int Capacity => _slots == null ? 0 : _slots.Length;

        // Live entries that carry an isFinished probe.
        internal int ProbedCount => _probed;

        internal int Head => _head;

        internal bool ShouldSweep => _count >= (_sweepAt < MinSweepThreshold ? MinSweepThreshold : _sweepAt);

        internal void Add(in EntrySlot entry, out int id, out int version)
        {
            if (_free != 0)
            {
                id = _free;
                ref var reused = ref _slots[id - 1];
                _free = reused.Next;
                version = reused.Version;
            }
            else
            {
                if (_slots == null)
                {
                    _slots = new EntrySlot[InitialCapacity];
                }
                else if (_highWater == _slots.Length)
                {
                    Array.Resize(ref _slots, _highWater * 2);
                }

                id = ++_highWater;
                version = 1;
            }

            ref var slot = ref _slots[id - 1];
            slot = entry;
            slot.Version = version;
            slot.Flags = InUseFlag;
            slot.Prev = 0;
            slot.Next = _head;
            if (_head != 0)
            {
                _slots[_head - 1].Prev = id;
            }

            _head = id;
            _count++;
            if (entry.Probe != null)
            {
                _probed++;
            }
        }

        internal bool IsLive(int id, int version)
        {
            if (_slots == null || id <= 0 || id > _slots.Length)
            {
                return false;
            }

            ref var slot = ref _slots[id - 1];
            return (slot.Flags & InUseFlag) != 0 && slot.Version == version;
        }

        // Removes exactly the entry (id, version) and hands it back; the caller decides whether to terminate it.
        internal bool TryTake(int id, int version, out EntrySlot entry)
        {
            if (!IsLive(id, version))
            {
                entry = default;
                return false;
            }

            entry = _slots[id - 1];
            Release(id);
            return true;
        }

        internal bool TryPopNewest(out EntrySlot entry)
        {
            if (_head == 0)
            {
                entry = default;
                return false;
            }

            return TryTake(_head, _slots[_head - 1].Version, out entry);
        }

        internal ref EntrySlot At(int id)
        {
            return ref _slots[id - 1];
        }

        // Call after every sweep: the next one waits for twice the surviving count.
        internal void CompleteSweep()
        {
            var next = 2 * _count;
            _sweepAt = next < MinSweepThreshold ? MinSweepThreshold : next;
        }

        private void Release(int id)
        {
            ref var slot = ref _slots[id - 1];
            if (slot.Prev != 0)
            {
                _slots[slot.Prev - 1].Next = slot.Next;
            }
            else
            {
                _head = slot.Next;
            }

            if (slot.Next != 0)
            {
                _slots[slot.Next - 1].Prev = slot.Prev;
            }

            if (slot.Probe != null)
            {
                _probed--;
            }

            var nextVersion = slot.Version + 1;
            slot = default;
            slot.Version = nextVersion;
            slot.Next = _free;
            _free = id;
            _count--;
            if (_count == 0)
            {
                _sweepAt = 0;
            }
        }
    }
}
