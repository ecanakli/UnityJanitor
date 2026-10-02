using System;
using System.Threading;

namespace Ecanakli.Janitor
{
    // Registration, Token and CreateChild require the main thread; Cancel and Dispose are marshalled instead.
    internal sealed class MainThreadGuard
    {
        internal MainThreadGuard(int mainThreadId)
        {
            MainThreadId = mainThreadId;
        }

        // Settable so tests can simulate the wrong thread.
        internal int MainThreadId { get; set; }

        internal bool IsMainThread => Thread.CurrentThread.ManagedThreadId == MainThreadId;

        internal void EnsureMainThread(string operation)
        {
            if (Thread.CurrentThread.ManagedThreadId != MainThreadId)
            {
                ThrowOffMainThread(operation);
            }
        }

        private static void ThrowOffMainThread(string operation)
        {
            throw new InvalidOperationException("Janitor: " + operation + " must be called on the main thread.");
        }
    }
}
