using System;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests
{
    // Every kind of callback that can fail while a lifetime ends, on the lifetime itself and on a child, during Cancel and
    // during Dispose. In each case the teardown does not throw, every other item still runs once, the generation ends, the error
    // is routed exactly once with the source of its kind, and the lifetime is usable afterwards (after a Cancel) or refuses work.
    // LifetimeNeverThrowsTests covers the plain OnCancel action, a token callback and a disposable during Cancel; the matrix
    // adds the other overloads, Dispose, a child, events, tasks and timers.
    [TestFixture]
    public sealed class TeardownExceptionMatrixTests
    {
        private const string Boom = "boom";

        private TestScope _t;
        private ManualClock _clock;

        [SetUp]
        public void SetUp()
        {
            _t = new TestScope();
            _clock = _t.Tree.UseManualClock();
        }

        [TearDown]
        public void TearDown()
        {
            _t.Complete();
        }

        public enum Kind
        {
            OnCancelAction,
            OnCancelState,
            OnCancelStateWithProbe,
            OnCancelTwoStates,
            PairedRemove,
            DisposableDispose,
            TokenCallback,
            OwnedEventHandler,
            RunThrowsBeforeItsFirstAwait,
            RunThrowsAfterItsFirstAwait,
            AfterCallback,
            EveryCallback,
        }

        public enum Operation
        {
            Cancel,
            Dispose,
        }

        public enum Placement
        {
            OnTheLifetime,
            OnAChild,
        }

        [Test]
        public void Teardown_WhenACallbackThrows_EveryOtherItemRuns_TheGenerationEnds_AndTheErrorIsRoutedOnce(
            [Values] Kind kind,
            [Values] Operation operation,
            [Values] Placement placement)
        {
            var area = _t.App.CreateChild("area");
            var child = area.CreateChild("child");
            var other = _t.App.CreateChild("other");
            var target = placement == Placement.OnTheLifetime ? area : child;
            area.Record(_t.Log, "area");
            child.Record(_t.Log, "child");
            target.Record(_t.Log, "early");
            var plan = Install(kind, operation, area, target, other);
            target.Record(_t.Log, "late");

            Assert.DoesNotThrow(() => plan.Trigger(), "ending a lifetime never throws");
            if (plan.Settle != null)
            {
                Assert.That(_t.Errors.Count, Is.Zero, "the fault of a task comes after its await, not during the teardown");
                plan.Settle();
            }

            Assert.That(_t.Log.ToArray(), Is.EquivalentTo(new[] { "area", "child", "early", "late" }), "every other item ran, once");
            Assert.That(_t.Errors.Count, Is.EqualTo(1), "the error is routed exactly once:\n" + _t.Errors);
            var captured = _t.Errors[0];
            Assert.That(captured.Context.Source, Is.EqualTo(plan.Source));
            Assert.That(captured.Exception, Is.TypeOf<InvalidOperationException>());
            Assert.That(captured.Exception.Message, Is.EqualTo(Boom));
            Assert.That(captured.Context.LifetimeName, Is.EqualTo(plan.ErrorLifetime));
            Assert.That(area.EntryCount, Is.Zero);
            Assert.That(child.EntryCount, Is.Zero);
            Assert.That(_clock.PendingCount, Is.Zero, "a timer that failed does not run again");
            AssertGenerationEnded(operation, area, child);

            _t.Errors.Clear();
            AssertUsableOrRefusing(operation, area);
        }

        [Test]
        public void Teardown_EveryKindOfCallbackThrowingAtOnce_RoutesEachOnceAndFinishes([Values] Operation operation)
        {
            var area = _t.App.CreateChild("area");
            var child = area.CreateChild("child");
            var other = _t.App.CreateChild("other");
            area.Record(_t.Log, "area");
            child.Record(_t.Log, "child");
            var kinds = new[]
            {
                Kind.OnCancelAction,
                Kind.OnCancelState,
                Kind.OnCancelStateWithProbe,
                Kind.OnCancelTwoStates,
                Kind.PairedRemove,
                Kind.DisposableDispose,
                Kind.TokenCallback,
                Kind.OwnedEventHandler,
            };
            for (var i = 0; i < kinds.Length; i++)
            {
                area.Record(_t.Log, "between" + i);
                Install(kinds[i], operation, area, area, other);
            }

            Assert.DoesNotThrow(() => Teardown(area, operation));

            var byKind = new System.Collections.Generic.Dictionary<LifetimeErrorSource, int>();
            for (var i = 0; i < _t.Errors.Count; i++)
            {
                var source = _t.Errors[i].Context.Source;
                byKind[source] = byKind.TryGetValue(source, out var seen) ? seen + 1 : 1;
            }

            Assert.That(_t.Errors.Count, Is.EqualTo(kinds.Length), "one error per failing callback:\n" + _t.Errors);
            Assert.That(byKind[LifetimeErrorSource.CancelAction], Is.EqualTo(6), "four OnCancel overloads, a paired remove and a disposable");
            Assert.That(byKind[LifetimeErrorSource.TokenCallback], Is.EqualTo(1));
            Assert.That(byKind[LifetimeErrorSource.EventHandler], Is.EqualTo(1));
            Assert.That(_t.Log.Count, Is.EqualTo(2 + kinds.Length), "the plain items between the failing ones all ran");
            AssertGenerationEnded(operation, area, child);

            _t.Errors.Clear();
            AssertUsableOrRefusing(operation, area);
        }

        [Test]
        public void Teardown_AnOwnedEventHandlerOfTheEndingLifetime_IsNotCalledWhileItEnds([Values] Operation operation)
        {
            var area = _t.App.CreateChild("area");
            var evt = new OwnedEvent("evt");
            var called = 0;
            evt.Subscribe(() => called++, area);
            area.OnCancel(evt, static e => e.Invoke());

            Assert.DoesNotThrow(() => Teardown(area, operation));

            Assert.That(called, Is.Zero, "the owner of the handler is ending, so the event skips it");
            Assert.That(evt.SubscriberCount, Is.Zero, "and its slot is gone");
        }

        // Installs one failing callback of the kind on target and says how to run the teardown and what to expect.
        private Plan Install(Kind kind, Operation operation, Lifetime area, Lifetime target, Lifetime other)
        {
            var plan = new Plan
            {
                Trigger = () => Teardown(area, operation),
                Source = LifetimeErrorSource.CancelAction,
                ErrorLifetime = target.Name,
            };

            switch (kind)
            {
                case Kind.OnCancelAction:
                    target.OnCancel(() => throw new InvalidOperationException(Boom));
                    break;
                case Kind.OnCancelState:
                    target.OnCancel(_t.Log, static log => throw new InvalidOperationException(Boom));
                    break;
                case Kind.OnCancelStateWithProbe:
                    target.OnCancel(_t.Log, static log => throw new InvalidOperationException(Boom), static log => false);
                    break;
                case Kind.OnCancelTwoStates:
                    target.OnCancel(_t.Log, new Counter(), static (log, counter) => throw new InvalidOperationException(Boom));
                    break;
                case Kind.PairedRemove:
                    Action handler = () => { };
                    target.Subscribe(h => { }, h => throw new InvalidOperationException(Boom), handler);
                    break;
                case Kind.DisposableDispose:
                    new DisposeProbe { ThrowOnDispose = new InvalidOperationException(Boom) }.AddTo(target);
                    break;
                case Kind.TokenCallback:
                    target.Token.Register(() => throw new InvalidOperationException(Boom));
                    plan.Source = LifetimeErrorSource.TokenCallback;
                    break;
                case Kind.OwnedEventHandler:
                    var evt = new OwnedEvent("evt");
                    evt.Subscribe(() => throw new InvalidOperationException(Boom), other);
                    target.OnCancel(evt, static e => e.Invoke());
                    plan.Source = LifetimeErrorSource.EventHandler;
                    plan.ErrorLifetime = other.Name;
                    break;
                case Kind.RunThrowsBeforeItsFirstAwait:
                    target.OnCancel(other, static o => o.Run(static ct => throw new InvalidOperationException(Boom)));
                    plan.Source = LifetimeErrorSource.Task;
                    plan.ErrorLifetime = other.Name;
                    break;
                case Kind.RunThrowsAfterItsFirstAwait:
                    var source = AutoResetUniTaskCompletionSource.Create();
                    target.Run(source, static async (s, ct) =>
                    {
                        await s.Task;
                        throw new InvalidOperationException(Boom);
                    });
                    plan.Source = LifetimeErrorSource.Task;
                    plan.Settle = () => source.TrySetResult();
                    break;
                case Kind.AfterCallback:
                    target.After(1f, () =>
                    {
                        Teardown(area, operation);
                        throw new InvalidOperationException(Boom);
                    });
                    plan.Trigger = () => _clock.Advance(1f);
                    plan.Source = LifetimeErrorSource.Timer;
                    break;
                case Kind.EveryCallback:
                    target.Every(1f, () =>
                    {
                        Teardown(area, operation);
                        throw new InvalidOperationException(Boom);
                    });
                    plan.Trigger = () => _clock.Advance(1f);
                    plan.Source = LifetimeErrorSource.Timer;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }

            return plan;
        }

        private static void Teardown(Lifetime lifetime, Operation operation)
        {
            if (operation == Operation.Cancel)
            {
                lifetime.Cancel();
            }
            else
            {
                lifetime.Dispose();
            }
        }

        private static void AssertGenerationEnded(Operation operation, Lifetime area, Lifetime child)
        {
            if (operation == Operation.Cancel)
            {
                Assert.That(area.Generation, Is.EqualTo(1), "a failing item must not stop the generation from ending");
                Assert.That(child.Generation, Is.EqualTo(1));
                Assert.That(area.State, Is.EqualTo(LifetimeState.Active));
                Assert.That(child.State, Is.EqualTo(LifetimeState.Active));
            }
            else
            {
                Assert.That(area.IsDisposed, Is.True);
                Assert.That(child.IsDisposed, Is.True);
            }
        }

        // After a Cancel the lifetime takes work again; after a Dispose it ends new work at once. Neither routes an error.
        private void AssertUsableOrRefusing(Operation operation, Lifetime area)
        {
            var before = _t.Log.Count;

            var again = area.Record(_t.Log, "again");

            if (operation == Operation.Cancel)
            {
                Assert.That(again.IsActive, Is.True, "the lifetime is usable again after a Cancel");
                Assert.That(area.Token.IsCancellationRequested, Is.False, "with a live token");
                area.Cancel();
            }
            else
            {
                Assert.That(again.IsActive, Is.False, "a disposed lifetime refuses work");
            }

            Assert.That(_t.Log.Count, Is.EqualTo(before + 1), "the new item ran exactly once, at the Cancel or at once");
            Assert.That(_t.Errors.Count, Is.Zero);
        }

        private sealed class Plan
        {
            internal Action Trigger;
            internal Action Settle;
            internal LifetimeErrorSource Source;
            internal string ErrorLifetime;
        }
    }
}
