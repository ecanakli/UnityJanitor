using System;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests
{
    // The amortized probe sweep keeps live slots at or below 2 x live + 16.
    [TestFixture]
    public sealed class EntrySweepTests
    {
        private static readonly Action<Item> CountRun = static i => i.Runs++;
        private static readonly Func<Item, bool> ReportFinished = static i => i.Finished;
        private static readonly Func<Item, bool> ThrowingProbe = static i => throw new InvalidOperationException("probe boom");

        private static readonly Action<Reentry> CountReentryRun = static r => r.Runs++;
        private static readonly Func<Reentry, bool> CancelThenRegisterFinished = ProbeThatCancelsAndRegisters;
        private static readonly Func<Reentry, bool> CancelOnly = ProbeThatCancels;

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
        public void Sweep_ReachingTheThreshold_DropsFinishedEntriesWithoutRunningTheirActions()
        {
            var finished = new Item { Finished = true };

            for (var i = 1; i <= 15; i++)
            {
                _area.OnCancel(finished, CountRun, ReportFinished);
                Assert.That(_area.EntryCount, Is.EqualTo(i), "no sweep may run below the threshold of 16");
            }

            _area.OnCancel(finished, CountRun, ReportFinished);

            Assert.That(_area.EntryCount, Is.Zero, "the 16th registration must sweep every finished entry");
            Assert.That(finished.Runs, Is.Zero, "a finished entry's action must not run");

            _area.Cancel();

            Assert.That(finished.Runs, Is.Zero);
        }

        [Test]
        public void Sweep_FinishedEntriesOver10000Cycles_StayBoundedByTheMinimumThreshold()
        {
            var finished = new Item { Finished = true };
            var maxCount = 0;

            for (var i = 0; i < 10000; i++)
            {
                _area.OnCancel(finished, CountRun, ReportFinished);
                maxCount = Math.Max(maxCount, _area.EntryCount);
            }

            Assert.That(maxCount, Is.LessThanOrEqualTo(2 * 0 + EntryList.MinSweepThreshold), "live is zero, so the bound is 16");
            Assert.That(_area.EntryCapacity, Is.LessThanOrEqualTo(2 * EntryList.MinSweepThreshold));
            Assert.That(finished.Runs, Is.Zero, "no swept entry may have run its action");
        }

        [Test]
        public void Sweep_LiveEntriesWithChurn_StayBoundedByTwiceLivePlusSixteen()
        {
            const int live = 100;
            var liveItems = new Item[live];
            var registrations = new LifetimeRegistration[live];
            for (var i = 0; i < live; i++)
            {
                liveItems[i] = new Item();
                registrations[i] = _area.OnCancel(liveItems[i], CountRun, ReportFinished);
            }

            var churn = new Item { Finished = true };
            var maxCount = 0;
            for (var i = 0; i < 10000; i++)
            {
                _area.OnCancel(churn, CountRun, ReportFinished);
                maxCount = Math.Max(maxCount, _area.EntryCount);
            }

            Assert.That(maxCount, Is.LessThanOrEqualTo(2 * live + EntryList.MinSweepThreshold));
            for (var i = 0; i < live; i++)
            {
                Assert.That(registrations[i].IsActive, Is.True, "a live entry must never be swept");
            }

            _area.Cancel();

            for (var i = 0; i < live; i++)
            {
                Assert.That(liveItems[i].Runs, Is.EqualTo(1), "every live entry must still terminate exactly once");
            }
        }

        [Test]
        public void Sweep_EntriesRegisteredWithoutAProbe_AreNeverDropped()
        {
            var counter = new Counter();
            for (var i = 0; i < 100; i++)
            {
                _area.OnCancel(counter, static c => c.Value++);
            }

            Assert.That(_area.EntryCount, Is.EqualTo(100));

            _area.Cancel();

            Assert.That(counter.Value, Is.EqualTo(100));
        }

        [Test]
        public void Sweep_ProbeThrows_IsRoutedAndTheEntryIsKept()
        {
            var item = new Item();
            for (var i = 0; i < 16; i++)
            {
                _area.OnCancel(item, CountRun, ThrowingProbe);
            }

            Assert.That(_t.Errors.Count, Is.EqualTo(16), "every probe must run once and its failure must be routed");
            Assert.That(_t.Errors[0].Exception.Message, Is.EqualTo("probe boom"));
            Assert.That(_t.Errors[0].Context.Source, Is.EqualTo(LifetimeErrorSource.CancelAction));
            Assert.That(_area.EntryCount, Is.EqualTo(16), "a throwing probe must count as not finished");
            _t.Errors.Clear();

            _area.Cancel();

            Assert.That(item.Runs, Is.EqualTo(16));
        }

        [Test]
        public void Sweep_ProbeCancelsTheLifetime_LeavesAConsistentEmptyList()
        {
            var plain = new Item();
            var reentry = new Reentry { Area = _area };
            for (var i = 0; i < 15; i++)
            {
                _area.OnCancel(plain, CountRun, ReportFinished);
            }

            var last = _area.OnCancel(reentry, CountReentryRun, CancelOnly);

            Assert.That(_area.EntryCount, Is.Zero);
            Assert.That(_area.Generation, Is.EqualTo(1));
            Assert.That(last.IsActive, Is.False);
            Assert.That(plain.Runs, Is.EqualTo(15));
            Assert.That(reentry.Runs, Is.EqualTo(1));
        }

        [Test]
        public void Sweep_ProbeReentersTheLifetime_StopsInsteadOfWalkingStaleLinks()
        {
            // Slots are handed out 1..16 for the first 16 entries, so the sweep starts at slot 16 and next is slot 15.
            // The probe cancels the lifetime and registers 15 finished entries, which reuse slots 1..15 at version 2.
            var plain = new Item();
            var finished = new Item { Finished = true };
            var reentry = new Reentry { Area = _area, Finished = finished };
            for (var i = 0; i < 15; i++)
            {
                _area.OnCancel(plain, CountRun, ReportFinished);
            }

            _area.OnCancel(reentry, CountReentryRun, CancelThenRegisterFinished);

            Assert.That(plain.Runs, Is.EqualTo(15));
            Assert.That(reentry.Runs, Is.EqualTo(1));
            Assert.That(_area.Generation, Is.EqualTo(1));
            Assert.That(_area.EntryCount, Is.EqualTo(15), "the sweep must stop when its next slot was recycled under it");
            Assert.That(finished.Runs, Is.Zero);

            _area.Cancel();

            Assert.That(finished.Runs, Is.EqualTo(15));
        }

        private static bool ProbeThatCancels(Reentry reentry)
        {
            reentry.Area.Cancel();
            return false;
        }

        private static bool ProbeThatCancelsAndRegisters(Reentry reentry)
        {
            reentry.Area.Cancel();
            for (var i = 0; i < 15; i++)
            {
                reentry.Area.OnCancel(reentry.Finished, CountRun, ReportFinished);
            }

            return false;
        }

        private sealed class Item
        {
            public bool Finished;
            public int Runs;
        }

        private sealed class Reentry
        {
            public Lifetime Area;
            public Item Finished;
            public int Runs;
        }
    }
}
