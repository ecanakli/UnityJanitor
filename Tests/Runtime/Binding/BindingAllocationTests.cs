using System;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Ecanakli.Janitor.Tests.Binding
{
    // The Unity binding: 0 B in steady state. Every measured delegate is run before it is
    // measured (Mono allocates on the first run of an un-run lambda), warm paths are warmed on the same call sites,
    // and each test asserts afterwards that the measured block really did the work.
    [TestFixture]
    public sealed class BindingAllocationTests
    {
        private const int Count = 16;

        private BindingSession _s;

        [SetUp]
        public void SetUp()
        {
            _s = BindingSession.Begin();
        }

        [TearDown]
        public void TearDown()
        {
            _s.Complete();
        }

        [Test]
        public void GetLifetime_RepeatLookup_AllocatesNothing()
        {
            var probe = _s.NewProbe();
            var expected = probe.GetLifetime();
            var seen = expected;
            TestDelegate lookup = () =>
            {
                for (var i = 0; i < 100; i++)
                {
                    seen = probe.GetLifetime();
                }
            };
            lookup();

            Assert.That(lookup, Is.Not.AllocatingGCMemory());

            Assert.That(seen, Is.SameAs(expected));
            Assert.That(_s.Tree.Owners.Count, Is.EqualTo(1));
        }

        [Test]
        public void GetActiveLifetime_RepeatLookup_AllocatesNothing()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var expectedActive = probe.GetActiveLifetime();
            var expectedObject = go.GetLifetime();
            var seenActive = expectedActive;
            var seenObject = expectedObject;
            TestDelegate lookup = () =>
            {
                for (var i = 0; i < 100; i++)
                {
                    seenActive = probe.GetActiveLifetime();
                    seenObject = go.GetLifetime();
                }
            };
            lookup();

            Assert.That(lookup, Is.Not.AllocatingGCMemory());

            Assert.That(seenActive, Is.SameAs(expectedActive));
            Assert.That(seenObject, Is.SameAs(expectedObject));
            Assert.That(_s.Tree.Owners.Count, Is.EqualTo(2));
        }

        [Test]
        public void Run_StateWithStaticSynchronousWorkOnAMonoBehaviour_AllocatesNothing()
        {
            var probe = _s.NewProbe();
            var counter = new Counter();
            TestDelegate run = () =>
            {
                for (var i = 0; i < Count; i++)
                {
                    probe.Run(counter, static (c, ct) =>
                    {
                        c.Value++;
                        return UniTask.CompletedTask;
                    });
                }
            };
            run();
            run();

            Assert.That(run, Is.Not.AllocatingGCMemory());

            Assert.That(counter.Value, Is.EqualTo(3 * Count), "the measured block must really have run the work");
            Assert.That(probe.GetLifetime().EntryCount, Is.Zero);
        }

        [Test]
        public void AddTo_ClassDisposableOnAMonoBehaviour_AllocatesNothing()
        {
            var probe = _s.NewProbe();
            var lifetime = probe.GetLifetime();
            var disposable = new DisposeProbe();
            TestDelegate add = () =>
            {
                for (var i = 0; i < Count; i++)
                {
                    disposable.AddTo(probe);
                }
            };
            add();
            lifetime.Cancel();
            add();
            lifetime.Cancel();

            Assert.That(add, Is.Not.AllocatingGCMemory());

            Assert.That(lifetime.EntryCount, Is.EqualTo(Count), "the measured block must really have registered");
            lifetime.Cancel();
            Assert.That(disposable.DisposeCount, Is.EqualTo(3 * Count));
        }

        [Test]
        public void OwnedEventSubscribe_CachedHandlerWithAMonoBehaviourOwner_AllocatesNothing()
        {
            var probe = _s.NewProbe();
            var lifetime = probe.GetLifetime();
            var evt = new OwnedEvent("alloc");
            var counter = new Counter();
            Action handler = () => counter.Value++;
            TestDelegate subscribe = () => evt.Subscribe(handler, probe);
            subscribe();
            lifetime.Cancel();
            subscribe();
            lifetime.Cancel();

            Assert.That(subscribe, Is.Not.AllocatingGCMemory());

            Assert.That(evt.SubscriberCount, Is.EqualTo(1), "the measured block must really have subscribed");
            evt.Invoke();
            Assert.That(counter.Value, Is.EqualTo(1));
        }
    }
}
