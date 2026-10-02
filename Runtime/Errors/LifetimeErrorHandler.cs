using System;

namespace Ecanakli.Janitor
{
    /// <summary>Receives every error that Janitor catches instead of throwing.</summary>
    /// <param name="exception">The caught exception. It is never an <see cref="OperationCanceledException"/>.</param>
    /// <param name="context">Where the error came from.</param>
    public delegate void LifetimeErrorHandler(Exception exception, in LifetimeErrorContext context);
}
