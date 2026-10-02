using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.EditorTools.Tests
{
    // The Docs button's URL: pinned to the installed version's tag, main when the version is unknown, the anchor from DiagnosticIds.
    [TestFixture]
    public sealed class DocLinksTests
    {
        private const string Repository = "https://github.com/ecanakli/UnityJanitor/blob/";

        public static IEnumerable<string> AllIds()
        {
            return DiagnosticIds.All;
        }

        [SetUp]
        public void SetUp()
        {
            DocLinks.ResetSeams();
        }

        [TearDown]
        public void TearDown()
        {
            DocLinks.ResetSeams();
        }

        // Version tag

        [Test]
        public void BuildUrl_KnownVersion_PinsTheUrlToTheVersionTag()
        {
            DocLinks.VersionProvider = () => "1.2.3";

            var url = DocLinks.BuildUrl(DiagnosticIds.TaskOverrun);

            Assert.That(url, Is.EqualTo("https://github.com/ecanakli/UnityJanitor/blob/v1.2.3/Documentation~/Troubleshooting.md#janitor101"));
        }

        [Test]
        public void BuildUrl_PreReleaseVersion_KeepsTheWholeVersionInTheTag()
        {
            DocLinks.VersionProvider = () => "1.0.0-preview.2";

            var url = DocLinks.BuildUrl(DiagnosticIds.Growth);

            Assert.That(url, Does.StartWith(Repository + "v1.0.0-preview.2/"));
        }

        [Test]
        public void BuildUrl_VersionWithSurroundingWhitespace_IsTrimmed()
        {
            DocLinks.VersionProvider = () => " 2.0.0 ";

            Assert.That(DocLinks.BuildUrl(DiagnosticIds.Growth), Does.StartWith(Repository + "v2.0.0/"));
        }

        // Fallback

        [Test]
        public void BuildUrl_VersionNull_FallsBackToMain()
        {
            DocLinks.VersionProvider = () => null;

            var url = DocLinks.BuildUrl(DiagnosticIds.TaskOverrun);

            Assert.That(url, Is.EqualTo("https://github.com/ecanakli/UnityJanitor/blob/main/Documentation~/Troubleshooting.md#janitor101"));
        }

        [TestCase("")]
        [TestCase("   ")]
        public void BuildUrl_VersionBlank_FallsBackToMain(string version)
        {
            DocLinks.VersionProvider = () => version;

            Assert.That(DocLinks.BuildUrl(DiagnosticIds.Growth), Does.StartWith(Repository + "main/"));
        }

        [Test]
        public void BuildUrl_VersionProviderThrows_FallsBackToMainWithoutThrowing()
        {
            DocLinks.VersionProvider = () => throw new InvalidOperationException("package manager not ready");

            string url = null;
            Assert.DoesNotThrow(() => url = DocLinks.BuildUrl(DiagnosticIds.Growth));

            Assert.That(url, Does.StartWith(Repository + "main/"));
        }

        [Test]
        public void BuildUrl_NoVersionProvider_FallsBackToMain()
        {
            DocLinks.VersionProvider = null;

            Assert.That(DocLinks.BuildUrl(DiagnosticIds.Growth), Does.StartWith(Repository + "main/"));
        }

        // Anchors

        [TestCaseSource(nameof(AllIds))]
        public void BuildUrl_EveryPackageId_EndsWithTheAnchorFromDiagnosticIds(string id)
        {
            DocLinks.VersionProvider = () => "9.9.9";

            var url = DocLinks.BuildUrl(id);

            Assert.That(url, Is.EqualTo(Repository + "v9.9.9/" + DiagnosticIds.GetAnchor(id)));
            Assert.That(url, Does.EndWith("Documentation~/Troubleshooting.md#" + id.ToLowerInvariant()));
        }

        [Test]
        public void BuildUrl_Janitor101To116_EachHasItsOwnAnchor()
        {
            DocLinks.VersionProvider = () => "1.0.0";
            var seen = new HashSet<string>();

            for (var number = 101; number <= 116; number++)
            {
                var url = DocLinks.BuildUrl("JANITOR" + number);

                Assert.That(url, Does.EndWith("#janitor" + number), "JANITOR" + number);
                Assert.That(seen.Add(url), Is.True, "anchors must be distinct");
            }

            Assert.That(seen.Count, Is.EqualTo(16));
        }

        [Test]
        public void BuildUrl_IdThatIsNotThePackages_ReturnsNull()
        {
            Assert.That(DocLinks.BuildUrl("CUSTOM001"), Is.Null);
            Assert.That(DocLinks.BuildUrl("JANITOR999"), Is.Null);
            Assert.That(DocLinks.BuildUrl(string.Empty), Is.Null);
            Assert.That(DocLinks.BuildUrl(null), Is.Null);
        }

        // Default provider

        [Test]
        public void BuildUrl_DefaultProvider_UsesThePackageVersionFromThePackageManager()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DocLinks).Assembly);
            Assume.That(package, Is.Not.Null, "the package manager does not know the Editor assembly's package in this environment");
            Assume.That(package.version, Is.Not.Null.And.Not.Empty);

            var url = DocLinks.BuildUrl(DiagnosticIds.TaskOverrun);

            Assert.That(url, Does.StartWith(Repository + "v" + package.version + "/Documentation~/"));
        }

        // The package manager is asked once per domain, whatever the answer

        [Test]
        public void BuildUrl_PackageManagerCannotReadTheVersion_AsksOnlyOnceForManyUrls()
        {
            var asked = 0;
            DocLinks.PackageVersionSource = () =>
            {
                asked++;
                return null;
            };

            for (var i = 0; i < 20; i++)
            {
                Assert.That(DocLinks.BuildUrl(DiagnosticIds.Growth), Does.StartWith(Repository + "main/"));
            }

            Assert.That(asked, Is.EqualTo(1), "a failed read is kept too");
        }

        [Test]
        public void BuildUrl_PackageManagerThrows_AsksOnlyOnceAndFallsBackToMain()
        {
            var asked = 0;
            DocLinks.PackageVersionSource = () =>
            {
                asked++;
                throw new InvalidOperationException("package manager not ready");
            };

            for (var i = 0; i < 5; i++)
            {
                Assert.That(DocLinks.BuildUrl(DiagnosticIds.Growth), Does.StartWith(Repository + "main/"));
            }

            Assert.That(asked, Is.EqualTo(1));
        }

        [Test]
        public void BuildUrl_PackageManagerKnowsTheVersion_AsksOnlyOnce()
        {
            var asked = 0;
            DocLinks.PackageVersionSource = () =>
            {
                asked++;
                return "3.1.4";
            };

            for (var i = 0; i < 5; i++)
            {
                Assert.That(DocLinks.BuildUrl(DiagnosticIds.Growth), Does.StartWith(Repository + "v3.1.4/"));
            }

            Assert.That(asked, Is.EqualTo(1));
        }

        [Test]
        public void ResetSeams_ForgetsTheCachedAnswer()
        {
            var answer = (string)null;
            DocLinks.PackageVersionSource = () => answer;
            Assert.That(DocLinks.BuildUrl(DiagnosticIds.Growth), Does.StartWith(Repository + "main/"));

            answer = "2.0.0";
            DocLinks.ResetSeams();
            DocLinks.PackageVersionSource = () => answer;

            Assert.That(DocLinks.BuildUrl(DiagnosticIds.Growth), Does.StartWith(Repository + "v2.0.0/"));
        }

        // Open

        [Test]
        public void Open_PackageId_HandsTheUrlToTheOpener()
        {
            DocLinks.VersionProvider = () => "1.2.3";
            string opened = null;
            DocLinks.Opener = url => opened = url;

            var result = DocLinks.Open(DiagnosticIds.DuplicateSubscription);

            Assert.That(result, Is.True);
            Assert.That(opened, Is.EqualTo(DocLinks.BuildUrl(DiagnosticIds.DuplicateSubscription)));
        }

        [Test]
        public void Open_IdThatIsNotThePackages_OpensNothing()
        {
            var calls = 0;
            DocLinks.Opener = url => calls++;

            var result = DocLinks.Open("CUSTOM001");

            Assert.That(result, Is.False);
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void Open_OpenerThrows_ReturnsFalseAndLogsTheException()
        {
            DocLinks.Opener = url => throw new InvalidOperationException("no browser");
            LogAssert.Expect(LogType.Exception, new Regex("no browser"));

            var result = DocLinks.Open(DiagnosticIds.DuplicateSubscription);

            Assert.That(result, Is.False);
        }
    }
}
