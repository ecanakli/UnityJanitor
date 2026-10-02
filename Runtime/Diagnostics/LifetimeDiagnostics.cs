using System;
using System.Diagnostics;
#if UNITY_EDITOR
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;
#endif

namespace Ecanakli.Janitor
{
    /// <summary>
    /// Editor-only diagnostics for the Janitor window. The core records into it, and integration assemblies (DOTween,
    /// Zenject, an event bus) report their own problems through the one public hook, <see cref="Report"/>. Nothing here
    /// costs anything in a player: the hooks are conditional on <c>UNITY_EDITOR</c>, so call sites and their arguments are removed
    /// by the compiler, and the recorded data exists only in the editor.
    /// </summary>
    public static partial class LifetimeDiagnostics
    {
        /// <summary>
        /// Records a diagnostic in the editor's Janitor window. Integration assemblies use it to report their own problems
        /// next to the core's warnings. This is a no-op outside the Unity Editor: the call and the evaluation of its arguments are
        /// compiled out of player builds. It does not write to the console; keep your own console message if you want one.
        /// </summary>
        /// <param name="lifetime">
        /// The lifetime the diagnostic belongs to. When it is null, or is the lifetime that is currently ending an item, and the call
        /// is made from inside that item's cancel action, the registration call site of that item is attached too. A null lifetime
        /// outside a cancel action leaves the diagnostic unattached.
        /// </param>
        /// <param name="diagnosticId">
        /// A stable ID, normally one of the <see cref="DiagnosticIds"/> constants. Another non-empty string is accepted and shown without
        /// a documentation link. A null or empty ID is ignored.
        /// </param>
        /// <param name="message">A short description. Repeats of the same ID, lifetime and message are merged into one entry with a counter.</param>
        /// <param name="context">An optional object the window pings when the warning is chosen, like the console does with a log's context.</param>
        /// <remarks>
        /// Safe to call from any thread, and it never throws. It does nothing while the window's tracking switch is off.
        /// </remarks>
        [Conditional("UNITY_EDITOR")]
        public static void Report(Lifetime lifetime, string diagnosticId, string message, UnityEngine.Object context = null)
        {
#if UNITY_EDITOR
            try
            {
                string member = null;
                var line = 0;
                var ending = s_terminatingLifetime;
                if (ending != null && (lifetime == null || ReferenceEquals(lifetime, ending)) && IsMainThread())
                {
                    lifetime = ending;
                    member = s_terminatingMember;
                    line = s_terminatingLine;
                }

                AddWarning(diagnosticId, lifetime, message, context, member, line);
            }
            catch (Exception exception)
            {
                Failed(exception);
            }
#endif
        }

#if UNITY_EDITOR
        // Ring buffer size, and the growth limits of JANITOR106.
        internal const int RecentCapacity = 200;
        internal const int GrowthEntryLimit = 256;
        internal const int GrowthChildLimit = 64;

        // Settings the editor window sets (from EditorPrefs there; the core cannot use them). Main thread.
        internal static bool TrackingEnabled = true;
        internal static bool CaptureStackTraces;

        // A task that is still running more than this many frames after its generation ended is reported (JANITOR101).
        // The scan runs before a cancelled await can observe its token in the next frame, so a lower value is raised to the minimum.
        internal const int MinOverrunFrames = 1;
        internal static int OverrunFrames = 3;

        // OverrunFrames as the scan uses it.
        internal static int EffectiveOverrunFrames => OverrunFrames < MinOverrunFrames ? MinOverrunFrames : OverrunFrames;

        // Test seams: null means Time.frameCount and the UniTask player loop.
        internal static Func<int> FrameProvider;
        internal static Action<Action> Scheduler;

        private static int s_session;
        private static int s_lastFrame;
        private static int s_nextId;
        private static int s_failureLogged;

        // Set while an item's cancel action runs, so a report made from inside it can be attributed. Main thread only.
        private static Lifetime s_terminatingLifetime;
        private static string s_terminatingMember;
        private static int s_terminatingLine;

        // Play sessions begun since the domain loaded; recorded entries carry the session they belong to.
        internal static int Session => s_session;

        internal static int NextLifetimeId()
        {
            return Interlocked.Increment(ref s_nextId);
        }

        // The current frame. Off the main thread it is the last frame seen there, because Time.frameCount is main thread only.
        internal static int CurrentFrame()
        {
            var provider = FrameProvider;
            if (provider != null)
            {
                return provider();
            }

            if (!IsMainThread())
            {
                return s_lastFrame;
            }

            try
            {
                s_lastFrame = Time.frameCount;
            }
            catch (Exception)
            {
                // Unity refused the call (not on its main thread); keep the last known frame.
            }

            return s_lastFrame;
        }

        internal static bool IsMainThread()
        {
            var mainId = PlayerLoopHelper.MainThreadId;
            return mainId == 0 || Thread.CurrentThread.ManagedThreadId == mainId;
        }

        internal readonly struct TerminateContext
        {
            internal readonly Lifetime Lifetime;
            internal readonly string Member;
            internal readonly int Line;

            internal TerminateContext(Lifetime lifetime, string member, int line)
            {
                Lifetime = lifetime;
                Member = member;
                Line = line;
            }
        }

        // Called around every terminate action; returns what to restore afterwards, so nested terminations stay correct.
        internal static TerminateContext EnterTerminate(Lifetime lifetime, string member, int line)
        {
            var previous = new TerminateContext(s_terminatingLifetime, s_terminatingMember, s_terminatingLine);
            s_terminatingLifetime = lifetime;
            s_terminatingMember = member;
            s_terminatingLine = line;
            return previous;
        }

        internal static void ExitTerminate(TerminateContext previous)
        {
            s_terminatingLifetime = previous.Lifetime;
            s_terminatingMember = previous.Member;
            s_terminatingLine = previous.Line;
        }

        // A diagnostics fault is logged once per session and never propagates into Cancel, Dispose or a registration.
        internal static void Failed(Exception exception)
        {
            if (Interlocked.Exchange(ref s_failureLogged, 1) != 0)
            {
                return;
            }

            try
            {
                Debug.LogException(new InvalidOperationException("Janitor: diagnostics recording failed; the failure was contained.", exception));
            }
            catch (Exception)
            {
                // Logging itself failed; nothing left to report to.
            }
        }

        // A new play session: warnings and tracked tasks belong to the old one. The recently disposed buffer is kept.
        internal static void BeginSession()
        {
            s_session++;
            s_failureLogged = 0;
            s_terminatingLifetime = null;
            s_terminatingMember = null;
            s_terminatingLine = 0;
            ClearWarnings();
            TaskOverrunTracker.Reset();
        }

        // Back to the defaults with nothing recorded; the tests call it from SetUp.
        internal static void ResetAll()
        {
            TrackingEnabled = true;
            CaptureStackTraces = false;
            OverrunFrames = 3;
            FrameProvider = null;
            Scheduler = null;
            s_session = 0;
            s_lastFrame = 0;
            s_failureLogged = 0;
            s_terminatingLifetime = null;
            s_terminatingMember = null;
            s_terminatingLine = 0;
            ClearWarnings();
            ClearRecent();
            TaskOverrunTracker.Reset();
        }
#endif
    }
}
