using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Ecanakli.Janitor
{
    /// <summary>
    /// An event the game declares, with no argument. Every subscription names its owner and the package removes it,
    /// so user code never writes an unsubscribe line. Handlers run in subscription order, each isolated by its own
    /// try/catch; a handler whose owner ended, or moved to another generation, is skipped. A removal during
    /// <see cref="Invoke"/> takes effect at once, while a subscription made during <see cref="Invoke"/> is first
    /// delivered by the next one. Main thread only. It is not a bus: there is no global registry or routing by type.
    /// </summary>
    public sealed partial class OwnedEvent : IOwnedEvent, IOwnedEventNode
    {
        private readonly string _name;
        private SubscriberList<Action> _list;
        private string _label;

        /// <summary>Creates an event.</summary>
        /// <param name="name">An optional display name for diagnostics.</param>
        public OwnedEvent(string name = null)
        {
            _name = name;
        }

        /// <summary>The number of live subscriptions.</summary>
        public int SubscriberCount => _list.Count;

        // Slots in use, holes included. Outside Invoke it equals SubscriberCount for up to 16 slots.
        internal int SlotCount => _list.SlotCount;

        // Invokes currently on the stack.
        internal int InvokeDepth => _list.Depth;

        /// <inheritdoc/>
        public LifetimeRegistration Subscribe(Action handler, Lifetime owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            return _list.Subscribe(handler, owner, member, line, this);
        }

        /// <summary>
        /// Calls every live subscriber in subscription order. A handler exception is routed to
        /// <see cref="LifetimeErrors.Handler"/> with source <see cref="LifetimeErrorSource.EventHandler"/>, the
        /// subscriber's owner and its call site, and the remaining handlers still run. A nested call is safe; at
        /// 64 nested calls it routes an <see cref="InvalidOperationException"/> (JANITOR115) and returns without delivering.
        /// </summary>
        /// <exception cref="InvalidOperationException">Called off the main thread while subscribers exist; checked in the editor and in development builds only.</exception>
        public void Invoke()
        {
            EnsureThread();
            var entry = _list.TryEnter(out var end);
            if (entry != InvokeEntry.Entered)
            {
                if (entry == InvokeEntry.TooDeep)
                {
                    OwnedEventEntry.ReportDepthExceeded(this);
                }

                return;
            }

            try
            {
                for (var i = 0; i < end; i++)
                {
                    Action handler;
                    Lifetime owner;
                    string member;
                    int line;
                    if (!_list.TryGetDeliverable(i, out handler, out owner, out member, out line))
                    {
                        continue;
                    }

                    try
                    {
                        handler();
                    }
                    catch (Exception exception)
                    {
                        owner.Tree.ReportError(exception, LifetimeErrorSource.EventHandler, owner, member, line);
                    }
                }
            }
            finally
            {
                _list.Exit();
            }
        }

        string IOwnedEventNode.Label => _label ?? (_label = OwnedEventEntry.FormatLabel("OwnedEvent", _name));

        void IOwnedEventNode.RemoveSubscriber(long serial)
        {
            _list.RemoveBySerial(serial);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        private void EnsureThread()
        {
            _list.EnsureInvokeThread();
        }
    }
}
