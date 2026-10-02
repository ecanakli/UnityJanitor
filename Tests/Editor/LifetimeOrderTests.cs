using System;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests
{
    // Cancel order: every token before any item; items deepest first, then newest first; Dispose the same.
    [TestFixture]
    public sealed class LifetimeOrderTests
    {
        // Tree: root -> [a -> [a1], b]. Pre-order is root, a, a1, b; the drain walks it in reverse.
        private static readonly string[] FullOrder =
        {
            "token:root", "token:a", "token:a1", "token:b",
            "item:b", "item:a1", "item:a", "item:root:2", "item:root:1",
        };

        private TestScope _t;
        private Lifetime _root;

        [SetUp]
        public void SetUp()
        {
            _t = new TestScope();
            _root = _t.App.CreateChild("root");
            var a = _root.CreateChild("a");
            var a1 = a.CreateChild("a1");
            var b = _root.CreateChild("b");

            _root.RecordToken(_t.Log, "token:root");
            a.RecordToken(_t.Log, "token:a");
            a1.RecordToken(_t.Log, "token:a1");
            b.RecordToken(_t.Log, "token:b");

            _root.Record(_t.Log, "item:root:1");
            _root.Record(_t.Log, "item:root:2");
            a.Record(_t.Log, "item:a");
            a1.Record(_t.Log, "item:a1");
            b.Record(_t.Log, "item:b");
        }

        [TearDown]
        public void TearDown()
        {
            _t.Complete();
        }

        [Test]
        public void Cancel_TokensCancelledBeforeAnyItemTerminates()
        {
            _root.Cancel();

            var log = _t.Log.ToArray();
            var lastToken = Array.FindLastIndex(log, e => e.StartsWith("token:", StringComparison.Ordinal));
            var firstItem = Array.FindIndex(log, e => e.StartsWith("item:", StringComparison.Ordinal));
            Assert.That(lastToken, Is.EqualTo(3), "all four tokens must fire");
            Assert.That(firstItem, Is.GreaterThan(lastToken), "an item terminated before every token was cancelled: " + _t.Log);
        }

        [Test]
        public void Cancel_TokenCallbacksFirePreOrder()
        {
            _root.Cancel();

            Assert.That(_t.Log.WithPrefix("token:"), Is.EqualTo(new[] { "token:root", "token:a", "token:a1", "token:b" }));
        }

        [Test]
        public void Cancel_ItemsTerminateDeepestFirstThenNewestFirst()
        {
            _root.Cancel();

            Assert.That(_t.Log.WithPrefix("item:"), Is.EqualTo(new[] { "item:b", "item:a1", "item:a", "item:root:2", "item:root:1" }));
        }

        [Test]
        public void Cancel_FullSequence_MatchesTheDocumentedOrder()
        {
            _root.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(FullOrder));
        }

        [Test]
        public void Cancel_EntriesInOneLifetime_TerminateNewestFirst()
        {
            var area = _t.App.CreateChild("single");
            area.Record(_t.Log, "one");
            area.Record(_t.Log, "two");
            area.Record(_t.Log, "three");

            area.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "three", "two", "one" }));
        }

        [Test]
        public void Cancel_Siblings_TerminateNewestFirst()
        {
            var parent = _t.App.CreateChild("parent");
            var older = parent.CreateChild("older");
            var newer = parent.CreateChild("newer");
            older.Record(_t.Log, "sibling:older");
            newer.Record(_t.Log, "sibling:newer");

            parent.Cancel();

            Assert.That(_t.Log.WithPrefix("sibling:"), Is.EqualTo(new[] { "sibling:newer", "sibling:older" }));
        }

        [Test]
        public void Cancel_Descendant_TerminatesBeforeItsAncestor()
        {
            var top = _t.App.CreateChild("top");
            var middle = top.CreateChild("middle");
            var bottom = middle.CreateChild("bottom");
            top.Record(_t.Log, "chain:top");
            middle.Record(_t.Log, "chain:middle");
            bottom.Record(_t.Log, "chain:bottom");

            top.Cancel();

            Assert.That(_t.Log.WithPrefix("chain:"), Is.EqualTo(new[] { "chain:bottom", "chain:middle", "chain:top" }));
        }

        [Test]
        public void Dispose_FollowsTheSameOrderAsCancel()
        {
            _root.Dispose();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(FullOrder));
        }

        [Test]
        public void Dispose_TokensCancelledBeforeAnyItemTerminates()
        {
            _root.Dispose();

            var log = _t.Log.ToArray();
            var lastToken = Array.FindLastIndex(log, e => e.StartsWith("token:", StringComparison.Ordinal));
            var firstItem = Array.FindIndex(log, e => e.StartsWith("item:", StringComparison.Ordinal));
            Assert.That(firstItem, Is.GreaterThan(lastToken), "an item terminated before every token was cancelled: " + _t.Log);
        }
    }
}
