using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests
{
    // Lifetime.App resolves through LifetimeTree.Default; every test restores the previous default.
    [TestFixture]
    public sealed class LifetimeAppTests
    {
        private TestScope _t;
        private LifetimeTree _previousDefault;

        [SetUp]
        public void SetUp()
        {
            _previousDefault = LifetimeTree.Default;
            _t = new TestScope();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                _t.Complete();
            }
            finally
            {
                LifetimeTree.Default = _previousDefault;
            }
        }

        [Test]
        public void App_WithoutADefaultTree_ThrowsInvalidOperationExceptionMentioningPlayMode()
        {
            LifetimeTree.Default = null;

            var exception = Assert.Throws<InvalidOperationException>(() => { _ = Lifetime.App; });

            Assert.That(exception.Message, Does.Contain("Play Mode"));
        }

        [Test]
        public void App_WithADefaultTree_ReturnsThatTreesAppLifetime()
        {
            _t.Tree.MakeDefault();

            Assert.That(Lifetime.App, Is.SameAs(_t.App));
            Assert.That(Lifetime.App, Is.SameAs(Lifetime.App));
        }

        [Test]
        public void App_IsThePackageOwnedRoot()
        {
            _t.Tree.MakeDefault();

            var app = Lifetime.App;

            Assert.That(app.Name, Is.EqualTo("App"));
            Assert.That(app.Kind, Is.EqualTo(LifetimeKind.App));
            Assert.That(app.PackageOwned, Is.True);
            Assert.That(app.Parent, Is.Null);
            Assert.That(app.IsDisposed, Is.False);
        }

        [Test]
        public void App_ChildrenCreatedThroughIt_AreAttachedToTheDefaultTree()
        {
            _t.Tree.MakeDefault();

            var child = Lifetime.App.CreateChild("child");

            Assert.That(child.Parent, Is.SameAs(_t.App));
        }

        [Test]
        public void Dispose_OnApp_IsIgnoredWithAJanitor107Warning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR107"));
            _t.Tree.MakeDefault();
            Lifetime.App.Record(_t.Log, "app:item");

            Lifetime.App.Dispose();

            Assert.That(Lifetime.App.IsDisposed, Is.False);
            Assert.That(Lifetime.App.State, Is.EqualTo(LifetimeState.Active));
            Assert.That(_t.Log.Count, Is.Zero);
        }

        [Test]
        public void Cancel_OnApp_CancelsEveryDescendantWithoutDisposingAnything()
        {
            _t.Tree.MakeDefault();
            var area = Lifetime.App.CreateChild("area");
            var nested = area.CreateChild("nested");
            Lifetime.App.Record(_t.Log, "app");
            area.Record(_t.Log, "area");
            nested.Record(_t.Log, "nested");

            Lifetime.App.Cancel();

            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "nested", "area", "app" }));
            Assert.That(Lifetime.App.State, Is.EqualTo(LifetimeState.Active));
            Assert.That(area.State, Is.EqualTo(LifetimeState.Active));
            Assert.That(nested.State, Is.EqualTo(LifetimeState.Active));
            Assert.That(Lifetime.App.Generation, Is.EqualTo(1));
        }

        [Test]
        public void Shutdown_DisposesTheAppAndEverythingBelowIt()
        {
            var area = _t.App.CreateChild("area");
            _t.App.Record(_t.Log, "app");
            area.Record(_t.Log, "area");

            _t.Tree.Shutdown();

            Assert.That(_t.App.IsDisposed, Is.True);
            Assert.That(area.IsDisposed, Is.True);
            Assert.That(_t.Log.ToArray(), Is.EqualTo(new[] { "area", "app" }));
        }

        [Test]
        public void TestTree_MakeDefault_RestoresThePreviousDefaultOnDispose()
        {
            var before = LifetimeTree.Default;

            _t.Tree.MakeDefault();
            Assert.That(LifetimeTree.Default, Is.Not.SameAs(before));
            _t.Tree.Dispose();

            Assert.That(LifetimeTree.Default, Is.SameAs(before));
        }
    }
}
