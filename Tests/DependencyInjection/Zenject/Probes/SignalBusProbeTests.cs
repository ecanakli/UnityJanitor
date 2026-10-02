using System;
using NUnit.Framework;
using Zenject;

namespace Ecanakli.Janitor.DependencyInjection.Tests.Probes
{
    // Pins SignalBus behaviour the Zenject integration relies on.
    public sealed class SignalBusProbeTests : ZenjectUnitTestFixture
    {
        public sealed class ProbeSignal
        {
        }

        [Test]
        public void EngineProbe_14c_SignalBus_UnsubscribingCurrentHandlerDuringFire_IsSafe()
        {
            SignalBusInstaller.Install(Container);
            Container.DeclareSignal<ProbeSignal>();
            var bus = Container.Resolve<SignalBus>();
            int first = 0;
            int second = 0;
            Action<ProbeSignal> firstHandler = null;
            firstHandler = _ =>
            {
                first++;
                bus.TryUnsubscribe(firstHandler);
            };
            bus.Subscribe(firstHandler);
            bus.Subscribe<ProbeSignal>(_ => second++);

            Assert.DoesNotThrow(() => bus.Fire(new ProbeSignal()));
            bus.Fire(new ProbeSignal());

            Assert.That(first, Is.EqualTo(1));
            Assert.That(second, Is.EqualTo(2));
        }

        [Test]
        public void EngineProbe_14d_SignalBus_UnsubscribingLaterHandlerDuringFire()
        {
            SignalBusInstaller.Install(Container);
            Container.DeclareSignal<ProbeSignal>();
            var bus = Container.Resolve<SignalBus>();
            int later = 0;
            Action<ProbeSignal> laterHandler = _ => later++;
            bus.Subscribe<ProbeSignal>(_ => bus.TryUnsubscribe(laterHandler));
            bus.Subscribe(laterHandler);

            bus.Fire(new ProbeSignal());

            Assert.That(later, Is.Zero, "a later handler unsubscribed during Fire does not run: the removal takes effect at once");

            bus.Fire(new ProbeSignal());

            Assert.That(later, Is.Zero, "and it stays removed");
        }
    }
}
