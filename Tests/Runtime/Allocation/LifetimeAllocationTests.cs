using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Ecanakli.Janitor.Tests
{
    // 0 B in steady state. Every test warms the exact call sites first, never reads Token
    // inside a measured block (one CTS per generation is the documented floor) and asserts the work really happened.
    [TestFixture]
    public sealed class LifetimeAllocationTests
    {
        private const int Entries = 64;

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
        public void AddTo_ClassDisposable_AllocatesNothing()
        {
            var area = _t.App.CreateChild("area");
            var probe = new DisposeProbe();
            RegisterDisposables(area, probe, Entries);
            area.Cancel();

            Assert.That(() => RegisterDisposables(area, probe, Entries), Is.Not.AllocatingGCMemory());

            Assert.That(area.EntryCount, Is.EqualTo(Entries), "the measured block must really have registered");
            area.Cancel();
            Assert.That(probe.DisposeCount, Is.EqualTo(2 * Entries));
        }

        [Test]
        public void OnCancel_StateWithStaticAction_AllocatesNothing()
        {
            var area = _t.App.CreateChild("area");
            var counter = new Counter();
            RegisterCounted(area, counter, Entries);
            area.Cancel();

            Assert.That(() => RegisterCounted(area, counter, Entries), Is.Not.AllocatingGCMemory());

            Assert.That(area.EntryCount, Is.EqualTo(Entries));
            area.Cancel();
            Assert.That(counter.Value, Is.EqualTo(2 * Entries));
        }

        [Test]
        public void OnCancel_TwoStatesWithStaticAction_AllocatesNothing()
        {
            var area = _t.App.CreateChild("area");
            var first = new Counter();
            var second = new Counter();
            RegisterPairs(area, first, second, Entries);
            area.Cancel();

            Assert.That(() => RegisterPairs(area, first, second, Entries), Is.Not.AllocatingGCMemory());

            Assert.That(area.EntryCount, Is.EqualTo(Entries));
            area.Cancel();
            Assert.That(first.Value, Is.EqualTo(2 * Entries));
            Assert.That(second.Value, Is.EqualTo(2 * Entries));
        }

        [Test]
        public void Cancel_64EntriesAcrossALifetimeWithEightDescendants_AllocatesNothing()
        {
            var root = _t.App.CreateChild("root");
            var nodes = BuildSubtree(root);
            var counter = new Counter();
            var probe = new DisposeProbe();
            for (var warm = 0; warm < 2; warm++)
            {
                FillEntries(nodes, counter, probe, Entries);
                root.Cancel();
            }

            for (var round = 0; round < 3; round++)
            {
                FillEntries(nodes, counter, probe, Entries);
                Assert.That(() => root.Cancel(), Is.Not.AllocatingGCMemory());
            }

            Assert.That(counter.Value + probe.DisposeCount, Is.EqualTo(5 * Entries), "every entry must have terminated exactly once");
            Assert.That(root.Generation, Is.EqualTo(5));
            Assert.That(nodes[8].Generation, Is.EqualTo(5));
        }

        [Test]
        public void Dispose_WarmSubtree_AllocatesNothing()
        {
            var counter = new Counter();
            var probe = new DisposeProbe();
            var warmRoot = _t.App.CreateChild("warm");
            FillEntries(BuildSubtree(warmRoot), counter, probe, Entries);
            warmRoot.Dispose();

            for (var round = 0; round < 3; round++)
            {
                var doomed = _t.App.CreateChild("doomed");
                var nodes = BuildSubtree(doomed);
                FillEntries(nodes, counter, probe, Entries);

                Assert.That(() => doomed.Dispose(), Is.Not.AllocatingGCMemory());

                Assert.That(doomed.IsDisposed, Is.True);
                Assert.That(nodes[8].IsDisposed, Is.True);
            }

            Assert.That(counter.Value + probe.DisposeCount, Is.EqualTo(4 * Entries));
        }

        [Test]
        public void RegistrationCancel_AllocatesNothing()
        {
            var area = _t.App.CreateChild("area");
            var counter = new Counter();
            var registrations = new LifetimeRegistration[Entries];
            RegisterAndKeep(area, counter, registrations);
            CancelAll(registrations);
            RegisterAndKeep(area, counter, registrations);

            Assert.That(() => CancelAll(registrations), Is.Not.AllocatingGCMemory());

            Assert.That(counter.Value, Is.EqualTo(2 * Entries));
            Assert.That(area.EntryCount, Is.Zero);
        }

        [Test]
        public void RegistrationIsActive_AllocatesNothing()
        {
            var area = _t.App.CreateChild("area");
            var counter = new Counter();
            var registrations = new LifetimeRegistration[Entries];
            RegisterAndKeep(area, counter, registrations);
            var active = 0;
            active += CountActive(registrations);

            Assert.That(() => { active += CountActive(registrations); }, Is.Not.AllocatingGCMemory());

            Assert.That(active, Is.EqualTo(2 * Entries));
        }

        [Test]
        public void CancelAndReregister_RepeatedCycles_AllocateNothing()
        {
            var area = _t.App.CreateChild("area");
            var child = area.CreateChild("child");
            var counter = new Counter();
            var probe = new DisposeProbe();
            var nodes = new[] { area, child };
            FillEntries(nodes, counter, probe, Entries);
            area.Cancel();

            Assert.That(() =>
            {
                for (var i = 0; i < 100; i++)
                {
                    FillEntries(nodes, counter, probe, Entries);
                    area.Cancel();
                }
            }, Is.Not.AllocatingGCMemory());

            Assert.That(counter.Value + probe.DisposeCount, Is.EqualTo(101 * Entries));
        }

        private static void RegisterDisposables(Lifetime area, DisposeProbe probe, int count)
        {
            for (var i = 0; i < count; i++)
            {
                probe.AddTo(area);
            }
        }

        private static void RegisterCounted(Lifetime area, Counter counter, int count)
        {
            for (var i = 0; i < count; i++)
            {
                area.OnCancel(counter, static c => c.Value++);
            }
        }

        private static void RegisterPairs(Lifetime area, Counter first, Counter second, int count)
        {
            for (var i = 0; i < count; i++)
            {
                area.OnCancel(first, second, static (a, b) =>
                {
                    a.Value++;
                    b.Value++;
                });
            }
        }

        private static void RegisterAndKeep(Lifetime area, Counter counter, LifetimeRegistration[] registrations)
        {
            for (var i = 0; i < registrations.Length; i++)
            {
                registrations[i] = area.OnCancel(counter, static c => c.Value++);
            }
        }

        private static void CancelAll(LifetimeRegistration[] registrations)
        {
            for (var i = 0; i < registrations.Length; i++)
            {
                registrations[i].Cancel();
            }
        }

        private static int CountActive(LifetimeRegistration[] registrations)
        {
            var active = 0;
            for (var i = 0; i < registrations.Length; i++)
            {
                if (registrations[i].IsActive)
                {
                    active++;
                }
            }

            return active;
        }

        // The root plus four children that each have one child: eight descendants.
        private static Lifetime[] BuildSubtree(Lifetime root)
        {
            var nodes = new Lifetime[9];
            nodes[0] = root;
            for (var i = 0; i < 4; i++)
            {
                nodes[1 + 2 * i] = root.CreateChild();
                nodes[2 + 2 * i] = nodes[1 + 2 * i].CreateChild();
            }

            return nodes;
        }

        // Alternates OnCancel and AddTo entries round-robin over the nodes.
        private static void FillEntries(Lifetime[] nodes, Counter counter, DisposeProbe probe, int total)
        {
            for (var i = 0; i < total; i++)
            {
                var node = nodes[i % nodes.Length];
                if ((i & 1) == 0)
                {
                    node.OnCancel(counter, static c => c.Value++);
                }
                else
                {
                    probe.AddTo(node);
                }
            }
        }
    }
}
