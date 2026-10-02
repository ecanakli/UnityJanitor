using NUnit.Framework;
using UnityEngine.Events;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Ecanakli.Janitor.Tests.Events
{
    // UnityEvent cost: Subscribe allocates the guard by design, only Invoke is measured. Each measured delegate runs once first (Mono ldstr artifact).
    // The raw-listener control separates the engine's own cost from the guard's.
    [TestFixture]
    public sealed class UnityEventAllocationTests
    {
        private const int Count = 16;
        private const int Rounds = 100;

        private EventFixture _f;

        [SetUp]
        public void SetUp()
        {
            _f = new EventFixture();
        }

        [TearDown]
        public void TearDown()
        {
            _f.Complete();
        }

        // Guard: UnityEventGuard.Invoke is four field reads and a call; the listener delegate is built once in the constructor.
        [Test]
        public void Invoke_SixteenGuardedListeners_AllocatesNothing()
        {
            var sink = new CountingSink();
            var evt = new UnityEvent();
            for (var i = 0; i < Count; i++)
            {
                evt.Subscribe(sink.Zero, _f.Area.CreateChild("o" + i));
            }

            TestDelegate measured = () =>
            {
                for (var i = 0; i < Rounds; i++)
                {
                    evt.Invoke();
                }
            };
            measured();

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(sink.Value, Is.EqualTo(Count * Rounds * 2), "warm-up and measured runs, every invoke reaching every listener");
            UnityEventInspector.AssertCount(evt, Count);
        }

        // Guard: UnityEventGuard<T0>.
        [Test]
        public void Invoke_SixteenGuardedListenersOneArgument_AllocatesNothing()
        {
            var sink = new CountingSink();
            var evt = new UnityEvent<int>();
            for (var i = 0; i < Count; i++)
            {
                evt.Subscribe(sink.One, _f.Area.CreateChild("o" + i));
            }

            TestDelegate measured = () =>
            {
                for (var i = 0; i < Rounds; i++)
                {
                    evt.Invoke(i);
                }
            };
            measured();

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(sink.Value, Is.EqualTo(Count * Rounds * 2));
        }

        // Guard: UnityEventGuard<T0, T1, T2, T3>, the widest form, with a string argument (passed by reference, no formatting).
        [Test]
        public void Invoke_SixteenGuardedListenersFourArguments_AllocatesNothing()
        {
            var sink = new CountingSink();
            var evt = new UnityEvent<int, string, long, bool>();
            for (var i = 0; i < Count; i++)
            {
                evt.Subscribe(sink.Four, _f.Area.CreateChild("o" + i));
            }

            TestDelegate measured = () =>
            {
                for (var i = 0; i < Rounds; i++)
                {
                    evt.Invoke(i, "x", 3L, true);
                }
            };
            measured();

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(sink.Value, Is.EqualTo(Count * Rounds * 2));
        }

        // Control: the engine's own cost for the same shape with raw listeners; it must be zero for the guarded tests to mean anything.
        [Test]
        public void Invoke_SixteenRawListeners_ControlAllocatesNothing()
        {
            var sink = new CountingSink();
            var evt = new UnityEvent();
            for (var i = 0; i < Count; i++)
            {
                evt.AddListener(sink.Zero);
            }

            TestDelegate measured = () =>
            {
                for (var i = 0; i < Rounds; i++)
                {
                    evt.Invoke();
                }
            };
            measured();

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(sink.Value, Is.EqualTo(Count * Rounds * 2));
        }

        // Guard: a guard that was removed is skipped by the engine, so invoking after teardown allocates nothing either.
        [Test]
        public void Invoke_AfterAllSubscriptionsWereCancelled_AllocatesNothing()
        {
            var sink = new CountingSink();
            var evt = new UnityEvent();
            for (var i = 0; i < Count; i++)
            {
                evt.Subscribe(sink.Zero, _f.Area.CreateChild("o" + i));
            }

            _f.Area.Cancel();
            TestDelegate measured = () =>
            {
                for (var i = 0; i < Rounds; i++)
                {
                    evt.Invoke();
                }
            };
            measured();

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(sink.Value, Is.Zero);
            UnityEventInspector.AssertCount(evt, 0);
        }
    }
}
