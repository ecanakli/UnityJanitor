using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests
{
    // OwnedEvent<int>: order, isolation, ownership, re-entrancy, duplicates, storage.
    [TestFixture]
    public sealed class OwnedEventTests
    {
        private static readonly Regex DuplicateWarning = new Regex("JANITOR105");

        private TestScope _t;
        private Lifetime _owner;
        private Lifetime _other;
        private OwnedEvent<int> _event;
        private int _handled;

        [SetUp]
        public void SetUp()
        {
            _t = new TestScope();
            _owner = _t.App.CreateChild("owner");
            _other = _t.App.CreateChild("other");
            _event = new OwnedEvent<int>("Coins");
            _handled = 0;
        }

        [TearDown]
        public void TearDown()
        {
            _t.Complete();
        }

        // Delivery

        [Test]
        public void Invoke_WithoutSubscribers_DoesNothing()
        {
            Assert.DoesNotThrow(() => _event.Invoke(1));

            Assert.That(_event.SubscriberCount, Is.Zero);
            Assert.That(_event.InvokeDepth, Is.Zero);
        }

        [Test]
        public void Invoke_DeliversInSubscriptionOrderAcrossOwners()
        {
            _event.Subscribe(Recorder("a"), _owner);
            _event.Subscribe(Recorder("b"), _other);
            _event.Subscribe(Recorder("c"), _owner);

            _event.Invoke(1);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a1", "b1", "c1" }));
            Assert.That(_event.SubscriberCount, Is.EqualTo(3));
        }

        [Test]
        public void Invoke_PassesTheArgumentToEveryHandler()
        {
            var seen = new List<int>();
            _event.Subscribe(v => seen.Add(v), _owner);
            _event.Subscribe(v => seen.Add(v * 10), _other);

            _event.Invoke(7);

            Assert.That(seen, Is.EqualTo(new[] { 7, 70 }));
        }

        [Test]
        public void Invoke_CalledRepeatedly_DeliversEachTime()
        {
            _event.Subscribe(Handle, _owner);

            _event.Invoke(1);
            _event.Invoke(2);
            _event.Invoke(3);

            Assert.That(_handled, Is.EqualTo(6));
        }

        // Error isolation and routing

        [Test]
        public void Invoke_HandlerThrows_IsRoutedWithTheOwnerAndTheCallSiteAndTheOthersStillRun()
        {
            _event.Subscribe(Recorder("first"), _owner);
            _event.Subscribe(v => throw new InvalidOperationException("handler boom"), _owner, "SubscribeSite", 42);
            _event.Subscribe(Recorder("last"), _other);

            Assert.DoesNotThrow(() => _event.Invoke(1));

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "first1", "last1" }), "the handlers around the failing one must run");
            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            var captured = _t.Errors[0];
            Assert.That(captured.Exception, Is.TypeOf<InvalidOperationException>());
            Assert.That(captured.Exception.Message, Is.EqualTo("handler boom"));
            Assert.That(captured.Context.Source, Is.EqualTo(LifetimeErrorSource.EventHandler));
            Assert.That(captured.Context.LifetimeName, Is.EqualTo("owner"));
            Assert.That(captured.Context.Member, Is.EqualTo("SubscribeSite"));
            Assert.That(captured.Context.Line, Is.EqualTo(42));
            Assert.That(_event.InvokeDepth, Is.Zero, "a failing handler must not leave the depth raised");
            _t.Errors.Clear();
        }

        [Test]
        public void Invoke_HandlerThrows_ContextCarriesTheOwnersUnityObject()
        {
            var host = new GameObject("errorHost");
            try
            {
                var lifetime = _t.App.CreateChildCore("hosted", LifetimeKind.GameObject, false, host, null, 0);
                _event.Subscribe(v => throw new InvalidOperationException("hosted boom"), lifetime);

                _event.Invoke(1);

                Assert.That(_t.Errors.Count, Is.EqualTo(1));
                Assert.That(_t.Errors[0].Context.Owner, Is.EqualTo(host));
                _t.Errors.Clear();
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Invoke_CallSiteDefaults_UseTheSubscribingMember()
        {
            _event.Subscribe(v => throw new InvalidOperationException("boom"), _owner);

            _event.Invoke(1);

            Assert.That(_t.Errors[0].Context.Member, Is.EqualTo(nameof(Invoke_CallSiteDefaults_UseTheSubscribingMember)));
            Assert.That(_t.Errors[0].Context.Line, Is.GreaterThan(0));
            _t.Errors.Clear();
        }

        [Test]
        public void Invoke_HandlerThrowsOperationCanceledException_IsSilentAndTheOthersRun()
        {
            _event.Subscribe(v => throw new OperationCanceledException(), _owner);
            _event.Subscribe(Recorder("after"), _owner);

            _event.Invoke(1);

            Assert.That(_t.Errors.Count, Is.Zero);
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "after1" }));
        }

        [Test]
        public void Invoke_EveryHandlerThrows_AllAreRoutedAndTheEventStaysUsable()
        {
            for (var i = 0; i < 4; i++)
            {
                var index = i;
                _event.Subscribe(v => throw new InvalidOperationException("boom" + index), _owner);
            }

            _event.Invoke(1);

            Assert.That(_t.Errors.Count, Is.EqualTo(4));
            Assert.That(_event.SubscriberCount, Is.EqualTo(4), "a failing handler stays subscribed");
            _t.Errors.Clear();
        }

        // Ownership

        [Test]
        public void Invoke_AfterTheOwnerIsCancelled_SkipsItsHandlersAndTheyAreRemoved()
        {
            _event.Subscribe(Recorder("owned"), _owner);
            _event.Subscribe(Recorder("kept"), _other);

            _owner.Cancel();
            _event.Invoke(1);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "kept1" }));
            Assert.That(_event.SubscriberCount, Is.EqualTo(1), "the package removes the subscription when the generation ends");
        }

        [Test]
        public void Invoke_AfterTheOwnerIsDisposed_SkipsItsHandlersAndTheyAreRemoved()
        {
            var child = _t.App.CreateChild("child");
            _event.Subscribe(Recorder("owned"), child);
            _event.Subscribe(Recorder("kept"), _other);

            child.Dispose();
            _event.Invoke(1);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "kept1" }));
            Assert.That(_event.SubscriberCount, Is.EqualTo(1));
        }

        [Test]
        public void Invoke_InsideATokenCallbackOfTheEndingGeneration_SkipsOnlyTheEndingOwner()
        {
            _owner.Token.Register(() => _event.Invoke(1));
            _event.Subscribe(Recorder("ending"), _owner);
            _event.Subscribe(Recorder("alive"), _other);

            _owner.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "alive1" }), "an owner that is Cancelling must be skipped before its entries are drained");
        }

        [Test]
        public void Invoke_SubscriberFromAnotherGeneration_IsSkipped()
        {
            _event.Subscribe(Recorder("stale"), _owner);
            _event.Subscribe(Recorder("alive"), _other);
            _owner.FinishCancel();

            _event.Invoke(1);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "alive1" }), "a slot pinned to an older generation must not be delivered");
            _owner.Cancel();
            Assert.That(_event.SubscriberCount, Is.EqualTo(1), "the leftover entry still removes its slot");
        }

        [Test]
        public void Invoke_OwnerCancelledMidInvokeBeforeItsHandlerIsReached_IsSkippedInTheSameInvoke()
        {
            _event.Subscribe(v =>
            {
                _t.Log.Add("a");
                _other.Cancel();
            }, _owner);
            _event.Subscribe(v => _t.Log.Add("b"), _other);
            _event.Subscribe(v => _t.Log.Add("c"), _owner);

            _event.Invoke(1);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a", "c" }), "a handler removed during Invoke must not run in that Invoke");
            Assert.That(_event.SubscriberCount, Is.EqualTo(2));
        }

        [Test]
        public void Invoke_RegistrationCancelledMidInvokeBeforeItsHandlerIsReached_IsSkippedInTheSameInvoke()
        {
            LifetimeRegistration second = default;
            _event.Subscribe(v =>
            {
                _t.Log.Add("a");
                second.Cancel();
            }, _owner);
            second = _event.Subscribe(v => _t.Log.Add("b"), _owner);
            _event.Subscribe(v => _t.Log.Add("c"), _owner);

            _event.Invoke(1);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a", "c" }));
        }

        [Test]
        public void Invoke_OwnerCancelledByItsOwnHandler_SkipsItsLaterHandlers()
        {
            _event.Subscribe(v =>
            {
                _t.Log.Add("a");
                _owner.Cancel();
            }, _owner);
            _event.Subscribe(v => _t.Log.Add("b"), _owner);
            _event.Subscribe(v => _t.Log.Add("c"), _other);

            _event.Invoke(1);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a", "c" }));
            Assert.That(_event.SubscriberCount, Is.EqualTo(1));
        }

        [Test]
        public void Invoke_HandlerCancelsItsOwnRegistration_TheRestStillRun()
        {
            LifetimeRegistration self = default;
            self = _event.Subscribe(v =>
            {
                _t.Log.Add("self");
                self.Cancel();
            }, _owner);
            _event.Subscribe(v => _t.Log.Add("next"), _owner);

            _event.Invoke(1);
            _event.Invoke(2);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "self", "next", "next" }));
            Assert.That(_event.SubscriberCount, Is.EqualTo(1));
        }

        [Test]
        public void OwnerCancel_RemovesAllOfThatOwnersSubscriptionsAndResubscribingLandsInTheNewGeneration()
        {
            var first = _event.Subscribe(Recorder("a"), _owner);
            _event.Subscribe(Recorder("b"), _owner);
            _event.Subscribe(Recorder("kept"), _other);

            _owner.Cancel();

            Assert.That(_event.SubscriberCount, Is.EqualTo(1));
            Assert.That(first.IsActive, Is.False);

            _event.Subscribe(Recorder("again"), _owner);
            _event.Invoke(1);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "kept1", "again1" }));
            _owner.Cancel();
            Assert.That(_event.SubscriberCount, Is.EqualTo(1), "the new generation's subscription is removed by the next Cancel");
        }

        [Test]
        public void OwnerDispose_RemovesItsSubscriptionsAndLaterSubscribesAreRefused()
        {
            var child = _t.App.CreateChild("child");
            _event.Subscribe(Handle, child);

            child.Dispose();
            var refused = _event.Subscribe(Handle, child);

            Assert.That(_event.SubscriberCount, Is.Zero);
            Assert.That(refused.IsActive, Is.False);
            _event.Invoke(1);
            Assert.That(_handled, Is.Zero);
        }

        [Test]
        public void RegistrationCancel_RemovesExactlyOneSubscription()
        {
            var a = _event.Subscribe(Recorder("a"), _owner);
            var b = _event.Subscribe(Recorder("b"), _owner);
            var c = _event.Subscribe(Recorder("c"), _owner);

            b.Cancel();
            _event.Invoke(1);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a1", "c1" }));
            Assert.That(a.IsActive, Is.True);
            Assert.That(b.IsActive, Is.False);
            Assert.That(c.IsActive, Is.True);
            Assert.That(_event.SubscriberCount, Is.EqualTo(2));
        }

        [Test]
        public void RegistrationCancel_CalledTwice_IsANoOp()
        {
            var a = _event.Subscribe(Recorder("a"), _owner);
            _event.Subscribe(Recorder("b"), _owner);

            a.Cancel();
            a.Cancel();

            Assert.That(_event.SubscriberCount, Is.EqualTo(1));
        }

        [Test]
        public void RegistrationCancel_AfterTheOwnerWasCancelled_IsANoOp()
        {
            var a = _event.Subscribe(Recorder("a"), _owner);
            _event.Subscribe(Recorder("kept"), _other);
            _owner.Cancel();

            Assert.DoesNotThrow(() => a.Cancel());

            Assert.That(_event.SubscriberCount, Is.EqualTo(1));
        }

        // Subscribe rules

        [Test]
        public void Subscribe_NullOwner_ThrowsArgumentNullExceptionAndAddsNothing()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => _event.Subscribe(Handle, (Lifetime)null));

            Assert.That(exception.ParamName, Is.EqualTo("owner"));
            Assert.That(_event.SubscriberCount, Is.Zero);
            _event.Invoke(1);
            Assert.That(_handled, Is.Zero);
        }

        [Test]
        public void Subscribe_NullHandler_ThrowsArgumentNullExceptionAndAddsNothing()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => _event.Subscribe(null, _owner));

            Assert.That(exception.ParamName, Is.EqualTo("handler"));
            Assert.That(_event.SubscriberCount, Is.Zero);
            Assert.That(_owner.EntryCount, Is.Zero);
        }

        [Test]
        public void Subscribe_WhileTheOwnerIsCancelling_AddsNothing()
        {
            LifetimeRegistration seen = default;
            _owner.OnCancel(() => seen = _event.Subscribe(Handle, _owner));

            _owner.Cancel();

            Assert.That(seen.IsActive, Is.False);
            Assert.That(_event.SubscriberCount, Is.Zero, "a subscription made while the generation is ending must not be added");
            Assert.That(_owner.EntryCount, Is.Zero);
        }

        [Test]
        public void Subscribe_OnADisposedOwner_AddsNothing()
        {
            var child = _t.App.CreateChild("child");
            child.Dispose();

            var registration = _event.Subscribe(Handle, child);

            Assert.That(registration.IsActive, Is.False);
            Assert.That(_event.SubscriberCount, Is.Zero);
        }

        [Test]
        public void Subscribe_OffMainThread_ThrowsInvalidOperationExceptionAndAddsNothing()
        {
            var failure = ThreadRunner.Run(() => _event.Subscribe(Handle, _owner));

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(_event.SubscriberCount, Is.Zero);
            Assert.That(_owner.EntryCount, Is.Zero);
        }

        [Test]
        public void Invoke_OffMainThreadWithSubscribers_ThrowsInEditorAndDevelopmentBuilds()
        {
            _event.Subscribe(Handle, _owner);

            var failure = ThreadRunner.Run(() => _event.Invoke(1));

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(_handled, Is.Zero, "nothing may be delivered off the main thread");
            Assert.That(_event.InvokeDepth, Is.Zero);
        }

        [Test]
        public void Invoke_OffMainThreadWithoutSubscribers_DoesNotThrow()
        {
            var failure = ThreadRunner.Run(() => _event.Invoke(1));

            Assert.That(failure, Is.Null);
        }

        // Duplicates

        [Test]
        public void Subscribe_EqualHandlerForTheSameOwner_ReturnsTheExistingRegistrationAndRaisesJanitor105()
        {
            LogAssert.Expect(LogType.Warning, DuplicateWarning);
            var first = _event.Subscribe(Handle, _owner);

            var second = _event.Subscribe(Handle, _owner);

            Assert.That(_event.SubscriberCount, Is.EqualTo(1));
            Assert.That(second.IsActive, Is.True);
            _event.Invoke(1);
            Assert.That(_handled, Is.EqualTo(1), "a duplicate must not double the calls");
            second.Cancel();
            Assert.That(first.IsActive, Is.False, "both handles address the same entry");
            Assert.That(_event.SubscriberCount, Is.Zero);
        }

        [Test]
        public void Subscribe_TheSameDelegateInstanceTwice_IsADuplicate()
        {
            LogAssert.Expect(LogType.Warning, DuplicateWarning);
            Action<int> handler = Handle;
            _event.Subscribe(handler, _owner);

            _event.Subscribe(handler, _owner);

            Assert.That(_event.SubscriberCount, Is.EqualTo(1));
        }

        [Test]
        public void Subscribe_TheSameHandlerForADifferentOwner_IsASeparateSubscription()
        {
            _event.Subscribe(Handle, _owner);
            _event.Subscribe(Handle, _other);

            _event.Invoke(1);

            Assert.That(_event.SubscriberCount, Is.EqualTo(2));
            Assert.That(_handled, Is.EqualTo(2));
        }

        [Test]
        public void Subscribe_DifferentHandlersOnTheSameTargetForTheSameOwner_AreNotDuplicates()
        {
            _event.Subscribe(Handle, _owner);
            _event.Subscribe(HandleTwice, _owner);

            _event.Invoke(1);

            Assert.That(_event.SubscriberCount, Is.EqualTo(2));
            Assert.That(_handled, Is.EqualTo(3));
        }

        [Test]
        public void Subscribe_SameHandlerAfterTheOwnersCancel_IsNotADuplicate()
        {
            _event.Subscribe(Handle, _owner);
            _owner.Cancel();

            _event.Subscribe(Handle, _owner);

            Assert.That(_event.SubscriberCount, Is.EqualTo(1));
            _event.Invoke(1);
            Assert.That(_handled, Is.EqualTo(1));
        }

        [Test]
        public void Subscribe_SameHandlerAfterItsRegistrationWasCancelled_IsNotADuplicate()
        {
            var first = _event.Subscribe(Handle, _owner);
            first.Cancel();

            var second = _event.Subscribe(Handle, _owner);

            Assert.That(second.IsActive, Is.True);
            Assert.That(_event.SubscriberCount, Is.EqualTo(1));
        }

        // Re-entrancy

        [Test]
        public void Invoke_SubscribeDuringInvoke_IsDeliveredByTheNextInvokeOnly()
        {
            var added = false;
            _event.Subscribe(v =>
            {
                _t.Log.Add("a" + v);
                if (!added)
                {
                    added = true;
                    _event.Subscribe(Recorder("late"), _owner);
                }
            }, _owner);
            _event.Subscribe(Recorder("b"), _owner);

            _event.Invoke(1);
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a1", "b1" }), "the new subscriber must not be delivered by the running Invoke");
            Assert.That(_event.SubscriberCount, Is.EqualTo(3));

            _event.Invoke(2);
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a1", "b1", "a2", "b2", "late2" }));
        }

        [Test]
        public void Invoke_NestedInvoke_DeliversToEverySubscriptionThenTheOuterContinues()
        {
            var nested = false;
            _event.Subscribe(v =>
            {
                _t.Log.Add("a" + v);
                if (!nested)
                {
                    nested = true;
                    _event.Invoke(2);
                }
            }, _owner);
            _event.Subscribe(Recorder("b"), _owner);
            _event.Subscribe(Recorder("c"), _owner);

            _event.Invoke(1);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a1", "a2", "b2", "c2", "b1", "c1" }));
            Assert.That(_event.InvokeDepth, Is.Zero);
        }

        [Test]
        public void Invoke_NestedInvokeStartedAfterASubscribe_DeliversTheNewSubscriberInTheNestedCallOnly()
        {
            var started = false;
            _event.Subscribe(v =>
            {
                _t.Log.Add("a" + v);
                if (!started)
                {
                    started = true;
                    _event.Subscribe(Recorder("late"), _owner);
                    _event.Invoke(2);
                }
            }, _owner);
            _event.Subscribe(Recorder("b"), _owner);

            _event.Invoke(1);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a1", "a2", "b2", "late2", "b1" }), "the nested call sees every subscription live when it starts; the outer call keeps its own bound");
        }

        [Test]
        public void Invoke_RemovalDuringInvoke_CompactionWaitsForTheOutermostInvoke()
        {
            var registrations = new LifetimeRegistration[4];
            var slotCounts = new List<int>();
            registrations[0] = _event.Subscribe(v => _t.Log.Add("a"), _owner);
            registrations[1] = _event.Subscribe(v =>
            {
                _t.Log.Add("b");
                registrations[0].Cancel();
                slotCounts.Add(_event.SlotCount);
            }, _owner);
            registrations[2] = _event.Subscribe(v =>
            {
                _t.Log.Add("c");
                slotCounts.Add(_event.SlotCount);
            }, _owner);
            registrations[3] = _event.Subscribe(v => _t.Log.Add("d"), _owner);

            _event.Invoke(1);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a", "b", "c", "d" }), "compacting a removed earlier slot mid-invoke would skip a handler");
            Assert.That(slotCounts, Is.EqualTo(new[] { 4, 4 }), "the removed slot stays a hole while the invoke runs");
            Assert.That(_event.SubscriberCount, Is.EqualTo(3));
            Assert.That(_event.SlotCount, Is.EqualTo(3), "the hole is compacted when the outermost invoke returns");
        }

        [Test]
        public void Invoke_RemovalInsideANestedInvoke_KeepsTheOuterIterationCorrect()
        {
            var registrations = new LifetimeRegistration[4];
            registrations[0] = _event.Subscribe(v => _t.Log.Add("a" + v), _owner);
            registrations[1] = _event.Subscribe(v =>
            {
                _t.Log.Add("b" + v);
                if (v == 1)
                {
                    registrations[0].Cancel();
                    _event.Invoke(9);
                    _t.Log.Add("slots" + _event.SlotCount);
                }
            }, _owner);
            registrations[2] = _event.Subscribe(v => _t.Log.Add("c" + v), _owner);
            registrations[3] = _event.Subscribe(v => _t.Log.Add("d" + v), _owner);

            _event.Invoke(1);

            Assert.That(
                _t.Log.ToArray(),
                Is.EqualTo(new[] { "a1", "b1", "b9", "c9", "d9", "slots4", "c1", "d1" }),
                "the nested invoke must not compact, and the outer call must still visit c and d");
            Assert.That(_event.SlotCount, Is.EqualTo(3));
        }

        [Test]
        public void Invoke_DepthLimit_Delivers64AndRefusesThe65thWithJanitor115()
        {
            var deliveries = 0;
            _event.Subscribe(v =>
            {
                deliveries++;
                _event.Invoke(v + 1);
            }, _owner);

            _event.Invoke(0);

            Assert.That(deliveries, Is.EqualTo(64), "64 nested invokes deliver");
            Assert.That(_t.Errors.Count, Is.EqualTo(1), "the refusal is routed exactly once");
            var captured = _t.Errors[0];
            Assert.That(captured.Exception, Is.TypeOf<InvalidOperationException>());
            Assert.That(captured.Exception.Message, Does.Contain("JANITOR115"));
            Assert.That(captured.Context.Source, Is.EqualTo(LifetimeErrorSource.EventHandler));
            Assert.That(captured.Context.Owner, Is.Null);
            Assert.That(captured.Context.LifetimeName, Is.EqualTo("OwnedEvent<Int32> \"Coins\""));
            Assert.That(_event.InvokeDepth, Is.Zero, "the depth must unwind completely");
            _t.Errors.Clear();
        }

        [Test]
        public void Invoke_AfterTheDepthLimitWasHit_WorksAgain()
        {
            var recurse = true;
            var deliveries = 0;
            _event.Subscribe(v =>
            {
                deliveries++;
                if (recurse)
                {
                    _event.Invoke(v + 1);
                }
            }, _owner);
            _event.Invoke(0);
            _t.Errors.Clear();
            recurse = false;
            deliveries = 0;

            _event.Invoke(0);

            Assert.That(deliveries, Is.EqualTo(1));
            Assert.That(_t.Errors.Count, Is.Zero);
        }

        [Test]
        public void Invoke_DepthLimitInterleavedWithAnotherSubscriber_StillDeliversToTheOtherSubscriberAtEveryLevel()
        {
            var reentrant = 0;
            var passive = 0;
            _event.Subscribe(v =>
            {
                reentrant++;
                _event.Invoke(v + 1);
            }, _owner);
            _event.Subscribe(v => passive++, _other);

            _event.Invoke(0);

            Assert.That(reentrant, Is.EqualTo(64));
            Assert.That(passive, Is.EqualTo(64), "the second subscriber runs once per delivered level as each level unwinds");
            _t.Errors.Clear();
        }

        [Test]
        public void Invoke_SubscribeManyDuringInvoke_GrowsTheStorageWithoutDisturbingTheRunningInvoke()
        {
            var grown = false;
            _event.Subscribe(v =>
            {
                _t.Log.Add("a" + v);
                if (!grown)
                {
                    grown = true;
                    for (var i = 0; i < 20; i++)
                    {
                        _event.Subscribe(Tagged(i), _owner);
                    }
                }
            }, _owner);
            _event.Subscribe(Recorder("b"), _owner);

            _event.Invoke(1);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a1", "b1" }), "the array was replaced mid-loop and the running Invoke must not see the new slots");
            Assert.That(_event.SubscriberCount, Is.EqualTo(22));
            _t.Log.Clear();
            _event.Invoke(2);
            var expected = new List<string> { "a2", "b2" };
            for (var i = 0; i < 20; i++)
            {
                expected.Add("t" + i);
            }

            Assert.That(_t.Log.ToArray(), Is.EqualTo(expected.ToArray()));
        }

        // Storage

        [Test]
        public void Subscribe_AfterCompaction_OwnerCancelStillFindsTheRightSlots()
        {
            var owners = new Lifetime[5];
            for (var i = 0; i < owners.Length; i++)
            {
                owners[i] = _t.App.CreateChild("o" + i);
                _event.Subscribe(Tagged(i), owners[i]);
            }

            owners[1].Cancel();
            owners[4].Cancel();
            _event.Invoke(0);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "t0", "t2", "t3" }), "the serial lookup must follow slots that compaction moved");
            owners[2].Cancel();
            _t.Log.Clear();
            _event.Invoke(0);
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "t0", "t3" }));
        }

        [Test]
        public void Subscribe_AfterCompaction_RegistrationCancelStillFindsTheRightSlots()
        {
            var registrations = new LifetimeRegistration[5];
            for (var i = 0; i < registrations.Length; i++)
            {
                registrations[i] = _event.Subscribe(Tagged(i), _owner);
            }

            registrations[1].Cancel();
            registrations[4].Cancel();
            registrations[2].Cancel();
            _event.Invoke(0);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "t0", "t3" }));
            Assert.That(registrations[0].IsActive, Is.True);
            Assert.That(registrations[3].IsActive, Is.True);
        }

        [Test]
        public void Remove_LargeListInterior_CompactsOnceAQuarterOfTheSlotsAreHoles()
        {
            const int Count = 40;
            var owners = new Lifetime[Count];
            for (var i = 0; i < Count; i++)
            {
                owners[i] = _t.App.CreateChild("o" + i);
                _event.Subscribe(Tagged(i), owners[i]);
            }

            for (var i = 1; i <= 9; i++)
            {
                owners[i].Cancel();
            }

            Assert.That(_event.SubscriberCount, Is.EqualTo(31));
            Assert.That(_event.SlotCount, Is.EqualTo(40), "nine holes are below a quarter of the slots");
            _event.Invoke(0);
            Assert.That(_t.Log.Count, Is.EqualTo(31), "holes are skipped");

            owners[10].Cancel();

            Assert.That(_event.SubscriberCount, Is.EqualTo(30));
            Assert.That(_event.SlotCount, Is.EqualTo(30), "the tenth hole reaches a quarter and compacts");
            _t.Log.Clear();
            _event.Invoke(0);
            var expected = new List<string> { "t0" };
            for (var i = 11; i < Count; i++)
            {
                expected.Add("t" + i);
            }

            Assert.That(_t.Log.ToArray(), Is.EqualTo(expected.ToArray()));
        }

        [Test]
        public void Remove_TheNewestSubscriptions_TrimTheTailAtOnceEvenInALargeList()
        {
            const int Count = 30;
            var registrations = new LifetimeRegistration[Count];
            for (var i = 0; i < Count; i++)
            {
                registrations[i] = _event.Subscribe(Tagged(i), _owner);
            }

            for (var i = Count - 1; i >= 20; i--)
            {
                registrations[i].Cancel();
            }

            Assert.That(_event.SubscriberCount, Is.EqualTo(20));
            Assert.That(_event.SlotCount, Is.EqualTo(20), "trailing holes are trimmed without a compaction pass");
        }

        [Test]
        public void SubscribeAndCancel_10000Cycles_KeepDeliveryOrderAndBoundedStorage()
        {
            const int LongLived = 3;
            const int Temps = 8;
            const int Cycles = 10000;
            var delivered = new List<int>();
            var handlers = new Action<int>[LongLived + Temps];
            for (var i = 0; i < handlers.Length; i++)
            {
                var id = i;
                handlers[i] = v => delivered.Add(id);
            }

            var registrations = new LifetimeRegistration[handlers.Length];
            var model = new List<int>();
            for (var i = 0; i < LongLived; i++)
            {
                registrations[i] = _event.Subscribe(handlers[i], _owner);
                model.Add(i);
            }

            for (var cycle = 0; cycle < Cycles; cycle++)
            {
                var id = LongLived + (cycle % Temps);
                if (registrations[id].IsActive)
                {
                    registrations[id].Cancel();
                    model.Remove(id);
                }

                registrations[id] = _event.Subscribe(handlers[id], _owner);
                model.Add(id);
                if (cycle % 2500 == 2499)
                {
                    AssertDeliveryOrder(delivered, model);
                }
            }

            Assert.That(_event.SubscriberCount, Is.EqualTo(model.Count));
            Assert.That(_event.SlotCount, Is.EqualTo(model.Count), "small lists must not accumulate holes");
            Assert.That(_owner.EntryCount, Is.EqualTo(model.Count), "the owner holds exactly one entry per live subscription");
            Assert.That(_owner.EntryCapacity, Is.LessThanOrEqualTo(16), "entry slots must be reused");

            for (var i = LongLived; i < handlers.Length; i++)
            {
                registrations[i].Cancel();
                model.Remove(i);
            }

            AssertDeliveryOrder(delivered, model);
            Assert.That(model, Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void SubscribeAndOwnerCancel_10000Cycles_KeepStorageBounded()
        {
            const int Cycles = 10000;
            var keeper = _event.Subscribe(Handle, _other);
            Action<int> temp = HandleTwice;

            for (var cycle = 0; cycle < Cycles; cycle++)
            {
                _event.Subscribe(temp, _owner);
                if ((cycle & 1) == 0)
                {
                    _owner.Cancel();
                }
                else
                {
                    _owner.Cancel();
                    _event.Subscribe(temp, _owner);
                    _owner.Cancel();
                }
            }

            Assert.That(_event.SubscriberCount, Is.EqualTo(1));
            Assert.That(_event.SlotCount, Is.EqualTo(1));
            Assert.That(keeper.IsActive, Is.True);
            Assert.That(_owner.EntryCount, Is.Zero);
        }

        // Views and labels

        [Test]
        public void IOwnedEventView_Subscribe_DeliversAndFlowsTheCallerInfo()
        {
            IOwnedEvent<int> view = _event;
            view.Subscribe(v => throw new InvalidOperationException("view boom"), _owner);

            _event.Invoke(1);

            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].Context.Member, Is.EqualTo(nameof(IOwnedEventView_Subscribe_DeliversAndFlowsTheCallerInfo)));
            _t.Errors.Clear();
        }

        [Test]
        public void IOwnedEventView_DoesNotExposeInvoke()
        {
            Assert.That(typeof(IOwnedEvent<int>).GetMethod("Invoke"), Is.Null);
            Assert.That(typeof(IOwnedEvent<int>).GetMethods().Length, Is.EqualTo(1));
        }

        [Test]
        public void Label_NamedAndUnnamedEvents_UseTheTypeAndTheName()
        {
            IOwnedEventNode named = _event;
            IOwnedEventNode unnamed = new OwnedEvent<string>();

            Assert.That(named.Label, Is.EqualTo("OwnedEvent<Int32> \"Coins\""));
            Assert.That(unnamed.Label, Is.EqualTo("OwnedEvent<String>"));
        }

        private void Handle(int value)
        {
            _handled += value;
        }

        private void HandleTwice(int value)
        {
            _handled += value * 2;
        }

        private Action<int> Recorder(string label)
        {
            return value => _t.Log.Add(label + value);
        }

        private Action<int> Tagged(int tag)
        {
            return value => _t.Log.Add("t" + tag);
        }

        // Invokes the event and compares the delivery order with the model of live subscriptions.
        private void AssertDeliveryOrder(List<int> delivered, List<int> model)
        {
            delivered.Clear();
            _event.Invoke(0);
            Assert.That(delivered, Is.EqualTo(model));
        }
    }
}
