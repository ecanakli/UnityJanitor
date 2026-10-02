using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.Janitor.Tests
{
    // A deterministic delay provider for LifetimeTree.Delay. Time moves only through Advance.
    // Steady-state use allocates nothing (pooled promises, reused lists), so it can sit inside allocation tests.
    public sealed class ManualClock
    {
        private readonly List<Pending> _pending = new List<Pending>(64);
        private readonly List<Pending> _due = new List<Pending>(64);
        private float _now;
        private long _sequence;
        private bool _advancing;

        // Delays that were requested and have not completed yet.
        public int PendingCount => _pending.Count;

        // Every Delay call, including synchronous and throwing ones.
        public int RequestCount { get; private set; }

        public float LastSeconds { get; private set; }

        public bool LastIgnoreTimeScale { get; private set; }

        // A misbehaving provider: a delay whose token was cancelled still completes successfully once it is due.
        public bool CompleteCancelledDelaysSuccessfully { get; set; }

        // Every delay is returned already completed.
        public bool CompleteSynchronously { get; set; }

        // The provider itself throws.
        public Exception ThrowOnDelay { get; set; }

        // Matches LifetimeTree's DelayProvider.
        public UniTask Delay(float seconds, bool ignoreTimeScale, CancellationToken token)
        {
            RequestCount++;
            LastSeconds = seconds;
            LastIgnoreTimeScale = ignoreTimeScale;
            if (ThrowOnDelay != null)
            {
                throw ThrowOnDelay;
            }

            if (CompleteSynchronously)
            {
                return UniTask.CompletedTask;
            }

            var source = AutoResetUniTaskCompletionSource.Create();
            _pending.Add(new Pending(_now + seconds, _sequence++, token, source));
            return source.Task;
        }

        // Completes every delay that is due, in due order. Like UniTask.Delay, a delay whose token was cancelled
        // completes as cancelled at the next advance, whatever its due time. Continuations run synchronously here.
        public void Advance(float seconds)
        {
            if (_advancing)
            {
                throw new InvalidOperationException("ManualClock.Advance is not re-entrant.");
            }

            _advancing = true;
            try
            {
                _now += seconds;
                CollectDue();
                for (var i = 0; i < _due.Count; i++)
                {
                    Complete(_due[i]);
                }

                _due.Clear();
            }
            finally
            {
                _advancing = false;
            }
        }

        // Splits the pending list; delays requested by the continuations wait for the next advance.
        private void CollectDue()
        {
            _due.Clear();
            var write = 0;
            for (var read = 0; read < _pending.Count; read++)
            {
                var item = _pending[read];
                var cancelled = item.Token.IsCancellationRequested && !CompleteCancelledDelaysSuccessfully;
                if (item.DueAt <= _now || cancelled)
                {
                    InsertDue(item);
                }
                else
                {
                    _pending[write++] = item;
                }
            }

            _pending.RemoveRange(write, _pending.Count - write);
        }

        private void InsertDue(Pending item)
        {
            var index = _due.Count;
            while (index > 0 && IsLater(_due[index - 1], item))
            {
                index--;
            }

            _due.Insert(index, item);
        }

        private static bool IsLater(Pending first, Pending second)
        {
            return first.DueAt > second.DueAt || (first.DueAt == second.DueAt && first.Sequence > second.Sequence);
        }

        private void Complete(Pending item)
        {
            if (item.Token.IsCancellationRequested && !CompleteCancelledDelaysSuccessfully)
            {
                item.Source.TrySetCanceled(item.Token);
            }
            else
            {
                item.Source.TrySetResult();
            }
        }

        private readonly struct Pending
        {
            internal readonly float DueAt;
            internal readonly long Sequence;
            internal readonly CancellationToken Token;
            internal readonly AutoResetUniTaskCompletionSource Source;

            internal Pending(float dueAt, long sequence, CancellationToken token, AutoResetUniTaskCompletionSource source)
            {
                DueAt = dueAt;
                Sequence = sequence;
                Token = token;
                Source = source;
            }
        }
    }
}
