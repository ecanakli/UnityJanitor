using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests
{
    // Every arity has its own Invoke loop, so each one gets the behaviours that live in that loop.
    // The shared storage rules are covered exhaustively on OwnedEvent<int> in OwnedEventTests.
    [TestFixture]
    public sealed class OwnedEventNoArgumentTests
    {
        private static readonly Regex DuplicateWarning = new Regex("JANITOR105");

        private TestScope _t;
        private Lifetime _owner;
        private Lifetime _other;
        private OwnedEvent _event;
        private int _handled;

        [SetUp]
        public void SetUp()
        {
            _t = new TestScope();
            _owner = _t.App.CreateChild("owner");
            _other = _t.App.CreateChild("other");
            _event = new OwnedEvent("Ping");
            _handled = 0;
        }

        [TearDown]
        public void TearDown()
        {
            _t.Complete();
        }

        [Test]
        public void Invoke_DeliversInSubscriptionOrder()
        {
            _event.Subscribe(Recorder("a"), _owner);
            _event.Subscribe(Recorder("b"), _other);
            _event.Subscribe(Recorder("c"), _owner);

            _event.Invoke();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void Invoke_HandlerThrows_IsRoutedWithTheOwnerAndCallSiteAndTheOthersRun()
        {
            _event.Subscribe(Recorder("first"), _owner);
            _event.Subscribe(() => throw new InvalidOperationException("boom"), _owner, "Site", 7);
            _event.Subscribe(Recorder("last"), _other);

            _event.Invoke();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "first", "last" }));
            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].Context.Source, Is.EqualTo(LifetimeErrorSource.EventHandler));
            Assert.That(_t.Errors[0].Context.LifetimeName, Is.EqualTo("owner"));
            Assert.That(_t.Errors[0].Context.Member, Is.EqualTo("Site"));
            Assert.That(_t.Errors[0].Context.Line, Is.EqualTo(7));
            _t.Errors.Clear();
        }

        [Test]
        public void OwnerCancel_RemovesItsSubscriptionsAndResubscribingLandsInTheNewGeneration()
        {
            _event.Subscribe(Recorder("a"), _owner);
            _event.Subscribe(Recorder("kept"), _other);

            _owner.Cancel();
            Assert.That(_event.SubscriberCount, Is.EqualTo(1));
            _event.Subscribe(Recorder("again"), _owner);
            _event.Invoke();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "kept", "again" }));
        }

        [Test]
        public void Subscribe_EqualHandlerForTheSameOwner_ReturnsTheExistingRegistrationAndRaisesJanitor105()
        {
            LogAssert.Expect(LogType.Warning, DuplicateWarning);
            var first = _event.Subscribe(Handle, _owner);

            var second = _event.Subscribe(Handle, _owner);
            _event.Subscribe(Handle, _other);
            _event.Invoke();

            Assert.That(_event.SubscriberCount, Is.EqualTo(2), "a different owner is a separate subscription");
            Assert.That(_handled, Is.EqualTo(2));
            second.Cancel();
            Assert.That(first.IsActive, Is.False);
        }

        [Test]
        public void Subscribe_NullOwner_ThrowsAndAddsNothing()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => _event.Subscribe(Handle, (Lifetime)null));

            Assert.That(exception.ParamName, Is.EqualTo("owner"));
            Assert.That(_event.SubscriberCount, Is.Zero);
        }

        [Test]
        public void Subscribe_OwnerNotActive_AddsNothing()
        {
            var child = _t.App.CreateChild("child");
            child.Dispose();

            var registration = _event.Subscribe(Handle, child);

            Assert.That(registration.IsActive, Is.False);
            Assert.That(_event.SubscriberCount, Is.Zero);
        }

        [Test]
        public void Invoke_OwnerCancelledMidInvoke_IsSkippedInTheSameInvoke()
        {
            _event.Subscribe(() =>
            {
                _t.Log.Add("a");
                _other.Cancel();
            }, _owner);
            _event.Subscribe(Recorder("b"), _other);
            _event.Subscribe(Recorder("c"), _owner);

            _event.Invoke();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a", "c" }));
        }

        [Test]
        public void Invoke_SubscribeDuringInvoke_IsDeliveredByTheNextInvokeOnly()
        {
            var added = false;
            _event.Subscribe(() =>
            {
                _t.Log.Add("a");
                if (!added)
                {
                    added = true;
                    _event.Subscribe(Recorder("late"), _owner);
                }
            }, _owner);

            _event.Invoke();
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a" }));
            _event.Invoke();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a", "a", "late" }));
        }

        [Test]
        public void Invoke_RemovalDuringInvoke_CompactionWaitsForTheOutermostInvoke()
        {
            var first = _event.Subscribe(Recorder("a"), _owner);
            _event.Subscribe(() =>
            {
                _t.Log.Add("b");
                first.Cancel();
                _t.Log.Add("slots" + _event.SlotCount);
            }, _owner);
            _event.Subscribe(Recorder("c"), _owner);

            _event.Invoke();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a", "b", "slots3", "c" }));
            Assert.That(_event.SlotCount, Is.EqualTo(2));
        }

        [Test]
        public void Invoke_DepthLimit_Delivers64AndRefusesThe65thWithJanitor115()
        {
            var deliveries = 0;
            _event.Subscribe(() =>
            {
                deliveries++;
                _event.Invoke();
            }, _owner);

            _event.Invoke();

            Assert.That(deliveries, Is.EqualTo(64));
            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].Exception.Message, Does.Contain("JANITOR115"));
            Assert.That(_t.Errors[0].Context.LifetimeName, Is.EqualTo("OwnedEvent \"Ping\""));
            Assert.That(_event.InvokeDepth, Is.Zero);
            _t.Errors.Clear();
        }

        [Test]
        public void Invoke_OffMainThreadWithSubscribers_ThrowsInEditorAndDevelopmentBuilds()
        {
            _event.Subscribe(Handle, _owner);

            var failure = ThreadRunner.Run(() => _event.Invoke());

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(_handled, Is.Zero);
        }

        [Test]
        public void IOwnedEventView_Subscribe_Delivers()
        {
            IOwnedEvent view = _event;
            view.Subscribe(Handle, _owner);

            _event.Invoke();

            Assert.That(_handled, Is.EqualTo(1));
            Assert.That(typeof(IOwnedEvent).GetMethod("Invoke"), Is.Null);
        }

        [Test]
        public void Label_UsesTheTypeAndTheName()
        {
            IOwnedEventNode named = _event;
            IOwnedEventNode unnamed = new OwnedEvent();

            Assert.That(named.Label, Is.EqualTo("OwnedEvent \"Ping\""));
            Assert.That(unnamed.Label, Is.EqualTo("OwnedEvent"));
        }

        private void Handle()
        {
            _handled++;
        }

        private Action Recorder(string label)
        {
            return () => _t.Log.Add(label);
        }
    }

    [TestFixture]
    public sealed class OwnedEventTwoArgumentTests
    {
        private static readonly Regex DuplicateWarning = new Regex("JANITOR105");

        private TestScope _t;
        private Lifetime _owner;
        private Lifetime _other;
        private OwnedEvent<int, string> _event;
        private int _handled;

        [SetUp]
        public void SetUp()
        {
            _t = new TestScope();
            _owner = _t.App.CreateChild("owner");
            _other = _t.App.CreateChild("other");
            _event = new OwnedEvent<int, string>("Pair");
            _handled = 0;
        }

        [TearDown]
        public void TearDown()
        {
            _t.Complete();
        }

        [Test]
        public void Invoke_DeliversInSubscriptionOrderWithBothArguments()
        {
            _event.Subscribe(Recorder("a"), _owner);
            _event.Subscribe(Recorder("b"), _other);

            _event.Invoke(3, "x");

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a3x", "b3x" }));
        }

        [Test]
        public void Invoke_HandlerThrows_IsRoutedWithTheOwnerAndCallSiteAndTheOthersRun()
        {
            _event.Subscribe(Recorder("first"), _owner);
            _event.Subscribe((n, s) => throw new InvalidOperationException("boom"), _owner, "Site", 8);
            _event.Subscribe(Recorder("last"), _other);

            _event.Invoke(1, "y");

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "first1y", "last1y" }));
            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].Context.Source, Is.EqualTo(LifetimeErrorSource.EventHandler));
            Assert.That(_t.Errors[0].Context.LifetimeName, Is.EqualTo("owner"));
            Assert.That(_t.Errors[0].Context.Member, Is.EqualTo("Site"));
            Assert.That(_t.Errors[0].Context.Line, Is.EqualTo(8));
            _t.Errors.Clear();
        }

        [Test]
        public void OwnerCancel_RemovesItsSubscriptionsAndResubscribingLandsInTheNewGeneration()
        {
            _event.Subscribe(Recorder("a"), _owner);
            _event.Subscribe(Recorder("kept"), _other);

            _owner.Cancel();
            Assert.That(_event.SubscriberCount, Is.EqualTo(1));
            _event.Subscribe(Recorder("again"), _owner);
            _event.Invoke(1, "z");

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "kept1z", "again1z" }));
        }

        [Test]
        public void Subscribe_EqualHandlerForTheSameOwner_ReturnsTheExistingRegistrationAndRaisesJanitor105()
        {
            LogAssert.Expect(LogType.Warning, DuplicateWarning);
            var first = _event.Subscribe(Handle, _owner);

            var second = _event.Subscribe(Handle, _owner);
            _event.Subscribe(Handle, _other);
            _event.Invoke(1, "q");

            Assert.That(_event.SubscriberCount, Is.EqualTo(2));
            Assert.That(_handled, Is.EqualTo(2));
            second.Cancel();
            Assert.That(first.IsActive, Is.False);
        }

        [Test]
        public void Subscribe_NullOwner_ThrowsAndAddsNothing()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => _event.Subscribe(Handle, (Lifetime)null));

            Assert.That(exception.ParamName, Is.EqualTo("owner"));
            Assert.That(_event.SubscriberCount, Is.Zero);
        }

        [Test]
        public void Invoke_OwnerCancelledMidInvoke_IsSkippedInTheSameInvoke()
        {
            _event.Subscribe((n, s) =>
            {
                _t.Log.Add("a");
                _other.Cancel();
            }, _owner);
            _event.Subscribe(Recorder("b"), _other);
            _event.Subscribe(Recorder("c"), _owner);

            _event.Invoke(1, "s");

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a", "c1s" }));
        }

        [Test]
        public void Invoke_SubscribeDuringInvoke_IsDeliveredByTheNextInvokeOnly()
        {
            var added = false;
            _event.Subscribe((n, s) =>
            {
                _t.Log.Add("a" + n);
                if (!added)
                {
                    added = true;
                    _event.Subscribe(Recorder("late"), _owner);
                }
            }, _owner);

            _event.Invoke(1, "s");
            _event.Invoke(2, "s");

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a1", "a2", "late2s" }));
        }

        [Test]
        public void Invoke_RemovalDuringInvoke_CompactionWaitsForTheOutermostInvoke()
        {
            var first = _event.Subscribe(Recorder("a"), _owner);
            _event.Subscribe((n, s) =>
            {
                _t.Log.Add("b");
                first.Cancel();
                _t.Log.Add("slots" + _event.SlotCount);
            }, _owner);
            _event.Subscribe(Recorder("c"), _owner);

            _event.Invoke(0, "");

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a0", "b", "slots3", "c0" }));
            Assert.That(_event.SlotCount, Is.EqualTo(2));
        }

        [Test]
        public void Invoke_DepthLimit_Delivers64AndRefusesThe65thWithJanitor115()
        {
            var deliveries = 0;
            _event.Subscribe((n, s) =>
            {
                deliveries++;
                _event.Invoke(n + 1, s);
            }, _owner);

            _event.Invoke(0, "d");

            Assert.That(deliveries, Is.EqualTo(64));
            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].Exception.Message, Does.Contain("JANITOR115"));
            Assert.That(_t.Errors[0].Context.LifetimeName, Is.EqualTo("OwnedEvent<Int32, String> \"Pair\""));
            Assert.That(_event.InvokeDepth, Is.Zero);
            _t.Errors.Clear();
        }

        [Test]
        public void Invoke_OffMainThreadWithSubscribers_ThrowsInEditorAndDevelopmentBuilds()
        {
            _event.Subscribe(Handle, _owner);

            var failure = ThreadRunner.Run(() => _event.Invoke(1, "t"));

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(_handled, Is.Zero);
        }

        [Test]
        public void IOwnedEventView_Subscribe_Delivers()
        {
            IOwnedEvent<int, string> view = _event;
            view.Subscribe(Handle, _owner);

            _event.Invoke(1, "v");

            Assert.That(_handled, Is.EqualTo(1));
            Assert.That(typeof(IOwnedEvent<int, string>).GetMethod("Invoke"), Is.Null);
        }

        private void Handle(int number, string text)
        {
            _handled++;
        }

        private Action<int, string> Recorder(string label)
        {
            return (number, text) => _t.Log.Add(label + number + text);
        }
    }

    [TestFixture]
    public sealed class OwnedEventThreeArgumentTests
    {
        private static readonly Regex DuplicateWarning = new Regex("JANITOR105");

        private TestScope _t;
        private Lifetime _owner;
        private Lifetime _other;
        private OwnedEvent<int, string, bool> _event;
        private int _handled;

        [SetUp]
        public void SetUp()
        {
            _t = new TestScope();
            _owner = _t.App.CreateChild("owner");
            _other = _t.App.CreateChild("other");
            _event = new OwnedEvent<int, string, bool>("Triple");
            _handled = 0;
        }

        [TearDown]
        public void TearDown()
        {
            _t.Complete();
        }

        [Test]
        public void Invoke_DeliversInSubscriptionOrderWithAllThreeArguments()
        {
            _event.Subscribe(Recorder("a"), _owner);
            _event.Subscribe(Recorder("b"), _other);

            _event.Invoke(3, "x", true);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a3xTrue", "b3xTrue" }));
        }

        [Test]
        public void Invoke_HandlerThrows_IsRoutedWithTheOwnerAndCallSiteAndTheOthersRun()
        {
            _event.Subscribe(Recorder("first"), _owner);
            _event.Subscribe((n, s, b) => throw new InvalidOperationException("boom"), _owner, "Site", 9);
            _event.Subscribe(Recorder("last"), _other);

            _event.Invoke(1, "y", false);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "first1yFalse", "last1yFalse" }));
            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].Context.Source, Is.EqualTo(LifetimeErrorSource.EventHandler));
            Assert.That(_t.Errors[0].Context.LifetimeName, Is.EqualTo("owner"));
            Assert.That(_t.Errors[0].Context.Member, Is.EqualTo("Site"));
            Assert.That(_t.Errors[0].Context.Line, Is.EqualTo(9));
            _t.Errors.Clear();
        }

        [Test]
        public void OwnerCancel_RemovesItsSubscriptionsAndResubscribingLandsInTheNewGeneration()
        {
            _event.Subscribe(Recorder("a"), _owner);
            _event.Subscribe(Recorder("kept"), _other);

            _owner.Cancel();
            Assert.That(_event.SubscriberCount, Is.EqualTo(1));
            _event.Subscribe(Recorder("again"), _owner);
            _event.Invoke(1, "z", true);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "kept1zTrue", "again1zTrue" }));
        }

        [Test]
        public void Subscribe_EqualHandlerForTheSameOwner_ReturnsTheExistingRegistrationAndRaisesJanitor105()
        {
            LogAssert.Expect(LogType.Warning, DuplicateWarning);
            var first = _event.Subscribe(Handle, _owner);

            var second = _event.Subscribe(Handle, _owner);
            _event.Subscribe(Handle, _other);
            _event.Invoke(1, "q", true);

            Assert.That(_event.SubscriberCount, Is.EqualTo(2));
            Assert.That(_handled, Is.EqualTo(2));
            second.Cancel();
            Assert.That(first.IsActive, Is.False);
        }

        [Test]
        public void Subscribe_NullOwner_ThrowsAndAddsNothing()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => _event.Subscribe(Handle, (Lifetime)null));

            Assert.That(exception.ParamName, Is.EqualTo("owner"));
            Assert.That(_event.SubscriberCount, Is.Zero);
        }

        [Test]
        public void Invoke_OwnerCancelledMidInvoke_IsSkippedInTheSameInvoke()
        {
            _event.Subscribe((n, s, b) =>
            {
                _t.Log.Add("a");
                _other.Cancel();
            }, _owner);
            _event.Subscribe(Recorder("b"), _other);
            _event.Subscribe(Recorder("c"), _owner);

            _event.Invoke(1, "s", true);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a", "c1sTrue" }));
        }

        [Test]
        public void Invoke_SubscribeDuringInvoke_IsDeliveredByTheNextInvokeOnly()
        {
            var added = false;
            _event.Subscribe((n, s, b) =>
            {
                _t.Log.Add("a" + n);
                if (!added)
                {
                    added = true;
                    _event.Subscribe(Recorder("late"), _owner);
                }
            }, _owner);

            _event.Invoke(1, "s", true);
            _event.Invoke(2, "s", true);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a1", "a2", "late2sTrue" }));
        }

        [Test]
        public void Invoke_RemovalDuringInvoke_CompactionWaitsForTheOutermostInvoke()
        {
            var first = _event.Subscribe(Recorder("a"), _owner);
            _event.Subscribe((n, s, b) =>
            {
                _t.Log.Add("b");
                first.Cancel();
                _t.Log.Add("slots" + _event.SlotCount);
            }, _owner);
            _event.Subscribe(Recorder("c"), _owner);

            _event.Invoke(0, "", false);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a0False", "b", "slots3", "c0False" }));
            Assert.That(_event.SlotCount, Is.EqualTo(2));
        }

        [Test]
        public void Invoke_DepthLimit_Delivers64AndRefusesThe65thWithJanitor115()
        {
            var deliveries = 0;
            _event.Subscribe((n, s, b) =>
            {
                deliveries++;
                _event.Invoke(n + 1, s, b);
            }, _owner);

            _event.Invoke(0, "d", true);

            Assert.That(deliveries, Is.EqualTo(64));
            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].Exception.Message, Does.Contain("JANITOR115"));
            Assert.That(_t.Errors[0].Context.LifetimeName, Is.EqualTo("OwnedEvent<Int32, String, Boolean> \"Triple\""));
            Assert.That(_event.InvokeDepth, Is.Zero);
            _t.Errors.Clear();
        }

        [Test]
        public void Invoke_OffMainThreadWithSubscribers_ThrowsInEditorAndDevelopmentBuilds()
        {
            _event.Subscribe(Handle, _owner);

            var failure = ThreadRunner.Run(() => _event.Invoke(1, "t", true));

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(_handled, Is.Zero);
        }

        [Test]
        public void IOwnedEventView_Subscribe_Delivers()
        {
            IOwnedEvent<int, string, bool> view = _event;
            view.Subscribe(Handle, _owner);

            _event.Invoke(1, "v", true);

            Assert.That(_handled, Is.EqualTo(1));
            Assert.That(typeof(IOwnedEvent<int, string, bool>).GetMethod("Invoke"), Is.Null);
        }

        private void Handle(int number, string text, bool flag)
        {
            _handled++;
        }

        private Action<int, string, bool> Recorder(string label)
        {
            return (number, text, flag) => _t.Log.Add(label + number + text + flag);
        }
    }
}
