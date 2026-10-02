using System;
using UnityEditor;
using UnityEngine;

namespace Ecanakli.Janitor.EditorTools
{
    /// <summary>
    /// The window's settings. Reads and writes the EditorPrefs keys, pushes them into the core diagnostics (on load, on change
    /// and on every play mode state change), and carries the recorded history across a domain reload, which the core cannot
    /// do itself because it may not use SessionState.
    /// </summary>
    internal static class DiagnosticsSettings
    {
        internal const string TrackingKey = "Ecanakli.Janitor.Tracking";
        internal const string StackTracesKey = "Ecanakli.Janitor.StackTraces";
        internal const string OverrunFramesKey = "Ecanakli.Janitor.OverrunFrames";

        // Holds the exported history between the assembly unload and the next load. Not a user setting.
        internal const string StateKey = "Ecanakli.Janitor.DiagnosticsState";

        internal const bool DefaultTracking = true;
        internal const bool DefaultStackTraces = false;
        internal const int DefaultOverrunFrames = 3;
        internal const int MinOverrunFrames = LifetimeDiagnostics.MinOverrunFrames;
        internal const int MaxOverrunFrames = 3600;

        /// <summary>Raised after a saved history was imported, which means a domain reload just happened.</summary>
        internal static event Action StateRestored;

        /// <summary>Master switch of the recording.</summary>
        internal static bool TrackingEnabled
        {
            get => EditorPrefs.GetBool(TrackingKey, DefaultTracking);
            set
            {
                EditorPrefs.SetBool(TrackingKey, value);
                Apply();
            }
        }

        /// <summary>Opt-in stack trace per registration.</summary>
        internal static bool CaptureStackTraces
        {
            get => EditorPrefs.GetBool(StackTracesKey, DefaultStackTraces);
            set
            {
                EditorPrefs.SetBool(StackTracesKey, value);
                Apply();
            }
        }

        /// <summary>Frames a task may keep running after its generation ended before JANITOR101 is reported. The minimum is 1: a task that honours its token needs a frame to notice the cancel.</summary>
        internal static int OverrunFrames
        {
            get => ClampOverrunFrames(EditorPrefs.GetInt(OverrunFramesKey, DefaultOverrunFrames));
            set
            {
                EditorPrefs.SetInt(OverrunFramesKey, ClampOverrunFrames(value));
                Apply();
            }
        }

        /// <summary>Keeps the threshold in its allowed range.</summary>
        internal static int ClampOverrunFrames(int value)
        {
            if (value < MinOverrunFrames)
            {
                return MinOverrunFrames;
            }

            return value > MaxOverrunFrames ? MaxOverrunFrames : value;
        }

        /// <summary>Pushes the stored settings into the core diagnostics. Never throws.</summary>
        internal static void Apply()
        {
            try
            {
                LifetimeDiagnostics.TrackingEnabled = TrackingEnabled;
                LifetimeDiagnostics.CaptureStackTraces = CaptureStackTraces;
                LifetimeDiagnostics.OverrunFrames = OverrunFrames;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            // Subscribe once per domain; unsubscribing first keeps a repeated call from doubling the handlers.
            AssemblyReloadEvents.beforeAssemblyReload -= SaveState;
            AssemblyReloadEvents.beforeAssemblyReload += SaveState;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            RestoreState();
            Apply();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            Apply();
        }

        // Runs before every domain reload (script changes and Play Mode with reload on).
        private static void SaveState()
        {
            try
            {
                SessionState.SetString(StateKey, LifetimeDiagnostics.ExportState() ?? string.Empty);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void RestoreState()
        {
            try
            {
                var json = SessionState.GetString(StateKey, string.Empty);
                if (string.IsNullOrEmpty(json))
                {
                    return;
                }

                LifetimeDiagnostics.ImportState(json);
                StateRestored?.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
