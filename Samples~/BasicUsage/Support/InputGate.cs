using System;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>Stand-in for the game's input lock: input is blocked while any block is held.</summary>
    public sealed class InputGate
    {
        private int _blocks;

        public bool IsBlocked => _blocks > 0;

        // The caller owns the returned block and must dispose it.
        public IDisposable Block()
        {
            _blocks++;
            Debug.Log($"[InputGate] Blocked ({_blocks} held).");
            return new Release(this);
        }

        private void Unblock()
        {
            _blocks--;
            Debug.Log($"[InputGate] Released ({_blocks} held).");
        }

        private sealed class Release : IDisposable
        {
            private InputGate _gate;

            public Release(InputGate gate) => _gate = gate;

            public void Dispose()
            {
                if (_gate == null)
                {
                    return;
                }

                _gate.Unblock();
                _gate = null;
            }
        }
    }
}
