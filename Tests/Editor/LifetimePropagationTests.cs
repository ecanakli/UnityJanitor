using NUnit.Framework;

namespace Ecanakli.Janitor.Tests
{
    // Cancel keeps children usable; Dispose reaches descendants; a disposed lifetime terminates every registration.
    [TestFixture]
    public sealed class LifetimePropagationTests
    {
        private TestScope _t;
        private Lifetime _parent;
        private Lifetime _child;
        private Lifetime _grandchild;

        [SetUp]
        public void SetUp()
        {
            _t = new TestScope();
            _parent = _t.App.CreateChild("parent");
            _child = _parent.CreateChild("child");
            _grandchild = _child.CreateChild("grandchild");
        }

        [TearDown]
        public void TearDown()
        {
            _t.Complete();
        }

        [Test]
        public void Cancel_Parent_KeepsChildrenUsableAndAttached()
        {
            _child.Record(_t.Log, "child");

            _parent.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "child" }));
            Assert.That(_child.State, Is.EqualTo(LifetimeState.Active));
            Assert.That(_grandchild.State, Is.EqualTo(LifetimeState.Active));
            Assert.That(_child.Parent, Is.SameAs(_parent));
            Assert.That(_grandchild.Parent, Is.SameAs(_child));
            Assert.That(_parent.ChildCount, Is.EqualTo(1));
            Assert.That(_child.Record(_t.Log, "again").IsActive, Is.True, "a child must accept registrations after the parent's Cancel");
        }

        [Test]
        public void Cancel_Parent_OpensNewGenerationOnEveryDescendant()
        {
            _parent.Cancel();

            Assert.That(_parent.Generation, Is.EqualTo(1));
            Assert.That(_child.Generation, Is.EqualTo(1));
            Assert.That(_grandchild.Generation, Is.EqualTo(1));
        }

        [Test]
        public void Cancel_Parent_CancelsDescendantTokens()
        {
            var childToken = _child.Token;
            var grandchildToken = _grandchild.Token;

            _parent.Cancel();

            Assert.That(childToken.IsCancellationRequested, Is.True);
            Assert.That(grandchildToken.IsCancellationRequested, Is.True);
            Assert.That(_child.Token.IsCancellationRequested, Is.False, "the child's next generation must be live");
        }

        [Test]
        public void Cancel_Child_LeavesParentAndSiblingsUntouched()
        {
            var sibling = _parent.CreateChild("sibling");
            _parent.Record(_t.Log, "parent");
            sibling.Record(_t.Log, "sibling");
            _child.Record(_t.Log, "child");

            _child.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "child" }));
            Assert.That(_parent.Generation, Is.Zero);
            Assert.That(sibling.Generation, Is.Zero);
        }

        [Test]
        public void Dispose_Parent_DisposesEveryDescendantAndRunsTheirItems()
        {
            _parent.Record(_t.Log, "parent");
            _child.Record(_t.Log, "child");
            _grandchild.Record(_t.Log, "grandchild");

            _parent.Dispose();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "grandchild", "child", "parent" }));
            Assert.That(_parent.IsDisposed, Is.True);
            Assert.That(_child.IsDisposed, Is.True);
            Assert.That(_grandchild.IsDisposed, Is.True);
        }

        [Test]
        public void Dispose_Parent_DetachesItFromItsOwnParent()
        {
            _parent.Dispose();

            Assert.That(_t.App.ChildCount, Is.Zero);
            Assert.That(_parent.Parent, Is.Null);
            Assert.That(_child.Parent, Is.Null, "a disposed child unlinks itself from the disposed parent");
            Assert.That(_parent.ChildCount, Is.Zero);
        }

        [Test]
        public void Dispose_Child_LeavesParentAndSiblingsUsable()
        {
            var sibling = _parent.CreateChild("sibling");

            _child.Dispose();

            Assert.That(_child.IsDisposed, Is.True);
            Assert.That(_grandchild.IsDisposed, Is.True);
            Assert.That(_parent.State, Is.EqualTo(LifetimeState.Active));
            Assert.That(sibling.State, Is.EqualTo(LifetimeState.Active));
            Assert.That(_parent.ChildCount, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_CalledTwice_IsIdempotent()
        {
            _parent.Record(_t.Log, "parent");

            _parent.Dispose();
            _parent.Dispose();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "parent" }));
            Assert.That(_parent.Generation, Is.EqualTo(1), "a second Dispose must not run a second operation");
            Assert.That(_t.Tree.FreeSnapshotBuffers, Is.EqualTo(1));
        }

        [Test]
        public void Registration_OnDisposedLifetime_DisposesTheItemImmediately()
        {
            var probe = new DisposeProbe();
            _parent.Dispose();

            var registration = probe.AddTo(_parent);

            Assert.That(probe.DisposeCount, Is.EqualTo(1));
            Assert.That(registration.IsActive, Is.False);
            Assert.That(_parent.EntryCount, Is.Zero);
        }

        [Test]
        public void Registration_OnDisposedLifetime_EveryLaterRegistrationIsTerminated()
        {
            _parent.Dispose();

            _parent.Record(_t.Log, "first");
            _parent.Record(_t.Log, "second");
            _child.Record(_t.Log, "descendant");

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "first", "second", "descendant" }));
            Assert.That(_parent.EntryCount, Is.Zero);
            Assert.That(_child.EntryCount, Is.Zero);
        }

        [Test]
        public void Token_OnDisposedLifetime_IsAlreadyCancelled()
        {
            _parent.Dispose();

            Assert.That(_parent.Token.IsCancellationRequested, Is.True);
            Assert.That(_child.Token.IsCancellationRequested, Is.True);
        }

        [Test]
        public void Cancel_OnDisposedLifetime_IsNoOp()
        {
            _parent.Dispose();
            var generation = _parent.Generation;

            _parent.Cancel();

            Assert.That(_parent.Generation, Is.EqualTo(generation));
            Assert.That(_parent.State, Is.EqualTo(LifetimeState.Disposed));
            Assert.That(_t.Tree.FreeSnapshotBuffers, Is.EqualTo(1), "a no-op Cancel must not start an operation");
        }
    }
}
