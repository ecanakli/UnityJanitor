using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.Coroutines
{
    // Records what Unity logs while an engine probe runs, without failing the test on it; the probe reports the messages.
    internal sealed class LogCapture : IDisposable
    {
        private readonly bool _previousIgnore;
        private readonly List<string> _messages = new List<string>();

        internal LogCapture()
        {
            _previousIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += OnLog;
        }

        internal int Count => _messages.Count;

        internal string Describe()
        {
            return _messages.Count == 0 ? "none" : string.Join(" | ", _messages);
        }

        public void Dispose()
        {
            Application.logMessageReceived -= OnLog;
            LogAssert.ignoreFailingMessages = _previousIgnore;
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            _messages.Add(type + ": " + condition);
        }
    }
}
