using System;
using System.Collections.Generic;
using Ecanakli.Janitor.Tests;
using NUnit.Framework;
using Zenject;

namespace Ecanakli.Janitor.DependencyInjection.Tests
{
    // A SignalBus on the fixture's container, an area as the owner and a spy for the JANITOR105 warnings. TearDown ends
    // every lifetime and checks that the subscription table is empty: each termination path must have forgotten its record.
    public abstract class SignalBusFixtureBase : ZenjectUnitTestFixture
    {
        protected TestScope Scope { get; private set; }

        protected Lifetime Area { get; private set; }

        protected SignalBus Bus { get; private set; }

        protected WarningSpy Warnings { get; private set; }

        // The values of the TestSignal instances the typed handler received, in order.
        protected List<int> Received { get; private set; }

        // How often the plain handler ran.
        protected int PlainHits { get; set; }

        // Stable delegate instances, so a test can subscribe "the same handler" by reference.
        protected Action<TestSignal> Typed { get; private set; }

        protected Action Plain { get; private set; }

        [SetUp]
        public void SetUpSignalBus()
        {
            Scope = new TestScope();
            Scope.Tree.MakeDefault();
            SignalSubscriptions.ResetSession();
            Warnings = new WarningSpy();
            Received = new List<int>();
            PlainHits = 0;
            Typed = signal => Received.Add(signal.Value);
            Plain = () => PlainHits++;
            Bus = NewBus(Container, false);
            Area = Scope.App.CreateChild("area");
        }

        [TearDown]
        public void TearDownSignalBus()
        {
            try
            {
                Scope.Tree.Shutdown();
                Assert.That(SignalSubscriptions.LiveCount, Is.Zero, "after the whole tree is disposed no subscription record may remain");
            }
            finally
            {
                Warnings.Dispose();
                Scope.Complete();
            }
        }

        // SignalBus with TestSignal and OtherSignal declared. A missing handler is not an error, so a fire after a cancel stays quiet.
        protected static SignalBus NewBus(DiContainer container, bool strictUnsubscribe)
        {
            if (strictUnsubscribe)
            {
                var signals = new ZenjectSettings.SignalSettings(SignalDefaultSyncModes.Synchronous, SignalMissingHandlerResponses.Ignore, requireStrictUnsubscribe: true);
                container.Settings = new ZenjectSettings(ValidationErrorResponses.Log, signalSettings: signals);
            }

            SignalBusInstaller.Install(container);
            container.DeclareSignal<TestSignal>().OptionalSubscriber();
            container.DeclareSignal<OtherSignal>().OptionalSubscriber();
            return container.Resolve<SignalBus>();
        }

        protected void FireTyped(int value)
        {
            Bus.Fire(new TestSignal { Value = value });
        }

        // Method-group sources for "an equal handler from a fresh delegate".
        protected void OnTyped(TestSignal signal)
        {
            Received.Add(signal.Value);
        }

        protected void OnPlain()
        {
            PlainHits++;
        }
    }
}
