using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // Every message that carries a JANITOR id takes it from DiagnosticIds. A string literal with an id in it anywhere in the
    // runtime sources, outside DiagnosticIds.cs itself, is a second source of truth and fails here.
    [TestFixture]
    public sealed class DiagnosticIdSourceScanTests
    {
        private static readonly string[] ScannedFolders = { "Runtime", "DOTween", "DependencyInjection" };

        // A quoted string with JANITOR and three digits somewhere inside it.
        private static readonly Regex IdLiteral = new Regex(@"""(?:[^""\\]|\\.)*JANITOR\d{3}(?:[^""\\]|\\.)*""");

        [Test]
        public void IdLiteral_FindsAStringWithAnIdAndIgnoresEverythingElse()
        {
            Assert.That(IdLiteral.IsMatch("Debug.LogWarning(Format(\"JANITOR107\", message));"), Is.True);
            Assert.That(IdLiteral.IsMatch("var text = \"[JANITOR115] depth\";"), Is.True);
            Assert.That(IdLiteral.IsMatch("var text = \"see \" + \"JANITOR110 later\";"), Is.True);
            Assert.That(IdLiteral.IsMatch("Debug.LogWarning(Format(DiagnosticIds.DisposeIgnored, message));"), Is.False);
            Assert.That(IdLiteral.IsMatch("Debug.Log(\"no id here\"); // JANITOR107 is explained here"), Is.False);
            Assert.That(IdLiteral.IsMatch("var anchor = \"#janitor107\";"), Is.False, "the lower-case anchor is built from the id, not an id");
        }

        [Test]
        public void RuntimeSources_HaveNoJanitorIdLiteralOutsideDiagnosticIds()
        {
            var root = PackageRoot();
            var offenders = new List<string>();
            var scanned = 0;
            for (var f = 0; f < ScannedFolders.Length; f++)
            {
                var folder = Path.Combine(root, ScannedFolders[f]);
                Assert.That(Directory.Exists(folder), Is.True, "missing folder " + folder);
                var files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories);
                for (var i = 0; i < files.Length; i++)
                {
                    if (Path.GetFileName(files[i]) == "DiagnosticIds.cs")
                    {
                        continue;
                    }

                    scanned++;
                    var lines = File.ReadAllLines(files[i]);
                    for (var line = 0; line < lines.Length; line++)
                    {
                        if (lines[line].TrimStart().StartsWith("//") || !IdLiteral.IsMatch(lines[line]))
                        {
                            continue;
                        }

                        offenders.Add(files[i].Substring(root.Length) + ":" + (line + 1) + ": " + lines[line].Trim());
                    }
                }
            }

            Assert.That(scanned, Is.GreaterThan(30), "the scan found too few files; the package root is probably wrong: " + root);
            Assert.That(offenders, Is.Empty, "ID literals outside DiagnosticIds:\n" + string.Join("\n", offenders));
        }

        [Test]
        public void DiagnosticIdsFile_HoldsEveryIdLiteral()
        {
            var text = File.ReadAllText(Path.Combine(PackageRoot(), "Runtime", "Diagnostics", "DiagnosticIds.cs"));

            foreach (var id in DiagnosticIds.All)
            {
                Assert.That(text, Does.Contain("\"" + id + "\""), id + " must be defined as a literal in DiagnosticIds.cs");
            }
        }

        // Keeps the diagnostic ids and the Troubleshooting sections in step; skipped when the guide is absent.
        [Test]
        public void TroubleshootingGuide_HasExactlyOneSectionPerId()
        {
            var path = Path.Combine(PackageRoot(), "Documentation~", "Troubleshooting.md");
            if (!File.Exists(path))
            {
                Assert.Ignore("Documentation~/Troubleshooting.md does not exist.");
            }

            var headings = new List<string>();
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var match = Regex.Match(lines[i], @"^##\s+(JANITOR\d{3})\s*$");
                if (match.Success)
                {
                    headings.Add(match.Groups[1].Value);
                }
            }

            Assert.That(headings, Is.EquivalentTo(DiagnosticIds.All), "one '## JANITOR1xx' heading per ID, and no heading without an ID");
            foreach (var id in DiagnosticIds.All)
            {
                Assert.That(DiagnosticIds.GetAnchor(id), Does.EndWith("#" + id.ToLowerInvariant()), "GitHub turns the heading '" + id + "' into this anchor");
            }
        }

        private static string PackageRoot()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(Lifetime).Assembly);
            if (info != null && !string.IsNullOrEmpty(info.resolvedPath) && Directory.Exists(info.resolvedPath))
            {
                return info.resolvedPath;
            }

            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "com.ecanakli.janitor"));
        }
    }
}
