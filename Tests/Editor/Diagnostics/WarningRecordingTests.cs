using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // The warning rules the core detects without Unity objects: 105 (OwnedEvent), 106, 107, 111 and 115.
    // Each is recorded once per cause (a repeat only raises the counter), attached to the right lifetime, and still logged where it
    // logged before. The Unity-object rules (103, 104, 108-110, 113, 114) are in the PlayMode diagnostics tests.
    [TestFixture]
    public sealed class WarningRecordingTests
    {
        private static readonly Regex MarshalWarning = new Regex("JANITOR111");

        private DiagnosticsRecordingKit _d;
        private TestScope _t;
        private WarningSpy _spy;

        [SetUp]
        public void SetUp()
        {
            _d = new DiagnosticsRecordingKit();
            _t = new TestScope();
            _t.Tree.MakeDefault();
            _spy = new WarningSpy();
        }

        [TearDown]
        public void TearDown()
        {
            _spy.Dispose();
            try
            {
                _t.Complete();
            }
            finally
            {
                _d.Dispose();
            }
        }

        // 107

        [Test]
        public void Dispose_OnApp_RecordsJanitor107OnceAttachedToAppAndMergesRepeats()
        {
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR107"));

            _t.App.Dispose();

            var warning = _d.Single(DiagnosticIds.DisposeIgnored);
            Assert.That(warning.Lifetime, Is.SameAs(_t.App));
            Assert.That(warning.LifetimeLabel, Is.EqualTo("App"));
            Assert.That(warning.Message, Does.Contain("Dispose() was ignored on the package-owned lifetime 'App'"));
            Assert.That(warning.Count, Is.EqualTo(1));
            Assert.That(warning.IsInfo, Is.False);
            Assert.That(warning.FirstFrame, Is.EqualTo(_d.Frame));

            _d.AdvanceFrames(5);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR107"));
            _t.App.Dispose();

            var again = _d.Single(DiagnosticIds.DisposeIgnored);
            Assert.That(again.Count, Is.EqualTo(2), "the same cause is one entry with a counter");
            Assert.That(again.FirstFrame, Is.EqualTo(100));
            Assert.That(again.LastFrame, Is.EqualTo(105));
        }

        [Test]
        public void Dispose_OnApp_TheConsoleTextAndTheRecordedMessageAreTheSame()
        {
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR107"));

            _t.App.Dispose();

            var warning = _d.Single(DiagnosticIds.DisposeIgnored);
            Assert.That(_spy.Count(DiagnosticIds.DisposeIgnored), Is.EqualTo(1));
            Assert.That(_spy.TextOf(DiagnosticIds.DisposeIgnored), Is.EqualTo(DevWarnings.Format(DiagnosticIds.DisposeIgnored, warning.Message)));
        }

        // 105

        [Test]
        public void Subscribe_ADuplicateOnAnOwnedEvent_RecordsJanitor105AttachedToTheOwner()
        {
            var owner = _t.App.CreateChild("owner");
            var coins = new OwnedEvent<int>("Coins");
            Action<int> handler = _ => { };
            coins.Subscribe(handler, owner);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));

            coins.Subscribe(handler, owner);

            var warning = _d.Single(DiagnosticIds.DuplicateSubscription);
            Assert.That(warning.Lifetime, Is.SameAs(owner));
            Assert.That(warning.LifetimeLabel, Is.EqualTo("owner"));
            Assert.That(warning.Message, Does.Contain("OwnedEvent<Int32> \"Coins\"").And.Contain("'owner'"));
            Assert.That(warning.Count, Is.EqualTo(1));
            Assert.That(_spy.TextOf(DiagnosticIds.DuplicateSubscription), Is.EqualTo(DevWarnings.Format(DiagnosticIds.DuplicateSubscription, warning.Message)));
        }

        [Test]
        public void Subscribe_ADuplicateAttemptedAgain_RaisesTheCounterNotTheNumberOfEntries()
        {
            var owner = _t.App.CreateChild("owner");
            var coins = new OwnedEvent<int>("Coins");
            Action<int> handler = _ => { };
            coins.Subscribe(handler, owner);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));

            coins.Subscribe(handler, owner);
            _d.AdvanceFrames(3);
            coins.Subscribe(handler, owner);
            coins.Subscribe(handler, owner);

            var warning = _d.Single(DiagnosticIds.DuplicateSubscription);
            Assert.That(warning.Count, Is.EqualTo(3));
            Assert.That(warning.LastFrame - warning.FirstFrame, Is.EqualTo(3));
        }

        [Test]
        public void Subscribe_TheSameHandlerUnderAnotherOwner_IsNotADuplicateAndRecordsNothing()
        {
            var first = _t.App.CreateChild("first");
            var second = _t.App.CreateChild("second");
            var coins = new OwnedEvent<int>("Coins");
            Action<int> handler = _ => { };

            coins.Subscribe(handler, first);
            coins.Subscribe(handler, second);

            Assert.That(_d.Count(DiagnosticIds.DuplicateSubscription), Is.Zero);
        }

        // 106

        [Test]
        public void Register_Beyond256Entries_RecordsJanitor106OnceForThatLifetime()
        {
            var area = _t.App.CreateChild("crowded");
            var counter = new Counter();
            for (var i = 0; i < LifetimeDiagnostics.GrowthEntryLimit; i++)
            {
                area.OnCancel(counter, static c => c.Value++);
            }

            Assert.That(_d.Count(DiagnosticIds.Growth), Is.Zero, "256 entries is the limit, not past it");

            area.OnCancel(counter, static c => c.Value++);

            var warning = _d.Single(DiagnosticIds.Growth);
            Assert.That(warning.Lifetime, Is.SameAs(area));
            Assert.That(warning.LifetimeLabel, Is.EqualTo("crowded"));
            Assert.That(warning.Message, Does.Contain("257 entries").And.Contain("limit is 256"));
            Assert.That(warning.IsInfo, Is.False);

            for (var i = 0; i < 50; i++)
            {
                area.OnCancel(counter, static c => c.Value++);
            }

            Assert.That(_d.Single(DiagnosticIds.Growth).Count, Is.EqualTo(1), "once per lifetime, not once per registration");

            area.Cancel();
            for (var i = 0; i < 300; i++)
            {
                area.OnCancel(counter, static c => c.Value++);
            }

            Assert.That(_d.Single(DiagnosticIds.Growth).Count, Is.EqualTo(1), "a later generation of the same lifetime does not warn again");
        }

        [Test]
        public void Register_Beyond256EntriesOnApp_IsNotExempt()
        {
            var counter = new Counter();
            for (var i = 0; i <= LifetimeDiagnostics.GrowthEntryLimit; i++)
            {
                _t.App.OnCancel(counter, static c => c.Value++);
            }

            var warning = _d.Single(DiagnosticIds.Growth);

            Assert.That(warning.Lifetime, Is.SameAs(_t.App));
        }

        [Test]
        public void CreateChild_Beyond64LiveChildren_RecordsJanitor106OnceForThatLifetime()
        {
            var area = _t.App.CreateChild("parent");
            for (var i = 0; i < LifetimeDiagnostics.GrowthChildLimit; i++)
            {
                area.CreateChild();
            }

            Assert.That(_d.Count(DiagnosticIds.Growth), Is.Zero, "64 children is the limit, not past it");

            area.CreateChild();

            var warning = _d.Single(DiagnosticIds.Growth);
            Assert.That(warning.Lifetime, Is.SameAs(area));
            Assert.That(warning.Message, Does.Contain("65 live children").And.Contain("limit is 64"));

            for (var i = 0; i < 20; i++)
            {
                area.CreateChild();
            }

            Assert.That(_d.Single(DiagnosticIds.Growth).Count, Is.EqualTo(1), "once per lifetime");
        }

        [Test]
        public void CreateChild_DisposedChildren_DoNotCountTowardsTheLimit()
        {
            var area = _t.App.CreateChild("parent");
            for (var i = 0; i < 200; i++)
            {
                area.CreateChild().Dispose();
            }

            Assert.That(_d.Count(DiagnosticIds.Growth), Is.Zero, "only live children are counted");
        }

        [Test]
        public void CreateChildCore_ObjectLifetimesBeyond64OnAppOrAScene_AreExempt()
        {
            var scene = _t.App.CreateChildCore("Scene", LifetimeKind.Scene, true, null, null, 0);

            for (var i = 0; i < 70; i++)
            {
                _t.App.CreateChildCore("object" + i, LifetimeKind.GameObject, true, null, null, 0);
                scene.CreateChildCore("object" + i, LifetimeKind.Component, true, null, null, 0);
            }

            Assert.That(_t.App.ChildCount, Is.GreaterThan(LifetimeDiagnostics.GrowthChildLimit), "premise: App is past the limit");
            Assert.That(scene.ChildCount, Is.GreaterThan(LifetimeDiagnostics.GrowthChildLimit), "premise: the scene is past the limit");
            Assert.That(_d.Count(DiagnosticIds.Growth), Is.Zero, "every object lifetime of a scene is its child by design");
        }

        [Test]
        public void CreateChild_Beyond64LiveAreasOnASceneOrApp_RecordsJanitor106ForEach()
        {
            var scene = _t.App.CreateChildCore("Scene", LifetimeKind.Scene, true, null, null, 0);

            for (var i = 0; i <= LifetimeDiagnostics.GrowthChildLimit; i++)
            {
                scene.CreateChild();
            }

            Assert.That(_d.Single(DiagnosticIds.Growth).Lifetime, Is.SameAs(scene), "a scene lifetime is no longer exempt for areas");

            for (var i = 0; i <= LifetimeDiagnostics.GrowthChildLimit; i++)
            {
                _t.App.CreateChild();
            }

            var rows = _d.Warnings(DiagnosticIds.Growth);
            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(rows[1].Lifetime, Is.SameAs(_t.App));
        }

        [Test]
        public void CreateChildCore_100ObjectLifetimesInOneCategory_RecordsNothing()
        {
            var combat = _t.App.CreateChild("Combat");

            for (var i = 0; i < 100; i++)
            {
                combat.CreateChildCore("unit" + i, LifetimeKind.Component, true, null, null, 0);
            }

            Assert.That(combat.ChildCount, Is.EqualTo(100), "premise: the category holds 100 placed object lifetimes");
            Assert.That(_d.Count(DiagnosticIds.Growth), Is.Zero, "placed object lifetimes are legitimate children, not undisposed areas");
        }

        [Test]
        public void CreateChild_AreasBelowTheLimitBesideManyObjectLifetimes_RecordsNothing()
        {
            var combat = _t.App.CreateChild("Combat");
            for (var i = 0; i < 100; i++)
            {
                combat.CreateChildCore("unit" + i, LifetimeKind.Component, true, null, null, 0);
            }

            for (var i = 0; i < LifetimeDiagnostics.GrowthChildLimit; i++)
            {
                combat.CreateChild();
            }

            Assert.That(combat.ChildCount, Is.EqualTo(100 + LifetimeDiagnostics.GrowthChildLimit));
            Assert.That(_d.Count(DiagnosticIds.Growth), Is.Zero, "64 areas is the limit, whatever else the lifetime holds");
        }

        [Test]
        public void CreateChild_MoreThanTheLimitOfAreasBesideManyObjectLifetimes_StillRecordsJanitor106()
        {
            var combat = _t.App.CreateChild("Combat");
            for (var i = 0; i < 100; i++)
            {
                combat.CreateChildCore("unit" + i, LifetimeKind.Component, true, null, null, 0);
            }

            for (var i = 0; i <= LifetimeDiagnostics.GrowthChildLimit; i++)
            {
                combat.CreateChild();
            }

            var warning = _d.Single(DiagnosticIds.Growth);
            Assert.That(warning.Lifetime, Is.SameAs(combat));
            Assert.That(warning.Message, Does.Contain("65 live children that are areas").And.Contain("limit is 64"));
        }

        [Test]
        public void CreateChild_AreasThatAreDisposedAgainBesideManyObjectLifetimes_RecordNothing()
        {
            var scene = _t.App.CreateChildCore("Scene", LifetimeKind.Scene, true, null, null, 0);
            for (var i = 0; i < 100; i++)
            {
                scene.CreateChildCore("unit" + i, LifetimeKind.Component, true, null, null, 0);
            }

            for (var i = 0; i < 300; i++)
            {
                scene.CreateChild().Dispose();
            }

            Assert.That(_d.Count(DiagnosticIds.Growth), Is.Zero, "only live areas are counted");
        }

        [Test]
        public void CreateChildCore_InjectedKindBeyond64LiveAreas_CountsLikeAnArea()
        {
            var scene = _t.App.CreateChildCore("Scene", LifetimeKind.Scene, true, null, null, 0);

            for (var i = 0; i <= LifetimeDiagnostics.GrowthChildLimit; i++)
            {
                scene.CreateChildCore("service" + i, LifetimeKind.Injected, false, null, null, 0);
            }

            var warning = _d.Single(DiagnosticIds.Growth);
            Assert.That(warning.Lifetime, Is.SameAs(scene));
            Assert.That(warning.Message, Does.Contain("65 live children that are areas"));
        }

        [Test]
        public void CreateChild_AreasOfBothKindsTogether_AreCountedTogether()
        {
            var scene = _t.App.CreateChildCore("Scene", LifetimeKind.Scene, true, null, null, 0);

            for (var i = 0; i < 32; i++)
            {
                scene.CreateChild();
                scene.CreateChildCore("service" + i, LifetimeKind.Injected, false, null, null, 0);
            }

            Assert.That(_d.Count(DiagnosticIds.Growth), Is.Zero, "64 areas in all is the limit, not past it");

            scene.CreateChild();

            Assert.That(_d.Single(DiagnosticIds.Growth).Lifetime, Is.SameAs(scene));
        }

        [Test]
        public void CreateChild_Beyond64ChildrenOnAnObjectLifetime_IsNotExempt()
        {
            var component = _t.App.CreateChildCore("Component", LifetimeKind.Component, true, null, null, 0);

            for (var i = 0; i <= LifetimeDiagnostics.GrowthChildLimit; i++)
            {
                component.CreateChild();
            }

            Assert.That(_d.Single(DiagnosticIds.Growth).Lifetime, Is.SameAs(component));
        }

        [Test]
        public void Growth_EntriesAndChildrenOnOneLifetime_AreTwoSeparateRecords()
        {
            var area = _t.App.CreateChild("both");
            var counter = new Counter();
            for (var i = 0; i <= LifetimeDiagnostics.GrowthEntryLimit; i++)
            {
                area.OnCancel(counter, static c => c.Value++);
            }

            for (var i = 0; i <= LifetimeDiagnostics.GrowthChildLimit; i++)
            {
                area.CreateChild();
            }

            var rows = _d.Warnings(DiagnosticIds.Growth);
            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(rows[0].Message, Does.Contain("entries"));
            Assert.That(rows[1].Message, Does.Contain("children"));
        }

        // 111

        [Test]
        public void Cancel_OffTheMainThread_RecordsJanitor111AttachedToTheLifetime()
        {
            var area = _t.App.CreateChild("area");
            const string Message = "Lifetime.Cancel() was called off the main thread and is marshalled to the next main-thread tick.";

            // The console warning comes from the worker thread, which Application.logMessageReceived (the WarningSpy) never sees,
            // so the exact console text is pinned through LogAssert, which does.
            LogAssert.Expect(LogType.Warning, DevWarnings.Format(DiagnosticIds.Marshalled, Message));

            var failure = ThreadRunner.Run(() => area.Cancel());
            _t.Tree.RunPostedDrains();

            Assert.That(failure, Is.Null);
            var warning = _d.Single(DiagnosticIds.Marshalled);
            Assert.That(warning.Lifetime, Is.SameAs(area));
            Assert.That(warning.Message, Is.EqualTo(Message), "the record holds the same text as the console, without the id wrapper");
            Assert.That(warning.FirstFrame, Is.EqualTo(_d.Frame));
        }

        [Test]
        public void Dispose_OffTheMainThread_RecordsJanitor111AsItsOwnEntry()
        {
            var area = _t.App.CreateChild("area");
            LogAssert.Expect(LogType.Warning, MarshalWarning);
            LogAssert.Expect(LogType.Warning, MarshalWarning);

            ThreadRunner.Run(() => area.Cancel());
            var failure = ThreadRunner.Run(() => area.Dispose());
            _t.Tree.RunPostedDrains();

            Assert.That(failure, Is.Null);
            var rows = _d.Warnings(DiagnosticIds.Marshalled);
            Assert.That(rows.Count, Is.EqualTo(2), "Cancel and Dispose are different causes");
            Assert.That(rows[1].Message, Does.Contain("Lifetime.Dispose()"));
            Assert.That(rows[1].Lifetime, Is.SameAs(area));
        }

        [Test]
        public void RegistrationCancel_OffTheMainThread_RecordsJanitor111AttachedToTheOwnerLifetime()
        {
            var area = _t.App.CreateChild("area");
            var registration = area.OnCancel(() => { });
            LogAssert.Expect(LogType.Warning, MarshalWarning);

            var failure = ThreadRunner.Run(() => registration.Cancel());
            _t.Tree.RunPostedDrains();

            Assert.That(failure, Is.Null);
            var warning = _d.Single(DiagnosticIds.Marshalled);
            Assert.That(warning.Lifetime, Is.SameAs(area));
            Assert.That(warning.Message, Does.Contain("LifetimeRegistration.Cancel()"));
        }

        [Test]
        public void Cancel_OffTheMainThreadRepeatedly_RaisesTheCounterOfOneEntry()
        {
            var area = _t.App.CreateChild("area");
            LogAssert.Expect(LogType.Warning, MarshalWarning);
            LogAssert.Expect(LogType.Warning, MarshalWarning);
            LogAssert.Expect(LogType.Warning, MarshalWarning);

            ThreadRunner.Run(() => area.Cancel());
            ThreadRunner.Run(() => area.Cancel());
            ThreadRunner.Run(() => area.Cancel());
            _t.Tree.RunPostedDrains();

            Assert.That(_d.Single(DiagnosticIds.Marshalled).Count, Is.EqualTo(3));
        }

        [Test]
        public void Cancel_OffTheMainThreadWithTheRealFrameCounter_RecordsTheLastFrameSeenOnTheMainThread()
        {
            _d.Dispose();
            _d = new DiagnosticsRecordingKit(manualFrames: false);
            var area = _t.App.CreateChild("area");
            var mainFrame = LifetimeDiagnostics.CurrentFrame();
            LogAssert.Expect(LogType.Warning, MarshalWarning);

            var failure = ThreadRunner.Run(() => area.Cancel());
            _t.Tree.RunPostedDrains();

            Assert.That(failure, Is.Null, "Time.frameCount is main-thread only; the record must not touch it off the main thread");
            Assert.That(_d.Single(DiagnosticIds.Marshalled).FirstFrame, Is.EqualTo(mainFrame));
        }

        // 115

        [Test]
        public void Invoke_PastTheDepthLimit_RecordsJanitor115UnattachedAndStillRoutesTheError()
        {
            var owner = _t.App.CreateChild("owner");
            var coins = new OwnedEvent<int>("Coins");
            coins.Subscribe(v => coins.Invoke(v + 1), owner);

            coins.Invoke(0);

            var warning = _d.Single(DiagnosticIds.DepthExceeded);
            Assert.That(warning.Lifetime, Is.Null, "the event is not tied to one lifetime");
            Assert.That(warning.LifetimeLabel, Is.Null);
            Assert.That(warning.Message, Does.Contain("Re-entrancy depth exceeded 64").And.Contain("OwnedEvent<Int32> \"Coins\""));
            Assert.That(warning.Count, Is.EqualTo(1));
            Assert.That(_t.Errors.Count, Is.EqualTo(1), "it is routed through the error handler in every build");
            Assert.That(_t.Errors[0].Exception.Message, Is.EqualTo(DevWarnings.Format(DiagnosticIds.DepthExceeded, warning.Message)));
            _t.Errors.Clear();
        }

        [Test]
        public void Invoke_PastTheDepthLimitTwice_RaisesTheCounterOfOneEntry()
        {
            var owner = _t.App.CreateChild("owner");
            var coins = new OwnedEvent<int>("Coins");
            coins.Subscribe(v => coins.Invoke(v + 1), owner);

            coins.Invoke(0);
            coins.Invoke(0);

            Assert.That(_d.Single(DiagnosticIds.DepthExceeded).Count, Is.EqualTo(2));
            _t.Errors.Clear();
        }

        [Test]
        public void Invoke_WithinTheDepthLimit_RecordsNothing()
        {
            var owner = _t.App.CreateChild("owner");
            var coins = new OwnedEvent<int>("Coins");
            var depth = 0;
            coins.Subscribe(v =>
            {
                if (++depth < 10)
                {
                    coins.Invoke(v + 1);
                }
            }, owner);

            coins.Invoke(0);

            Assert.That(_d.Total, Is.Zero);
        }
    }
}
