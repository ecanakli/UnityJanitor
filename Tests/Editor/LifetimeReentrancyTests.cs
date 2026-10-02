using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests
{
    // The re-entrancy matrix, one test per reachable row. Tests that call a no-op operation assert that
    // it did not rent a snapshot buffer, which proves the early return rather than a later skip.
    [TestFixture]
    public sealed class LifetimeReentrancyTests
    {
        private TestScope _t;

        [SetUp]
        public void SetUp()
        {
            _t = new TestScope();
        }

        [TearDown]
        public void TearDown()
        {
            _t.Complete();
        }

        // Row: Cancel on a lifetime that is Cancelling, Disposing or Disposed is a no-op.

        [Test]
        public void Cancel_OnLifetimeThatIsCancelling_IsNoOp()
        {
            var parent = _t.App.CreateChild("parent");
            var child = parent.CreateChild("child");
            child.OnCancel(() =>
            {
                _t.Log.Add("child:begin");
                parent.Cancel();
                _t.Log.Add("child:end");
            });
            parent.Record(_t.Log, "parent");

            parent.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "child:begin", "child:end", "parent" }));
            Assert.That(parent.Generation, Is.EqualTo(1), "the nested Cancel must not open a second generation");
            Assert.That(child.Generation, Is.EqualTo(1));
            Assert.That(_t.Tree.FreeSnapshotBuffers, Is.EqualTo(1), "the nested Cancel must not start an operation");
        }

        [Test]
        public void Cancel_OnLifetimeThatIsDisposing_IsNoOp()
        {
            var parent = _t.App.CreateChild("parent");
            var child = parent.CreateChild("child");
            child.OnCancel(() =>
            {
                _t.Log.Add("child:begin");
                parent.Cancel();
                _t.Log.Add("child:end");
            });
            parent.Record(_t.Log, "parent");

            parent.Dispose();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "child:begin", "child:end", "parent" }));
            Assert.That(parent.State, Is.EqualTo(LifetimeState.Disposed));
            Assert.That(child.State, Is.EqualTo(LifetimeState.Disposed));
            Assert.That(_t.Tree.FreeSnapshotBuffers, Is.EqualTo(1), "the nested Cancel must not start an operation");
        }

        [Test]
        public void Cancel_OnLifetimeThatIsDisposed_IsNoOp()
        {
            var area = _t.App.CreateChild("area");
            area.Dispose();
            var generation = area.Generation;

            area.Cancel();

            Assert.That(area.Generation, Is.EqualTo(generation));
            Assert.That(area.State, Is.EqualTo(LifetimeState.Disposed));
            Assert.That(_t.Tree.FreeSnapshotBuffers, Is.EqualTo(1));
        }

        // Row: Cancel of the same lifetime from inside its own cancel callbacks is a no-op.

        [Test]
        public void Cancel_InsideItsOwnCancelCallback_IsNoOp()
        {
            var area = _t.App.CreateChild("area");
            area.OnCancel(() =>
            {
                _t.Log.Add("first:begin");
                area.Cancel();
                _t.Log.Add("first:end");
            });
            area.Record(_t.Log, "second");

            area.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "second", "first:begin", "first:end" }));
            Assert.That(area.Generation, Is.EqualTo(1));
            Assert.That(area.State, Is.EqualTo(LifetimeState.Active));
            Assert.That(_t.Tree.FreeSnapshotBuffers, Is.EqualTo(1));
        }

        [Test]
        public void Cancel_InsideItsOwnTokenCallback_IsNoOp()
        {
            var area = _t.App.CreateChild("area");
            area.Token.Register(() =>
            {
                _t.Log.Add("token:begin");
                area.Cancel();
                _t.Log.Add("token:end");
            });
            area.Record(_t.Log, "item");

            area.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "token:begin", "token:end", "item" }));
            Assert.That(area.Generation, Is.EqualTo(1));
            Assert.That(_t.Tree.FreeSnapshotBuffers, Is.EqualTo(1));
        }

        // Row: Cancel of the same lifetime from ordinary code registered in it runs normally.

        [Test]
        public void Cancel_SelfCancelFromOrdinaryCode_RunsNormallyAndCancelsTheCallersToken()
        {
            var area = _t.App.CreateChild("area");
            var callerToken = area.Token;
            var seen = false;
            callerToken.Register(() => seen = true);
            area.Record(_t.Log, "item");

            // Ordinary code (not inside a teardown), such as a task body, cancels its own lifetime.
            area.Cancel();

            Assert.That(seen, Is.True);
            Assert.That(callerToken.IsCancellationRequested, Is.True, "the caller keeps running and sees a cancelled token");
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "item" }));
            Assert.That(area.State, Is.EqualTo(LifetimeState.Active));
            Assert.That(area.Generation, Is.EqualTo(1));
        }

        // Row: registration while the target is Cancelling is terminated immediately.

        [Test]
        public void Registration_DuringCancelCallback_IsTerminatedImmediately()
        {
            var area = _t.App.CreateChild("area");
            var probe = new DisposeProbe();
            var disposedInside = -1;
            var activeInside = true;
            area.OnCancel(() =>
            {
                var registration = probe.AddTo(area);
                disposedInside = probe.DisposeCount;
                activeInside = registration.IsActive;
                area.OnCancel(() => _t.Log.Add("late"));
                _t.Log.Add("callback:end");
            });

            area.Cancel();

            Assert.That(disposedInside, Is.EqualTo(1), "the item must be disposed inside the registration call");
            Assert.That(activeInside, Is.False);
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "late", "callback:end" }));
            Assert.That(area.EntryCount, Is.Zero, "a refused item must never be stored");

            area.Cancel();

            Assert.That(probe.DisposeCount, Is.EqualTo(1), "the refused item must not linger into the next generation");
            Assert.That(_t.Log.Count, Is.EqualTo(2));
        }

        [Test]
        public void Registration_DuringTokenCallback_IsTerminatedImmediately()
        {
            var area = _t.App.CreateChild("area");
            var probe = new DisposeProbe();
            area.Token.Register(() => probe.AddTo(area));

            area.Cancel();

            Assert.That(probe.DisposeCount, Is.EqualTo(1));
            Assert.That(area.EntryCount, Is.Zero);
        }

        [Test]
        public void Token_ReadDuringTokenCallback_IsAlreadyCancelled()
        {
            var area = _t.App.CreateChild("area");
            var cancelledInside = false;
            area.Token.Register(() => cancelledInside = area.Token.IsCancellationRequested);

            area.Cancel();

            Assert.That(cancelledInside, Is.True, "a token read while the generation is ending must not be a fresh live source");
            Assert.That(area.Token.IsCancellationRequested, Is.False);
        }

        // Row: registration after Cancel returns lands in the new generation.

        [Test]
        public void Registration_AfterCancelReturns_LandsInTheNewGeneration()
        {
            var area = _t.App.CreateChild("area");
            area.Cancel();

            var registration = area.Record(_t.Log, "after");

            Assert.That(registration.IsActive, Is.True);
            Assert.That(_t.Log.Count, Is.Zero, "the item must wait for the next Cancel");

            area.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "after" }));
        }

        // Row: CreateChild while the parent subtree is Cancelling.

        [Test]
        public void CreateChild_DuringCancel_ChildIsBornCancellingAndBecomesActiveAtFinalize()
        {
            var parent = _t.App.CreateChild("parent");
            Lifetime born = null;
            var stateInside = LifetimeState.Active;
            var tokenCancelledInside = false;
            var registrationActiveInside = true;
            parent.OnCancel(() =>
            {
                born = parent.CreateChild("born");
                stateInside = born.State;
                tokenCancelledInside = born.Token.IsCancellationRequested;
                registrationActiveInside = born.Record(_t.Log, "born:item").IsActive;
            });

            parent.Cancel();

            Assert.That(stateInside, Is.EqualTo(LifetimeState.Cancelling));
            Assert.That(tokenCancelledInside, Is.True);
            Assert.That(registrationActiveInside, Is.False);
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "born:item" }), "a registration on a born-Cancelling child is terminated immediately");
            Assert.That(born.State, Is.EqualTo(LifetimeState.Active), "the born child must be finalized with the operation");
            Assert.That(born.Parent, Is.SameAs(parent));
            Assert.That(parent.ChildCount, Is.EqualTo(1));
            Assert.That(born.Token.IsCancellationRequested, Is.False);
            Assert.That(born.Record(_t.Log, "later").IsActive, Is.True);
        }

        [Test]
        public void CreateChild_DuringCancelOfAncestor_ChildOfADescendantIsBornCancelling()
        {
            var root = _t.App.CreateChild("root");
            var middle = root.CreateChild("middle");
            Lifetime born = null;
            var stateInside = LifetimeState.Active;
            middle.OnCancel(() =>
            {
                born = middle.CreateChild("born");
                stateInside = born.State;
            });

            root.Cancel();

            Assert.That(stateInside, Is.EqualTo(LifetimeState.Cancelling));
            Assert.That(born.State, Is.EqualTo(LifetimeState.Active));
            Assert.That(middle.ChildCount, Is.EqualTo(1));
        }

        // Extra rule: CreateChild under a Disposing parent.

        [Test]
        public void CreateChild_DuringDispose_ChildIsBornDisposingAndFinalizesDisposed()
        {
            var area = _t.App.CreateChild("area");
            Lifetime born = null;
            var stateInside = LifetimeState.Active;
            var registrationActiveInside = true;
            area.OnCancel(() =>
            {
                born = area.CreateChild("born");
                stateInside = born.State;
                registrationActiveInside = born.Record(_t.Log, "born:item").IsActive;
            });

            area.Dispose();

            Assert.That(stateInside, Is.EqualTo(LifetimeState.Disposing));
            Assert.That(registrationActiveInside, Is.False);
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "born:item" }));
            Assert.That(born.IsDisposed, Is.True, "a child born during Dispose must not survive it");
            Assert.That(born.Parent, Is.Null);
            Assert.That(area.ChildCount, Is.Zero);
        }

        // Extra rule: CreateChild under a Disposed parent.

        [Test]
        public void CreateChild_UnderDisposedParent_ReturnsDetachedDisposedChild()
        {
            var area = _t.App.CreateChild("area");
            area.Dispose();

            var late = area.CreateChild("late");
            var registration = late.Record(_t.Log, "late:item");

            Assert.That(late.IsDisposed, Is.True);
            Assert.That(late.Parent, Is.Null);
            Assert.That(area.ChildCount, Is.Zero);
            Assert.That(registration.IsActive, Is.False);
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "late:item" }));
            Assert.That(late.Token.IsCancellationRequested, Is.True);
            Assert.DoesNotThrow(() => late.Cancel());
        }

        // Row: a child's Dispose during the parent's Cancel: Dispose wins.

        [Test]
        public void Dispose_ChildDisposedDuringParentCancel_DisposeWins()
        {
            var parent = _t.App.CreateChild("parent");
            var older = parent.CreateChild("older");
            var newer = parent.CreateChild("newer");
            older.Record(_t.Log, "older:1");
            older.Record(_t.Log, "older:2");
            var disposedInside = false;
            newer.OnCancel(() =>
            {
                _t.Log.Add("newer:begin");
                older.Dispose();
                disposedInside = older.IsDisposed;
                _t.Log.Add("newer:end");
            });
            parent.Record(_t.Log, "parent");

            parent.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "newer:begin", "older:2", "older:1", "newer:end", "parent" }),
                "the child must be drained by its own Dispose, once, at the moment it is called");
            Assert.That(disposedInside, Is.True);
            Assert.That(older.State, Is.EqualTo(LifetimeState.Disposed), "the parent's finalize must not revive the child");
            Assert.That(older.Parent, Is.Null);
            Assert.That(parent.ChildCount, Is.EqualTo(1));
            Assert.That(parent.State, Is.EqualTo(LifetimeState.Active));
            Assert.That(newer.State, Is.EqualTo(LifetimeState.Active));
        }

        // Row: the parent's Cancel during a child's Dispose skips the Disposing subtree.

        [Test]
        public void Cancel_ParentCancelledDuringChildDispose_SkipsTheDisposingSubtree()
        {
            var parent = _t.App.CreateChild("parent");
            var child = parent.CreateChild("child");
            var sibling = parent.CreateChild("sibling");
            child.Record(_t.Log, "child:1");
            child.OnCancel(() =>
            {
                _t.Log.Add("child:2:begin");
                parent.Cancel();
                _t.Log.Add("child:2:end");
            });
            sibling.Record(_t.Log, "sibling");
            parent.Record(_t.Log, "parent");

            child.Dispose();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "child:2:begin", "sibling", "parent", "child:2:end", "child:1" }),
                "the rest of the tree is cancelled at once; the Disposing child keeps draining under its own Dispose");
            Assert.That(child.IsDisposed, Is.True);
            Assert.That(parent.State, Is.EqualTo(LifetimeState.Active));
            Assert.That(parent.Generation, Is.EqualTo(1));
            Assert.That(sibling.Generation, Is.EqualTo(1));
            Assert.That(parent.ChildCount, Is.EqualTo(1));
        }

        // Row: the parent's Dispose during a child's Cancel upgrades the child and never flips it back to Active.

        [Test]
        public void Dispose_ParentDisposedDuringChildCancel_UpgradesTheChildAndNeverFlipsItBack()
        {
            var grand = _t.App.CreateChild("grand");
            var parent = grand.CreateChild("parent");
            var child = parent.CreateChild("child");
            var leaf = child.CreateChild("leaf");
            leaf.Record(_t.Log, "leaf:1");
            child.Record(_t.Log, "child:1");
            child.OnCancel(() =>
            {
                _t.Log.Add("child:2:begin");
                parent.Dispose();
                _t.Log.Add("child:2:end");
            });
            parent.Record(_t.Log, "parent:1");

            child.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "leaf:1", "child:2:begin", "child:1", "parent:1", "child:2:end" }));
            Assert.That(parent.IsDisposed, Is.True);
            Assert.That(child.State, Is.EqualTo(LifetimeState.Disposed), "the child's own finalize must not flip a Disposed node back to Active");
            Assert.That(leaf.State, Is.EqualTo(LifetimeState.Disposed));
            Assert.That(grand.ChildCount, Is.Zero);
        }

        [Test]
        public void Dispose_InsideItsOwnCancelCallback_UpgradesToDisposedAndNeverFlipsBackToActive()
        {
            var area = _t.App.CreateChild("area");
            area.Record(_t.Log, "old");
            area.OnCancel(() =>
            {
                _t.Log.Add("new:begin");
                area.Dispose();
                _t.Log.Add("new:end");
            });

            area.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "new:begin", "old", "new:end" }));
            Assert.That(area.State, Is.EqualTo(LifetimeState.Disposed));
            Assert.That(area.Parent, Is.Null);
        }

        [Test]
        public void Dispose_InsideItsOwnDisposeCallback_IsNoOp()
        {
            var area = _t.App.CreateChild("area");
            area.OnCancel(() =>
            {
                _t.Log.Add("begin");
                area.Dispose();
                _t.Log.Add("end");
            });
            area.Record(_t.Log, "other");

            area.Dispose();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "other", "begin", "end" }));
            Assert.That(area.IsDisposed, Is.True);
            Assert.That(_t.Tree.FreeSnapshotBuffers, Is.EqualTo(1), "the nested Dispose must not start an operation");
        }

        // Extra rule: the mark pass skips another running operation's Cancelling nodes with their subtrees.

        [Test]
        public void Cancel_NestedCancelOfAncestor_SkipsNodesOwnedByTheRunningOperation()
        {
            var top = _t.App.CreateChild("top");
            var middle = top.CreateChild("middle");
            var deep = middle.CreateChild("deep");
            var other = top.CreateChild("other");
            top.Record(_t.Log, "top:1");
            middle.Record(_t.Log, "middle:1");
            other.Record(_t.Log, "other:1");
            deep.Record(_t.Log, "deep:1");
            deep.OnCancel(() =>
            {
                _t.Log.Add("deep:2:begin");
                top.Cancel();
                _t.Log.Add("deep:2:end");
            });

            middle.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "deep:2:begin", "other:1", "top:1", "deep:2:end", "deep:1", "middle:1" }),
                "the nested Cancel must leave the running operation's nodes to that operation");
            Assert.That(top.Generation, Is.EqualTo(1));
            Assert.That(middle.Generation, Is.EqualTo(1));
            Assert.That(deep.Generation, Is.EqualTo(1));
            Assert.That(other.Generation, Is.EqualTo(1));
            Assert.That(top.State, Is.EqualTo(LifetimeState.Active));
            Assert.That(middle.State, Is.EqualTo(LifetimeState.Active));
            Assert.That(_t.Tree.FreeSnapshotBuffers, Is.EqualTo(2), "each nested operation returns its own snapshot buffer");
        }

        // Row: Dispose on a package-owned lifetime is ignored with JANITOR107.

        [Test]
        public void Dispose_OnPackageOwnedRoot_IsIgnoredWithWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR107"));
            var registration = _t.App.Record(_t.Log, "app:item");

            _t.App.Dispose();

            Assert.That(_t.App.IsDisposed, Is.False);
            Assert.That(_t.App.State, Is.EqualTo(LifetimeState.Active));
            Assert.That(registration.IsActive, Is.True);
            Assert.That(_t.Log.Count, Is.Zero);
            Assert.That(_t.Tree.FreeSnapshotBuffers, Is.Zero, "an ignored Dispose must not start an operation");
        }

        [Test]
        public void Dispose_OnPackageOwnedChild_IsIgnoredAndTheOwnerCanStillDisposeIt()
        {
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR107"));
            var owned = _t.App.CreateChildCore("owned", LifetimeKind.Component, true, null, "test", 0);
            owned.Record(_t.Log, "owned:item");

            owned.Dispose();

            Assert.That(owned.IsDisposed, Is.False);
            Assert.That(_t.Log.Count, Is.Zero);

            owned.DisposeFromOwner();

            Assert.That(owned.IsDisposed, Is.True);
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "owned:item" }));
        }

        [Test]
        public void Dispose_PackageOwnedDescendantOfAnArea_IsDisposedWithTheArea()
        {
            var area = _t.App.CreateChild("area");
            var owned = area.CreateChildCore("owned", LifetimeKind.Injected, true, null, "test", 0);
            owned.Record(_t.Log, "owned:item");

            area.Dispose();

            Assert.That(owned.IsDisposed, Is.True);
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "owned:item" }));
        }

        // Row: a stale or repeated LifetimeRegistration.Cancel() is a no-op.

        [Test]
        public void RegistrationCancel_StaleHandle_IsNoOp()
        {
            var area = _t.App.CreateChild("area");
            var stale = area.Record(_t.Log, "old");
            area.Cancel();
            area.Record(_t.Log, "new");

            stale.Cancel();
            stale.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "old" }));
            Assert.That(area.EntryCount, Is.EqualTo(1));
        }

        [Test]
        public void RegistrationCancel_CalledFromItsOwnTerminateAction_DoesNotTerminateTwice()
        {
            var area = _t.App.CreateChild("area");
            var runs = 0;
            var registration = default(LifetimeRegistration);
            registration = area.OnCancel(() =>
            {
                runs++;
                registration.Cancel();
            });

            registration.Cancel();

            Assert.That(runs, Is.EqualTo(1));
            Assert.That(registration.IsActive, Is.False);
        }

        [Test]
        public void RegistrationCancel_DuringLifetimeCancel_TerminatesThatItemOnceAndKeepsTheRest()
        {
            var area = _t.App.CreateChild("area");
            var victim = area.Record(_t.Log, "victim");
            area.OnCancel(() =>
            {
                _t.Log.Add("killer");
                victim.Cancel();
            });

            area.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "killer", "victim" }), "the item is terminated once, by whoever reaches it first");
        }
    }
}
