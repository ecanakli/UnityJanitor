using System;

namespace Ecanakli.Janitor
{
    // What the owner's entry and the diagnostics need from an OwnedEvent of any arity. Implemented explicitly.
    internal interface IOwnedEventNode
    {
        // Built on demand, so it allocates only on error and diagnostic paths.
        string Label { get; }

        void RemoveSubscriber(long serial);
    }

    internal static class OwnedEventEntry
    {
        // A nested Invoke deeper than this is refused (JANITOR115).
        internal const int MaxInvokeDepth = 64;

        // The owner's entry stores (event, serial) and this static invoker removes exactly that subscriber.
        internal static readonly EntryTerminate Terminate = TerminateCore;

        internal static string FormatLabel(string typeLabel, string name)
        {
            return name == null ? typeLabel : typeLabel + " \"" + name + "\"";
        }

        // Turns event ping-pong into a logged error instead of a stack overflow; logs in every build.
        internal static void ReportDepthExceeded(IOwnedEventNode node)
        {
            var label = node.Label;
            var text = "Re-entrancy depth exceeded " + MaxInvokeDepth + " on " + label + "; the invoke was skipped.";
            var message = DevWarnings.Format(DiagnosticIds.DepthExceeded, text);
            var context = new LifetimeErrorContext(LifetimeErrorSource.EventHandler, null, label, null, 0);
            LifetimeErrors.Dispatch(new InvalidOperationException(message), in context);
            LifetimeDiagnostics.Record(DiagnosticIds.DepthExceeded, null, text);
        }

        private static void TerminateCore(object first, object second, Delegate fn, long aux)
        {
            ((IOwnedEventNode)first).RemoveSubscriber(aux);
        }
    }
}
