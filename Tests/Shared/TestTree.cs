using System;
using System.Collections.Generic;
using System.Threading;

namespace Ecanakli.Janitor.Tests
{
    // One isolated lifetime tree per test. Marshal drains are captured here and run only when the test asks.
    public sealed class TestTree : IDisposable
    {
        private readonly object _gate = new object();
        private readonly ManualResetEventSlim _postSignal = new ManualResetEventSlim(false);
        private readonly List<Action> _posted = new List<Action>();
        private readonly LifetimeTree _tree;
        private LifetimeTree _previousDefault;
        private bool _isDefault;

        // The constructing thread is this tree's main thread.
        public TestTree()
            : this(Thread.CurrentThread.ManagedThreadId)
        {
        }

        public TestTree(int mainThreadId)
        {
            _tree = new LifetimeTree(mainThreadId, Post);
        }

        public Lifetime App => _tree.App;

        // Settable so a test can make the current thread look like a worker thread.
        public int MainThreadId
        {
            get => _tree.Guard.MainThreadId;
            set => _tree.Guard.MainThreadId = value;
        }

        public int PendingMarshalCount => _tree.Marshal.PendingCount;

        // How many times the marshal queue asked for a drain to be scheduled.
        public int PostedDrainCount
        {
            get
            {
                lock (_gate)
                {
                    return _posted.Count;
                }
            }
        }

        // Cancel and Dispose calls currently on the teardown stack; zero when nothing is running.
        public int RunningOperations => _tree.Teardown.RunningCount;

        public int FreeSnapshotBuffers => _tree.Buffers.FreeCount;

        // Installs a manual clock as this tree's delay provider (the After and Every seam).
        public ManualClock UseManualClock()
        {
            var clock = new ManualClock();
            _tree.Delay = clock.Delay;
            return clock;
        }

        // Blocks until a worker thread has asked for a main-thread hop; bounded, so a missing hop fails the test.
        public bool WaitForPost(int timeoutMilliseconds = 10000)
        {
            return _postSignal.Wait(timeoutMilliseconds);
        }

        // Runs every scheduled drain on the calling thread.
        public void RunPostedDrains()
        {
            Action[] toRun;
            lock (_gate)
            {
                toRun = _posted.ToArray();
                _posted.Clear();
                _postSignal.Reset();
            }

            for (var i = 0; i < toRun.Length; i++)
            {
                toRun[i]();
            }
        }

        // Drains the marshal queue directly, without going through a scheduled post.
        public void DrainMarshalQueue()
        {
            _tree.Marshal.Drain();
        }

        public void Shutdown()
        {
            _tree.Shutdown();
        }

        // Makes this tree what Lifetime.App resolves to until Dispose.
        public void MakeDefault()
        {
            if (_isDefault)
            {
                return;
            }

            _previousDefault = LifetimeTree.Default;
            LifetimeTree.Default = _tree;
            _isDefault = true;
        }

        public void Dispose()
        {
            if (_isDefault)
            {
                LifetimeTree.Default = _previousDefault;
                _previousDefault = null;
                _isDefault = false;
            }
        }

        private void Post(Action drain)
        {
            lock (_gate)
            {
                _posted.Add(drain);
                _postSignal.Set();
            }
        }
    }
}
