using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.Binding
{
    // JANITOR109 for objects of a disposed scene: an object that asks every frame must not log every frame,
    // and nothing is logged while the application is exiting.
    [TestFixture]
    public sealed class DisposedSceneWarningTests
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
        public IEnumerator GetLifetime_AskedAgainAndAgainOnObjectsOfADisposedScene_WarnsOncePerScene()
        {
            var first = _s.NewScene("First");
            var second = _s.NewScene("Second");
            var firstProbes = new[] { _s.NewProbeIn(first), _s.NewProbeIn(first), _s.NewProbeIn(first) };
            var secondProbe = _s.NewProbeIn(second);
            SceneLifetimes.Dispose(first);
            SceneLifetimes.Dispose(second);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR109"));
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR109"));

            for (var round = 0; round < 3; round++)
            {
                for (var i = 0; i < firstProbes.Length; i++)
                {
                    Assert.That(firstProbes[i].GetLifetime().IsDisposed, Is.True);
                }

                Assert.That(secondProbe.GetLifetime().IsDisposed, Is.True);
            }

            Assert.That(_s.Logs.Count("JANITOR109"), Is.EqualTo(2), "one for each disposed scene, however often its objects ask");
            yield break;
        }

        [UnityTest]
        public IEnumerator WarnDisposedScene_TheSameSceneLifetimeTwice_LogsOnce()
        {
            var first = _s.NewScene("First");
            var second = _s.NewScene("Second");
            SceneLifetimes.Dispose(first);
            SceneLifetimes.Dispose(second);
            var firstLifetime = SceneLifetimes.Get(first);
            var secondLifetime = SceneLifetimes.Get(second);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR109"));
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR109"));

            SceneBinding.WarnDisposedScene(firstLifetime);
            SceneBinding.WarnDisposedScene(firstLifetime);
            SceneBinding.WarnDisposedScene(secondLifetime);

            Assert.That(_s.Logs.Count("JANITOR109"), Is.EqualTo(2));
            yield break;
        }

        [UnityTest]
        public IEnumerator GetLifetime_OnAnObjectOfADisposedScene_WhileTheApplicationIsExiting_LogsNothing()
        {
            var scene = _s.NewScene("Exiting");
            var probe = _s.NewProbeIn(scene);
            SceneLifetimes.Get(scene);
            _s.Exit.Cancel();

            var lifetime = probe.GetLifetime();

            Assert.That(lifetime.IsDisposed, Is.True);
            Assert.That(_s.Logs.Count("JANITOR109"), Is.Zero);
            LogAssert.NoUnexpectedReceived();
            yield break;
        }
    }
}
