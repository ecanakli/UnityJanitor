using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Ecanakli.Janitor.Tests.Probes
{
    // Records Unity lifecycle messages into a shared log for the engine probes.
    [AddComponentMenu("")]
    internal sealed class ProbeBehaviour : MonoBehaviour
    {
        internal static List<string> Log;
        internal static CancellationToken TokenReadInOnDestroy;

        // The binding tests run code inside Awake through this; it is null for the engine probes.
        internal static System.Action<ProbeBehaviour> AwakeHook;

        internal bool ReadTokenInOnDestroy;

        private void Awake()
        {
            Log?.Add("Awake");
            AwakeHook?.Invoke(this);
        }

        private void OnEnable() => Log?.Add("OnEnable");

        private void OnDisable() => Log?.Add("OnDisable");

        private void OnDestroy()
        {
            Log?.Add("OnDestroy");
            if (ReadTokenInOnDestroy)
            {
                TokenReadInOnDestroy = destroyCancellationToken;
            }
        }
    }
}
