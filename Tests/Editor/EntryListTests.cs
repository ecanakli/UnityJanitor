using System;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests
{
    // Storage-level tests: LIFO drain, O(1) removal by (slot, version), the free list and the sweep threshold.
    [TestFixture]
    public sealed class EntryListTests
    {
        private static EntrySlot Slot(string label)
        {
            return new EntrySlot { A = label };
        }

        [Test]
        public void TryPopNewest_ReturnsEntriesNewestFirst()
        {
            var list = new EntryList();
            list.Add(Slot("a"), out _, out _);
            list.Add(Slot("b"), out _, out _);
            list.Add(Slot("c"), out _, out _);

            Assert.That(Pop(ref list), Is.EqualTo("c"));
            Assert.That(Pop(ref list), Is.EqualTo("b"));
            Assert.That(Pop(ref list), Is.EqualTo("a"));
            Assert.That(list.Count, Is.Zero);
        }

        [Test]
        public void TryPopNewest_OnEmptyList_ReturnsFalse()
        {
            var list = new EntryList();

            Assert.That(list.TryPopNewest(out _), Is.False);
        }

        [Test]
        public void TryTake_MiddleEntry_UnlinksWithoutBreakingTheOrder()
        {
            var list = new EntryList();
            list.Add(Slot("a"), out _, out _);
            list.Add(Slot("b"), out var middleId, out var middleVersion);
            list.Add(Slot("c"), out _, out _);

            Assert.That(list.TryTake(middleId, middleVersion, out var taken), Is.True);

            Assert.That(taken.A, Is.EqualTo("b"));
            Assert.That(list.Count, Is.EqualTo(2));
            Assert.That(Pop(ref list), Is.EqualTo("c"));
            Assert.That(Pop(ref list), Is.EqualTo("a"));
        }

        [Test]
        public void TryTake_HeadEntry_MakesTheNextNewestTheHead()
        {
            var list = new EntryList();
            list.Add(Slot("a"), out _, out _);
            list.Add(Slot("b"), out var headId, out var headVersion);

            list.TryTake(headId, headVersion, out _);

            Assert.That(Pop(ref list), Is.EqualTo("a"));
        }

        [Test]
        public void TryTake_TailEntry_LeavesTheNewerEntriesIntact()
        {
            var list = new EntryList();
            list.Add(Slot("a"), out var tailId, out var tailVersion);
            list.Add(Slot("b"), out _, out _);

            list.TryTake(tailId, tailVersion, out _);

            Assert.That(Pop(ref list), Is.EqualTo("b"));
            Assert.That(list.TryPopNewest(out _), Is.False);
        }

        [Test]
        public void TryTake_StaleVersion_ReturnsFalseAndKeepsTheEntry()
        {
            var list = new EntryList();
            list.Add(Slot("a"), out var id, out var version);

            Assert.That(list.TryTake(id, version + 1, out _), Is.False);
            Assert.That(list.Count, Is.EqualTo(1));
            Assert.That(list.IsLive(id, version), Is.True);
        }

        [Test]
        public void TryTake_SameHandleTwice_SecondCallReturnsFalse()
        {
            var list = new EntryList();
            list.Add(Slot("a"), out var id, out var version);

            Assert.That(list.TryTake(id, version, out _), Is.True);
            Assert.That(list.TryTake(id, version, out _), Is.False);
        }

        [Test]
        public void IsLive_OutOfRangeIds_ReturnFalse()
        {
            var list = new EntryList();
            Assert.That(list.IsLive(1, 1), Is.False, "an untouched list has no slots");

            list.Add(Slot("a"), out _, out _);

            Assert.That(list.IsLive(0, 1), Is.False);
            Assert.That(list.IsLive(-1, 1), Is.False);
            Assert.That(list.IsLive(99, 1), Is.False);
        }

        [Test]
        public void Add_AfterRelease_ReusesTheSlotWithANewVersion()
        {
            var list = new EntryList();
            list.Add(Slot("a"), out var firstId, out var firstVersion);
            list.TryTake(firstId, firstVersion, out _);

            list.Add(Slot("b"), out var secondId, out var secondVersion);

            Assert.That(secondId, Is.EqualTo(firstId));
            Assert.That(secondVersion, Is.Not.EqualTo(firstVersion));
            Assert.That(list.IsLive(firstId, firstVersion), Is.False);
            Assert.That(list.IsLive(secondId, secondVersion), Is.True);
            Assert.That(list.Capacity, Is.EqualTo(4));
        }

        [Test]
        public void Add_BeyondTheInitialCapacity_GrowsAndKeepsEveryEntry()
        {
            var list = new EntryList();
            for (var i = 0; i < 100; i++)
            {
                list.Add(Slot(i.ToString()), out _, out _);
            }

            Assert.That(list.Count, Is.EqualTo(100));
            Assert.That(list.Capacity, Is.GreaterThanOrEqualTo(100));
            for (var i = 99; i >= 0; i--)
            {
                Assert.That(Pop(ref list), Is.EqualTo(i.ToString()));
            }
        }

        [Test]
        public void Add_ChurnWithinOneCapacity_NeverGrowsTheArray()
        {
            var list = new EntryList();
            for (var i = 0; i < 1000; i++)
            {
                list.Add(Slot("x"), out var id, out var version);
                list.TryTake(id, version, out _);
            }

            Assert.That(list.Capacity, Is.EqualTo(4));
            Assert.That(list.Count, Is.Zero);
        }

        [Test]
        public void ProbedCount_CountsOnlyEntriesThatCarryAProbe()
        {
            var list = new EntryList();
            var probe = new Func<int, bool>(_ => true);
            list.Add(Slot("plain"), out _, out _);
            list.Add(new EntrySlot { A = "probed", Probe = probe }, out var probedId, out var probedVersion);

            Assert.That(list.ProbedCount, Is.EqualTo(1));

            list.TryTake(probedId, probedVersion, out _);

            Assert.That(list.ProbedCount, Is.Zero);
        }

        [Test]
        public void ShouldSweep_UsesTheMinimumThresholdThenTwiceTheSurvivors()
        {
            var list = new EntryList();
            for (var i = 0; i < 15; i++)
            {
                list.Add(Slot("x"), out _, out _);
            }

            Assert.That(list.ShouldSweep, Is.False);
            list.Add(Slot("x"), out _, out _);
            Assert.That(list.ShouldSweep, Is.True);

            list.CompleteSweep();

            Assert.That(list.ShouldSweep, Is.False, "16 survivors push the next sweep to 32");
            for (var i = 0; i < 15; i++)
            {
                list.Add(Slot("x"), out _, out _);
            }

            Assert.That(list.ShouldSweep, Is.False);
            list.Add(Slot("x"), out _, out _);
            Assert.That(list.ShouldSweep, Is.True);
        }

        [Test]
        public void ShouldSweep_ResetsToTheMinimumWhenTheListEmpties()
        {
            var list = new EntryList();
            for (var i = 0; i < 40; i++)
            {
                list.Add(Slot("x"), out _, out _);
            }

            list.CompleteSweep();
            while (list.TryPopNewest(out _))
            {
            }

            for (var i = 0; i < 15; i++)
            {
                list.Add(Slot("x"), out _, out _);
            }

            Assert.That(list.ShouldSweep, Is.False);
            list.Add(Slot("x"), out _, out _);
            Assert.That(list.ShouldSweep, Is.True, "a drained list must sweep at 16 again, not at the old 80");
        }

        private static string Pop(ref EntryList list)
        {
            Assert.That(list.TryPopNewest(out var entry), Is.True);
            return (string)entry.A;
        }
    }
}
