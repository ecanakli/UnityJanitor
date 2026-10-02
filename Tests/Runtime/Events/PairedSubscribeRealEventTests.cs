using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.Events
{
    // Paired Subscribe through the TDelegate form on the real Application.lowMemory event.
    // lowMemory cannot be raised, so add and remove count their calls and keep the handler they got: bookkeeping only, delivery is not testable.
    [TestFixture]
    public sealed class PairedSubscribeRealEventTests
    {
        private EventFixture _f;
        private int _adds;
        private int _removes;
        private Application.LowMemoryCallback _added;
        private Application.LowMemoryCallback _removed;

        [SetUp]
        public void SetUp()
        {
            _f = new EventFixture();
            _adds = 0;
            _removes = 0;
            _added = null;
            _removed = null;
        }

        [TearDown]
        public void TearDown()
        {
            // Complete() disposes the tree, which also detaches anything a failed test left on Application.lowMemory.
            _f.Complete();
        }

        // Guard: the TDelegate overload is selected for a delegate type that is not Action; add runs once, immediately.
        [Test]
        public void Subscribe_LowMemoryCallbackOnALifetime_CallsAddOnceWithTheHandler()
        {
            Application.LowMemoryCallback handler = OnLowMemory;

            var registration = _f.Area.Subscribe<Application.LowMemoryCallback>(Add, Remove, handler);

            Assert.That(_adds, Is.EqualTo(1));
            Assert.That(_removes, Is.Zero);
            Assert.That(_added, Is.SameAs(handler), "add must receive the very handler that was passed");
            Assert.That(registration.IsActive, Is.True);
            Assert.That(_f.Area.EntryCount, Is.EqualTo(1));
        }

        // Guard: the entry stores remove and handler; Cancel calls remove(handler) exactly once, however often Cancel runs.
        [Test]
        public void Cancel_LowMemoryCallbackOnALifetime_CallsRemoveOnceWithTheSameHandler()
        {
            Application.LowMemoryCallback handler = OnLowMemory;
            var registration = _f.Area.Subscribe<Application.LowMemoryCallback>(Add, Remove, handler);

            _f.Area.Cancel();
            _f.Area.Cancel();
            registration.Cancel();

            Assert.That(_adds, Is.EqualTo(1));
            Assert.That(_removes, Is.EqualTo(1), "remove runs exactly once");
            Assert.That(_removed, Is.SameAs(handler), "remove must receive the very handler that add received");
            Assert.That(registration.IsActive, Is.False);
            Assert.That(_f.Area.EntryCount, Is.Zero);
        }

        // Guard: registration.Cancel removes it; a later lifetime Cancel does not remove it again.
        [Test]
        public void RegistrationCancel_LowMemoryCallbackOnALifetime_CallsRemoveOnceAndNotAgainOnTheLifetimeCancel()
        {
            Application.LowMemoryCallback handler = OnLowMemory;
            var registration = _f.Area.Subscribe<Application.LowMemoryCallback>(Add, Remove, handler);

            registration.Cancel();
            _f.Area.Cancel();

            Assert.That(_removes, Is.EqualTo(1));
            Assert.That(_removed, Is.SameAs(handler));
        }

        // Guard: Dispose drives the same remove.
        [Test]
        public void Dispose_LowMemoryCallbackOnAChildLifetime_CallsRemoveOnce()
        {
            Application.LowMemoryCallback handler = OnLowMemory;
            var child = _f.Area.CreateChild("child");
            child.Subscribe<Application.LowMemoryCallback>(Add, Remove, handler);

            child.Dispose();
            child.Dispose();

            Assert.That(_adds, Is.EqualTo(1));
            Assert.That(_removes, Is.EqualTo(1));
        }

        // Guard: nothing is added, and remove is never called, for an owner that is not Active.
        [Test]
        public void Subscribe_LowMemoryCallbackOnADisposedLifetime_NeverCallsAddOrRemove()
        {
            Application.LowMemoryCallback handler = OnLowMemory;
            var child = _f.Area.CreateChild("child");
            child.Dispose();

            var registration = child.Subscribe<Application.LowMemoryCallback>(Add, Remove, handler);

            Assert.That(_adds, Is.Zero);
            Assert.That(_removes, Is.Zero);
            Assert.That(registration.IsActive, Is.False);
        }

        // Guard: the MonoBehaviour overload resolves to the component lifetime, so destroying the component calls remove.
        [Test]
        public void Destroy_LowMemoryCallbackOnAComponent_CallsRemoveOnce()
        {
            Application.LowMemoryCallback handler = OnLowMemory;
            var listener = _f.NewListener();
            listener.Subscribe<Application.LowMemoryCallback>(Add, Remove, handler);
            Assert.That(_adds, Is.EqualTo(1));

            Object.DestroyImmediate(listener.gameObject);

            Assert.That(_removes, Is.EqualTo(1));
            Assert.That(_removed, Is.SameAs(handler));
        }

        // Guard: the documented form, static lambdas on a static source, attaches and detaches without a closure.
        [Test]
        public void Subscribe_LowMemoryCallbackWithStaticLambdas_AttachesAndDetaches()
        {
            Application.LowMemoryCallback handler = OnLowMemory;

            var registration = _f.Area.Subscribe<Application.LowMemoryCallback>(static h => Application.lowMemory += h, static h => Application.lowMemory -= h, handler);
            Assert.That(registration.IsActive, Is.True);
            registration.Cancel();

            Assert.That(registration.IsActive, Is.False);
            Assert.That(_f.Area.EntryCount, Is.Zero);
            Assert.That(_f.Errors.Count, Is.Zero);
        }

        private void Add(Application.LowMemoryCallback handler)
        {
            _adds++;
            _added = handler;
            Application.lowMemory += handler;
        }

        private void Remove(Application.LowMemoryCallback handler)
        {
            _removes++;
            _removed = handler;
            Application.lowMemory -= handler;
        }

        private void OnLowMemory()
        {
        }
    }
}
