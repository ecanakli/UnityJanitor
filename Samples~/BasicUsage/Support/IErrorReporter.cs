using System;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>Stand-in for the game's own error reporting service.</summary>
    public interface IErrorReporter
    {
        void Report(string category, string location, Exception exception);
    }
}
