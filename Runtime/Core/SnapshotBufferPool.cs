using System;
using System.Collections.Generic;

namespace Ecanakli.Janitor
{
    // A pooled pre-order subtree snapshot for one Cancel or Dispose operation.
    internal sealed class SnapshotBuffer
    {
        internal Lifetime[] Items = new Lifetime[16];
        internal int Count;
        internal int OpId;

        internal void Add(Lifetime lifetime)
        {
            if (Count == Items.Length)
            {
                Array.Resize(ref Items, Count * 2);
            }

            Items[Count++] = lifetime;
        }

        internal void Reset()
        {
            Array.Clear(Items, 0, Count);
            Count = 0;
            OpId = 0;
        }
    }

    // Nested operations each rent their own buffer, so the pool grows only to the deepest nesting seen.
    internal sealed class SnapshotBufferPool
    {
        private readonly Stack<SnapshotBuffer> _free = new Stack<SnapshotBuffer>(4);

        internal int FreeCount => _free.Count;

        internal SnapshotBuffer Rent(int opId)
        {
            var buffer = _free.Count > 0 ? _free.Pop() : new SnapshotBuffer();
            buffer.OpId = opId;
            return buffer;
        }

        internal void Return(SnapshotBuffer buffer)
        {
            buffer.Reset();
            _free.Push(buffer);
        }
    }
}
