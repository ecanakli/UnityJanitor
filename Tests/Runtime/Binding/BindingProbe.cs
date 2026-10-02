using System;
using UnityEngine;

namespace Ecanakli.Janitor.Tests.Binding
{
    // Records its Unity messages and lets a test run code inside each one through the static hooks.
    [AddComponentMenu("")]
    internal sealed class BindingProbe : MonoBehaviour
    {
        internal static CallLog Log;
        internal static Action<BindingProbe> AwakeHook;
        internal static Action<BindingProbe> EnableHook;
        internal static Action<BindingProbe> DisableHook;
        internal static Action<BindingProbe> DestroyHook;

        internal static void ResetStatics()
        {
            Log = null;
            AwakeHook = null;
            EnableHook = null;
            DisableHook = null;
            DestroyHook = null;
        }

        private void Awake()
        {
            Log?.Add("Awake");
            AwakeHook?.Invoke(this);
        }

        private void OnEnable()
        {
            Log?.Add("OnEnable");
            EnableHook?.Invoke(this);
        }

        private void OnDisable()
        {
            Log?.Add("OnDisable");
            DisableHook?.Invoke(this);
        }

        private void OnDestroy()
        {
            Log?.Add("OnDestroy");
            DestroyHook?.Invoke(this);
        }
    }
}
