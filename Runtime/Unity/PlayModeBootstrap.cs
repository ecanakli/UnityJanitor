using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace Ecanakli.Janitor
{
    // Per-session reset. Everything static that the package holds is rebuilt here, so a session starts clean even
    // when domain reload is disabled. In the editor, returning to Edit Mode leaves no default tree, which is why
    // Lifetime.App, SceneLifetimes and the component lifetimes throw there.
    internal static class PlayModeBootstrap
    {
        private static readonly UnityAction<Scene> SceneUnloadedHandler = HandleSceneUnloaded;
        private static readonly Action QuittingHandler = HandleQuitting;
        private static readonly Action<object> ExitCallback = ExitCore;

        private static CancellationTokenRegistration _exitRegistration;
        private static CancellationToken _exitToken;
        private static LifetimeTree _sessionTree;
        private static bool _exiting;

        // Sessions begun and sceneUnloaded events handled since the domain loaded; tests read them.
        internal static int SessionCount { get; private set; }

        internal static int SceneUnloadedCount { get; private set; }

        // True once the current session is ending: its exit token was cancelled, or the quit signal arrived.
        internal static bool IsExiting => _exiting || _exitToken.IsCancellationRequested;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnSubsystemRegistration()
        {
            BeginSession(Application.exitCancellationToken);
        }

        // The five steps, in order. exitToken is read fresh by the caller for every session; tests pass their own.
        internal static void BeginSession(CancellationToken exitToken)
        {
            // 1. Dispose the old trees without throwing. The old session tree is tracked, because Default may be cleared already.
            _exitRegistration.Dispose();
            _exitRegistration = default;
            var previous = LifetimeTree.Default;
            ShutdownQuietly(previous);
            if (!ReferenceEquals(_sessionTree, previous))
            {
                ShutdownQuietly(_sessionTree);
            }

            // The old session's recorded warnings and tracked tasks go; the recently disposed buffer stays.
            LifetimeDiagnostics.SessionStarted();

            // 2. A fresh tree for this session.
            var tree = new LifetimeTree();
            LifetimeTree.Default = tree;
            _sessionTree = tree;
            _exiting = false;

            // 3. Unsubscribe before subscribing, so a session that keeps the old domain never doubles a handler.
            RemoveSceneUnloaded();
            AddSceneUnloaded();
            RemoveQuitting();
            AddQuitting();
#if UNITY_EDITOR
            RemovePlayModeState();
            AddPlayModeState();
#endif

            // 4. A handler left behind by the previous session must not receive this session's errors.
            LifetimeErrors.Handler = null;

            // 5. App is disposed when the exit token fires. A token that is already cancelled here is stale, and binding
            // it would dispose the new session's App at once, so it is not bound; the quit signal ends the session too.
            _exitToken = default;
            if (!exitToken.IsCancellationRequested)
            {
                _exitToken = exitToken;
                _exitRegistration = exitToken.RegisterWithoutCaptureExecutionContext(ExitCallback, tree);
            }

            SessionCount++;
        }

        // The fallback for a scene that was unloaded without SceneLifetimes.Dispose: late, and with a hint.
        internal static void HandleSceneUnloaded(Scene scene)
        {
            SceneUnloadedCount++;
            try
            {
                SceneBinding.OnSceneUnloaded(LifetimeTree.Default, scene, IsExiting);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        // A second signal for the shutdown the exit token runs, for a token that was stale or never fires.
        internal static void HandleQuitting()
        {
            _exiting = true;
            ShutdownQuietly(_sessionTree);
        }

        private static void ExitCore(object state)
        {
            ShutdownQuietly((LifetimeTree)state);
        }

        // Each engine event is touched here only. In the editor the package handlers on it are counted, for tests.
        private static void AddSceneUnloaded()
        {
            SceneManager.sceneUnloaded += SceneUnloadedHandler;
#if UNITY_EDITOR
            SceneUnloadedHandlerCount++;
#endif
        }

        private static void RemoveSceneUnloaded()
        {
            SceneManager.sceneUnloaded -= SceneUnloadedHandler;
#if UNITY_EDITOR
            SceneUnloadedHandlerCount = Math.Max(0, SceneUnloadedHandlerCount - 1);
#endif
        }

        private static void AddQuitting()
        {
            Application.quitting += QuittingHandler;
#if UNITY_EDITOR
            QuittingHandlerCount++;
#endif
        }

        private static void RemoveQuitting()
        {
            Application.quitting -= QuittingHandler;
#if UNITY_EDITOR
            QuittingHandlerCount = Math.Max(0, QuittingHandlerCount - 1);
#endif
        }

        private static void ShutdownQuietly(LifetimeTree tree)
        {
            if (tree == null)
            {
                return;
            }

            try
            {
                tree.Shutdown();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

#if UNITY_EDITOR
        private static readonly Action<UnityEditor.PlayModeStateChange> PlayModeStateHandler = HandlePlayModeStateChanged;

        private const string ScriptReloadWarning = "Janitor: a script reload during Play Mode discarded every lifetime and the work they owned. A new lifetime tree was started. Restart Play Mode for a clean state.";

        // Package handlers each engine event carries, kept from the adds and removes above; tests read them.
        internal static int SceneUnloadedHandlerCount { get; private set; }

        internal static int QuittingHandlerCount { get; private set; }

        internal static int PlayModeStateHandlerCount { get; private set; }

        private static void AddPlayModeState()
        {
            UnityEditor.EditorApplication.playModeStateChanged += PlayModeStateHandler;
            PlayModeStateHandlerCount++;
        }

        private static void RemovePlayModeState()
        {
            UnityEditor.EditorApplication.playModeStateChanged -= PlayModeStateHandler;
            PlayModeStateHandlerCount = Math.Max(0, PlayModeStateHandlerCount - 1);
        }

        // The editor is back in Edit Mode: every object is gone, so nothing can need the tree any more.
        internal static void HandlePlayModeStateChanged(UnityEditor.PlayModeStateChange change)
        {
            if (change == UnityEditor.PlayModeStateChange.EnteredEditMode)
            {
                EndSession();
            }
        }

        // Runs the shutdown once more (it is idempotent), removes the handlers and leaves no default tree.
        internal static void EndSession()
        {
            ShutdownQuietly(_sessionTree);
            _exitRegistration.Dispose();
            _exitRegistration = default;
            _exitToken = default;
            _exiting = false;
            RemoveSceneUnloaded();
            RemoveQuitting();
            RemovePlayModeState();
            if (ReferenceEquals(LifetimeTree.Default, _sessionTree))
            {
                LifetimeTree.Default = null;
            }

            _sessionTree = null;
        }

        // A script reload in Play Mode drops the tree and the bootstrap does not run again. Nothing the old tree
        // tracked survived, so the first caller that needs a tree starts a new session. Null when that is not allowed.
        internal static LifetimeTree RestartAfterScriptReload()
        {
            try
            {
                var mainId = PlayerLoopHelper.MainThreadId;
                if ((mainId != 0 && Thread.CurrentThread.ManagedThreadId != mainId) || !Application.isPlaying)
                {
                    return null;
                }

                BeginSession(Application.exitCancellationToken);
                Debug.LogWarning(ScriptReloadWarning);
                return LifetimeTree.Default;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return null;
            }
        }
#endif
    }
}
