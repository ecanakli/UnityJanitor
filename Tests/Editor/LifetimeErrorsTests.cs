using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests
{
    // The error routing surface: the handler property, the context, the wrapper exception and the labels.
    [TestFixture]
    public sealed class LifetimeErrorsTests
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
        public void Handler_WhenSetToNull_ReturnsTheDefaultHandler()
        {
            LifetimeErrors.Handler = null;

            Assert.That(LifetimeErrors.Handler, Is.Not.Null);
        }

        [Test]
        public void Handler_SetNullAfterCustom_RestoresTheSameDefaultInstance()
        {
            LifetimeErrors.Handler = null;
            var defaultHandler = LifetimeErrors.Handler;

            LifetimeErrors.Handler = (Exception e, in LifetimeErrorContext c) => { };
            Assert.That(LifetimeErrors.Handler, Is.Not.SameAs(defaultHandler));
            LifetimeErrors.Handler = null;

            Assert.That(LifetimeErrors.Handler, Is.SameAs(defaultHandler));
        }

        [Test]
        public void DefaultHandler_LogsALifetimeWorkExceptionWithTheContext()
        {
            // Unity prints the innermost exception first; the wrapper follows as "Rethrow as LifetimeWorkException".
            LogAssert.Expect(LogType.Exception, new Regex(@"InvalidOperationException: default boom"));
            LifetimeErrors.Handler = null;
            var area = _t.App.CreateChild("area");
            area.OnCancel(() => throw new InvalidOperationException("default boom"));

            area.Cancel();
        }

        [Test]
        public void Dispatch_OperationCanceledException_IsNotRouted()
        {
            var context = new LifetimeErrorContext(LifetimeErrorSource.Task, null, "x", "m", 1);

            LifetimeErrors.Dispatch(new OperationCanceledException(), in context);
            LifetimeErrors.Dispatch(new TaskCanceledException(), in context);

            Assert.That(_t.Errors.Count, Is.Zero);
        }

        [Test]
        public void Dispatch_NullException_IsIgnored()
        {
            var context = new LifetimeErrorContext(LifetimeErrorSource.Task, null, "x", "m", 1);

            Assert.DoesNotThrow(() => LifetimeErrors.Dispatch(null, in context));
            Assert.That(_t.Errors.Count, Is.Zero);
        }

        [Test]
        public void Dispatch_OrdinaryException_ReachesTheHandlerWithItsContext()
        {
            var context = new LifetimeErrorContext(LifetimeErrorSource.EventHandler, null, "name", "member", 42);
            var failure = new InvalidOperationException("routed");

            LifetimeErrors.Dispatch(failure, in context);

            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].Exception, Is.SameAs(failure));
            Assert.That(_t.Errors[0].Context.Source, Is.EqualTo(LifetimeErrorSource.EventHandler));
            Assert.That(_t.Errors[0].Context.LifetimeName, Is.EqualTo("name"));
            Assert.That(_t.Errors[0].Context.Member, Is.EqualTo("member"));
            Assert.That(_t.Errors[0].Context.Line, Is.EqualTo(42));
            _t.Errors.Clear();
        }

        [Test]
        public void Context_UnnamedLifetime_IsLabelledByCallSite()
        {
            var unnamed = _t.App.CreateChild();
            unnamed.OnCancel(() => throw new InvalidOperationException("boom"));

            unnamed.Cancel();

            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].Context.LifetimeName, Does.StartWith(nameof(Context_UnnamedLifetime_IsLabelledByCallSite) + ":"));
            _t.Errors.Clear();
        }

        [Test]
        public void Context_NamedLifetime_UsesItsName()
        {
            var named = _t.App.CreateChild("named");
            named.OnCancel(() => throw new InvalidOperationException("boom"));

            named.Cancel();

            Assert.That(_t.Errors[0].Context.LifetimeName, Is.EqualTo("named"));
            _t.Errors.Clear();
        }

        [Test]
        public void LifetimeWorkException_ExposesContextAndInnerException()
        {
            var context = new LifetimeErrorContext(LifetimeErrorSource.Timer, null, "life", "Tick", 7);
            var inner = new InvalidOperationException("inner text");

            var wrapper = new LifetimeWorkException(context, inner);

            Assert.That(wrapper.InnerException, Is.SameAs(inner));
            Assert.That(wrapper.Context.Source, Is.EqualTo(LifetimeErrorSource.Timer));
            Assert.That(wrapper.Context.LifetimeName, Is.EqualTo("life"));
            Assert.That(wrapper.Message, Does.Contain("Timer"));
            Assert.That(wrapper.Message, Does.Contain("'life'"));
            Assert.That(wrapper.Message, Does.Contain("Tick:7"));
            Assert.That(wrapper.Message, Does.Contain("inner text"));
        }

        [Test]
        public void LifetimeWorkException_ContextWithoutNames_StillFormatsAMessage()
        {
            var context = new LifetimeErrorContext(LifetimeErrorSource.Coroutine, null, null, null, 0);

            var wrapper = new LifetimeWorkException(context, new InvalidOperationException("x"));

            Assert.That(wrapper.Message, Does.Contain("<unnamed>"));
            Assert.That(wrapper.Message, Does.Contain("<unknown>"));
        }

        [Test]
        public void LifetimeErrorContext_ExposesEveryComponent()
        {
            var context = new LifetimeErrorContext(LifetimeErrorSource.TokenCallback, null, "life", "Member", 9);

            Assert.That(context.Source, Is.EqualTo(LifetimeErrorSource.TokenCallback));
            Assert.That(context.Owner, Is.Null);
            Assert.That(context.LifetimeName, Is.EqualTo("life"));
            Assert.That(context.Member, Is.EqualTo("Member"));
            Assert.That(context.Line, Is.EqualTo(9));
        }
    }
}
