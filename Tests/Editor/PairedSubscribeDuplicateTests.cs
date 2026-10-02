using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Ecanakli.Janitor.Tests
{
    // The paired Subscribe ignores a repeat of the same add, remove and handler on one owner, like OwnedEvent does.
    // Equal means the same target and method each, so lambdas that capture a per-call variable are not recognised.
    [TestFixture]
    public sealed class PairedSubscribeDuplicateTests
    {
        private delegate void CustomCallback(string name);

        private DiagnosticsRecordingKit _d;
        private TestScope _t;
        private Lifetime _area;
        private Source _source;
        private Source _other;
        private Sink _sink;

        [SetUp]
        public void SetUp()
        {
            _d = new DiagnosticsRecordingKit();
            _t = new TestScope();
            _area = _t.App.CreateChild("area");
            _source = new Source();
            _other = new Source();
            _sink = new Sink();
            StaticSource.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            StaticSource.Clear();
            try
            {
                _t.Complete();
            }
            finally
            {
                _d.Dispose();
            }
        }

        // The lambdas capture only the fixture, so every call makes new delegates for the same two methods.
        private LifetimeRegistration SubscribeZero(Lifetime owner, Action handler)
        {
            return owner.Subscribe(h => _source.Zero += h, h => _source.Zero -= h, handler);
        }

        private LifetimeRegistration SubscribeZeroOnOther(Lifetime owner, Action handler)
        {
            return owner.Subscribe(h => _other.Zero += h, h => _other.Zero -= h, handler);
        }

        private LifetimeRegistration SubscribeOne(Lifetime owner, Action<int> handler)
        {
            return owner.Subscribe<int>(h => _source.One += h, h => _source.One -= h, handler);
        }

        private LifetimeRegistration SubscribeCustom(Lifetime owner, CustomCallback handler)
        {
            return owner.Subscribe<CustomCallback>(h => _source.Custom += h, h => _source.Custom -= h, handler);
        }

        private static LifetimeRegistration SubscribeStatic(Lifetime owner, Action handler)
        {
            return owner.Subscribe(static h => StaticSource.Add(h), static h => StaticSource.Remove(h), handler);
        }

        private void AddZero(Action handler)
        {
            _source.Zero += handler;
        }

        private void RemoveZero(Action handler)
        {
            _source.Zero -= handler;
        }

        [Test]
        public void Subscribe_TheSameAddRemoveAndHandlerTwice_AddsOnceAndReturnsTheFirstRegistration()
        {
            Action handler = _sink.OnZero;
            var first = SubscribeZero(_area, handler);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));

            var second = SubscribeZero(_area, handler);
            _source.RaiseZero();

            Assert.That(_source.Adds, Is.EqualTo(1), "add runs once");
            Assert.That(_area.EntryCount, Is.EqualTo(1));
            Assert.That(second.IsActive, Is.True, "the second call hands back the live registration");
            Assert.That(_sink.Value, Is.EqualTo(1), "the handler is delivered once");
            Assert.That(_d.Count("JANITOR105"), Is.EqualTo(1));

            second.Cancel();

            Assert.That(first.IsActive, Is.False, "both handles are the same entry");
            Assert.That(_source.Removes, Is.EqualTo(1));
        }

        [Test]
        public void Subscribe_TheHandlerAsAMethodGroupOnEachCall_IsTheSameHandler()
        {
            SubscribeZero(_area, _sink.OnZero);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));

            SubscribeZero(_area, _sink.OnZero);

            Assert.That(_source.Adds, Is.EqualTo(1));
            Assert.That(_area.EntryCount, Is.EqualTo(1));
        }

        [Test]
        public void Subscribe_StaticLambdasAndAMethodGroupHandler_AreTheSameSubscription()
        {
            SubscribeStatic(_area, _sink.OnZero);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));

            SubscribeStatic(_area, _sink.OnZero);

            Assert.That(StaticSource.Count, Is.EqualTo(1));
            Assert.That(_area.EntryCount, Is.EqualTo(1));
        }

        [Test]
        public void Subscribe_AddAndRemoveGivenAsMethodGroups_AreTheSameSubscription()
        {
            Action handler = _sink.OnZero;
            _area.Subscribe(AddZero, RemoveZero, handler);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));

            _area.Subscribe(AddZero, RemoveZero, handler);

            Assert.That(_source.Adds, Is.EqualTo(1));
            Assert.That(_area.EntryCount, Is.EqualTo(1));
        }

        [Test]
        public void Subscribe_OneArgumentAndCustomDelegateForms_IgnoreARepeatToo()
        {
            Action<int> one = _sink.OnInt;
            CustomCallback custom = _sink.OnCustom;
            SubscribeOne(_area, one);
            SubscribeCustom(_area, custom);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));

            SubscribeOne(_area, one);
            SubscribeCustom(_area, custom);

            Assert.That(_source.Adds, Is.EqualTo(2), "one add per form");
            Assert.That(_area.EntryCount, Is.EqualTo(2));
        }

        [Test]
        public void Subscribe_ADifferentHandler_IsASeparateSubscription()
        {
            SubscribeZero(_area, _sink.OnZero);
            SubscribeZero(_area, _sink.OnZeroAlt);

            Assert.That(_source.Adds, Is.EqualTo(2));
            Assert.That(_area.EntryCount, Is.EqualTo(2));
            Assert.That(_d.Count("JANITOR105"), Is.Zero);
        }

        [Test]
        public void Subscribe_ADifferentOwner_IsASeparateSubscription()
        {
            var second = _area.CreateChild("second");
            Action handler = _sink.OnZero;

            SubscribeZero(_area, handler);
            SubscribeZero(second, handler);

            Assert.That(_source.Adds, Is.EqualTo(2));
            Assert.That(_d.Count("JANITOR105"), Is.Zero);
        }

        [Test]
        public void Subscribe_ADifferentEvent_IsASeparateSubscription()
        {
            Action handler = _sink.OnZero;

            SubscribeZero(_area, handler);
            SubscribeZeroOnOther(_area, handler);

            Assert.That(_source.Adds, Is.EqualTo(1));
            Assert.That(_other.Adds, Is.EqualTo(1));
            Assert.That(_area.EntryCount, Is.EqualTo(2));
            Assert.That(_d.Count("JANITOR105"), Is.Zero);
        }

        [Test]
        public void Subscribe_AfterTheFirstRegistrationWasCancelled_RegistersAgain()
        {
            Action handler = _sink.OnZero;
            var first = SubscribeZero(_area, handler);
            first.Cancel();

            var second = SubscribeZero(_area, handler);

            Assert.That(_source.Adds, Is.EqualTo(2));
            Assert.That(second.IsActive, Is.True);
            Assert.That(_d.Count("JANITOR105"), Is.Zero);
        }

        [Test]
        public void Subscribe_AfterTheOwnerWasCancelled_RegistersInTheNewGeneration()
        {
            Action handler = _sink.OnZero;
            SubscribeZero(_area, handler);
            _area.Cancel();

            SubscribeZero(_area, handler);

            Assert.That(_source.Adds, Is.EqualTo(2));
            Assert.That(_source.Removes, Is.EqualTo(1));
            Assert.That(_d.Count("JANITOR105"), Is.Zero);
        }

        [Test]
        public void Subscribe_AnOnCancelEntryWithTheSameHandlerAndRemove_IsNeverTakenForAPairedOne()
        {
            Action handler = _sink.OnZero;
            _area.OnCancel(handler, static h => { });
            _area.OnCancel(handler, RemoveZero);

            SubscribeZero(_area, handler);
            _area.Subscribe(AddZero, RemoveZero, handler);

            Assert.That(_source.Adds, Is.EqualTo(2), "the two paired calls use different lambdas and methods");
            Assert.That(_area.EntryCount, Is.EqualTo(4));
            Assert.That(_d.Count("JANITOR105"), Is.Zero);
        }

        // The documented limit: a lambda that captures a variable declared per call is a new object each time.
        [Test]
        public void Subscribe_LambdasCapturingAPerCallVariable_AreNotRecognisedAsRepeats()
        {
            Action handler = _sink.OnZero;

            for (var i = 0; i < 2; i++)
            {
                var source = _source;
                _area.Subscribe(h => source.Zero += h, h => source.Zero -= h, handler);
            }

            Assert.That(_source.Adds, Is.EqualTo(2));
            Assert.That(_area.EntryCount, Is.EqualTo(2));
            Assert.That(_d.Count("JANITOR105"), Is.Zero);
        }

        [Test]
        public void Subscribe_ManyDifferentHandlersOnOneOwner_AllocateNothingInTheScan()
        {
            const int count = 16;
            var handlers = new Action[count];
            for (var i = 0; i < count / 2; i++)
            {
                var sink = new Sink();
                handlers[2 * i] = sink.OnZero;
                handlers[(2 * i) + 1] = sink.OnZeroAlt;
            }

            SubscribeAll(_area, handlers);
            _area.Cancel();
            SubscribeAll(_area, handlers);
            _area.Cancel();

            Assert.That(() => SubscribeAll(_area, handlers), Is.Not.AllocatingGCMemory());

            Assert.That(_area.EntryCount, Is.EqualTo(count), "every call must have registered");
            Assert.That(StaticSource.Count, Is.EqualTo(count));
            _area.Cancel();
            Assert.That(StaticSource.Count, Is.Zero);
        }

        private static void SubscribeAll(Lifetime owner, Action[] handlers)
        {
            for (var i = 0; i < handlers.Length; i++)
            {
                SubscribeStatic(owner, handlers[i]);
            }
        }

        private sealed class Sink
        {
            public int Value;

            public void OnZero()
            {
                Value++;
            }

            public void OnZeroAlt()
            {
                Value += 2;
            }

            public void OnInt(int value)
            {
                Value += value;
            }

            public void OnCustom(string name)
            {
                Value++;
            }
        }

        private sealed class Source
        {
            private Action _zero;
            private Action<int> _one;
            private CustomCallback _custom;

            public int Adds;
            public int Removes;

            public event Action Zero
            {
                add
                {
                    _zero += value;
                    Adds++;
                }
                remove
                {
                    _zero -= value;
                    Removes++;
                }
            }

            public event Action<int> One
            {
                add
                {
                    _one += value;
                    Adds++;
                }
                remove
                {
                    _one -= value;
                    Removes++;
                }
            }

            public event CustomCallback Custom
            {
                add
                {
                    _custom += value;
                    Adds++;
                }
                remove
                {
                    _custom -= value;
                    Removes++;
                }
            }

            public void RaiseZero()
            {
                _zero?.Invoke();
            }
        }

        // A static source that stores handlers by reference in a fixed array, so it never allocates itself.
        private static class StaticSource
        {
            private static readonly Action[] Handlers = new Action[64];

            public static int Count
            {
                get
                {
                    var count = 0;
                    for (var i = 0; i < Handlers.Length; i++)
                    {
                        if (!ReferenceEquals(Handlers[i], null))
                        {
                            count++;
                        }
                    }

                    return count;
                }
            }

            public static void Add(Action handler)
            {
                for (var i = 0; i < Handlers.Length; i++)
                {
                    if (ReferenceEquals(Handlers[i], null))
                    {
                        Handlers[i] = handler;
                        return;
                    }
                }
            }

            public static void Remove(Action handler)
            {
                for (var i = 0; i < Handlers.Length; i++)
                {
                    if (ReferenceEquals(Handlers[i], handler))
                    {
                        Handlers[i] = null;
                        return;
                    }
                }
            }

            public static void Clear()
            {
                Array.Clear(Handlers, 0, Handlers.Length);
            }
        }
    }
}
