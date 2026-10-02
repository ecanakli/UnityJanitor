using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests
{
    // The window labels a paired Subscribe by its handler type; an OnCancel whose state is a delegate is still an OnCancel.
    [TestFixture]
    public sealed class PairedEntryLabelTests
    {
        private DiagnosticsRecordingKit _d;
        private TestScope _t;
        private Lifetime _area;

        [SetUp]
        public void SetUp()
        {
            _d = new DiagnosticsRecordingKit();
            _t = new TestScope();
            _area = _t.App.CreateChild("area");
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                _t.Complete();
            }
            finally
            {
                _d.Dispose();
            }
        }

        [Test]
        public void Entries_APairedSubscribeIsLabelledPaired_AndAnOnCancelWithADelegateStateIsNot()
        {
            Action handler = () => { };
            _area.Subscribe(h => { }, h => { }, handler);
            _area.OnCancel(handler, static h => { });
            _area.OnCancel(handler, static h => { }, static h => false);
            _area.OnCancel(handler, handler, static (a, b) => { });
            var entries = new List<EntryView>();

            LifetimeDiagnostics.CaptureEntries(_area, entries);

            Assert.That(entries.Count, Is.EqualTo(4));
            Assert.That(entries[0].FormatLabel(), Is.EqualTo("OnCancel<Action, Action>"), "newest first");
            Assert.That(entries[1].FormatLabel(), Is.EqualTo("OnCancel<Action>"), "the form with a probe");
            Assert.That(entries[2].FormatLabel(), Is.EqualTo("OnCancel<Action>"));
            Assert.That(entries[3].FormatLabel(), Is.EqualTo("Paired<Action>"));
            Assert.That(entries[0].Kind, Is.EqualTo(EntryKind.Other));
            Assert.That(entries[1].Kind, Is.EqualTo(EntryKind.Other));
            Assert.That(entries[2].Kind, Is.EqualTo(EntryKind.Other));
            Assert.That(entries[3].Kind, Is.EqualTo(EntryKind.Subscription));
        }

        [Test]
        public void Entries_APairedSubscribeWithAnArgument_IsLabelledPaired()
        {
            Action<int> handler = value => { };
            _area.Subscribe<int>(h => { }, h => { }, handler);
            _area.OnCancel(handler, static h => { });
            var entries = new List<EntryView>();

            LifetimeDiagnostics.CaptureEntries(_area, entries);

            Assert.That(entries.Count, Is.EqualTo(2));
            Assert.That(entries[0].FormatLabel(), Is.EqualTo("OnCancel<Action<Int32>>"));
            Assert.That(entries[1].FormatLabel(), Is.EqualTo("Paired<Action<Int32>>"));
        }
    }
}
