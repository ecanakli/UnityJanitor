using System;
using UnityEngine;

namespace Ecanakli.Janitor
{
    /// <summary>Central error routing. Cancel, dispose and item cancellation never throw; they report here instead.</summary>
    public static class LifetimeErrors
    {
        private static readonly LifetimeErrorHandler DefaultHandler = LogDefault;

        private static LifetimeErrorHandler _handler;

        /// <summary>
        /// The handler that receives routed errors. Assigning null restores the default, which logs a
        /// <see cref="LifetimeWorkException"/> to the console with the owner object as context.
        /// </summary>
        public static LifetimeErrorHandler Handler
        {
            get => _handler ?? DefaultHandler;
            set => _handler = value;
        }

        // Never throws. Cancellation is not an error and is dropped here.
        internal static void Dispatch(Exception exception, in LifetimeErrorContext context)
        {
            if (exception == null || exception is OperationCanceledException)
            {
                return;
            }

            try
            {
                Handler(exception, in context);
            }
            catch (Exception handlerFailure)
            {
                LogFallback(handlerFailure, exception, in context);
            }
        }

        private static void LogDefault(Exception exception, in LifetimeErrorContext context)
        {
            Debug.LogException(new LifetimeWorkException(context, exception), context.Owner);
        }

        private static void LogFallback(Exception handlerFailure, Exception original, in LifetimeErrorContext context)
        {
            try
            {
                Debug.LogException(handlerFailure);
                Debug.LogException(new LifetimeWorkException(context, original), context.Owner);
            }
            catch
            {
                // Logging itself failed; nothing left to report to.
            }
        }
    }
}
