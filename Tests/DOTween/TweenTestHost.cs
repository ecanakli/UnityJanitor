using System;
using UnityEngine;

namespace Ecanakli.Janitor.Tests.TweenBinding
{
    // A plain component used as a tween owner; OnDestroy reports to the test through a hook.
    public sealed class TweenTestHost : MonoBehaviour
    {
        internal Action DestroyProbe;

        private void OnDestroy()
        {
            var probe = DestroyProbe;
            if (probe != null)
            {
                probe();
            }
        }
    }
}
