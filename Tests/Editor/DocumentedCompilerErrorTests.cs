using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Ecanakli.Janitor.Tests
{
    // The troubleshooting guide states three compiler errors as facts. Small sources are compiled against the package with the
    // Roslyn compiler the editor ships, at language version 9 like Unity's, in a temporary folder outside the project. The
    // references are the ones Unity gives this test assembly. Nothing is added to the project and no recompile is triggered.
    // Every case is compiled on its own: a compiler that finds a declaration error does not bind method bodies, so one case
    // could hide another. The error code and the file are what the tests pin; the English text is checked apart.
    // A clean run prints nothing, so any warning the compiler prints makes the run fail.
    [TestFixture]
    public sealed class DocumentedCompilerErrorTests
    {
        private const string TestAssemblyName = "Ecanakli.Janitor.Tests";
        private const string SupportFile = "Support.cs";
        private const int TimeoutMilliseconds = 120000;

        private static readonly Regex DiagnosticLine = new Regex(
            @"^(?<file>.*?)\((?<line>\d+),(?<column>\d+)(?:,\d+,\d+)?\): (?<severity>error|warning) (?<code>CS\d+): (?<message>.*)$");

        private static readonly Regex AnyCode = new Regex(@"\bCS\d{4}\b");

        private readonly Dictionary<string, CompileResult> _results = new Dictionary<string, CompileResult>();
        private string _directory;
        private string _compilerDll;
        private string _host;
        private List<string> _references;

        [OneTimeSetUp]
        public void Prepare()
        {
            _compilerDll = CompilerDll();
            _host = CompilerHost();
            _references = References();
            _directory = Path.Combine(Path.GetTempPath(), "janitor-compile-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [OneTimeTearDown]
        public void DeleteTheFolder()
        {
            try
            {
                if (_directory != null && Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        // Without the type argument the compiler binds the plain Action overload and rejects both lambdas: CS0029 twice, once for
        // the add and once for the remove. It does not report CS0411, which the guide must not name for this call.
        [Test]
        public void Subscribe_WithoutItsTypeArgument_OnAMonoBehaviour_FailsWithCS0029ForBothLambdas()
        {
            AssertMissingTypeArgument(Compile("MissingTypeArgument.cs"), "MissingTypeArgument.cs");
        }

        [Test]
        public void Subscribe_WithoutItsTypeArgument_OnALifetime_FailsWithCS0029ForBothLambdas()
        {
            AssertMissingTypeArgument(Compile("MissingTypeArgumentOnLifetime.cs"), "MissingTypeArgumentOnLifetime.cs");
        }

        [Test]
        public void Subscribe_WithTheDocumentedTypeArguments_Compiles()
        {
            AssertCompilesCleanly(Compile("DocumentedTypeArguments.cs"));
        }

        [Test]
        public void Lifetime_WhenAnotherNamespaceDefinesTheSameName_FailsWithCS0104()
        {
            var result = Compile("AmbiguousLifetime.cs");

            var errors = AssertFailsWith(result, "AmbiguousLifetime.cs", "CS0104");

            Assert.That(errors[0].Message, Does.Contain("Ecanakli.Janitor.Lifetime").And.Contain("VContainerLike.Lifetime"), result.Describe("the error must name both types"));
        }

        [Test]
        public void Lifetime_WithTheDocumentedAliasLine_Compiles()
        {
            AssertCompilesCleanly(Compile("AliasedLifetime.cs"));
        }

        [Test]
        public void AddTo_WhenAnotherExtensionHasTheSameShape_FailsWithCS0121()
        {
            var result = Compile("AmbiguousAddTo.cs");

            var errors = AssertFailsWith(result, "AmbiguousAddTo.cs", "CS0121");

            Assert.That(errors[0].Message, Does.Contain("AddTo"), result.Describe("the error must name the AddTo methods"));
        }

        [Test]
        public void AddTo_WithTheLifetimeFormOfTheFix_Compiles()
        {
            AssertCompilesCleanly(Compile("LifetimeAddTo.cs"));
        }

        // The guide names R3 as the second library. An overload without optional parameters should win the tie-break over ours,
        // which has two, so such a library gives no error and the call binds to the other library (the assignment to
        // DisposableProbe compiles only if it does). Only a second AddTo with the same optional tail is ambiguous.
        [Test]
        public void AddTo_AnotherExtensionWithoutOptionalParameters_IsPreferredWithoutAnError()
        {
            AssertCompilesCleanly(Compile("PlainAddTo.cs"));
        }

        // The sentences the guide quotes, checked apart from the codes. The environment of the compiler process gives English.
        [Test]
        public void Messages_MatchTheEnglishTextOfTheGuide()
        {
            var missing = Compile("MissingTypeArgument.cs");
            var ambiguousName = Compile("AmbiguousLifetime.cs");
            var ambiguousCall = Compile("AmbiguousAddTo.cs");

            Assert.That(
                AssertFailsWith(missing, "MissingTypeArgument.cs", "CS0029")[0].Message,
                Does.Contain("Cannot implicitly convert type 'System.Action' to 'System.Action<string>'"),
                missing.Describe("CS0029 text"));
            Assert.That(
                AssertFailsWith(ambiguousName, "AmbiguousLifetime.cs", "CS0104")[0].Message,
                Does.Contain("is an ambiguous reference between"),
                ambiguousName.Describe("CS0104 text"));
            Assert.That(
                AssertFailsWith(ambiguousCall, "AmbiguousAddTo.cs", "CS0121")[0].Message,
                Does.Contain("The call is ambiguous between the following methods or properties"),
                ambiguousCall.Describe("CS0121 text"));
        }

        private static Dictionary<string, string> Sources()
        {
            return new Dictionary<string, string>
            {
                {
                    SupportFile,
                    @"using System;
using UnityEngine;

public sealed class AdsEvents
{
    public event Action<string> RewardGranted;
    public event Action Closed;

    public void Raise()
    {
        RewardGranted?.Invoke(""reward"");
        Closed?.Invoke();
    }
}

public sealed class DisposableProbe : IDisposable
{
    public void Dispose()
    {
    }
}

namespace VContainerLike
{
    public enum Lifetime
    {
        Transient,
        Singleton,
    }
}

namespace OtherLibMatching
{
    public static class DisposableExtensions
    {
        public static T AddTo<T>(this T disposable, Component owner, string member = null, int line = 0)
            where T : IDisposable
        {
            return disposable;
        }
    }
}

namespace OtherLibPlain
{
    public static class DisposableExtensions
    {
        public static T AddTo<T>(this T disposable, Component owner)
            where T : IDisposable
        {
            return disposable;
        }
    }
}
"
                },
                {
                    "MissingTypeArgument.cs",
                    @"using Ecanakli.Janitor;
using UnityEngine;

public sealed class MissingTypeArgumentUser : MonoBehaviour
{
    private readonly AdsEvents _ads = new AdsEvents();

    private void OnRewardGranted(string reward)
    {
    }

    private void OnEnable()
    {
        this.Subscribe(h => _ads.RewardGranted += h, h => _ads.RewardGranted -= h, OnRewardGranted);
    }
}
"
                },
                {
                    "MissingTypeArgumentOnLifetime.cs",
                    @"using Ecanakli.Janitor;

public static class MissingTypeArgumentOnLifetimeUser
{
    private static void OnRewardGranted(string reward)
    {
    }

    public static void Bind(Lifetime lifetime, AdsEvents ads)
    {
        lifetime.Subscribe(h => ads.RewardGranted += h, h => ads.RewardGranted -= h, OnRewardGranted);
    }
}
"
                },
                {
                    "DocumentedTypeArguments.cs",
                    @"using Ecanakli.Janitor;
using UnityEngine;

public sealed class DocumentedTypeArgumentsUser : MonoBehaviour
{
    private readonly AdsEvents _ads = new AdsEvents();

    private void OnRewardGranted(string reward)
    {
    }

    private void OnAdClosed()
    {
    }

    private void OnLowMemory()
    {
    }

    private void OnEnable()
    {
        this.Subscribe<Application.LowMemoryCallback>(
            static h => Application.lowMemory += h,
            static h => Application.lowMemory -= h,
            OnLowMemory);
        this.Subscribe<string>(h => _ads.RewardGranted += h, h => _ads.RewardGranted -= h, OnRewardGranted);
        this.Subscribe(h => _ads.Closed += h, h => _ads.Closed -= h, OnAdClosed);
    }
}
"
                },
                {
                    "AmbiguousLifetime.cs",
                    @"using Ecanakli.Janitor;
using VContainerLike;

public sealed class AmbiguousLifetimeUser
{
    public Lifetime Field;
}
"
                },
                {
                    "AliasedLifetime.cs",
                    @"using Ecanakli.Janitor;
using VContainerLike;
using Lifetime = Ecanakli.Janitor.Lifetime;

public sealed class AliasedLifetimeUser
{
    public Lifetime Janitor;
    public VContainerLike.Lifetime Registration = VContainerLike.Lifetime.Singleton;
}
"
                },
                {
                    "AmbiguousAddTo.cs",
                    @"using Ecanakli.Janitor;
using OtherLibMatching;
using UnityEngine;

public sealed class AmbiguousAddToUser : MonoBehaviour
{
    private void OnEnable()
    {
        new DisposableProbe().AddTo(this);
    }
}
"
                },
                {
                    "LifetimeAddTo.cs",
                    @"using Ecanakli.Janitor;
using OtherLibMatching;
using UnityEngine;

public sealed class LifetimeAddToUser : MonoBehaviour
{
    private void OnEnable()
    {
        new DisposableProbe().AddTo(this.GetLifetime());
    }
}
"
                },
                {
                    "PlainAddTo.cs",
                    @"using Ecanakli.Janitor;
using OtherLibPlain;
using UnityEngine;

public sealed class PlainAddToUser : MonoBehaviour
{
    private void OnEnable()
    {
        DisposableProbe bound = new DisposableProbe().AddTo(this);
    }
}
"
                },
            };
        }

        // The run had no error at all: no diagnostic, exit code 0, and nothing the compiler said outside the diagnostics.
        private static void AssertCompilesCleanly(CompileResult result)
        {
            Assert.That(result.Unattributed, Is.Empty, result.Describe("the compiler did not run as expected"));
            Assert.That(result.Errors, Is.Empty, result.Describe(result.Name + " must compile without an error"));
            Assert.That(result.ExitCode, Is.Zero, result.Describe("the compiler must exit with 0"));
        }

        // Exactly two CS0029 errors in the file (the add lambda and the remove lambda), both about Action and Action<string>, and no CS0411.
        private static void AssertMissingTypeArgument(CompileResult result, string file)
        {
            var coded = AssertFailsWith(result, file, "CS0029");
            var inFile = result.Errors.FindAll(e => e.File == file);
            Assert.That(inFile.Count, Is.EqualTo(2), result.Describe(file + " must report exactly two errors"));
            Assert.That(coded.Count, Is.EqualTo(2), result.Describe("both errors must be CS0029: one for the add lambda, one for the remove lambda"));
            Assert.That(inFile.Exists(e => e.Code == "CS0411"), Is.False, result.Describe("CS0411 must not be reported for a missing type argument"));
            for (var i = 0; i < coded.Count; i++)
            {
                Assert.That(coded[i].Message, Does.Contain("System.Action").And.Contain("System.Action<string>"), result.Describe("the conversion error must name both delegate types"));
            }
        }

        // The file has an error with the code. Returns the errors of that code in the file.
        private static List<Diagnostic> AssertFailsWith(CompileResult result, string file, string code)
        {
            Assert.That(result.Unattributed, Is.Empty, result.Describe("the compiler did not run as expected"));
            var inFile = result.Errors.FindAll(e => e.File == file);
            Assert.That(inFile, Is.Not.Empty, result.Describe(file + " must fail to compile"));
            var coded = inFile.FindAll(e => e.Code == code);
            Assert.That(coded, Is.Not.Empty, result.Describe(file + " must fail with " + code));
            return coded;
        }

        private static string Quote(string path)
        {
            return "\"" + path + "\"";
        }

        // One compiler run for the support file and the case file. The result is kept, so a case is compiled once.
        private CompileResult Compile(string file)
        {
            if (_results.TryGetValue(file, out var known))
            {
                return known;
            }

            var sources = Sources();
            var folder = Path.Combine(_directory, Path.GetFileNameWithoutExtension(file));
            Directory.CreateDirectory(folder);
            var response = new StringBuilder();
            response.AppendLine("-nologo");
            response.AppendLine("-nostdlib+");
            response.AppendLine("-target:library");
            response.AppendLine("-langversion:9");
            response.AppendLine("-warn:0");
            response.AppendLine("-utf8output");
            response.AppendLine("-out:" + Quote(Path.Combine(folder, "probe.dll")));
            for (var i = 0; i < _references.Count; i++)
            {
                response.AppendLine("-r:" + Quote(_references[i]));
            }

            var names = new[] { SupportFile, file };
            for (var i = 0; i < names.Length; i++)
            {
                var path = Path.Combine(folder, names[i]);
                File.WriteAllText(path, sources[names[i]]);
                response.AppendLine(Quote(path));
            }

            var responsePath = Path.Combine(folder, "compile.rsp");
            File.WriteAllText(responsePath, response.ToString());

            // -noconfig is only honoured on the command line.
            var result = Run(file, folder, Quote(_compilerDll) + " -noconfig @" + Quote(responsePath));
            _results[file] = result;
            return result;
        }

        // The references Unity compiles this test assembly with, plus the outputs of the assemblies it references.
        private static List<string> References()
        {
            UnityEditor.Compilation.Assembly testAssembly = null;
            var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor);
            for (var i = 0; i < assemblies.Length; i++)
            {
                if (assemblies[i].name == TestAssemblyName)
                {
                    testAssembly = assemblies[i];
                    break;
                }
            }

            Assert.That(testAssembly, Is.Not.Null, "the editor does not list the assembly " + TestAssemblyName);
            var root = Directory.GetParent(Application.dataPath).FullName;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var references = new List<string>();
            void Add(string path)
            {
                var full = Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(root, path));
                if (File.Exists(full) && seen.Add(full))
                {
                    references.Add(full);
                }
            }

            for (var i = 0; i < testAssembly.compiledAssemblyReferences.Length; i++)
            {
                Add(testAssembly.compiledAssemblyReferences[i]);
            }

            for (var i = 0; i < testAssembly.assemblyReferences.Length; i++)
            {
                Add(testAssembly.assemblyReferences[i].outputPath);
            }

            Assert.That(references.Exists(r => Path.GetFileName(r) == "Ecanakli.Janitor.dll"), Is.True, "the package assembly is not among the references");
            return references;
        }

        private static string CompilerDll()
        {
            var contents = EditorApplication.applicationContentsPath;
            var candidates = new[]
            {
                Path.Combine(contents, "Resources", "Scripting", "DotNetSdkRoslyn", "csc.dll"),
                Path.Combine(contents, "DotNetSdkRoslyn", "csc.dll"),
                Path.Combine(contents, "Tools", "Roslyn", "csc.dll"),
            };
            for (var i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                {
                    return candidates[i];
                }
            }

            Assert.Fail("The Roslyn compiler of the editor was not found. Looked for:\n" + string.Join("\n", candidates));
            return null;
        }

        // The .NET host the editor ships; a dotnet on the path is the fallback.
        private static string CompilerHost()
        {
            var name = Application.platform == RuntimePlatform.WindowsEditor ? "dotnet.exe" : "dotnet";
            var contents = EditorApplication.applicationContentsPath;
            var candidates = new[]
            {
                Path.Combine(contents, "Resources", "Scripting", "NetCoreRuntime", name),
                Path.Combine(contents, "NetCoreRuntime", name),
            };
            for (var i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                {
                    return candidates[i];
                }
            }

            return name;
        }

        private CompileResult Run(string name, string folder, string arguments)
        {
            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = _host,
                Arguments = arguments,
                WorkingDirectory = folder,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true,
            };

            // The editor's own Mono settings must not reach the .NET host.
            var inherited = new List<string>();
            foreach (var key in info.EnvironmentVariables.Keys)
            {
                var variable = (string)key;
                if (variable.StartsWith("MONO_", StringComparison.OrdinalIgnoreCase))
                {
                    inherited.Add(variable);
                }
            }

            for (var i = 0; i < inherited.Count; i++)
            {
                info.EnvironmentVariables.Remove(inherited[i]);
            }

            // The compiler answers in English whatever the language of the machine.
            info.EnvironmentVariables["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1";
            info.EnvironmentVariables["DOTNET_CLI_UI_LANGUAGE"] = "en";
            info.EnvironmentVariables["VSLANG"] = "1033";
            info.EnvironmentVariables["PreferredUILang"] = "en-US";

            var output = new StringBuilder();
            var gate = new object();
            var exitCode = 0;
            using (var process = new System.Diagnostics.Process { StartInfo = info })
            {
                process.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        lock (gate)
                        {
                            output.AppendLine(args.Data);
                        }
                    }
                };
                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        lock (gate)
                        {
                            output.AppendLine(args.Data);
                        }
                    }
                };

                try
                {
                    process.Start();
                }
                catch (Exception exception)
                {
                    Assert.Fail("The compiler host '" + _host + "' could not be started: " + exception.Message);
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(TimeoutMilliseconds))
                {
                    process.Kill();
                    Assert.Fail("The compiler did not finish " + name + " within " + TimeoutMilliseconds / 1000 + " s.");
                }

                // The overload without a timeout waits until the asynchronous output has been flushed.
                process.WaitForExit();
                exitCode = process.ExitCode;
            }

            string text;
            lock (gate)
            {
                text = output.ToString();
            }

            return new CompileResult(name, exitCode, text);
        }

        private sealed class CompileResult
        {
            internal readonly string Name;
            internal readonly int ExitCode;
            internal readonly string Output;
            internal readonly List<Diagnostic> Diagnostics = new List<Diagnostic>();

            // Lines that mention a compiler code or a crash but are not a diagnostic with a file position.
            internal readonly List<string> Unattributed = new List<string>();

            internal CompileResult(string name, int exitCode, string output)
            {
                Name = name;
                ExitCode = exitCode;
                Output = output;
                var lines = output.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].TrimEnd('\r');
                    var match = DiagnosticLine.Match(line);
                    if (match.Success)
                    {
                        Diagnostics.Add(new Diagnostic(
                            Path.GetFileName(match.Groups["file"].Value),
                            int.Parse(match.Groups["line"].Value),
                            match.Groups["severity"].Value == "error",
                            match.Groups["code"].Value,
                            match.Groups["message"].Value));
                    }
                    else if (AnyCode.IsMatch(line) || line.Contains("Unhandled exception"))
                    {
                        Unattributed.Add(line);
                    }
                }
            }

            internal List<Diagnostic> Errors => Diagnostics.FindAll(d => d.IsError);

            // The headline and everything the compiler said, so one run shows the truth whichever way an assertion goes.
            internal string Describe(string headline)
            {
                return headline + "\ncase " + Name + ", exit code " + ExitCode + ", compiler output:\n" + (Output.Length == 0 ? "(nothing)" : Output);
            }
        }

        private readonly struct Diagnostic
        {
            internal readonly string File;
            internal readonly int Line;
            internal readonly bool IsError;
            internal readonly string Code;
            internal readonly string Message;

            internal Diagnostic(string file, int line, bool isError, string code, string message)
            {
                File = file;
                Line = line;
                IsError = isError;
                Code = code;
                Message = message;
            }

            public override string ToString()
            {
                return File + "(" + Line + "): " + (IsError ? "error " : "warning ") + Code + ": " + Message;
            }
        }
    }
}
