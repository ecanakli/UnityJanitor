using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // The stable IDs JANITOR101 to JANITOR116, each with a one-line title and a Troubleshooting anchor.
    [TestFixture]
    public sealed class DiagnosticIdsTests
    {
        private static readonly string[] Expected =
        {
            "JANITOR101", "JANITOR102", "JANITOR103", "JANITOR104", "JANITOR105",
            "JANITOR106", "JANITOR107", "JANITOR108", "JANITOR109", "JANITOR110",
            "JANITOR111", "JANITOR112", "JANITOR113", "JANITOR114", "JANITOR115",
            "JANITOR116",
        };

        [Test]
        public void PublicConstants_AreExactlyJanitor101ToJanitor116()
        {
            var values = new List<string>();
            var fields = typeof(DiagnosticIds).GetFields(BindingFlags.Public | BindingFlags.Static);
            for (var i = 0; i < fields.Length; i++)
            {
                if (fields[i].IsLiteral && fields[i].FieldType == typeof(string))
                {
                    values.Add((string)fields[i].GetRawConstantValue());
                }
            }

            Assert.That(values.Count, Is.EqualTo(16));
            Assert.That(values, Is.EquivalentTo(Expected));
        }

        [Test]
        public void Constants_AreNamedAfterTheirMeaning()
        {
            Assert.That(DiagnosticIds.TaskOverrun, Is.EqualTo("JANITOR101"));
            Assert.That(DiagnosticIds.UnkillableTween, Is.EqualTo("JANITOR102"));
            Assert.That(DiagnosticIds.OrphanLifetime, Is.EqualTo("JANITOR103"));
            Assert.That(DiagnosticIds.SceneDisposedLate, Is.EqualTo("JANITOR104"));
            Assert.That(DiagnosticIds.DuplicateSubscription, Is.EqualTo("JANITOR105"));
            Assert.That(DiagnosticIds.Growth, Is.EqualTo("JANITOR106"));
            Assert.That(DiagnosticIds.DisposeIgnored, Is.EqualTo("JANITOR107"));
            Assert.That(DiagnosticIds.RegistrationOnInactiveObject, Is.EqualTo("JANITOR108"));
            Assert.That(DiagnosticIds.RegistrationOnDisposedScene, Is.EqualTo("JANITOR109"));
            Assert.That(DiagnosticIds.CoroutineNotStarted, Is.EqualTo("JANITOR110"));
            Assert.That(DiagnosticIds.Marshalled, Is.EqualTo("JANITOR111"));
            Assert.That(DiagnosticIds.MissingSceneInstall, Is.EqualTo("JANITOR112"));
            Assert.That(DiagnosticIds.ParentMismatch, Is.EqualTo("JANITOR113"));
            Assert.That(DiagnosticIds.Rehomed, Is.EqualTo("JANITOR114"));
            Assert.That(DiagnosticIds.DepthExceeded, Is.EqualTo("JANITOR115"));
            Assert.That(DiagnosticIds.RepeatedOnEnable, Is.EqualTo("JANITOR116"));
        }

        [Test]
        public void All_ListsEveryConstantInIdOrder()
        {
            Assert.That(DiagnosticIds.All, Is.EqualTo(Expected));
        }

        [Test]
        public void GetTitle_ForEveryId_IsOneNonEmptyLineAndUnique()
        {
            var seen = new HashSet<string>();
            foreach (var id in DiagnosticIds.All)
            {
                var title = DiagnosticIds.GetTitle(id);

                Assert.That(title, Is.Not.Null.And.Not.Empty, id);
                Assert.That(title, Does.Not.Contain("\n").And.Not.Contain("\r"), id + " must be a single line");
                Assert.That(seen.Add(title), Is.True, "two IDs share the title of " + id);
            }

            Assert.That(seen.Count, Is.EqualTo(16));
        }

        [Test]
        public void GetTitle_OfTheRepeatedOnEnableId_SaysTheRegistrationWasMadeAgain()
        {
            Assert.That(DiagnosticIds.GetTitle(DiagnosticIds.RepeatedOnEnable), Is.EqualTo("Work registered in OnEnable on a lifetime that does not follow activation was registered again"));
            Assert.That(DiagnosticIds.GetAnchor(DiagnosticIds.RepeatedOnEnable), Is.EqualTo("Documentation~/Troubleshooting.md#janitor116"));
        }

        [Test]
        public void GetTitle_OfTheCoroutineId_NamesEveryHostCauseTheCodeReports()
        {
            Assert.That(DiagnosticIds.GetTitle(DiagnosticIds.CoroutineNotStarted), Is.EqualTo("Coroutine not started: the host is null, destroyed or inactive"));
        }

        [Test]
        public void GetTitle_OfTheZenjectInstallId_DoesNotClaimAnAppLevelLifetime()
        {
            var title = DiagnosticIds.GetTitle(DiagnosticIds.MissingSceneInstall);

            Assert.That(title, Does.Not.Contain("App-level"), "the lifetime may come from any outer context");
            Assert.That(title, Does.Contain("outer context").And.Contain("SceneContext").And.Contain("GameObjectContext").And.Contain("LifetimeInstaller.Install"));
        }

        [Test]
        public void GetTitle_OfTheGrowthId_CountsChildAreasNotChildren()
        {
            Assert.That(DiagnosticIds.GetTitle(DiagnosticIds.Growth), Is.EqualTo("Growth: more than 256 entries or 64 live child areas on one lifetime"));
        }

        [Test]
        public void GetAnchor_ForEveryId_PointsAtItsOwnTroubleshootingSectionAndIsUnique()
        {
            var seen = new HashSet<string>();
            foreach (var id in DiagnosticIds.All)
            {
                var anchor = DiagnosticIds.GetAnchor(id);

                Assert.That(anchor, Is.EqualTo("Documentation~/Troubleshooting.md#" + id.ToLowerInvariant()));
                Assert.That(seen.Add(anchor), Is.True, "two IDs share the anchor of " + id);
            }
        }

        [Test]
        public void GetTitle_ForAnUnknownId_IsNull([Values("JANITOR100", "JANITOR117", "janitor101", "JANITOR101 ", "", "TEST001", null)] string id)
        {
            Assert.That(DiagnosticIds.GetTitle(id), Is.Null);
        }

        [Test]
        public void GetAnchor_ForAnUnknownId_IsNull([Values("JANITOR100", "JANITOR117", "janitor101", "JANITOR101 ", "", "TEST001", null)] string id)
        {
            Assert.That(DiagnosticIds.GetAnchor(id), Is.Null);
        }

        [Test]
        public void DevWarningsFormat_ProducesTheStableConsoleShapeForEveryId()
        {
            Assert.That(DevWarnings.Format(DiagnosticIds.Growth, "text"), Is.EqualTo("[JANITOR106] text (see Troubleshooting#janitor106)"));
            foreach (var id in DiagnosticIds.All)
            {
                var formatted = DevWarnings.Format(id, "m");

                Assert.That(formatted, Is.EqualTo("[" + id + "] m (see Troubleshooting#" + id.ToLowerInvariant() + ")"));
                Assert.That(DiagnosticIds.GetAnchor(id), Does.EndWith(formatted.Substring(formatted.IndexOf('#') + 1).TrimEnd(')')), "the console link and the anchor name the same section");
            }
        }
    }
}
