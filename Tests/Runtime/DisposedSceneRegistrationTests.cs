using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.Binding
{
    // Work offered straight to a disposed scene lifetime warns once per scene, never while the application exits,
    // and an area created below one is born disposed with the same single warning.
    [TestFixture]
    public sealed class DisposedSceneRegistrationTests
    {
        private BindingSession _s;

        [SetUp]
        public void SetUp()
        {
            _s = BindingSession.Begin();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return _s.CompleteAsync();
        }

        [UnityTest]
        public IEnumerator Register_OnADisposedSceneLifetimeAgainAndAgain_WarnsOncePerScene()
        {
            var first = _s.NewScene("First");
            var second = _s.NewScene("Second");
            var firstLifetime = SceneLifetimes.Get(first);
            var secondLifetime = SceneLifetimes.Get(second);
            SceneLifetimes.Dispose(first);
            SceneLifetimes.Dispose(second);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR109"));
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR109"));
            var ran = 0;

            for (var round = 0; round < 3; round++)
            {
                firstLifetime.OnCancel(() => ran++);
                secondLifetime.OnCancel(() => ran++);
            }

            Assert.That(ran, Is.EqualTo(6), "every item is still terminated at once");
            Assert.That(_s.Logs.Count("JANITOR109"), Is.EqualTo(2), "one for each disposed scene, however often work is offered");
            yield break;
        }

        [UnityTest]
        public IEnumerator Register_OnADisposedSceneLifetime_WhileTheApplicationIsExiting_LogsNothing()
        {
            var scene = _s.NewScene("Exiting");
            var sceneLifetime = SceneLifetimes.Get(scene);
            _s.Exit.Cancel();
            var ran = 0;

            sceneLifetime.OnCancel(() => ran++);

            Assert.That(ran, Is.EqualTo(1));
            Assert.That(_s.Logs.Count("JANITOR109"), Is.Zero);
            LogAssert.NoUnexpectedReceived();
            yield break;
        }

        [UnityTest]
        public IEnumerator CreateChild_UnderADisposedSceneLifetime_WarnsOncePerScene_AndTheAreaIsBornDisposed()
        {
            var scene = _s.NewScene("Disposed");
            var sceneLifetime = SceneLifetimes.Get(scene);
            SceneLifetimes.Dispose(scene);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR109"));

            var first = sceneLifetime.CreateChild("first");
            var second = sceneLifetime.CreateChild("second");

            Assert.That(first.IsDisposed, Is.True);
            Assert.That(second.IsDisposed, Is.True);
            Assert.That(sceneLifetime.ChildCount, Is.Zero, "a born-disposed area is never attached");
            Assert.That(_s.Logs.Count("JANITOR109"), Is.EqualTo(1), "the second call finds the scene already reported");
            yield break;
        }

        [UnityTest]
        public IEnumerator CreateChild_UnderADisposedAreaOrAnActiveScene_RaisesNothing()
        {
            var scene = _s.NewScene("Mixed");
            var sceneLifetime = SceneLifetimes.Get(scene);
            var area = sceneLifetime.CreateChild("area");
            area.Dispose();

            var child = area.CreateChild("child");
            var sibling = sceneLifetime.CreateChild("sibling");

            Assert.That(child.IsDisposed, Is.True);
            Assert.That(sibling.IsDisposed, Is.False);
            Assert.That(_s.Logs.Count("JANITOR109"), Is.Zero, "only a disposed scene lifetime is reported");
            yield break;
        }

        [UnityTest]
        public IEnumerator CreateChild_UnderADisposedSceneLifetime_WhileTheApplicationIsExiting_LogsNothing()
        {
            var scene = _s.NewScene("Exiting");
            var sceneLifetime = SceneLifetimes.Get(scene);
            _s.Exit.Cancel();

            var child = sceneLifetime.CreateChild("late");

            Assert.That(child.IsDisposed, Is.True);
            Assert.That(_s.Logs.Count("JANITOR109"), Is.Zero);
            LogAssert.NoUnexpectedReceived();
            yield break;
        }
    }
}
