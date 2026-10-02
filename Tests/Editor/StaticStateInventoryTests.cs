using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Ecanakli.Janitor.Tests
{
    // Every mutable static of the runtime assemblies is classified here: a pool, an immutable delegate, a constant table,
    // a counter or setting that is meant to outlive a session, or state that the session start resets. A static added
    // later fails the first test until someone decides which it is, and a state that claims a reset is dirtied and checked.
    [TestFixture]
    public sealed class StaticStateInventoryTests
    {
        private const string Core = "Ecanakli.Janitor";
        private const string DOTween = "Ecanakli.Janitor.DOTween";
        private const string Zenject = "Ecanakli.Janitor.DependencyInjection.Zenject";

        private const string ImmutableDelegate = "a cached delegate, assigned once and never replaced";
        private const string Pool = "a pool of cleared objects; it grows to the peak and holds nothing of a session";
        private const string WeakMarker = "weak or marker data keyed by an object; entries die with their key";
        private const string ConstantTable = "a table filled once and never changed";
        private const string Cache = "a cache of immutable facts about a type, filled lazily";
        private const string Scratch = "a scratch buffer that is cleared after every use";
        private const string Setting = "a setting of the editor window; it is meant to outlive a session";
        private const string TestSeam = "a test seam; null in production";
        private const string MonotonicCounter = "a monotonic counter or id source; it never needs to restart";
        private const string History = "history the window shows across sessions on purpose; bounded";
        private const string Lock = "a lock object";
        private const string ThreadGuard = "a re-entrancy guard of one call on one thread; the call restores it in a finally";

        private static readonly List<Classified> Classification = BuildClassification();

        [Test]
        public void EveryStaticField_IsClassified()
        {
            var assemblies = LoadedPackageAssemblies();
            Assert.That(assemblies.Exists(a => a.GetName().Name == Core), Is.True, "the core runtime assembly must be loaded");
            var classified = ClassifiedKeys();
            var unclassified = new StringBuilder();
            var count = 0;
            for (var a = 0; a < assemblies.Count; a++)
            {
                foreach (var field in StaticFields(assemblies[a]))
                {
                    if (classified.ContainsKey(KeyOf(field)))
                    {
                        continue;
                    }

                    count++;
                    unclassified.Append("  ").Append(KeyOf(field)).Append("  (").Append(field.FieldType.Name)
                        .Append(field.IsDefined(typeof(ThreadStaticAttribute), false) ? ", thread static" : string.Empty)
                        .Append(field.IsInitOnly ? ", readonly" : string.Empty).Append(") in ").Append(assemblies[a].GetName().Name).Append('\n');
                }
            }

            Assert.That(
                count,
                Is.Zero,
                count + " static field(s) have no classification. Decide for each: add it to Classification as one of the allowed kinds "
                + "with a reason, or make the session start reset it and add it as a reset entry with a check in "
                + nameof(SessionStart_ResetsEveryFieldClassifiedAsReset) + ".\n" + unclassified);
        }

        [Test]
        public void Classification_HasNoStaleOrDuplicateEntries()
        {
            var assemblies = LoadedPackageAssemblies();
            var present = new HashSet<string>();
            for (var a = 0; a < assemblies.Count; a++)
            {
                foreach (var field in StaticFields(assemblies[a]))
                {
                    present.Add(KeyOf(field));
                }
            }

            var seen = new HashSet<string>();
            var problems = new StringBuilder();
            for (var i = 0; i < Classification.Count; i++)
            {
                var entry = Classification[i];
                if (!seen.Add(entry.Key))
                {
                    problems.Append("  classified twice: ").Append(entry.Key).Append('\n');
                }

                if (entry.Reason.Length == 0)
                {
                    problems.Append("  no reason: ").Append(entry.Key).Append('\n');
                }

                if (IsLoaded(assemblies, entry.Assembly) && !present.Contains(entry.Key))
                {
                    problems.Append("  no such static field any more: ").Append(entry.Key).Append('\n');
                }
            }

            Assert.That(problems.Length, Is.Zero, "the classification and the code disagree:\n" + problems);
        }

        [Test]
        public void SessionStartHooks_AreTheKnownOnes()
        {
            var assemblies = LoadedPackageAssemblies();
            var found = new List<string>();
            for (var a = 0; a < assemblies.Count; a++)
            {
                foreach (var method in SessionStartHooks(assemblies[a]))
                {
                    found.Add(KeyOfMethod(method));
                }
            }

            var expected = new List<string> { "PlayModeBootstrap.OnSubsystemRegistration" };
            if (IsLoaded(assemblies, DOTween))
            {
                expected.Add("TweenCompletionPromise.ResetSession");
            }

            if (IsLoaded(assemblies, Zenject))
            {
                expected.Add("SignalSubscriptions.ResetSession");
            }

            Assert.That(found, Is.EquivalentTo(expected), "a new session start hook must be added to the reset test below, which runs every one of them");
        }

        [Test]
        public void SessionStart_ResetsEveryFieldClassifiedAsReset()
        {
            var assemblies = LoadedPackageAssemblies();
            var previousDefault = LifetimeTree.Default;
            var live = new CancellationTokenSource();
            var stale = new CancellationTokenSource();
            stale.Cancel();
            var kit = new DiagnosticsRecordingKit();
            var tree = new TestTree();
            AutoResetUniTaskCompletionSource source = null;
            try
            {
                PlayModeBootstrap.BeginSession(live.Token);
                var oldTree = LifetimeTree.Default;

                // Dirty every reset field.
                LifetimeErrors.Handler = IgnoreErrors;
                LifetimeDiagnostics.Report(null, "TEST100", "dirty");
                var area = tree.App.CreateChild("area");
                source = AutoResetUniTaskCompletionSource.Create();
                area.Run(source, static (s, ct) => s.Task);
                area.Cancel();
                SetStatic(typeof(LifetimeDiagnostics), "s_failureLogged", 1);
                LifetimeDiagnostics.EnterTerminate(area, "dirty", 7);
                var zenjectType = FindType(assemblies, "SignalSubscriptions");
                object oldTables = null;
                if (zenjectType != null)
                {
                    oldTables = GetStatic(zenjectType, "_tables");
                    SetStatic(zenjectType, "_liveCount", 7);
                    SetStatic(zenjectType, "_arming", new object());
                }

                var promiseType = FindType(assemblies, "TweenCompletionPromise");
                object droppedPromise = null;
                if (promiseType != null)
                {
                    // An await that was armed when the last session ended.
                    droppedPromise = Activator.CreateInstance(promiseType, true);
                    ((Array)GetStatic(promiseType, "_armedAwaits")).SetValue(droppedPromise, 0);
                    SetStatic(promiseType, "_armedAwaitCount", 1);
                    SetInstance(droppedPromise, "_armedAwaitIndex", 0);
                }

                PlayModeBootstrap.HandleQuitting();

                Assert.That(GetStatic(typeof(LifetimeErrors), "_handler"), Is.Not.Null, "premise: the handler is dirty");
                Assert.That(LifetimeDiagnostics.WarningCount, Is.GreaterThan(0), "premise: a warning is recorded");
                Assert.That(TaskOverrunTracker.Count, Is.EqualTo(1), "premise: a task that outlived its generation is tracked");
                Assert.That((bool)GetStatic(typeof(TaskOverrunTracker), "s_scheduled"), Is.True, "premise: the overrun scan is scheduled");
                Assert.That((int)GetStatic(typeof(LifetimeDiagnostics), "s_failureLogged"), Is.EqualTo(1), "premise: a failure was logged");
                Assert.That(GetStatic(typeof(LifetimeDiagnostics), "s_terminatingLifetime"), Is.Not.Null, "premise: a termination is in progress");
                Assert.That((bool)GetStatic(typeof(PlayModeBootstrap), "_exiting"), Is.True, "premise: the quit signal arrived");

                // The session start: the bootstrap with a stale exit token, then every other hook.
                PlayModeBootstrap.BeginSession(stale.Token);
                RunOtherSessionStartHooks(assemblies);

                var problems = new List<string>();
                var verified = new HashSet<string>();
                void Check(string key, bool ok, string message)
                {
                    verified.Add(key);
                    if (!ok)
                    {
                        problems.Add(key + ": " + message);
                    }
                }

                var current = LifetimeTree.Default;
                Check("LifetimeTree.<Default>k__BackingField", current != null && !ReferenceEquals(current, oldTree) && !current.App.IsDisposed && oldTree.App.IsDisposed, "a fresh tree replaced the old one, which was disposed");
                Check("LifetimeErrors._handler", GetStatic(typeof(LifetimeErrors), "_handler") == null, "the handler of the old session is dropped");
                Check("PlayModeBootstrap._sessionTree", ReferenceEquals(GetStatic(typeof(PlayModeBootstrap), "_sessionTree"), current), "the session tree is the new default");
                Check("PlayModeBootstrap._exiting", !(bool)GetStatic(typeof(PlayModeBootstrap), "_exiting"), "the quit flag is cleared");
                Check("PlayModeBootstrap._exitToken", ((CancellationToken)GetStatic(typeof(PlayModeBootstrap), "_exitToken")).Equals(default(CancellationToken)), "a stale exit token is not kept");
                Check("PlayModeBootstrap._exitRegistration", ((CancellationTokenRegistration)GetStatic(typeof(PlayModeBootstrap), "_exitRegistration")).Equals(default(CancellationTokenRegistration)), "the old registration is gone");
                Check("TaskOverrunTracker.Records", TaskOverrunTracker.Count == 0, "tracked tasks of the old session are dropped");
                Check("TaskOverrunTracker.s_scheduled", !(bool)GetStatic(typeof(TaskOverrunTracker), "s_scheduled"), "the scan is not left scheduled");
                Check("LifetimeDiagnostics.WarningList", LifetimeDiagnostics.WarningCount == 0, "recorded warnings of the old session are dropped");
                Check("LifetimeDiagnostics.s_failureLogged", (int)GetStatic(typeof(LifetimeDiagnostics), "s_failureLogged") == 0, "the failure is reported again in the new session");
                Check("LifetimeDiagnostics.s_terminatingLifetime", GetStatic(typeof(LifetimeDiagnostics), "s_terminatingLifetime") == null, "no termination context is left");
                Check("LifetimeDiagnostics.s_terminatingMember", GetStatic(typeof(LifetimeDiagnostics), "s_terminatingMember") == null, "no termination call site is left");
                Check("LifetimeDiagnostics.s_terminatingLine", (int)GetStatic(typeof(LifetimeDiagnostics), "s_terminatingLine") == 0, "no termination line is left");
                if (zenjectType != null)
                {
                    Check("SignalSubscriptions._tables", !ReferenceEquals(GetStatic(zenjectType, "_tables"), oldTables), "the tables of the old session are replaced");
                    Check("SignalSubscriptions._liveCount", (int)GetStatic(zenjectType, "_liveCount") == 0, "the live count restarts");
                    Check("SignalSubscriptions._arming", GetStatic(zenjectType, "_arming") == null, "no handler is left arming");
                }

                if (promiseType != null)
                {
                    Check("TweenCompletionPromise._armedAwaitCount", (int)GetStatic(promiseType, "_armedAwaitCount") == 0, "no armed await crosses into the new session");
                    Check(
                        "TweenCompletionPromise._armedAwaits",
                        ((Array)GetStatic(promiseType, "_armedAwaits")).GetValue(0) == null && (int)GetInstance(droppedPromise, "_armedAwaitIndex") == -1,
                        "the slots are cleared, and a dropped promise forgets its slot");
                }

                Assert.That(problems, Is.Empty, "the session start left state behind:\n" + string.Join("\n", problems));

                var claimed = new List<string>();
                for (var i = 0; i < Classification.Count; i++)
                {
                    if (Classification[i].Reset && IsLoaded(assemblies, Classification[i].Assembly))
                    {
                        claimed.Add(Classification[i].Key);
                    }
                }

                Assert.That(verified, Is.EquivalentTo(claimed), "every reset entry needs a check here, and every check needs a reset entry");
            }
            finally
            {
                try
                {
                    if (source != null)
                    {
                        source.TrySetResult();
                    }
                }
                finally
                {
                    PlayModeBootstrap.EndSession();
                    LifetimeTree.Default = previousDefault;
                    LifetimeErrors.Handler = null;
                    tree.Dispose();
                    kit.Dispose();
                    live.Dispose();
                    stale.Dispose();
                }
            }
        }

        private static void IgnoreErrors(Exception exception, in LifetimeErrorContext context)
        {
        }

        private static List<Classified> BuildClassification()
        {
            var list = new List<Classified>();
            void Allow(string assembly, string reason, params string[] keys)
            {
                for (var i = 0; i < keys.Length; i++)
                {
                    list.Add(new Classified(assembly, keys[i], reason, false));
                }
            }

            void Reset(string assembly, string reason, params string[] keys)
            {
                for (var i = 0; i < keys.Length; i++)
                {
                    list.Add(new Classified(assembly, keys[i], reason, true));
                }
            }

            Allow(
                Core,
                ImmutableDelegate,
                "EntryInvoker.InvokeAction",
                "EntryInvoker.DisposeItem",
                "EntryInvoker`1.Terminate",
                "EntryInvoker`1.PairedTerminate",
                "EntryInvoker`1.IsFinished",
                "EntryInvoker`2.Terminate",
                "OwnedEventEntry.Terminate",
                "UnityEventGuardBase.Terminate",
                "LifetimeTaskEntry.Terminate",
                "LifetimeTaskRunner.InvokeAction",
                "DelayProviders.Default",
                "LifetimeErrors.DefaultHandler",
                "ComponentLifetimes.DisposeOnDestroy",
                "ComponentLifetimes.DisposeOnEitherDestroy",
                "LifetimeCoroutine.Terminate",
                "LifetimeCoroutine.IsFinishedProbe",
                "PlayModeBootstrap.SceneUnloadedHandler",
                "PlayModeBootstrap.QuittingHandler",
                "PlayModeBootstrap.ExitCallback",
                "PlayModeBootstrap.PlayModeStateHandler",
                "TaskOverrunTracker.ScanAction");
            Allow(Core, Pool, "RunContinuation.Pool", "TimerContinuation`1.Pool");
            Allow(Core, WeakMarker, "SceneBinding.DisposedSceneWarned", "SceneBinding.WarnedMarker");
            Allow(Core, ConstantTable, "DiagnosticIds.AllIds", "DiagnosticIds.Titles", "DiagnosticIds.Anchors");
            Allow(Core, Cache, "EntryClassifier.TypeCodes");
            Allow(Core, Scratch, "LifetimeDiagnostics.ScratchSnapshot");
            Allow(Core, Setting, "LifetimeDiagnostics.TrackingEnabled", "LifetimeDiagnostics.CaptureStackTraces", "LifetimeDiagnostics.OverrunFrames");
            Allow(Core, TestSeam, "LifetimeDiagnostics.FrameProvider", "LifetimeDiagnostics.Scheduler");
            Allow(
                Core,
                MonotonicCounter,
                "LifetimeDiagnostics.s_session",
                "LifetimeDiagnostics.s_lastFrame",
                "LifetimeDiagnostics.s_nextId",
                "LifetimeDiagnostics.s_warningSequence",
                "LifetimeDiagnostics.s_warningsVersion",
                "PlayModeBootstrap.<SessionCount>k__BackingField",
                "PlayModeBootstrap.<SceneUnloadedCount>k__BackingField",
                "PlayModeBootstrap.<SceneUnloadedHandlerCount>k__BackingField",
                "PlayModeBootstrap.<QuittingHandlerCount>k__BackingField",
                "PlayModeBootstrap.<PlayModeStateHandlerCount>k__BackingField");
            Allow(
                Core,
                History,
                "LifetimeDiagnostics.RecentRing",
                "LifetimeDiagnostics.s_recentNext",
                "LifetimeDiagnostics.s_recentCount",
                "LifetimeDiagnostics.s_recentVersion");
            Allow(Core, Lock, "LifetimeDiagnostics.WarningsGate");

            Reset(
                Core,
                "the session start (BeginSession) replaces or clears it",
                "LifetimeTree.<Default>k__BackingField",
                "LifetimeErrors._handler",
                "PlayModeBootstrap._sessionTree",
                "PlayModeBootstrap._exiting",
                "PlayModeBootstrap._exitToken",
                "PlayModeBootstrap._exitRegistration",
                "TaskOverrunTracker.Records",
                "TaskOverrunTracker.s_scheduled",
                "LifetimeDiagnostics.WarningList",
                "LifetimeDiagnostics.s_failureLogged",
                "LifetimeDiagnostics.s_terminatingLifetime",
                "LifetimeDiagnostics.s_terminatingMember",
                "LifetimeDiagnostics.s_terminatingLine");

            Allow(DOTween, ImmutableDelegate, "TweenCompletionPromise.TokenCancelCallback", "TweenTermination.KillAction", "TweenTermination.CompleteAction", "TweenTermination.AwaitKillAction", "TweenTermination.IsFinishedProbe", "TweenTermination.AwaitIsFinishedProbe");
            Allow(DOTween, Pool, "TweenCompletionPromise._pool");
            Allow(DOTween, WeakMarker, "TweenTermination.UnkillableReported", "TweenTermination.ReportedMarker");
            Allow(DOTween, ThreadGuard, "TweenTermination._registering", "TweenTermination._completing");
            Reset(DOTween, "its own session start hook (ResetSession) clears it; an await armed in a finished session is dropped", "TweenCompletionPromise._armedAwaits", "TweenCompletionPromise._armedAwaitCount");

            Allow(Zenject, ImmutableDelegate, "SignalOps`1.SubscribeTyped", "SignalOps`1.TerminateTyped", "SignalOps`1.SubscribePlain", "SignalOps`1.TerminatePlain");
            Reset(Zenject, "its own session start hook (ResetSession) replaces or clears it", "SignalSubscriptions._tables", "SignalSubscriptions._liveCount", "SignalSubscriptions._arming");
            return list;
        }

        private static Dictionary<string, Classified> ClassifiedKeys()
        {
            var keys = new Dictionary<string, Classified>();
            for (var i = 0; i < Classification.Count; i++)
            {
                keys[Classification[i].Key] = Classification[i];
            }

            return keys;
        }

        private static List<Assembly> LoadedPackageAssemblies()
        {
            var found = new List<Assembly>();
            var all = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < all.Length; i++)
            {
                var name = all[i].GetName().Name;
                if (name == Core || name == DOTween || name == Zenject)
                {
                    found.Add(all[i]);
                }
            }

            return found;
        }

        private static bool IsLoaded(List<Assembly> assemblies, string name)
        {
            return assemblies.Exists(a => a.GetName().Name == name);
        }

        private static IEnumerable<Type> TypesOf(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                var loaded = new List<Type>();
                for (var i = 0; i < exception.Types.Length; i++)
                {
                    if (exception.Types[i] != null)
                    {
                        loaded.Add(exception.Types[i]);
                    }
                }

                return loaded;
            }
        }

        // Non-const static fields of the assembly's own types. Closures, lambda caches and other compiler-made types are skipped;
        // the backing field of an auto-property is real state and stays.
        private static List<FieldInfo> StaticFields(Assembly assembly)
        {
            var result = new List<FieldInfo>();
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var type in TypesOf(assembly))
            {
                if (type.IsDefined(typeof(CompilerGeneratedAttribute), false))
                {
                    continue;
                }

                var fields = type.GetFields(flags);
                for (var i = 0; i < fields.Length; i++)
                {
                    if (fields[i].IsLiteral || fields[i].Name.StartsWith("<>", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    result.Add(fields[i]);
                }
            }

            return result;
        }

        private static string KeyOf(FieldInfo field)
        {
            return TypeKey(field.DeclaringType) + "." + field.Name;
        }

        private static string KeyOfMethod(MethodInfo method)
        {
            return TypeKey(method.DeclaringType) + "." + method.Name;
        }

        // The type name without its namespace; a generic definition keeps its arity ("EntryInvoker`1"), a nested type its outer ("A+B").
        private static string TypeKey(Type type)
        {
            var name = type.FullName ?? type.Name;
            var prefix = string.IsNullOrEmpty(type.Namespace) ? null : type.Namespace + ".";
            return prefix != null && name.StartsWith(prefix, StringComparison.Ordinal) ? name.Substring(prefix.Length) : name;
        }

        private static IEnumerable<MethodInfo> SessionStartHooks(Assembly assembly)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var type in TypesOf(assembly))
            {
                var methods = type.GetMethods(flags);
                for (var i = 0; i < methods.Length; i++)
                {
                    var attributes = methods[i].GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    for (var a = 0; a < attributes.Length; a++)
                    {
                        if (((RuntimeInitializeOnLoadMethodAttribute)attributes[a]).loadType == RuntimeInitializeLoadType.SubsystemRegistration)
                        {
                            yield return methods[i];
                        }
                    }
                }
            }
        }

        // The bootstrap's own hook reads the engine's exit token; the test calls BeginSession with its own instead.
        private static void RunOtherSessionStartHooks(List<Assembly> assemblies)
        {
            for (var a = 0; a < assemblies.Count; a++)
            {
                foreach (var method in SessionStartHooks(assemblies[a]))
                {
                    if (method.DeclaringType == typeof(PlayModeBootstrap))
                    {
                        continue;
                    }

                    method.Invoke(null, null);
                }
            }
        }

        private static Type FindType(List<Assembly> assemblies, string key)
        {
            for (var a = 0; a < assemblies.Count; a++)
            {
                foreach (var type in TypesOf(assemblies[a]))
                {
                    if (TypeKey(type) == key)
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        private static FieldInfo StaticField(Type type, string name)
        {
            var field = type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Assert.That(field, Is.Not.Null, TypeKey(type) + "." + name + " no longer exists; update this test");
            return field;
        }

        private static object GetStatic(Type type, string name)
        {
            return StaticField(type, name).GetValue(null);
        }

        private static void SetStatic(Type type, string name, object value)
        {
            StaticField(type, name).SetValue(null, value);
        }

        private static FieldInfo InstanceField(object instance, string name)
        {
            var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Assert.That(field, Is.Not.Null, TypeKey(instance.GetType()) + "." + name + " no longer exists; update this test");
            return field;
        }

        private static object GetInstance(object instance, string name)
        {
            return InstanceField(instance, name).GetValue(instance);
        }

        private static void SetInstance(object instance, string name, object value)
        {
            InstanceField(instance, name).SetValue(instance, value);
        }

        private readonly struct Classified
        {
            internal readonly string Assembly;
            internal readonly string Key;
            internal readonly string Reason;
            internal readonly bool Reset;

            internal Classified(string assembly, string key, string reason, bool reset)
            {
                Assembly = assembly;
                Key = key;
                Reason = reason ?? string.Empty;
                Reset = reset;
            }
        }
    }
}
