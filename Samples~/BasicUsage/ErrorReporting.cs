using System;
using Ecanakli.Janitor;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>Forwards every error Janitor catches to the game's reporter and keeps the console logging.</summary>
    public static class ErrorReporting
    {
        private static readonly LifetimeErrorHandler Forwarder = Forward;
        private static IErrorReporter _reporter;
        private static LifetimeErrorHandler _console;

        // Call it in every Play Mode session, as Janitor resets the handler at its start; a repeat call only swaps the reporter.
        public static void Install(IErrorReporter reporter)
        {
            _reporter = reporter;
            if (LifetimeErrors.Handler == Forwarder)
            {
                return;
            }

            _console = LifetimeErrors.Handler;      // the default handler, which logs to the console
            LifetimeErrors.Handler = Forwarder;
        }

        private static void Forward(Exception exception, in LifetimeErrorContext context)
        {
            var owner = context.Owner != null ? context.Owner.name : "no owner";
            var location = $"{context.LifetimeName} ({owner}) {context.Member}:{context.Line}";
            _reporter.Report(context.Source.ToString(), location, exception);
            _console(exception, in context);
        }
    }
}
