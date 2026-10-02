using System;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests
{
    // Registration, Token and CreateChild throw off the main thread; Cancel and Dispose are marshalled.
    [TestFixture]
    public sealed class LifetimeThreadingTests
    {
        private static readonly Regex MarshalWarning = new Regex("JANITOR111");

        private TestScope _t;
        private Lifetime _area;

        [SetUp]
        public void SetUp()
        {
            _t = new TestScope();
            _area = _t.App.CreateChild("area");
        }

        [TearDown]
        public void TearDown()
        {
            _t.Complete();
        }

        [Test]
        public void LifetimeTree_DefaultConstructor_TreatsTheCurrentEditModeThreadAsMain()
        {
            var tree = new LifetimeTree();

            Assert.That(tree.Guard.MainThreadId, Is.EqualTo(PlayerLoopHelper.MainThreadId));
            Assert.That(tree.Guard.IsMainThread, Is.True);
        }

        [Test]
        public void OnCancel_OffMainThread_ThrowsInvalidOperationExceptionAndRegistersNothing()
        {
            var failure = ThreadRunner.Run(() => _area.OnCancel(() => { }));

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void OnCancel_WithStateOffMainThread_ThrowsInvalidOperationException()
        {
            var failure = ThreadRunner.Run(() => _area.OnCancel(_t.Log, l => { }));

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void AddTo_OffMainThread_ThrowsInvalidOperationException()
        {
            var probe = new DisposeProbe();

            var failure = ThreadRunner.Run(() => probe.AddTo(_area));

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void Token_OffMainThread_ThrowsInvalidOperationException()
        {
            var failure = ThreadRunner.Run(() => { _ = _area.Token; });

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void CreateChild_OffMainThread_ThrowsInvalidOperationExceptionAndCreatesNothing()
        {
            var failure = ThreadRunner.Run(() => _area.CreateChild("child"));

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(_area.ChildCount, Is.Zero);
        }

        [Test]
        public void Cancel_OffMainThread_IsMarshalledNotRunInlineAndRunsOnTheNextDrain()
        {
            LogAssert.Expect(LogType.Warning, MarshalWarning);
            _area.Record(_t.Log, "item");

            var failure = ThreadRunner.Run(() => _area.Cancel());

            Assert.That(failure, Is.Null, "an off-thread Cancel must never throw");
            Assert.That(_t.Log.Count, Is.Zero, "nothing may run inline on the worker thread");
            Assert.That(_area.Generation, Is.Zero);
            Assert.That(_t.Tree.PendingMarshalCount, Is.EqualTo(1));
            Assert.That(_t.Tree.PostedDrainCount, Is.EqualTo(1), "a drain must be scheduled for the next main-thread tick");

            _t.Tree.RunPostedDrains();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "item" }));
            Assert.That(_area.Generation, Is.EqualTo(1));
            Assert.That(_t.Tree.PendingMarshalCount, Is.Zero);
        }

        [Test]
        public void Dispose_OffMainThread_IsMarshalledNotRunInlineAndRunsOnTheNextDrain()
        {
            LogAssert.Expect(LogType.Warning, MarshalWarning);
            _area.Record(_t.Log, "item");

            var failure = ThreadRunner.Run(() => _area.Dispose());

            Assert.That(failure, Is.Null);
            Assert.That(_area.IsDisposed, Is.False);
            Assert.That(_t.Log.Count, Is.Zero);
            Assert.That(_t.Tree.PendingMarshalCount, Is.EqualTo(1));

            _t.Tree.RunPostedDrains();

            Assert.That(_area.IsDisposed, Is.True);
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "item" }));
        }

        [Test]
        public void RegistrationCancel_OffMainThread_IsMarshalledNotRunInlineAndRunsOnTheNextDrain()
        {
            LogAssert.Expect(LogType.Warning, MarshalWarning);
            var registration = _area.Record(_t.Log, "item");

            var failure = ThreadRunner.Run(() => registration.Cancel());

            Assert.That(failure, Is.Null);
            Assert.That(_t.Log.Count, Is.Zero);
            Assert.That(registration.IsActive, Is.True);
            Assert.That(_t.Tree.PendingMarshalCount, Is.EqualTo(1));

            _t.Tree.RunPostedDrains();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "item" }));
            Assert.That(registration.IsActive, Is.False);
        }

        [Test]
        public void Cancel_OffMainThread_TerminateActionsRunOnTheMainThread()
        {
            LogAssert.Expect(LogType.Warning, MarshalWarning);
            var ranOn = -1;
            _area.OnCancel(() => ranOn = Thread.CurrentThread.ManagedThreadId);
            var workerThread = -2;

            ThreadRunner.Run(() =>
            {
                workerThread = Thread.CurrentThread.ManagedThreadId;
                _area.Cancel();
            });
            _t.Tree.RunPostedDrains();

            Assert.That(ranOn, Is.EqualTo(Thread.CurrentThread.ManagedThreadId));
            Assert.That(ranOn, Is.Not.EqualTo(workerThread));
        }

        [Test]
        public void Marshal_TwoOffThreadCallsBeforeTheDrain_ScheduleOneDrainAndRunInOrder()
        {
            LogAssert.Expect(LogType.Warning, MarshalWarning);
            LogAssert.Expect(LogType.Warning, MarshalWarning);
            _area.Record(_t.Log, "item");

            ThreadRunner.Run(() =>
            {
                _area.Cancel();
                _area.Dispose();
            });

            Assert.That(_t.Tree.PendingMarshalCount, Is.EqualTo(2));
            Assert.That(_t.Tree.PostedDrainCount, Is.EqualTo(1), "a second call must not schedule a second drain");

            _t.Tree.RunPostedDrains();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "item" }));
            Assert.That(_area.IsDisposed, Is.True, "the queued Dispose must run after the queued Cancel");
            Assert.That(_area.Generation, Is.EqualTo(2), "Cancel then Dispose are two operations");
        }

        [Test]
        public void Marshal_DrainAfterTheQueueEmptied_AllowsANewDrainToBeScheduled()
        {
            LogAssert.Expect(LogType.Warning, MarshalWarning);
            LogAssert.Expect(LogType.Warning, MarshalWarning);
            ThreadRunner.Run(() => _area.Cancel());
            _t.Tree.RunPostedDrains();

            ThreadRunner.Run(() => _area.Cancel());

            Assert.That(_t.Tree.PostedDrainCount, Is.EqualTo(1));
            _t.Tree.RunPostedDrains();
            Assert.That(_area.Generation, Is.EqualTo(2));
        }

        [Test]
        public void Marshal_QueuedCancelForALifetimeDisposedBeforeTheDrain_IsNoOp()
        {
            LogAssert.Expect(LogType.Warning, MarshalWarning);
            _area.Record(_t.Log, "item");
            ThreadRunner.Run(() => _area.Cancel());

            _area.Dispose();
            var generationAfterDispose = _area.Generation;
            _t.Tree.RunPostedDrains();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "item" }), "the item must run once, in the Dispose");
            Assert.That(_area.Generation, Is.EqualTo(generationAfterDispose));
            Assert.That(_area.IsDisposed, Is.True);
        }

        [Test]
        public void Marshal_DrainCalledDirectly_RunsQueuedOperationsWithoutAScheduledPost()
        {
            LogAssert.Expect(LogType.Warning, MarshalWarning);
            _area.Record(_t.Log, "item");
            ThreadRunner.Run(() => _area.Cancel());

            _t.Tree.DrainMarshalQueue();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "item" }));
            Assert.That(_t.Tree.PendingMarshalCount, Is.Zero);
        }

        [Test]
        public void Marshal_PostThrows_IsLoggedAndTheNextOffThreadCallSchedulesAgain()
        {
            var posts = 0;
            var tree = new LifetimeTree(Thread.CurrentThread.ManagedThreadId, drain =>
            {
                posts++;
                throw new InvalidOperationException("post boom");
            });
            var area = tree.App.CreateChild("area");
            area.OnCancel(() => { });
            LogAssert.Expect(LogType.Exception, new Regex("post boom"));
            LogAssert.Expect(LogType.Warning, MarshalWarning);
            LogAssert.Expect(LogType.Exception, new Regex("post boom"));
            LogAssert.Expect(LogType.Warning, MarshalWarning);

            var first = ThreadRunner.Run(() => area.Cancel());
            var second = ThreadRunner.Run(() => area.Cancel());

            Assert.That(first, Is.Null);
            Assert.That(second, Is.Null);
            Assert.That(posts, Is.EqualTo(2), "a failed post must not leave the queue marked as scheduled");
            Assert.That(tree.Marshal.PendingCount, Is.EqualTo(2));

            tree.Marshal.Drain();

            Assert.That(area.Generation, Is.EqualTo(2), "both queued Cancels must still run when the drain is triggered by hand");
        }

        [Test]
        public void MainThreadId_Overridden_MakesTheCurrentThreadLookLikeAWorker()
        {
            LogAssert.Expect(LogType.Warning, MarshalWarning);
            var realId = _t.Tree.MainThreadId;
            _t.Tree.MainThreadId = realId + 1000;

            Assert.Throws<InvalidOperationException>(() => _area.OnCancel(() => { }));
            Assert.Throws<InvalidOperationException>(() => { _ = _area.Token; });
            Assert.Throws<InvalidOperationException>(() => _area.CreateChild("child"));
            Assert.DoesNotThrow(() => _area.Cancel());
            Assert.That(_t.Tree.PendingMarshalCount, Is.EqualTo(1));
            Assert.That(_area.Generation, Is.Zero);

            _t.Tree.MainThreadId = realId;
            _t.Tree.RunPostedDrains();

            Assert.That(_area.Generation, Is.EqualTo(1));
        }
    }
}
