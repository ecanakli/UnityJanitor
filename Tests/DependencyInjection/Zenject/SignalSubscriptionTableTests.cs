using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools.Constraints;
using Zenject;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Ecanakli.Janitor.DependencyInjection.Tests
{
    // The static subscription table behind the SignalBus duplicate rule: it forgets on every termination path, is reset per play
    // session (domain reload may be off), and never keeps a bus alive.
    [TestFixture]
    public sealed class SignalSubscriptionTableTests : SignalBusFixtureBase
    {
        // Termination paths

        // Guard: a tree shutdown ends every lifetime, and every subscription record goes with it.
        [Test]
        public void Shutdown_OfTheTree_ForgetsEveryRecord()
        {
            var child = Area.CreateChild("child");
            Bus.Subscribe<TestSignal>(Typed, Area);
            Bus.Subscribe<OtherSignal>(Plain, child);
            Assert.That(SignalSubscriptions.LiveCount, Is.EqualTo(2));

            Scope.Tree.Shutdown();

            Assert.That(SignalSubscriptions.LiveCount, Is.Zero);
            Assert.That(Bus.NumSubscribers, Is.Zero);
        }

        // Guard: repeated subscribe and cancel cycles neither pile up records nor trip the duplicate rule.
        [Test]
        public void SubscribeAndCancel_ManyCycles_LeaveNoRecordsAndNoWarnings()
        {
            for (var i = 0; i < 500; i++)
            {
                Bus.Subscribe<TestSignal>(Typed, Area);
                Assert.That(SignalSubscriptions.LiveCount, Is.EqualTo(1));
                Area.Cancel();
            }

            Assert.That(SignalSubscriptions.LiveCount, Is.Zero);
            Assert.That(Bus.NumSubscribers, Is.Zero);
            Assert.That(Warnings.Count("JANITOR105"), Is.Zero);
        }

        // Session reset

        // Guard: ResetSession empties the table, and a terminate that finds no record does not push the count below zero.
        [Test]
        public void ResetSession_ClearsTheTable_AndALaterTerminateDoesNotUnderflow()
        {
            Bus.Subscribe<TestSignal>(Typed, Area);
            Assert.That(SignalSubscriptions.LiveCount, Is.EqualTo(1));

            SignalSubscriptions.ResetSession();
            Assert.That(SignalSubscriptions.LiveCount, Is.Zero);
            Area.Cancel();

            Assert.That(SignalSubscriptions.LiveCount, Is.Zero, "a forgotten record is not forgotten twice");
            Assert.That(Bus.NumSubscribers, Is.Zero, "the terminate still unsubscribed from the bus");
        }

        // Guard (b): the reset is what Unity runs at every session start, so it survives disabled domain reload.
        [Test]
        public void ResetSession_IsRegisteredForEverySubsystemRegistration()
        {
            var method = typeof(SignalSubscriptions).GetMethod("ResetSession", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            var attribute = method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
            Assert.That(attribute, Is.Not.Null, "the reset must carry RuntimeInitializeOnLoadMethod");
            Assert.That(attribute.loadType, Is.EqualTo(RuntimeInitializeLoadType.SubsystemRegistration));
        }

        // Weak hold on the bus

        // Guard (c): the table hangs off the bus weakly. While a record is live the owner's entry holds the bus anyway, so a
        // behavioural check of that case cannot tell a strong table from a weak one; the field type is the guard.
        [Test]
        public void Table_IsKeyedWeaklyByTheBus()
        {
            var field = typeof(SignalSubscriptions).GetField("_tables", BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            Assert.That(field.FieldType.IsGenericType, Is.True);
            Assert.That(field.FieldType.GetGenericTypeDefinition(), Is.EqualTo(typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>)));
            Assert.That(field.FieldType.GetGenericArguments()[0], Is.EqualTo(typeof(SignalBus)));
        }

        // Allocation

        // Guard (0 B from the package): Zenject allocates its own subscription wrapper and boxes on unsubscribe, so the package's share is
        // the difference to a raw Subscribe and TryUnsubscribe pair with the same handler on the same bus. The unit is the number of GC.Alloc
        // samples the profiler recorder counts (the same source as Is.Not.AllocatingGCMemory), which works in the editor without a frame.
        // A per-call allocation in the package would add one sample per cycle, so 64 cycles make it obvious.
        [Test]
        public void SubscribeAndCancel_AllocateNoMoreThanRawSignalBusCalls()
        {
            const int cycles = 64;
            const int slack = 2;
            Action raw = () =>
            {
                for (var i = 0; i < cycles; i++)
                {
                    Bus.Subscribe<TestSignal>(Typed);
                    Bus.TryUnsubscribe(Typed);
                }
            };
            Action owned = () =>
            {
                for (var i = 0; i < cycles; i++)
                {
                    Bus.Subscribe<TestSignal>(Typed, Area);
                    Area.Cancel();
                }
            };
            raw();
            owned();

            var rawSamples = CountAllocations(raw);
            var ownedSamples = CountAllocations(owned);

            Assert.That(rawSamples, Is.GreaterThan(0), "the raw SignalBus calls allocate, so the measurement must see them");
            Assert.That(Bus.NumSubscribers, Is.Zero, "the measured loops really ran");
            Assert.That(ownedSamples, Is.LessThanOrEqualTo(rawSamples + slack), "raw: " + rawSamples + " allocations, with an owner: " + ownedSamples + ", over " + cycles + " cycles");
        }

        // Guard: a refused registration (owner already disposed) does no bus work and no table work, so a warm call allocates nothing.
        // The measured delegate runs once first: Mono allocates the first time a lambda's string literal (the caller-info member name) is loaded.
        [Test]
        public void Subscribe_OnADisposedOwner_AllocatesNothing()
        {
            var dead = Scope.App.CreateChild("dead");
            dead.Dispose();
            TestDelegate measured = () => { Bus.Subscribe<TestSignal>(Typed, dead); };
            measured();
            measured();

            Assert.That(measured, Is.Not.AllocatingGCMemory());

            Assert.That(SignalSubscriptions.LiveCount, Is.Zero);
            Assert.That(Bus.NumSubscribers, Is.Zero);
        }

        // Counts the GC.Alloc samples of the current thread while the action runs.
        private static int CountAllocations(Action action)
        {
            var recorder = Recorder.Get("GC.Alloc");
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;
            try
            {
                action();
            }
            finally
            {
                recorder.enabled = false;
                recorder.CollectFromAllThreads();
            }

            return recorder.sampleBlockCount;
        }
    }
}
