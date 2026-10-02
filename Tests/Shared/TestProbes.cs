using System;
using System.Collections.Generic;

namespace Ecanakli.Janitor.Tests
{
    // Records the order in which callbacks ran.
    public sealed class CallLog
    {
        private readonly List<string> _entries = new List<string>();

        public int Count => _entries.Count;

        public void Add(string entry)
        {
            _entries.Add(entry);
        }

        public void Clear()
        {
            _entries.Clear();
        }

        public string[] ToArray()
        {
            return _entries.ToArray();
        }

        public string[] WithPrefix(string prefix)
        {
            var result = new List<string>();
            for (var i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].StartsWith(prefix, StringComparison.Ordinal))
                {
                    result.Add(_entries[i]);
                }
            }

            return result.ToArray();
        }

        public override string ToString()
        {
            return string.Join(", ", _entries);
        }
    }

    // A reference-type counter usable as static-action state.
    public sealed class Counter
    {
        public int Value;
    }

    // An IDisposable that counts its Dispose calls and can be told to throw.
    public sealed class DisposeProbe : IDisposable
    {
        public int DisposeCount;
        public Exception ThrowOnDispose;
        public CallLog Log;
        public string Label;

        public void Dispose()
        {
            DisposeCount++;
            if (Log != null)
            {
                Log.Add(Label ?? "dispose");
            }

            if (ThrowOnDispose != null)
            {
                throw ThrowOnDispose;
            }
        }
    }

    // A struct disposable, to observe the documented boxing behaviour of AddTo.
    public struct StructDisposable : IDisposable
    {
        public CallLog Log;
        public string Label;

        public void Dispose()
        {
            Log.Add(Label);
        }
    }
}
