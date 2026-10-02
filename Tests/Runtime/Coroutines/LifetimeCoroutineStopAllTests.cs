using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.Coroutines
{
    // StopAllCoroutines on the host stops the routines without telling the package. Unity never disposes a stopped coroutine,
    // so the entry stays until the lifetime's generation ends, or until a counted deactivation lets a sweep drop it.
    [TestFixture]
    public sealed class LifetimeCoroutineStopAllTests
    {
        private CoroutineFixture _f;

        [SetUp]
        public void SetUp()
        {
            _f = new CoroutineFixture();
        }

        [TearDown]
        public void TearDown()
        {
            _f.Complete();
        }

        [UnityTest]
        public IEnumerator StopAllCoroutines_StopsTheRoutine_AndTheEntryStaysUntilTheLifetimeEnds()
        {
            var counter = new Counter();
            var host = _f.NewHost();
            var registration = _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(counter));
            var wrapper = CoroutineFixture.WrapperOf(_f.Area);
            yield return CoroutineFixture.Frames(2);
            var atStop = counter.Value;

            host.StopAllCoroutines();
            yield return CoroutineFixture.Frames(3);

            Assert.That(counter.Value, Is.EqualTo(atStop), "Unity stopped the routine");
            Assert.That(_f.Area.EntryCount, Is.EqualTo(1), "the package was not told, so the entry stays");
            Assert.That(registration.IsActive, Is.True);
            Assert.That(LifetimeCoroutine.IsFinishedProbe(wrapper), Is.False, "the host is alive and was not deactivated, so the sweep keeps it");

            Assert.DoesNotThrow(() => _f.Area.Cancel());

            Assert.That(_f.Area.EntryCount, Is.Zero, "the generation's end removes it");
            Assert.That(registration.IsActive, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        // The measured growth: one entry per start-and-stop-all cycle, and the sweep keeps all of them.
        [Test]
        public void RepeatedStartAndStopAll_OnALongLivedHost_GrowsByOneEntryPerCycleUntilTheGenerationEnds()
        {
            const int cycles = 200;
            var counter = new Counter();
            var host = _f.NewHost();

            for (var i = 0; i < cycles; i++)
            {
                _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(counter));
                host.StopAllCoroutines();
            }

            Assert.That(counter.Value, Is.EqualTo(cycles), "every routine took its first step and was stopped");
            Assert.That(_f.Area.EntryCount, Is.EqualTo(cycles), "no sweep can tell that these routines were stopped: " + _f.Area.EntryCount + " entries after " + cycles + " cycles");
            Assert.That(CoroutineFixture.CountCoroutineEntries(_f.Area), Is.EqualTo(cycles));

            Assert.DoesNotThrow(() => _f.Area.Cancel());

            Assert.That(_f.Area.EntryCount, Is.Zero, "a Cancel of the lifetime releases them all");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void StoppedByStopAllCoroutines_AreReclaimedBySweepsOnceTheHostIsDeactivated()
        {
            const int cycles = 40;
            var counter = new Counter();
            var host = _f.NewHost();
            for (var i = 0; i < cycles; i++)
            {
                _f.Area.StartCoroutine(host, CoroutineRoutines.Forever(counter));
                host.StopAllCoroutines();
            }

            Assert.That(_f.Area.EntryCount, Is.EqualTo(cycles), "premise: the sweeps so far kept all of them");

            host.gameObject.SetActive(false);
            host.gameObject.SetActive(true);

            // The deactivation was counted. The next sweep, at twice the surviving count at the latest, drops every stopped routine.
            for (var i = 0; i < 4 * cycles; i++)
            {
                _f.Area.OnCancel(() => { });
            }

            Assert.That(CoroutineFixture.CountCoroutineEntries(_f.Area), Is.Zero, "the stopped routines were reclaimed");
            Assert.DoesNotThrow(() => _f.Area.Cancel());
            Assert.That(_f.Area.EntryCount, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
