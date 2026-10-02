using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.Janitor
{
    // Composes the App root, the indexes, error dispatch, the thread guard, the marshal queue and the buffer pool.
    // PlayModeBootstrap creates the Default tree once per play session; tests construct their own.
    internal sealed class LifetimeTree
    {
        // Production code reaches the tree through this; only the bootstrap and tests assign it. Null outside Play Mode.
        internal static LifetimeTree Default { get; set; }

        // The Default tree, or InvalidOperationException outside Play Mode where none exists.
        internal static LifetimeTree RequireDefault(string api)
        {
            var tree = Default;
            if (tree == null)
            {
#if UNITY_EDITOR
                // A script reload during Play Mode drops the tree; the editor starts a new session instead of failing.
                tree = PlayModeBootstrap.RestartAfterScriptReload();
#endif
                if (tree == null)
                {
                    throw new InvalidOperationException("Janitor: " + api + " is available in Play Mode only. No lifetime tree exists.");
                }
            }

            return tree;
        }

        private DelayProvider _delay = DelayProviders.Default;

        // Uses UniTask's main thread id, which is valid in Edit Mode too.
        internal LifetimeTree()
            : this(ResolveMainThreadId(), null)
        {
        }

        // post runs a marshalled drain on the main thread; null posts to the UniTask player loop.
        internal LifetimeTree(int mainThreadId, Action<Action> post)
        {
            Guard = new MainThreadGuard(mainThreadId);
            Marshal = new MarshalQueue(post);
            Buffers = new SnapshotBufferPool();
            Teardown = new LifetimeTeardown(this);
            Owners = new OwnerIndex();
            Scenes = new SceneIndex();
            App = new Lifetime(this, "App", LifetimeKind.App, true, null, null, 0);
            Sentinel = CreateSentinel();
        }

        internal Lifetime App { get; }

        // The shared disposed lifetime handed out for destroyed owners and for scenes that are going away.
        // It is package-owned, so Dispose() on it is a silent no-op, and every registration on it terminates at once.
        internal Lifetime Sentinel { get; }

        // Placed object lifetimes re-homed since this tree was created (JANITOR114); the diagnostics record each one as well.
        internal int RehomeCount;

        // Delay seam behind After and Every; tests swap in a manual clock, null restores UniTask.Delay.
        internal DelayProvider Delay
        {
            get => _delay;
            set => _delay = value ?? DelayProviders.Default;
        }

        internal MainThreadGuard Guard { get; }

        internal MarshalQueue Marshal { get; }

        internal SnapshotBufferPool Buffers { get; }

        internal LifetimeTeardown Teardown { get; }

        internal OwnerIndex Owners { get; }

        internal SceneIndex Scenes { get; }

        // Routes to LifetimeErrors.Handler; never throws and never routes cancellation.
        internal void ReportError(Exception exception, LifetimeErrorSource source, Lifetime lifetime, string member, int line)
        {
            var context = new LifetimeErrorContext(source, lifetime?.OwnerObject, lifetime?.Label, member, line);
            LifetimeErrors.Dispatch(exception, in context);
        }

#if UNITY_EDITOR
        // Set once the session ends, so the recently disposed buffer does not record the whole tree going down.
        internal bool DiagShuttingDown;
#endif

        // Session end: disposes App and everything below it.
        internal void Shutdown()
        {
#if UNITY_EDITOR
            DiagShuttingDown = true;
#endif
            App.DisposeFromOwner();
        }

        private Lifetime CreateSentinel()
        {
            var sentinel = new Lifetime(this, "Disposed", LifetimeKind.Area, true, null, null, 0);
            sentinel.Mark(LifetimeState.Disposed, 0);
            return sentinel;
        }

        private static int ResolveMainThreadId()
        {
            var id = PlayerLoopHelper.MainThreadId;
            return id != 0 ? id : Thread.CurrentThread.ManagedThreadId;
        }
    }
}
