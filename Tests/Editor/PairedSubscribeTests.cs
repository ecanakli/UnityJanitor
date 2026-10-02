using System;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests
{
    // Paired Subscribe(add, remove, handler) for events the game does not own.
    [TestFixture]
    public sealed class PairedSubscribeTests
    {
        private delegate void CustomCallback(string name, int value);

        private TestScope _t;
        private Lifetime _area;
        private Source _source;

        [SetUp]
        public void SetUp()
        {
            _t = new TestScope();
            _area = _t.App.CreateChild("area");
            _source = new Source();
            StaticSource.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            StaticSource.Clear();
            _t.Complete();
        }

        // Action form

        [Test]
        public void Subscribe_Action_CallsAddOnceAndDeliversUntilCancel()
        {
            Action handler = () => _t.Log.Add("hit");

            var registration = _area.Subscribe(h => _source.Zero += h, h => _source.Zero -= h, handler);
            _source.RaiseZero();

            Assert.That(_source.Adds, Is.EqualTo(1), "add runs immediately, once");
            Assert.That(_source.Removes, Is.Zero);
            Assert.That(registration.IsActive, Is.True);
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "hit" }));
        }

        [Test]
        public void Subscribe_Action_RemoveRunsOnceOnCancelWithTheSameHandler()
        {
            Action handler = () => _t.Log.Add("hit");
            Action removed = null;
            _area.Subscribe(h => _source.Zero += h, h =>
            {
                removed = h;
                _source.Zero -= h;
            }, handler);

            _area.Cancel();
            _area.Cancel();
            _source.RaiseZero();

            Assert.That(removed, Is.SameAs(handler), "remove must receive the very handler that add received");
            Assert.That(_source.Removes, Is.EqualTo(1), "remove runs exactly once");
            Assert.That(_t.Log.Count, Is.Zero, "the handler is detached");
        }

        [Test]
        public void Subscribe_Action_RemoveRunsOnceOnDispose()
        {
            var child = _area.CreateChild("child");
            Action handler = () => _t.Log.Add("hit");
            child.Subscribe(h => _source.Zero += h, h => _source.Zero -= h, handler);

            child.Dispose();
            child.Dispose();
            _source.RaiseZero();

            Assert.That(_source.Removes, Is.EqualTo(1));
            Assert.That(_t.Log.Count, Is.Zero);
        }

        [Test]
        public void Subscribe_Action_RemoveRunsOnceOnRegistrationCancelAndNotAgainOnLifetimeCancel()
        {
            Action handler = () => _t.Log.Add("hit");
            var registration = _area.Subscribe(h => _source.Zero += h, h => _source.Zero -= h, handler);

            registration.Cancel();
            registration.Cancel();
            _area.Cancel();
            _source.RaiseZero();

            Assert.That(_source.Removes, Is.EqualTo(1), "a cancelled registration must not be removed again by the lifetime");
            Assert.That(registration.IsActive, Is.False);
            Assert.That(_t.Log.Count, Is.Zero);
        }

        [Test]
        public void Subscribe_Action_AfterCancelReturns_RegistersInTheNewGeneration()
        {
            Action handler = () => _t.Log.Add("hit");
            _area.Subscribe(h => _source.Zero += h, h => _source.Zero -= h, handler);
            _area.Cancel();

            _area.Subscribe(h => _source.Zero += h, h => _source.Zero -= h, handler);
            _source.RaiseZero();

            Assert.That(_source.Adds, Is.EqualTo(2));
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "hit" }));
            _area.Cancel();
            Assert.That(_source.Removes, Is.EqualTo(2));
        }

        [Test]
        public void Subscribe_TwoOwnersOnTheSameEvent_RemoveIndependently()
        {
            var first = _area.CreateChild("first");
            var second = _area.CreateChild("second");
            first.Subscribe(h => _source.Zero += h, h => _source.Zero -= h, () => _t.Log.Add("first"));
            second.Subscribe(h => _source.Zero += h, h => _source.Zero -= h, () => _t.Log.Add("second"));

            first.Cancel();
            _source.RaiseZero();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "second" }));
            Assert.That(_source.Removes, Is.EqualTo(1));
        }

        // Not added while ending

        [Test]
        public void Subscribe_WhileTheOwnerIsCancelling_DoesNotCallAdd()
        {
            LifetimeRegistration seen = default;
            _area.OnCancel(() => seen = _area.Subscribe(h => _source.Zero += h, h => _source.Zero -= h, () => { }));

            _area.Cancel();

            Assert.That(_source.Adds, Is.Zero, "nothing is added while the generation is ending");
            Assert.That(_source.Removes, Is.Zero);
            Assert.That(seen.IsActive, Is.False);
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void Subscribe_OnADisposedOwner_DoesNotCallAdd()
        {
            var child = _area.CreateChild("child");
            child.Dispose();

            var registration = child.Subscribe(h => _source.Zero += h, h => _source.Zero -= h, () => { });

            Assert.That(_source.Adds, Is.Zero);
            Assert.That(registration.IsActive, Is.False);
        }

        [Test]
        public void Subscribe_AddEndsTheOwner_RemoveRunsImmediatelyExactlyOnce()
        {
            Action handler = () => _t.Log.Add("hit");

            var registration = _area.Subscribe(
                h =>
                {
                    _source.Zero += h;
                    _area.Cancel();
                },
                h => _source.Zero -= h,
                handler);
            _source.RaiseZero();

            Assert.That(_source.Adds, Is.EqualTo(1));
            Assert.That(_source.Removes, Is.EqualTo(1), "an owner that ended during add must still get its remove call");
            Assert.That(registration.IsActive, Is.False);
            Assert.That(_t.Log.Count, Is.Zero);
            _area.Cancel();
            Assert.That(_source.Removes, Is.EqualTo(1), "and only one");
        }

        [Test]
        public void Subscribe_AddDisposesTheOwner_RemoveRunsImmediatelyExactlyOnce()
        {
            var child = _area.CreateChild("child");

            var registration = child.Subscribe(
                h =>
                {
                    _source.Zero += h;
                    child.Dispose();
                },
                h => _source.Zero -= h,
                () => _t.Log.Add("hit"));
            _source.RaiseZero();

            Assert.That(_source.Adds, Is.EqualTo(1));
            Assert.That(_source.Removes, Is.EqualTo(1));
            Assert.That(registration.IsActive, Is.False);
            Assert.That(_t.Log.Count, Is.Zero);
        }

        [Test]
        public void Subscribe_AddEndsTheOwnerAndRemoveThrows_IsRoutedAsACancelActionAndNeverThrown()
        {
            LifetimeRegistration registration = default;

            Assert.DoesNotThrow(() => registration = _area.Subscribe(
                h => _area.Cancel(),
                h => throw new InvalidOperationException("remove boom"),
                () => { }));

            Assert.That(registration.IsActive, Is.False);
            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].Context.Source, Is.EqualTo(LifetimeErrorSource.CancelAction));
            Assert.That(_t.Errors[0].Exception.Message, Is.EqualTo("remove boom"));
            _t.Errors.Clear();
        }

        // Failures

        [Test]
        public void Subscribe_ThrowingAdd_IsRoutedWithNoEntryAndRemoveIsNeverCalled()
        {
            var removed = 0;

            var registration = _area.Subscribe(h => throw new InvalidOperationException("add boom"), h => removed++, () => { });

            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            var captured = _t.Errors[0];
            Assert.That(captured.Exception.Message, Is.EqualTo("add boom"));
            Assert.That(captured.Context.Source, Is.EqualTo(LifetimeErrorSource.EventHandler));
            Assert.That(captured.Context.LifetimeName, Is.EqualTo("area"));
            Assert.That(captured.Context.Member, Is.EqualTo(nameof(Subscribe_ThrowingAdd_IsRoutedWithNoEntryAndRemoveIsNeverCalled)));
            Assert.That(captured.Context.Line, Is.GreaterThan(0));
            Assert.That(registration.IsActive, Is.False);
            Assert.That(_area.EntryCount, Is.Zero, "nothing may be registered when add fails");
            _area.Cancel();
            Assert.That(removed, Is.Zero, "there is nothing to remove");
            _t.Errors.Clear();
        }

        [Test]
        public void Subscribe_ThrowingRemove_IsRoutedAsACancelActionAndTheOtherItemsStillRun()
        {
            _area.Record(_t.Log, "before");
            _area.Subscribe(h => _source.Zero += h, h => throw new InvalidOperationException("remove boom"), () => { });
            _area.Record(_t.Log, "after");

            _area.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "after", "before" }));
            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].Context.Source, Is.EqualTo(LifetimeErrorSource.CancelAction));
            Assert.That(_t.Errors[0].Exception.Message, Is.EqualTo("remove boom"));
            _t.Errors.Clear();
        }

        // Arguments

        [Test]
        public void Subscribe_NullArguments_ThrowAndNeverCallAdd()
        {
            Lifetime none = null;
            Action handler = () => { };
            Action<Action> add = h => _source.Zero += h;
            Action<Action> remove = h => _source.Zero -= h;

            var owner = Assert.Throws<ArgumentNullException>(() => none.Subscribe(add, remove, handler));
            var addNull = Assert.Throws<ArgumentNullException>(() => _area.Subscribe(null, remove, handler));
            var removeNull = Assert.Throws<ArgumentNullException>(() => _area.Subscribe(add, null, handler));
            var handlerNull = Assert.Throws<ArgumentNullException>(() => _area.Subscribe(add, remove, (Action)null));

            Assert.That(owner.ParamName, Is.EqualTo("owner"));
            Assert.That(addNull.ParamName, Is.EqualTo("add"));
            Assert.That(removeNull.ParamName, Is.EqualTo("remove"));
            Assert.That(handlerNull.ParamName, Is.EqualTo("handler"));
            Assert.That(_source.Adds, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void Subscribe_OffMainThread_ThrowsInvalidOperationExceptionBeforeCallingAdd()
        {
            var failure = ThreadRunner.Run(() => _area.Subscribe(h => _source.Zero += h, h => _source.Zero -= h, () => { }));

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(_source.Adds, Is.Zero);
            Assert.That(_area.EntryCount, Is.Zero);
        }

        // The other forms

        [Test]
        public void Subscribe_OneArgumentForm_DeliversAndRemovesOnCancel()
        {
            Action<string> handler = text => _t.Log.Add(text);

            var registration = _area.Subscribe<string>(h => _source.One += h, h => _source.One -= h, handler);
            _source.RaiseOne("a");
            _area.Cancel();
            _source.RaiseOne("b");

            Assert.That(_source.Adds, Is.EqualTo(1));
            Assert.That(_source.Removes, Is.EqualTo(1));
            Assert.That(registration.IsActive, Is.False);
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void Subscribe_TwoArgumentForm_DeliversAndRemovesOnDispose()
        {
            var child = _area.CreateChild("child");
            Action<int, int> handler = (a, b) => _t.Log.Add(a + "+" + b);

            child.Subscribe<int, int>(h => _source.Two += h, h => _source.Two -= h, handler);
            _source.RaiseTwo(1, 2);
            child.Dispose();
            _source.RaiseTwo(3, 4);

            Assert.That(_source.Removes, Is.EqualTo(1));
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "1+2" }));
        }

        [Test]
        public void Subscribe_ThreeArgumentForm_DeliversAndRemovesOnRegistrationCancel()
        {
            Action<int, int, int> handler = (a, b, c) => _t.Log.Add(a + "," + b + "," + c);

            var registration = _area.Subscribe<int, int, int>(h => _source.Three += h, h => _source.Three -= h, handler);
            _source.RaiseThree(1, 2, 3);
            registration.Cancel();
            _source.RaiseThree(4, 5, 6);

            Assert.That(_source.Removes, Is.EqualTo(1));
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "1,2,3" }));
        }

        [Test]
        public void Subscribe_CustomDelegateForm_ResolvesToTheDelegateOverloadAndRemovesOnCancel()
        {
            CustomCallback handler = (name, value) => _t.Log.Add(name + value);

            var registration = _area.Subscribe<CustomCallback>(h => _source.Custom += h, h => _source.Custom -= h, handler);
            _source.RaiseCustom("n", 1);
            _area.Cancel();
            _source.RaiseCustom("n", 2);

            Assert.That(registration.IsActive, Is.False);
            Assert.That(_source.Adds, Is.EqualTo(1));
            Assert.That(_source.Removes, Is.EqualTo(1));
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "n1" }));
        }

        [Test]
        public void Subscribe_DelegateFormWithAnActionHandlerVariable_PrefersTheNonGenericOverloadAndStillWorks()
        {
            Action handler = () => _t.Log.Add("hit");

            _area.Subscribe(h => _source.Zero += h, h => _source.Zero -= h, handler);
            _source.RaiseZero();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "hit" }));
        }

        [Test]
        public void Subscribe_EveryForm_ThrowingAddIsRoutedAndRegistersNothing()
        {
            var one = _area.Subscribe<string>(h => throw new InvalidOperationException("one"), h => { }, s => { });
            var two = _area.Subscribe<int, int>(h => throw new InvalidOperationException("two"), h => { }, (a, b) => { });
            var three = _area.Subscribe<int, int, int>(h => throw new InvalidOperationException("three"), h => { }, (a, b, c) => { });
            var custom = _area.Subscribe<CustomCallback>(h => throw new InvalidOperationException("custom"), h => { }, (n, v) => { });

            Assert.That(_t.Errors.Count, Is.EqualTo(4));
            Assert.That(one.IsActive || two.IsActive || three.IsActive || custom.IsActive, Is.False);
            Assert.That(_area.EntryCount, Is.Zero);
            _t.Errors.Clear();
        }

        // A static source with static lambdas

        [Test]
        public void Subscribe_StaticSourceWithStaticLambdas_DeliversAndRemovesOnCancel()
        {
            Action handler = () => _t.Log.Add("static");

            _area.Subscribe(static h => StaticSource.Add(h), static h => StaticSource.Remove(h), handler);
            StaticSource.Raise();
            Assert.That(StaticSource.Count, Is.EqualTo(1));

            _area.Cancel();
            StaticSource.Raise();

            Assert.That(StaticSource.Count, Is.Zero, "the package must detach the handler from the static source");
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "static" }));
        }

        [Test]
        public void Subscribe_StaticSourceWithStaticLambdasAndAGenericHandler_RemovesOnCancel()
        {
            Action<int> handler = value => _t.Log.Add("v" + value);

            _area.Subscribe<int>(static h => StaticSource.AddInt(h), static h => StaticSource.RemoveInt(h), handler);
            StaticSource.RaiseInt(4);
            _area.Cancel();
            StaticSource.RaiseInt(5);

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "v4" }));
            Assert.That(StaticSource.IntCount, Is.Zero);
        }

        // A source with counting accessors, so add and remove calls can be asserted exactly.
        private sealed class Source
        {
            private Action _zero;
            private Action<string> _one;
            private Action<int, int> _two;
            private Action<int, int, int> _three;
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

            public event Action<string> One
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

            public event Action<int, int> Two
            {
                add
                {
                    _two += value;
                    Adds++;
                }
                remove
                {
                    _two -= value;
                    Removes++;
                }
            }

            public event Action<int, int, int> Three
            {
                add
                {
                    _three += value;
                    Adds++;
                }
                remove
                {
                    _three -= value;
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

            public void RaiseOne(string text)
            {
                _one?.Invoke(text);
            }

            public void RaiseTwo(int a, int b)
            {
                _two?.Invoke(a, b);
            }

            public void RaiseThree(int a, int b, int c)
            {
                _three?.Invoke(a, b, c);
            }

            public void RaiseCustom(string name, int value)
            {
                _custom?.Invoke(name, value);
            }
        }

        // A static source that stores handlers by reference in fixed arrays, so it never allocates itself.
        private static class StaticSource
        {
            private static readonly Action[] Handlers = new Action[128];
            private static readonly Action<int>[] IntHandlers = new Action<int>[128];

            public static int Count
            {
                get
                {
                    var count = 0;
                    for (var i = 0; i < Handlers.Length; i++)
                    {
                        if (Handlers[i] != null)
                        {
                            count++;
                        }
                    }

                    return count;
                }
            }

            public static int IntCount
            {
                get
                {
                    var count = 0;
                    for (var i = 0; i < IntHandlers.Length; i++)
                    {
                        if (IntHandlers[i] != null)
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
                    if (Handlers[i] == null)
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

            public static void AddInt(Action<int> handler)
            {
                for (var i = 0; i < IntHandlers.Length; i++)
                {
                    if (IntHandlers[i] == null)
                    {
                        IntHandlers[i] = handler;
                        return;
                    }
                }
            }

            public static void RemoveInt(Action<int> handler)
            {
                for (var i = 0; i < IntHandlers.Length; i++)
                {
                    if (ReferenceEquals(IntHandlers[i], handler))
                    {
                        IntHandlers[i] = null;
                        return;
                    }
                }
            }

            public static void Raise()
            {
                for (var i = 0; i < Handlers.Length; i++)
                {
                    Handlers[i]?.Invoke();
                }
            }

            public static void RaiseInt(int value)
            {
                for (var i = 0; i < IntHandlers.Length; i++)
                {
                    IntHandlers[i]?.Invoke(value);
                }
            }

            public static void Clear()
            {
                Array.Clear(Handlers, 0, Handlers.Length);
                Array.Clear(IntHandlers, 0, IntHandlers.Length);
            }
        }
    }
}
