using NUnit.Framework;

namespace Ecanakli.Janitor.Tests
{
    // Cancel keeps the lifetime usable and opens a new generation.
    [TestFixture]
    public sealed class LifetimeCancelReuseTests
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

        [Test]
        public void Cancel_RegistrationAfterCancel_LandsInNewGeneration()
        {
            var area = _t.App.CreateChild("area");
            area.Record(_t.Log, "old");
            var before = area.Generation;

            area.Cancel();
            var registration = area.Record(_t.Log, "new");

            Assert.That(area.Generation, Is.EqualTo(before + 1));
            Assert.That(registration.IsActive, Is.True);
            Assert.That(area.EntryCount, Is.EqualTo(1));
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "old" }), "the new item must not run in the Cancel that ended the old generation");

            area.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "old", "new" }));
        }

        [Test]
        public void Cancel_TokenReadAfterCancel_DiffersAndOldTokenStaysCancelled()
        {
            var area = _t.App.CreateChild("area");
            var first = area.Token;
            Assert.That(first.IsCancellationRequested, Is.False);

            area.Cancel();
            var second = area.Token;

            Assert.That(first.IsCancellationRequested, Is.True, "the old token stays cancelled forever");
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(second.IsCancellationRequested, Is.False);

            area.Cancel();

            Assert.That(second.IsCancellationRequested, Is.True);
        }

        [Test]
        public void Token_ReadTwiceInOneGeneration_ReturnsTheSameToken()
        {
            var area = _t.App.CreateChild("area");

            Assert.That(area.Token, Is.EqualTo(area.Token));
        }

        [Test]
        public void Cancel_TokenNeverReadBefore_TokenReadAfterIsNotCancelled()
        {
            var area = _t.App.CreateChild("area");

            area.Cancel();

            Assert.That(area.Token.IsCancellationRequested, Is.False);
        }

        [Test]
        public void Cancel_Repeated1000Times_KeepsCountsBounded()
        {
            var area = _t.App.CreateChild("area");
            var child = area.CreateChild("child");
            var counter = new Counter();

            for (var i = 0; i < 1000; i++)
            {
                var token = area.Token;
                area.OnCancel(counter, static c => c.Value++);
                area.OnCancel(counter, static c => c.Value++);
                area.OnCancel(counter, static c => c.Value++);
                child.OnCancel(counter, static c => c.Value++);
                child.OnCancel(counter, static c => c.Value++);

                area.Cancel();

                Assert.That(token.IsCancellationRequested, Is.True);
            }

            Assert.That(counter.Value, Is.EqualTo(5000));
            Assert.That(area.Generation, Is.EqualTo(1000));
            Assert.That(child.Generation, Is.EqualTo(1000));
            Assert.That(area.EntryCount, Is.Zero);
            Assert.That(child.EntryCount, Is.Zero);
            Assert.That(area.EntryCapacity, Is.LessThanOrEqualTo(4), "slots must be reused, not grown");
            Assert.That(child.EntryCapacity, Is.LessThanOrEqualTo(4));
            Assert.That(area.ChildCount, Is.EqualTo(1));
            Assert.That(_t.App.ChildCount, Is.EqualTo(1));
            Assert.That(_t.Tree.FreeSnapshotBuffers, Is.EqualTo(1), "one pooled buffer serves every non-nested operation");
            Assert.That(_t.Tree.RunningOperations, Is.Zero);
        }

        [Test]
        public void Cancel_LeavesLifetimeActiveAndAttached()
        {
            var area = _t.App.CreateChild("area");

            area.Cancel();

            Assert.That(area.State, Is.EqualTo(LifetimeState.Active));
            Assert.That(area.IsDisposed, Is.False);
            Assert.That(area.Parent, Is.SameAs(_t.App));
        }

        [Test]
        public void Cancel_OnLifetimeWithNothingRegistered_StillOpensNewGeneration()
        {
            var area = _t.App.CreateChild("area");

            area.Cancel();
            area.Cancel();

            Assert.That(area.Generation, Is.EqualTo(2));
        }
    }
}
