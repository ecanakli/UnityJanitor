using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ecanakli.Janitor
{
    internal enum MarshalOp : byte
    {
        Cancel,
        Dispose,
        CancelRegistration,
    }

    // Off-thread Cancel, Dispose and registration.Cancel land here and run on the next main-thread tick.
    internal sealed class MarshalQueue
    {
        private readonly object _gate = new object();
        private readonly Action<Action> _post;
        private readonly Action _drain;

        private List<Item> _pending = new List<Item>(8);
        private List<Item> _running = new List<Item>(8);
        private bool _scheduled;
        private bool _draining;

        // post must run the action on the main thread; null posts to the UniTask player loop.
        internal MarshalQueue(Action<Action> post)
        {
            _post = post ?? PostToPlayerLoop;
            _drain = Drain;
        }

        internal int PendingCount
        {
            get
            {
                lock (_gate)
                {
                    return _pending.Count;
                }
            }
        }

        // Safe from any thread.
        internal void Enqueue(MarshalOp op, Lifetime lifetime, int generation = 0, int slot = 0, int version = 0)
        {
            var schedule = false;
            lock (_gate)
            {
                _pending.Add(new Item(op, lifetime, generation, slot, version));
                if (!_scheduled)
                {
                    _scheduled = true;
                    schedule = true;
                }
            }

            if (schedule)
            {
                Post();
            }
        }

        // Safe from any thread. Runs the action on the main thread through the same post seam as the drain.
        internal void PostAction(Action action)
        {
            try
            {
                _post(action);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        // Main thread only. Runs everything queued so far, including items queued while draining.
        internal void Drain()
        {
            lock (_gate)
            {
                if (_draining)
                {
                    return;
                }

                _draining = true;
            }

            try
            {
                while (true)
                {
                    lock (_gate)
                    {
                        if (_pending.Count == 0)
                        {
                            _scheduled = false;
                            break;
                        }

                        var swap = _pending;
                        _pending = _running;
                        _running = swap;
                    }

                    for (var i = 0; i < _running.Count; i++)
                    {
                        Execute(_running[i]);
                    }

                    _running.Clear();
                }
            }
            finally
            {
                lock (_gate)
                {
                    _draining = false;
                }
            }
        }

        private static void PostToPlayerLoop(Action action)
        {
            PlayerLoopHelper.AddContinuation(PlayerLoopTiming.Update, action);
        }

        private static void Execute(Item item)
        {
            try
            {
                switch (item.Op)
                {
                    case MarshalOp.Cancel:
                        item.Lifetime.CancelOnMainThread();
                        break;
                    case MarshalOp.Dispose:
                        item.Lifetime.DisposeOnMainThread();
                        break;
                    case MarshalOp.CancelRegistration:
                        item.Lifetime.CancelRegistrationOnMainThread(item.Generation, item.Slot, item.Version);
                        break;
                }
            }
            catch (Exception exception)
            {
                item.Lifetime.Tree.ReportError(exception, LifetimeErrorSource.CancelAction, item.Lifetime, null, 0);
            }
        }

        private void Post()
        {
            try
            {
                _post(_drain);
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    _scheduled = false;
                }

                Debug.LogException(exception);
            }
        }

        private readonly struct Item
        {
            internal readonly MarshalOp Op;
            internal readonly Lifetime Lifetime;
            internal readonly int Generation;
            internal readonly int Slot;
            internal readonly int Version;

            internal Item(MarshalOp op, Lifetime lifetime, int generation, int slot, int version)
            {
                Op = op;
                Lifetime = lifetime;
                Generation = generation;
                Slot = slot;
                Version = version;
            }
        }
    }
}
