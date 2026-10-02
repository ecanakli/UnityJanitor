using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.Events;

namespace Ecanakli.Janitor.Tests.Events
{
    // Reads the engine's runtime listener count by reflection (UnityEvent has no public one), so a guard that stayed attached is visible.
    // Calibrated against a known add and remove first; UnityEventSubscribeTests.ListenerCountInspector_TracksAddAndRemove fails if it cannot be.
    internal static class UnityEventInspector
    {
        private static readonly FieldInfo CallsField = typeof(UnityEventBase).GetField("m_Calls", BindingFlags.Instance | BindingFlags.NonPublic);

        private static bool _calibrationDone;
        private static bool _calibrated;

        // The number of runtime listeners on the event, or -1 when the engine layout is not the expected one.
        internal static int Count(UnityEventBase evt)
        {
            try
            {
                if (CallsField == null)
                {
                    return -1;
                }

                var calls = CallsField.GetValue(evt);
                if (calls == null)
                {
                    return -1;
                }

                var property = calls.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property == null || property.PropertyType != typeof(int))
                {
                    return -1;
                }

                return (int)property.GetValue(calls);
            }
            catch (Exception)
            {
                return -1;
            }
        }

        // True when Count follows a known AddListener and RemoveListener on this engine version.
        internal static bool IsCalibrated()
        {
            if (!_calibrationDone)
            {
                var probe = new UnityEvent();
                UnityAction listener = () => { };
                var before = Count(probe);
                probe.AddListener(listener);
                var during = Count(probe);
                probe.RemoveListener(listener);
                var after = Count(probe);
                _calibrated = before >= 0 && during == before + 1 && after == before;
                _calibrationDone = true;
            }

            return _calibrated;
        }

        // Asserts the number of listeners attached to the event; a no-op when the count cannot be trusted.
        internal static void AssertCount(UnityEventBase evt, int expected)
        {
            if (IsCalibrated())
            {
                Assert.That(Count(evt), Is.EqualTo(expected), "listeners attached to the event");
            }
        }
    }
}
