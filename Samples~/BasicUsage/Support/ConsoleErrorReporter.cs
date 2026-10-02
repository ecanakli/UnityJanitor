using System;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>A reporter that writes one console line; a real one would send the error to a service.</summary>
    public sealed class ConsoleErrorReporter : IErrorReporter
    {
        public void Report(string category, string location, Exception exception)
        {
            Debug.Log($"[ErrorReporter] {category} at {location}: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
