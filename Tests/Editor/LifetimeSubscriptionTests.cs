using System;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests
{
    // OnCancel and AddTo(IDisposable). The UnityEvent part is covered by the PlayMode tests.
    [TestFixture]
    public sealed class LifetimeSubscriptionTests
    {
        private TestScope _t;
        private Lifetime _area;

        [SetUp]
        public void SetUp()
        {
            _t = new TestScope();
            _area = _t.App.CreateChild("area");
        }

        [TearDown]
        public void TearDown()
        {
            _t.Complete();
        }

        [Test]
        public void OnCancel_Action_RunsOncePerGeneration()
        {
            var counter = new Counter();
            _area.OnCancel(() => counter.Value++);

            _area.Cancel();
            Assert.That(counter.Value, Is.EqualTo(1));

            _area.Cancel();
            Assert.That(counter.Value, Is.EqualTo(1), "the item belongs to the generation that ended");

            _area.OnCancel(() => counter.Value++);
            _area.Cancel();
            Assert.That(counter.Value, Is.EqualTo(2));
        }

        [Test]
        public void OnCancel_Action_ReturnsAnActiveRegistrationUntilItRuns()
        {
            var registration = _area.OnCancel(() => { });

            Assert.That(registration.IsActive, Is.True);

            _area.Cancel();

            Assert.That(registration.IsActive, Is.False);
        }

        [Test]
        public void OnCancel_State_PassesTheStateToTheAction()
        {
            _area.OnCancel(_t.Log, static l => l.Add("state"));

            _area.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "state" }));
        }

        [Test]
        public void OnCancel_TwoStates_PassesBothToTheAction()
        {
            _area.OnCancel(_t.Log, "second", static (l, s) => l.Add(s));

            _area.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "second" }));
        }

        [Test]
        public void OnCancel_TwoStates_TerminatesOnDispose()
        {
            _area.OnCancel(_t.Log, "second", static (l, s) => l.Add(s));

            _area.Dispose();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "second" }));
        }

        [Test]
        public void OnCancel_WithIsFinishedFalse_RunsTheActionOnCancel()
        {
            var counter = new Counter();
            _area.OnCancel(counter, static c => c.Value++, static c => false);

            _area.Cancel();

            Assert.That(counter.Value, Is.EqualTo(1));
        }

        [Test]
        public void OnCancel_RegistrationCancel_RunsTheActionOnceAndNotAgainOnCancel()
        {
            var counter = new Counter();
            var registration = _area.OnCancel(counter, static c => c.Value++);

            registration.Cancel();
            _area.Cancel();

            Assert.That(counter.Value, Is.EqualTo(1));
        }

        [Test]
        public void OnCancel_NullAction_ThrowsArgumentNullExceptionAndRegistersNothing()
        {
            Assert.Throws<ArgumentNullException>(() => _area.OnCancel((Action)null));
            Assert.Throws<ArgumentNullException>(() => _area.OnCancel(_t.Log, (Action<CallLog>)null));
            Assert.Throws<ArgumentNullException>(() => _area.OnCancel(_t.Log, l => { }, (Func<CallLog, bool>)null));
            Assert.Throws<ArgumentNullException>(() => _area.OnCancel(_t.Log, (Action<CallLog>)null, l => false));
            Assert.Throws<ArgumentNullException>(() => _area.OnCancel(_t.Log, "x", (Action<CallLog, string>)null));

            Assert.That(_area.EntryCount, Is.Zero);
        }

        [Test]
        public void AddTo_OnCancel_DisposesExactlyOnce()
        {
            var probe = new DisposeProbe();
            probe.AddTo(_area);

            _area.Cancel();
            _area.Cancel();

            Assert.That(probe.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void AddTo_OnDispose_DisposesExactlyOnce()
        {
            var probe = new DisposeProbe();
            probe.AddTo(_area);

            _area.Dispose();
            _area.Dispose();

            Assert.That(probe.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void AddTo_RegistrationCancel_DisposesExactlyOnce()
        {
            var probe = new DisposeProbe();
            var registration = probe.AddTo(_area);

            registration.Cancel();
            registration.Cancel();
            _area.Cancel();

            Assert.That(probe.DisposeCount, Is.EqualTo(1));
            Assert.That(registration.IsActive, Is.False);
        }

        [Test]
        public void AddTo_ReturnsAnActiveRegistrationUntilTheItemEnds()
        {
            var probe = new DisposeProbe();

            var registration = probe.AddTo(_area);

            Assert.That(registration.IsActive, Is.True);
            Assert.That(probe.DisposeCount, Is.Zero, "registering must not dispose");
        }

        [Test]
        public void AddTo_TheSameItemTwice_IsDisposedOncePerRegistration()
        {
            var probe = new DisposeProbe();
            probe.AddTo(_area);
            probe.AddTo(_area);

            _area.Cancel();

            Assert.That(probe.DisposeCount, Is.EqualTo(2));
        }

        [Test]
        public void AddTo_NextGeneration_DisposesAgainOnlyWhenRegisteredAgain()
        {
            var probe = new DisposeProbe();
            probe.AddTo(_area);
            _area.Cancel();

            _area.Cancel();
            Assert.That(probe.DisposeCount, Is.EqualTo(1));

            probe.AddTo(_area);
            _area.Cancel();

            Assert.That(probe.DisposeCount, Is.EqualTo(2));
        }

        [Test]
        public void AddTo_WhileCancelling_DisposesImmediately()
        {
            var probe = new DisposeProbe();
            var disposedInside = -1;
            _area.OnCancel(() =>
            {
                probe.AddTo(_area);
                disposedInside = probe.DisposeCount;
            });

            _area.Cancel();

            Assert.That(disposedInside, Is.EqualTo(1));
            Assert.That(probe.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void AddTo_StructDisposable_IsBoxedAndDisposedOnCancel()
        {
            var item = new StructDisposable { Log = _t.Log, Label = "struct" };
            item.AddTo(_area);

            _area.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "struct" }));
        }

        [Test]
        public void AddTo_NullLifetime_DisposesTheItemThenThrowsArgumentNullException()
        {
            var probe = new DisposeProbe();

            var exception = Assert.Throws<ArgumentNullException>(() => probe.AddTo((Lifetime)null));

            Assert.That(exception.ParamName, Is.EqualTo("lifetime"));
            Assert.That(probe.DisposeCount, Is.EqualTo(1), "the item must be disposed before the exception is thrown");
        }

        [Test]
        public void AddTo_NullLifetimeAndDisposeThrows_RoutesTheErrorAndStillThrowsArgumentNull()
        {
            var probe = new DisposeProbe { ThrowOnDispose = new InvalidOperationException("dispose boom") };

            Assert.Throws<ArgumentNullException>(() => probe.AddTo((Lifetime)null));

            Assert.That(probe.DisposeCount, Is.EqualTo(1));
            Assert.That(_t.Errors.Count, Is.EqualTo(1));
            Assert.That(_t.Errors[0].Exception.Message, Is.EqualTo("dispose boom"));
            Assert.That(_t.Errors[0].Context.Source, Is.EqualTo(LifetimeErrorSource.CancelAction));
            _t.Errors.Clear();
        }

        [Test]
        public void AddTo_NullItem_ThrowsArgumentNullExceptionAndRegistersNothing()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => ((DisposeProbe)null).AddTo(_area));

            Assert.That(exception.ParamName, Is.EqualTo("disposable"));
            Assert.That(_area.EntryCount, Is.Zero);
        }
    }
}
