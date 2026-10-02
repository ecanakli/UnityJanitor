using System;

namespace Ecanakli.Janitor
{
    /// <summary>Wraps an exception thrown by work owned by a lifetime, together with the context it came from.</summary>
    public sealed class LifetimeWorkException : Exception
    {
        /// <summary>Creates the wrapper.</summary>
        /// <param name="context">Where the work came from.</param>
        /// <param name="innerException">The exception the work threw.</param>
        public LifetimeWorkException(LifetimeErrorContext context, Exception innerException)
            : base(BuildMessage(context, innerException), innerException)
        {
            Context = context;
        }

        /// <summary>Where the failed work came from.</summary>
        public LifetimeErrorContext Context { get; }

        private static string BuildMessage(LifetimeErrorContext context, Exception inner)
        {
            var name = context.LifetimeName ?? "<unnamed>";
            var member = context.Member ?? "<unknown>";
            var detail = inner == null ? string.Empty : inner.Message;
            return "Janitor " + context.Source + " failed in lifetime '" + name + "' (" + member + ":" + context.Line + "): " + detail;
        }
    }
}
