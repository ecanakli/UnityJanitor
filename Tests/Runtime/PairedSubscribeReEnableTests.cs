using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.Binding
{
    // A paired Subscribe made in OnEnable: on the component lifetime a re-enable is a repeat, on the active lifetime it is not.
    [TestFixture]
    public sealed class PairedSubscribeReEnableTests
    {
        private BindingSession _s;

        [SetUp]
        public void SetUp()
        {
            _s = BindingSession.Begin();
        }

        [TearDown]
        public void TearDown()
        {
            _s.Complete();
        }

        [Test]
        public void ComponentLifetime_SubscribedInOnEnable_AReEnableIsIgnoredAsARepeat_AndWarns()
        {
            var source = new BindingEventSource();
            var hits = 0;
            Action handler = () => hits++;
            BindingProbe.EnableHook = p => p.Subscribe(h => source.Zero += h, h => source.Zero -= h, handler);
            var probe = _s.NewProbe();
            Assert.That(source.Adds, Is.EqualTo(1), "OnEnable subscribed");
            probe.gameObject.SetActive(false);
            Assert.That(source.Removes, Is.Zero, "the component lifetime survives a deactivation");
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));

            probe.gameObject.SetActive(true);

            Assert.That(source.Adds, Is.EqualTo(1), "the second OnEnable did not subscribe again");
            Assert.That(source.ZeroListeners, Is.EqualTo(1));
            source.RaiseZero();
            Assert.That(hits, Is.EqualTo(1), "one delivery, not one per enable");
            Assert.That(_s.Logs.Count("JANITOR105"), Is.EqualTo(1));
        }

        [Test]
        public void ActiveLifetime_SubscribedInOnEnable_EveryReEnableSubscribesAgain_WithoutAWarning()
        {
            var source = new BindingEventSource();
            var hits = 0;
            Action handler = () => hits++;
            BindingProbe.EnableHook = p => p.GetActiveLifetime().Subscribe(h => source.Zero += h, h => source.Zero -= h, handler);
            var probe = _s.NewProbe();

            for (var cycle = 0; cycle < 3; cycle++)
            {
                probe.gameObject.SetActive(false);
                Assert.That(source.ZeroListeners, Is.Zero, "the deactivation removed it");

                probe.gameObject.SetActive(true);
                Assert.That(source.ZeroListeners, Is.EqualTo(1), "one listener per enabled period");
            }

            source.RaiseZero();
            Assert.That(hits, Is.EqualTo(1));
            Assert.That(source.Adds, Is.EqualTo(4));
            Assert.That(_s.Logs.Count("JANITOR105"), Is.Zero);
        }
    }
}
