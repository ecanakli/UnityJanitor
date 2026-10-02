using System;
using UnityEngine;

namespace Ecanakli.Janitor.Tests.Coroutines
{
    // A host whose OnDestroy runs test code, so a test can start work from the host's own teardown.
    [AddComponentMenu("")]
    internal sealed class CoroutineDestroyHost : MonoBehaviour
    {
        internal Action<CoroutineDestroyHost> OnDestroyAction;

        private void OnDestroy()
        {
            var action = OnDestroyAction;
            if (action != null)
            {
                action(this);
            }
        }
    }
}
