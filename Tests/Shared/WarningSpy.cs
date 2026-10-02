using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ecanakli.Janitor.Tests
{
    // Records the warnings Unity received, so a test can assert "exactly once" or "never" for one JANITOR id.
    public sealed class WarningSpy : IDisposable
    {
        private readonly object _gate = new object();
        private readonly List<string> _warnings = new List<string>();

        public WarningSpy()
        {
            Application.logMessageReceived += OnLog;
        }

        // How many warnings carried "[id]" in their text.
        public int Count(string id)
        {
            var tag = "[" + id + "]";
            lock (_gate)
            {
                var count = 0;
                for (var i = 0; i < _warnings.Count; i++)
                {
                    if (_warnings[i].IndexOf(tag, StringComparison.Ordinal) >= 0)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        // The first warning carrying "[id]", or an empty string.
        public string TextOf(string id)
        {
            var tag = "[" + id + "]";
            lock (_gate)
            {
                for (var i = 0; i < _warnings.Count; i++)
                {
                    if (_warnings[i].IndexOf(tag, StringComparison.Ordinal) >= 0)
                    {
                        return _warnings[i];
                    }
                }

                return string.Empty;
            }
        }

        public void Dispose()
        {
            Application.logMessageReceived -= OnLog;
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Warning)
            {
                return;
            }

            lock (_gate)
            {
                _warnings.Add(condition);
            }
        }
    }
}
