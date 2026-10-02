using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests
{
    // Cancel and Dispose never throw; failures are routed and the operation continues.
    [TestFixture]
    public sealed class LifetimeNeverThrowsTests
    {
        private TestScope _t;

        [SetUp]
        public void SetUp()
        {
            _t = new TestScope();
        }

        [TearDown]
        public void TearDown()
        {
            _t.Complete();
        }

        [Test]
        public void Cancel_TerminateActionThrows_IsRoutedWithContextAndRemainingItemsStillRun()
        {
            var area = _t.App.CreateChild("area");
            area.Record(_t.Log, "first");
            area.OnCancel(() => throw new InvalidOperationException("boom"));
            area.Record(_t.Log, "last");

            Assert.DoesNotThrow(() => area.Cancel());

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "last", "first" }), "items around the failing one must still run");
            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            var captured = _t.Errors[0];
            Assert.That(captured.Exception, Is.TypeOf<InvalidOperationException>());
            Assert.That(captured.Exception.Message, Is.EqualTo("boom"));
            Assert.That(captured.Context.Source, Is.EqualTo(LifetimeErrorSource.CancelAction));
            Assert.That(captured.Context.LifetimeName, Is.EqualTo("area"));
            Assert.That(captured.Context.Member, Is.EqualTo(nameof(Cancel_TerminateActionThrows_IsRoutedWithContextAndRemainingItemsStillRun)));
            Assert.That(captured.Context.Line, Is.GreaterThan(0));
            Assert.That(captured.Context.Owner, Is.Null);
            Assert.That(area.State, Is.EqualTo(LifetimeState.Active));
            Assert.That(area.Generation, Is.EqualTo(1), "a failing item must not stop the generation from ending");
            _t.Errors.Clear();
        }

        [Test]
        public void Dispose_TerminateActionThrows_IsRoutedAndRemainingItemsStillRun()
        {
            var area = _t.App.CreateChild("area");
            var child = area.CreateChild("child");
            area.Record(_t.Log, "area");
            child.OnCancel(() => throw new InvalidOperationException("boom"));
            child.Record(_t.Log, "child");

            Assert.DoesNotThrow(() => area.Dispose());

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "child", "area" }));
            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].Context.LifetimeName, Is.EqualTo("child"));
            Assert.That(area.IsDisposed, Is.True);
            Assert.That(child.IsDisposed, Is.True);
            _t.Errors.Clear();
        }

        [Test]
        public void Cancel_EveryItemThrows_AllAreRoutedAndTheGenerationStillEnds()
        {
            var area = _t.App.CreateChild("area");
            for (var i = 0; i < 5; i++)
            {
                area.OnCancel(() => throw new InvalidOperationException("boom"));
            }

            Assert.DoesNotThrow(() => area.Cancel());

            Assert.That(_t.Errors.Count, Is.EqualTo(5));
            Assert.That(area.EntryCount, Is.Zero);
            Assert.That(area.Generation, Is.EqualTo(1));
            _t.Errors.Clear();
        }

        [Test]
        public void Cancel_TerminateActionThrowsOperationCanceled_IsNotRoutedAndTheRestRun()
        {
            var area = _t.App.CreateChild("area");
            area.Record(_t.Log, "first");
            area.OnCancel(() => throw new OperationCanceledException());
            area.OnCancel(() => throw new TaskCanceledException());
            area.Record(_t.Log, "last");

            Assert.DoesNotThrow(() => area.Cancel());

            Assert.That(_t.Errors.Count, Is.Zero, "cancellation is never an error");
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "last", "first" }));
        }

        [Test]
        public void Cancel_TokenCallbacksThrow_AggregateIsUnwrappedAndEachInnerIsRouted()
        {
            var area = _t.App.CreateChild("area");
            area.Token.Register(() => throw new InvalidOperationException("callback one"));
            area.Token.Register(() => throw new ArgumentException("callback two"));
            area.Record(_t.Log, "item");

            Assert.DoesNotThrow(() => area.Cancel());

            Assert.That(_t.Errors.Count, Is.EqualTo(2));
            Assert.That(_t.Errors.Messages(), Is.EquivalentTo(new[] { "callback one", "callback two" }));
            for (var i = 0; i < _t.Errors.Count; i++)
            {
                Assert.That(_t.Errors[i].Exception, Is.Not.InstanceOf<AggregateException>(), "the inner exception must be routed, not the aggregate");
                Assert.That(_t.Errors[i].Context.Source, Is.EqualTo(LifetimeErrorSource.TokenCallback));
                Assert.That(_t.Errors[i].Context.LifetimeName, Is.EqualTo("area"));
            }

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "item" }), "items must still run after a failing token callback");
            Assert.That(area.Generation, Is.EqualTo(1));
            _t.Errors.Clear();
        }

        [Test]
        public void Cancel_SingleTokenCallbackThrows_IsRoutedAsTheInnerException()
        {
            var area = _t.App.CreateChild("area");
            area.Token.Register(() => throw new InvalidOperationException("only one"));

            Assert.DoesNotThrow(() => area.Cancel());

            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].Exception, Is.TypeOf<InvalidOperationException>());
            _t.Errors.Clear();
        }

        [Test]
        public void Cancel_TokenCallbackThrowsOperationCanceled_IsNotRouted()
        {
            var area = _t.App.CreateChild("area");
            area.Token.Register(() => throw new OperationCanceledException());
            area.Record(_t.Log, "item");

            Assert.DoesNotThrow(() => area.Cancel());

            Assert.That(_t.Errors.Count, Is.Zero);
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "item" }));
        }

        [Test]
        public void Cancel_TokenCallbackOfOneLifetimeThrows_OtherLifetimesTokensAndItemsStillRun()
        {
            var parent = _t.App.CreateChild("parent");
            var child = parent.CreateChild("child");
            parent.Token.Register(() => throw new InvalidOperationException("parent callback"));
            child.RecordToken(_t.Log, "child:token");
            child.Record(_t.Log, "child:item");
            parent.Record(_t.Log, "parent:item");

            Assert.DoesNotThrow(() => parent.Cancel());

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "child:token", "child:item", "parent:item" }));
            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            _t.Errors.Clear();
        }

        [Test]
        public void Cancel_HandlerThrows_FallsBackToDebugLogExceptionAndTheOperationContinues()
        {
            LogAssert.Expect(LogType.Exception, new Regex("handler boom"));
            LogAssert.Expect(LogType.Exception, new Regex("original boom"));
            LifetimeErrors.Handler = ThrowingHandler;
            var area = _t.App.CreateChild("area");
            area.Record(_t.Log, "first");
            area.OnCancel(() => throw new InvalidOperationException("original boom"));
            area.Record(_t.Log, "last");

            Assert.DoesNotThrow(() => area.Cancel());

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "last", "first" }));
            Assert.That(area.Generation, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_HandlerThrows_FallsBackToDebugLogExceptionAndTheOperationContinues()
        {
            LogAssert.Expect(LogType.Exception, new Regex("handler boom"));
            LogAssert.Expect(LogType.Exception, new Regex("original boom"));
            LifetimeErrors.Handler = ThrowingHandler;
            var area = _t.App.CreateChild("area");
            area.OnCancel(() => throw new InvalidOperationException("original boom"));
            area.Record(_t.Log, "other");

            Assert.DoesNotThrow(() => area.Dispose());

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "other" }));
            Assert.That(area.IsDisposed, Is.True);
        }

        [Test]
        public void Cancel_CapturingHandlerThrowsAfterRecording_ErrorIsStillCapturedAndFallbackLogs()
        {
            LogAssert.Expect(LogType.Exception, new Regex("recorder failure"));
            LogAssert.Expect(LogType.Exception, new Regex("item failure"));
            _t.Errors.ThrowFromHandler = new InvalidOperationException("recorder failure");
            var area = _t.App.CreateChild("area");
            area.OnCancel(() => throw new InvalidOperationException("item failure"));

            Assert.DoesNotThrow(() => area.Cancel());

            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            _t.Errors.Clear();
        }

        [Test]
        public void Cancel_ItemThrowsInChild_ParentItemsAndFinalizeStillHappen()
        {
            var parent = _t.App.CreateChild("parent");
            var child = parent.CreateChild("child");
            child.OnCancel(() => throw new InvalidOperationException("boom"));
            parent.Record(_t.Log, "parent");

            Assert.DoesNotThrow(() => parent.Cancel());

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "parent" }));
            Assert.That(parent.Generation, Is.EqualTo(1));
            Assert.That(child.Generation, Is.EqualTo(1));
            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            _t.Errors.Clear();
        }

        [Test]
        public void RegistrationCancel_TerminateActionThrows_IsRoutedAndDoesNotThrow()
        {
            var area = _t.App.CreateChild("area");
            var registration = area.OnCancel(() => throw new InvalidOperationException("boom"));

            Assert.DoesNotThrow(() => registration.Cancel());

            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].Context.Source, Is.EqualTo(LifetimeErrorSource.CancelAction));
            _t.Errors.Clear();
        }

        [Test]
        public void Registration_OnEndingLifetimeWithThrowingItem_IsRoutedAndDoesNotThrow()
        {
            var area = _t.App.CreateChild("area");
            area.Dispose();

            Assert.DoesNotThrow(() => area.OnCancel(() => throw new InvalidOperationException("boom")));

            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            _t.Errors.Clear();
        }

        [Test]
        public void AddTo_ItemDisposeThrowsOnCancel_IsRoutedAsCancelAction()
        {
            var area = _t.App.CreateChild("area");
            var probe = new DisposeProbe { ThrowOnDispose = new InvalidOperationException("dispose boom") };
            probe.AddTo(area);

            Assert.DoesNotThrow(() => area.Cancel());

            Assert.That(probe.DisposeCount, Is.EqualTo(1));
            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].Exception.Message, Is.EqualTo("dispose boom"));
            Assert.That(_t.Errors[0].Context.Source, Is.EqualTo(LifetimeErrorSource.CancelAction));
            _t.Errors.Clear();
        }

        // The handler is static and raises by design; LogAssert expects the fallback's two logs.
        private static void ThrowingHandler(Exception exception, in LifetimeErrorContext context)
        {
            throw new InvalidOperationException("handler boom");
        }
    }
}
