using NUnit.Framework;
using UnityEngine.Events;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.Events
{
    // The Lifetime-owner Subscribe exists once per arity (0 to 4) with its own guard class. Each arity gets the same four checks:
    // delivery plus guard removal, exact removal, the duplicate rule with its label, and the "A cancels B's owner" case.
    [TestFixture]
    public sealed class UnityEventArityTests
    {
        private EventFixture _f;
        private EventSink _sink;
        private Lifetime _owner;
        private Lifetime _ownerB;

        [SetUp]
        public void SetUp()
        {
            _f = new EventFixture();
            _sink = new EventSink(_f.Log, "s");
            _owner = _f.Area.CreateChild("owner");
            _ownerB = _f.Area.CreateChild("ownerB");
        }

        [TearDown]
        public void TearDown()
        {
            _f.Complete();
        }

        // 0 arguments: UnityEventGuard

        // Guard: UnityEventGuard delivers and is removed by Cancel; a raw listener survives.
        [Test]
        public void Subscribe_NoArguments_DeliversAndCancelRemovesTheGuard()
        {
            var evt = new UnityEvent();
            var raw = new EventSink(_f.Log, "raw");
            evt.AddListener(raw.Zero);
            evt.Subscribe(_sink.Zero, _owner);

            evt.Invoke();
            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "raw", "s" }));
            _f.Log.Clear();
            _owner.Cancel();
            evt.Invoke();

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "raw" }));
            UnityEventInspector.AssertCount(evt, 1);
        }

        // Guard: registration cancel and owner dispose each remove only their own guard.
        [Test]
        public void Subscribe_NoArguments_RegistrationCancelAndDispose_RemoveTheirOwnGuardOnly()
        {
            var evt = new UnityEvent();
            var sinkB = new EventSink(_f.Log, "b");
            var sinkC = new EventSink(_f.Log, "c");
            var registration = evt.Subscribe(_sink.Zero, _owner);
            evt.Subscribe(sinkB.Zero, _ownerB);
            evt.Subscribe(sinkC.Zero, _f.Area.CreateChild("third"));

            registration.Cancel();
            _ownerB.Dispose();
            evt.Invoke();

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "c" }));
            UnityEventInspector.AssertCount(evt, 1);
        }

        // Guard: UnityEventGuard.IsSame and Label.
        [Test]
        public void Subscribe_NoArguments_Duplicate_ReturnsTheExistingRegistrationAndLogsJanitor105WithTheLabel()
        {
            var evt = new UnityEvent();
            var first = evt.Subscribe(_sink.Zero, _owner);
            EventFixture.ExpectWarning("JANITOR105.*UnityEvent already has this handler");

            var second = evt.Subscribe(_sink.Zero, _owner);
            evt.Invoke();

            Assert.That(second.EntryId, Is.EqualTo(first.EntryId));
            Assert.That(_owner.EntryCount, Is.EqualTo(1));
            Assert.That(_sink.Calls, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: UnityEventGuard.Invoke checks CanDeliver before it calls the handler.
        [Test]
        public void Invoke_NoArguments_ListenerACancelsTheOwnerOfListenerB_BIsNotCalledInThatInvoke()
        {
            var evt = new UnityEvent();
            evt.Subscribe(() =>
            {
                _f.Log.Add("A");
                _ownerB.Cancel();
            }, _owner);
            evt.Subscribe(() => _f.Log.Add("B"), _ownerB);

            evt.Invoke();

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "A" }));
            LogAssert.NoUnexpectedReceived();
        }

        // 1 argument: UnityEventGuard<T0>

        // Guard: UnityEventGuard<T0> forwards its argument and is removed by Cancel.
        [Test]
        public void Subscribe_OneArgument_DeliversTheArgumentAndCancelRemovesTheGuard()
        {
            var evt = new UnityEvent<int>();
            var raw = new EventSink(_f.Log, "raw");
            evt.AddListener(raw.One);
            evt.Subscribe(_sink.One, _owner);

            evt.Invoke(7);
            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "raw:7", "s:7" }));
            _f.Log.Clear();
            _owner.Cancel();
            evt.Invoke(8);

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "raw:8" }));
            UnityEventInspector.AssertCount(evt, 1);
        }

        // Guard: registration cancel and owner dispose each remove only their own guard.
        [Test]
        public void Subscribe_OneArgument_RegistrationCancelAndDispose_RemoveTheirOwnGuardOnly()
        {
            var evt = new UnityEvent<int>();
            var sinkB = new EventSink(_f.Log, "b");
            var sinkC = new EventSink(_f.Log, "c");
            var registration = evt.Subscribe(_sink.One, _owner);
            evt.Subscribe(sinkB.One, _ownerB);
            evt.Subscribe(sinkC.One, _f.Area.CreateChild("third"));

            registration.Cancel();
            _ownerB.Dispose();
            evt.Invoke(1);

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "c:1" }));
            UnityEventInspector.AssertCount(evt, 1);
        }

        // Guard: UnityEventGuard<T0>.IsSame and Label.
        [Test]
        public void Subscribe_OneArgument_Duplicate_ReturnsTheExistingRegistrationAndLogsJanitor105WithTheLabel()
        {
            var evt = new UnityEvent<int>();
            var first = evt.Subscribe(_sink.One, _owner);
            EventFixture.ExpectWarning("JANITOR105.*UnityEvent<Int32> already has this handler");

            var second = evt.Subscribe(_sink.One, _owner);
            evt.Invoke(1);

            Assert.That(second.EntryId, Is.EqualTo(first.EntryId));
            Assert.That(_owner.EntryCount, Is.EqualTo(1));
            Assert.That(_sink.Calls, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: UnityEventGuard<T0>.Invoke checks CanDeliver before it calls the handler.
        [Test]
        public void Invoke_OneArgument_ListenerACancelsTheOwnerOfListenerB_BIsNotCalledInThatInvoke()
        {
            var evt = new UnityEvent<int>();
            evt.Subscribe(n =>
            {
                _f.Log.Add("A" + n);
                _ownerB.Cancel();
            }, _owner);
            evt.Subscribe(n => _f.Log.Add("B" + n), _ownerB);

            evt.Invoke(1);

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "A1" }));
            LogAssert.NoUnexpectedReceived();
        }

        // 2 arguments: UnityEventGuard<T0, T1>

        // Guard: UnityEventGuard<T0, T1> forwards both arguments and is removed by Cancel.
        [Test]
        public void Subscribe_TwoArguments_DeliversBothArgumentsAndCancelRemovesTheGuard()
        {
            var evt = new UnityEvent<int, string>();
            var raw = new EventSink(_f.Log, "raw");
            evt.AddListener(raw.Two);
            evt.Subscribe(_sink.Two, _owner);

            evt.Invoke(1, "x");
            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "raw:1,x", "s:1,x" }));
            _f.Log.Clear();
            _owner.Cancel();
            evt.Invoke(2, "y");

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "raw:2,y" }));
            UnityEventInspector.AssertCount(evt, 1);
        }

        // Guard: registration cancel and owner dispose each remove only their own guard.
        [Test]
        public void Subscribe_TwoArguments_RegistrationCancelAndDispose_RemoveTheirOwnGuardOnly()
        {
            var evt = new UnityEvent<int, string>();
            var sinkB = new EventSink(_f.Log, "b");
            var sinkC = new EventSink(_f.Log, "c");
            var registration = evt.Subscribe(_sink.Two, _owner);
            evt.Subscribe(sinkB.Two, _ownerB);
            evt.Subscribe(sinkC.Two, _f.Area.CreateChild("third"));

            registration.Cancel();
            _ownerB.Dispose();
            evt.Invoke(1, "x");

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "c:1,x" }));
            UnityEventInspector.AssertCount(evt, 1);
        }

        // Guard: UnityEventGuard<T0, T1>.IsSame and Label.
        [Test]
        public void Subscribe_TwoArguments_Duplicate_ReturnsTheExistingRegistrationAndLogsJanitor105WithTheLabel()
        {
            var evt = new UnityEvent<int, string>();
            var first = evt.Subscribe(_sink.Two, _owner);
            EventFixture.ExpectWarning("JANITOR105.*UnityEvent<Int32, String> already has this handler");

            var second = evt.Subscribe(_sink.Two, _owner);
            evt.Invoke(1, "x");

            Assert.That(second.EntryId, Is.EqualTo(first.EntryId));
            Assert.That(_owner.EntryCount, Is.EqualTo(1));
            Assert.That(_sink.Calls, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: UnityEventGuard<T0, T1>.Invoke checks CanDeliver before it calls the handler.
        [Test]
        public void Invoke_TwoArguments_ListenerACancelsTheOwnerOfListenerB_BIsNotCalledInThatInvoke()
        {
            var evt = new UnityEvent<int, string>();
            evt.Subscribe((n, s) =>
            {
                _f.Log.Add("A" + n + s);
                _ownerB.Cancel();
            }, _owner);
            evt.Subscribe((n, s) => _f.Log.Add("B" + n + s), _ownerB);

            evt.Invoke(1, "x");

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "A1x" }));
            LogAssert.NoUnexpectedReceived();
        }

        // 3 arguments: UnityEventGuard<T0, T1, T2>

        // Guard: UnityEventGuard<T0, T1, T2> forwards all three arguments and is removed by Cancel.
        [Test]
        public void Subscribe_ThreeArguments_DeliversAllArgumentsAndCancelRemovesTheGuard()
        {
            var evt = new UnityEvent<int, string, long>();
            var raw = new EventSink(_f.Log, "raw");
            evt.AddListener(raw.Three);
            evt.Subscribe(_sink.Three, _owner);

            evt.Invoke(1, "x", 3L);
            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "raw:1,x,3", "s:1,x,3" }));
            _f.Log.Clear();
            _owner.Cancel();
            evt.Invoke(2, "y", 4L);

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "raw:2,y,4" }));
            UnityEventInspector.AssertCount(evt, 1);
        }

        // Guard: registration cancel and owner dispose each remove only their own guard.
        [Test]
        public void Subscribe_ThreeArguments_RegistrationCancelAndDispose_RemoveTheirOwnGuardOnly()
        {
            var evt = new UnityEvent<int, string, long>();
            var sinkB = new EventSink(_f.Log, "b");
            var sinkC = new EventSink(_f.Log, "c");
            var registration = evt.Subscribe(_sink.Three, _owner);
            evt.Subscribe(sinkB.Three, _ownerB);
            evt.Subscribe(sinkC.Three, _f.Area.CreateChild("third"));

            registration.Cancel();
            _ownerB.Dispose();
            evt.Invoke(1, "x", 3L);

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "c:1,x,3" }));
            UnityEventInspector.AssertCount(evt, 1);
        }

        // Guard: UnityEventGuard<T0, T1, T2>.IsSame and Label.
        [Test]
        public void Subscribe_ThreeArguments_Duplicate_ReturnsTheExistingRegistrationAndLogsJanitor105WithTheLabel()
        {
            var evt = new UnityEvent<int, string, long>();
            var first = evt.Subscribe(_sink.Three, _owner);
            EventFixture.ExpectWarning("JANITOR105.*UnityEvent<Int32, String, Int64> already has this handler");

            var second = evt.Subscribe(_sink.Three, _owner);
            evt.Invoke(1, "x", 3L);

            Assert.That(second.EntryId, Is.EqualTo(first.EntryId));
            Assert.That(_owner.EntryCount, Is.EqualTo(1));
            Assert.That(_sink.Calls, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: UnityEventGuard<T0, T1, T2>.Invoke checks CanDeliver before it calls the handler.
        [Test]
        public void Invoke_ThreeArguments_ListenerACancelsTheOwnerOfListenerB_BIsNotCalledInThatInvoke()
        {
            var evt = new UnityEvent<int, string, long>();
            evt.Subscribe((n, s, l) =>
            {
                _f.Log.Add("A" + n + s + l);
                _ownerB.Cancel();
            }, _owner);
            evt.Subscribe((n, s, l) => _f.Log.Add("B" + n + s + l), _ownerB);

            evt.Invoke(1, "x", 3L);

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "A1x3" }));
            LogAssert.NoUnexpectedReceived();
        }

        // 4 arguments: UnityEventGuard<T0, T1, T2, T3>

        // Guard: UnityEventGuard<T0, T1, T2, T3> forwards all four arguments and is removed by Cancel.
        [Test]
        public void Subscribe_FourArguments_DeliversAllArgumentsAndCancelRemovesTheGuard()
        {
            var evt = new UnityEvent<int, string, long, bool>();
            var raw = new EventSink(_f.Log, "raw");
            evt.AddListener(raw.Four);
            evt.Subscribe(_sink.Four, _owner);

            evt.Invoke(1, "x", 3L, true);
            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "raw:1,x,3,True", "s:1,x,3,True" }));
            _f.Log.Clear();
            _owner.Cancel();
            evt.Invoke(2, "y", 4L, false);

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "raw:2,y,4,False" }));
            UnityEventInspector.AssertCount(evt, 1);
        }

        // Guard: registration cancel and owner dispose each remove only their own guard.
        [Test]
        public void Subscribe_FourArguments_RegistrationCancelAndDispose_RemoveTheirOwnGuardOnly()
        {
            var evt = new UnityEvent<int, string, long, bool>();
            var sinkB = new EventSink(_f.Log, "b");
            var sinkC = new EventSink(_f.Log, "c");
            var registration = evt.Subscribe(_sink.Four, _owner);
            evt.Subscribe(sinkB.Four, _ownerB);
            evt.Subscribe(sinkC.Four, _f.Area.CreateChild("third"));

            registration.Cancel();
            _ownerB.Dispose();
            evt.Invoke(1, "x", 3L, true);

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "c:1,x,3,True" }));
            UnityEventInspector.AssertCount(evt, 1);
        }

        // Guard: UnityEventGuard<T0, T1, T2, T3>.IsSame and Label.
        [Test]
        public void Subscribe_FourArguments_Duplicate_ReturnsTheExistingRegistrationAndLogsJanitor105WithTheLabel()
        {
            var evt = new UnityEvent<int, string, long, bool>();
            var first = evt.Subscribe(_sink.Four, _owner);
            EventFixture.ExpectWarning("JANITOR105.*UnityEvent<Int32, String, Int64, Boolean> already has this handler");

            var second = evt.Subscribe(_sink.Four, _owner);
            evt.Invoke(1, "x", 3L, true);

            Assert.That(second.EntryId, Is.EqualTo(first.EntryId));
            Assert.That(_owner.EntryCount, Is.EqualTo(1));
            Assert.That(_sink.Calls, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        // Guard: UnityEventGuard<T0, T1, T2, T3>.Invoke checks CanDeliver before it calls the handler.
        [Test]
        public void Invoke_FourArguments_ListenerACancelsTheOwnerOfListenerB_BIsNotCalledInThatInvoke()
        {
            var evt = new UnityEvent<int, string, long, bool>();
            evt.Subscribe((n, s, l, b) =>
            {
                _f.Log.Add("A" + n + s + l + b);
                _ownerB.Cancel();
            }, _owner);
            evt.Subscribe((n, s, l, b) => _f.Log.Add("B" + n + s + l + b), _ownerB);

            evt.Invoke(1, "x", 3L, true);

            Assert.That(_f.Log.ToArray(), Is.EqualTo(new[] { "A1x3True" }));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
