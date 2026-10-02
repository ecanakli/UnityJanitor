using NUnit.Framework;

namespace Ecanakli.Janitor.Tests
{
    // Registration handles are stamped with generation, slot and slot version.
    [TestFixture]
    public sealed class LifetimeGenerationTests
    {
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
        public void Registration_BeforeCancel_IsInactiveAfterwardsAndItsCancelIsNoOp()
        {
            var stale = _area.Record(_t.Log, "old");
            Assert.That(stale.IsActive, Is.True);

            _area.Cancel();
            var current = _area.Record(_t.Log, "new");
            stale.Cancel();

            Assert.That(stale.IsActive, Is.False);
            Assert.That(current.IsActive, Is.True, "a stale handle must not touch the new generation");
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "old" }));

            _area.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "old", "new" }));
        }

        [Test]
        public void Registration_SlotReusedInSameGeneration_StaleHandleDoesNotTerminateTheNewItem()
        {
            var first = _area.Record(_t.Log, "first");
            first.Cancel();
            var second = _area.Record(_t.Log, "second");

            first.Cancel();

            // A fresh list hands out slot 1 at version 1; releasing bumps the version, so the reuse is slot 1 at version 2.
            var reuseProbe = new LifetimeRegistration(_area, _area.Generation, 1, 2);
            Assert.That(reuseProbe.IsActive, Is.True, "the second item must have reused slot 1 with a bumped version");
            Assert.That(first.IsActive, Is.False);
            Assert.That(second.IsActive, Is.True, "the reused slot has a new version, so the old handle no longer matches");
            Assert.That(_area.EntryCount, Is.EqualTo(1));
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "first" }));
        }

        [Test]
        public void Registration_MatchingSlotAndVersionButStaleGeneration_IsInactive()
        {
            // The first entry of a fresh list is slot 1, version 1; the forged handle only differs in generation.
            _area.Record(_t.Log, "live");
            var sameGeneration = new LifetimeRegistration(_area, _area.Generation, 1, 1);
            var forged = new LifetimeRegistration(_area, _area.Generation + 5, 1, 1);
            Assert.That(sameGeneration.IsActive, Is.True, "the forged coordinates must address the live entry");

            forged.Cancel();

            Assert.That(forged.IsActive, Is.False);
            Assert.That(_area.EntryCount, Is.EqualTo(1), "the generation check alone must reject the handle");
            Assert.That(_t.Log.Count, Is.Zero);
        }

        [Test]
        public void Registration_DefaultHandle_IsInactiveAndCancelIsNoOp()
        {
            var handle = default(LifetimeRegistration);

            Assert.That(handle.IsActive, Is.False);
            Assert.DoesNotThrow(() => handle.Cancel());
        }

        [Test]
        public void Registration_CancelledTwice_TerminatesOnce()
        {
            var handle = _area.Record(_t.Log, "item");

            handle.Cancel();
            handle.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "item" }));
            Assert.That(handle.IsActive, Is.False);
        }

        [Test]
        public void Registration_CancelledThenLifetimeCancelled_DoesNotTerminateAgain()
        {
            var handle = _area.Record(_t.Log, "item");
            handle.Cancel();

            _area.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "item" }));
        }

        [Test]
        public void Registration_CancelInTheMiddle_TerminatesOnlyThatItemAndKeepsTheOrderOfTheRest()
        {
            _area.Record(_t.Log, "a");
            var middle = _area.Record(_t.Log, "b");
            _area.Record(_t.Log, "c");

            middle.Cancel();
            Assert.That(_area.EntryCount, Is.EqualTo(2));
            _area.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "b", "c", "a" }));
        }

        [Test]
        public void Registration_CancelTheNewest_LeavesTheOlderItemsDrainable()
        {
            _area.Record(_t.Log, "a");
            _area.Record(_t.Log, "b");
            var newest = _area.Record(_t.Log, "c");

            newest.Cancel();
            _area.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "c", "b", "a" }));
        }

        [Test]
        public void Registration_AfterDispose_IsInactive()
        {
            var handle = _area.Record(_t.Log, "item");

            _area.Dispose();

            Assert.That(handle.IsActive, Is.False);
            Assert.DoesNotThrow(() => handle.Cancel());
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "item" }));
        }
    }
}
