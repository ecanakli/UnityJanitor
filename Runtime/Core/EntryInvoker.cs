using System;

namespace Ecanakli.Janitor
{
    // Terminates one entry; first and second are the entry's A and B, fn is its delegate, aux is its packed id.
    internal delegate void EntryTerminate(object first, object second, Delegate fn, long aux);

    // Answers whether an entry has already finished by itself.
    internal delegate bool EntryProbe(object first, Delegate fn);

    // Markers an entry can carry in its aux when its terminate action ignores aux.
    internal static class EntryAux
    {
        // A paired Subscribe: the window labels it by its handler type, unlike an OnCancel whose state happens to be a delegate.
        internal const long Paired = long.MinValue + 2;
    }

    // Cached static delegates: entries carry no closures.
    internal static class EntryInvoker
    {
        internal static readonly EntryTerminate InvokeAction = InvokeActionCore;
        internal static readonly EntryTerminate DisposeItem = DisposeItemCore;

        private static void InvokeActionCore(object first, object second, Delegate fn, long aux)
        {
            ((Action)fn)();
        }

        private static void DisposeItemCore(object first, object second, Delegate fn, long aux)
        {
            ((IDisposable)first).Dispose();
        }
    }

    internal static class EntryInvoker<TState> where TState : class
    {
        internal static readonly EntryTerminate Terminate = TerminateCore;

        // The same call under another identity, so the duplicate scan of a paired Subscribe never matches an OnCancel entry.
        internal static readonly EntryTerminate PairedTerminate = TerminateCore;
        internal static readonly EntryProbe IsFinished = IsFinishedCore;

        private static void TerminateCore(object first, object second, Delegate fn, long aux)
        {
            ((Action<TState>)fn)((TState)first);
        }

        private static bool IsFinishedCore(object first, Delegate fn)
        {
            return ((Func<TState, bool>)fn)((TState)first);
        }
    }

    internal static class EntryInvoker<T1, T2> where T1 : class where T2 : class
    {
        internal static readonly EntryTerminate Terminate = TerminateCore;

        private static void TerminateCore(object first, object second, Delegate fn, long aux)
        {
            ((Action<T1, T2>)fn)((T1)first, (T2)second);
        }
    }
}
