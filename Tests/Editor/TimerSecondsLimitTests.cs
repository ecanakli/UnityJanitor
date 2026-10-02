using System;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests
{
    // A delay too large for a TimeSpan is rejected at the call, like NaN and infinity, instead of failing inside the timer.
    [TestFixture]
    public sealed class TimerSecondsLimitTests
    {
        private const float LargestAccepted = 900000000000f;

        private TestScope _t;
        private ManualClock _clock;
        private Lifetime _area;

        [SetUp]
        public void SetUp()
        {
            _t = new TestScope();
            _clock = _t.Tree.UseManualClock();
            _area = _t.App.CreateChild("area");
        }

        [TearDown]
        public void TearDown()
        {
            _t.Complete();
        }

        [TestCase(float.MaxValue)]
        [TestCase(1e12f)]
        [TestCase(900001000000f)]
        public void After_AHugeFiniteDelay_ThrowsAndSchedulesNothing(float seconds)
        {
            var counter = new Counter();

            var plain = Assert.Throws<ArgumentOutOfRangeException>(() => _area.After(seconds, () => counter.Value++));
            var withState = Assert.Throws<ArgumentOutOfRangeException>(() => _area.After(seconds, counter, static c => c.Value++));

            Assert.That(plain.ParamName, Is.EqualTo("seconds"));
            Assert.That(withState.ParamName, Is.EqualTo("seconds"));
            Assert.That(_clock.RequestCount, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero);
            Assert.That(_t.Errors.Count, Is.Zero, "nothing is routed as a timer error");
        }

        [TestCase(float.MaxValue)]
        [TestCase(1e12f)]
        [TestCase(900001000000f)]
        public void Every_AHugeFiniteInterval_ThrowsAndSchedulesNothing(float seconds)
        {
            var counter = new Counter();

            var plain = Assert.Throws<ArgumentOutOfRangeException>(() => _area.Every(seconds, () => counter.Value++));
            var withState = Assert.Throws<ArgumentOutOfRangeException>(() => _area.Every(seconds, counter, static c => c.Value++));

            Assert.That(plain.ParamName, Is.EqualTo("seconds"));
            Assert.That(withState.ParamName, Is.EqualTo("seconds"));
            Assert.That(_clock.RequestCount, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void After_TheLargestAcceptedDelay_IsScheduled_AndFitsInATimeSpan()
        {
            Assert.DoesNotThrow(() => TimeSpan.FromSeconds(LargestAccepted), "the real delay provider converts the value");

            _area.After(LargestAccepted, () => { });
            _area.Every(LargestAccepted, () => { });

            Assert.That(_clock.RequestCount, Is.EqualTo(2));
            Assert.That(_area.EntryCount, Is.EqualTo(2));
            _area.Cancel();
            _clock.Advance(1f);
        }

        [Test]
        public void After_AHugeNegativeDelay_StillRunsInPlace()
        {
            var counter = new Counter();

            _area.After(-float.MaxValue, counter, static c => c.Value++);

            Assert.That(counter.Value, Is.EqualTo(1), "values at or below zero keep their meaning");
        }
    }
}
