using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Ecanakli.Janitor.Tests
{
    // Installs LifetimeErrors.Handler, records every routed error and restores the previous handler on Dispose.
    public sealed class CapturingErrorHandler : IDisposable
    {
        private readonly object _gate = new object();
        private readonly List<Captured> _items = new List<Captured>();
        private readonly LifetimeErrorHandler _previous;

        public CapturingErrorHandler()
        {
            _previous = LifetimeErrors.Handler;
            LifetimeErrors.Handler = Handle;
        }

        // When set, the handler records the error and then throws this exception.
        public Exception ThrowFromHandler { get; set; }

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _items.Count;
                }
            }
        }

        public Captured this[int index]
        {
            get
            {
                lock (_gate)
                {
                    return _items[index];
                }
            }
        }

        public List<string> Messages()
        {
            lock (_gate)
            {
                var messages = new List<string>(_items.Count);
                for (var i = 0; i < _items.Count; i++)
                {
                    messages.Add(_items[i].Exception.Message);
                }

                return messages;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _items.Clear();
            }
        }

        public void Dispose()
        {
            LifetimeErrors.Handler = _previous;
        }

        public override string ToString()
        {
            lock (_gate)
            {
                var builder = new StringBuilder();
                for (var i = 0; i < _items.Count; i++)
                {
                    var item = _items[i];
                    builder.Append('[').Append(item.Context.Source).Append("] ")
                        .Append(item.Exception.GetType().Name).Append(": ").Append(item.Exception.Message)
                        .Append(" (lifetime ").Append(item.Context.LifetimeName).Append(")\n");
                }

                return builder.ToString();
            }
        }

        private void Handle(Exception exception, in LifetimeErrorContext context)
        {
            lock (_gate)
            {
                _items.Add(new Captured(exception, context, Thread.CurrentThread.ManagedThreadId));
            }

            var failure = ThrowFromHandler;
            if (failure != null)
            {
                throw failure;
            }
        }

        public readonly struct Captured
        {
            public Captured(Exception exception, LifetimeErrorContext context, int threadId)
            {
                Exception = exception;
                Context = context;
                ThreadId = threadId;
            }

            public Exception Exception { get; }

            public LifetimeErrorContext Context { get; }

            // The managed thread the handler ran on.
            public int ThreadId { get; }
        }
    }
}
