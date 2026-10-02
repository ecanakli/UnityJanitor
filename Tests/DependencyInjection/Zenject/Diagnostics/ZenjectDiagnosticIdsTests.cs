#if UNITY_EDITOR
using NUnit.Framework;

namespace Ecanakli.Janitor.DependencyInjection.Tests
{
    // The Zenject assembly keeps its own console format (the core's DevWarnings is internal), but it takes every ID from the
    // public DiagnosticIds, and its messages are formatted exactly like the core's.
    [TestFixture]
    public sealed class ZenjectDiagnosticIdsTests
    {
        [Test]
        public void ZenjectDiagnostics_Ids_AreTheCentralDiagnosticIds()
        {
            Assert.That(ZenjectDiagnostics.SceneDisposedLateId, Is.EqualTo(DiagnosticIds.SceneDisposedLate));
            Assert.That(ZenjectDiagnostics.DuplicateSubscriptionId, Is.EqualTo(DiagnosticIds.DuplicateSubscription));
            Assert.That(ZenjectDiagnostics.MissingSceneInstallId, Is.EqualTo(DiagnosticIds.MissingSceneInstall));
            Assert.That(ZenjectDiagnostics.SceneDisposedLateId, Is.EqualTo("JANITOR104"));
            Assert.That(ZenjectDiagnostics.DuplicateSubscriptionId, Is.EqualTo("JANITOR105"));
            Assert.That(ZenjectDiagnostics.MissingSceneInstallId, Is.EqualTo("JANITOR112"));
        }

        [Test]
        public void ZenjectDiagnostics_Format_IsTheCoreFormatForEveryId()
        {
            foreach (var id in DiagnosticIds.All)
            {
                Assert.That(ZenjectDiagnostics.Format(id, "message"), Is.EqualTo(DevWarnings.Format(id, "message")), id);
            }
        }
    }
}
#endif
