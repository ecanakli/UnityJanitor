#if UNITY_EDITOR
using System.Collections;
using System.Text.RegularExpressions;
using Ecanakli.Janitor.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Zenject;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.DependencyInjection.Tests.PlayMode
{
    // The two Zenject-side warnings with real SceneContexts, recorded through the public LifetimeDiagnostics.Report hook while
    // their console warnings stay: JANITOR104 from the SceneContext disposer (the game skipped SceneLifetimes.Dispose) and JANITOR112
    // (a scene that never called LifetimeInstaller.Install).
    [TestFixture]
    public sealed class ZenjectDiagnosticsPlayTests
    {
        private ZenjectPlaySession _s;
        private DiagnosticsRecordingKit _d;

        [SetUp]
        public void SetUp()
        {
            _s = ZenjectPlaySession.Begin();
            _d = new DiagnosticsRecordingKit();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
            {
                yield return _s.CompleteAsync();
            }
            finally
            {
                _d.Dispose();
            }
        }

        // 104

        [UnityTest]
        public IEnumerator Destroy_OfTheSceneContext_WhenTheOneLineCallWasSkipped_RecordsJanitor104OnceAttachedToTheSceneLifetime()
        {
            var scene = _s.NewScene("Skipped");
            var sceneName = scene.name;
            var context = _s.NewSceneContext(scene, c => LifetimeInstaller.Install(c));
            yield return _s.Frames(2);
            var sceneLifetime = SceneLifetimes.Get(scene);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR104"));

            Object.Destroy(context.gameObject);
            yield return _s.WaitUntil(() => sceneLifetime.IsDisposed, "the disposer to end the scene lifetime");

            var warning = _d.Single(DiagnosticIds.SceneDisposedLate);
            Assert.That(warning.Lifetime, Is.SameAs(sceneLifetime));
            Assert.That(warning.LifetimeLabel, Is.EqualTo(sceneName));
            Assert.That(warning.Message, Does.Contain("'" + sceneName + "'").And.Contain("was unloaded without SceneLifetimes.Dispose(scene) or DisposeAll()"));
            Assert.That(warning.Count, Is.EqualTo(1));
            Assert.That(_s.Warnings.Count("JANITOR104"), Is.EqualTo(1), "the console warning is kept");
            Assert.That(_s.Warnings.TextOf("JANITOR104"), Is.EqualTo(ZenjectDiagnostics.Format(DiagnosticIds.SceneDisposedLate, warning.Message)), "console and record agree");

            yield return _s.Unload(scene);

            Assert.That(_d.Single(DiagnosticIds.SceneDisposedLate).Count, Is.EqualTo(1), "the core's sceneUnloaded fallback finds the lifetime already disposed, so it adds nothing");
        }

        [UnityTest]
        public IEnumerator Destroy_OfTheSceneContext_AfterTheOneLineCall_RecordsNothing()
        {
            var scene = _s.NewScene("Called");
            var context = _s.NewSceneContext(scene, c => LifetimeInstaller.Install(c));
            yield return _s.Frames(2);

            SceneLifetimes.Dispose(scene);
            Object.Destroy(context.gameObject);
            yield return _s.WaitUntil(() => context == null, "the SceneContext to be destroyed");

            Assert.That(_d.Count(DiagnosticIds.SceneDisposedLate), Is.Zero);
        }

        [UnityTest]
        public IEnumerator Destroy_OfTwoSceneContextsWithoutTheOneLineCall_RecordsOneEntryPerScene()
        {
            var first = _s.NewScene("A");
            var second = _s.NewScene("B");
            var firstContext = _s.NewSceneContext(first, c => LifetimeInstaller.Install(c));
            var secondContext = _s.NewSceneContext(second, c => LifetimeInstaller.Install(c));
            yield return _s.Frames(2);
            var firstLifetime = SceneLifetimes.Get(first);
            var secondLifetime = SceneLifetimes.Get(second);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR104"));
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR104"));

            Object.Destroy(firstContext.gameObject);
            Object.Destroy(secondContext.gameObject);
            yield return _s.WaitUntil(() => firstLifetime.IsDisposed && secondLifetime.IsDisposed, "both disposers to run");

            var rows = _d.Warnings(DiagnosticIds.SceneDisposedLate);
            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(SnapshotRows.Has(rows, firstLifetime), Is.True);
            Assert.That(SnapshotRows.Has(rows, secondLifetime), Is.True);
        }

        // 112

        [UnityTest]
        public IEnumerator Resolve_InASceneWithoutInstall_RecordsJanitor112OncePerSceneThroughThePublicHook()
        {
            var parent = _s.NewInstalledParent();
            var scene = _s.NewScene("NoInstall");
            var context = _s.NewSceneContext(scene, c =>
            {
                c.Bind<AlphaService>().AsSingle();
                c.Bind<BetaService>().AsSingle();
            }, parent);
            yield return _s.Frames(2);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR112"));

            context.Container.Resolve<AlphaService>();
            context.Container.Resolve<BetaService>();

            var warning = _d.Single(DiagnosticIds.MissingSceneInstall);
            Assert.That(warning.Count, Is.EqualTo(1), "once per scene, not once per service");
            Assert.That(warning.Lifetime, Is.SameAs(Lifetime.App), "the lifetime the service received");
            Assert.That(warning.Context, Is.SameAs(context), "the SceneContext, so the window can ping it");
            Assert.That(warning.Message, Does.Contain(nameof(AlphaService)).And.Contain("'" + scene.name + "'"));
            Assert.That(_s.Warnings.Count("JANITOR112"), Is.EqualTo(1), "the console warning is kept");
        }

        [UnityTest]
        public IEnumerator Resolve_InTwoScenesWithoutInstall_RecordsOneEntryPerScene()
        {
            var parent = _s.NewInstalledParent();
            var first = _s.NewScene("NoInstallA");
            var second = _s.NewScene("NoInstallB");
            var firstContext = _s.NewSceneContext(first, c => c.Bind<AlphaService>().AsSingle(), parent);
            var secondContext = _s.NewSceneContext(second, c => c.Bind<AlphaService>().AsSingle(), parent);
            yield return _s.Frames(2);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR112"));
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR112"));

            firstContext.Container.Resolve<AlphaService>();
            secondContext.Container.Resolve<AlphaService>();

            var rows = _d.Warnings(DiagnosticIds.MissingSceneInstall);
            Assert.That(rows.Count, Is.EqualTo(2), "the message names the scene, so each scene is its own entry");
            Assert.That(rows[0].Context, Is.SameAs(firstContext));
            Assert.That(rows[1].Context, Is.SameAs(secondContext));
        }

        [UnityTest]
        public IEnumerator Resolve_InASceneWithInstall_RecordsNothing()
        {
            var parent = _s.NewInstalledParent();
            var scene = _s.NewScene("WithInstall");
            var context = _s.NewSceneContext(scene, c =>
            {
                LifetimeInstaller.Install(c);
                c.Bind<AlphaService>().AsSingle();
            }, parent);
            yield return _s.Frames(2);

            context.Container.Resolve<AlphaService>();

            Assert.That(_d.Count(DiagnosticIds.MissingSceneInstall), Is.Zero);
        }
    }

    // Rows of one recorded id, searched by lifetime.
    internal static class SnapshotRows
    {
        internal static bool Has(System.Collections.Generic.List<RecordedDiagnostic> rows, Lifetime lifetime)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                if (ReferenceEquals(rows[i].Lifetime, lifetime))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
#endif
