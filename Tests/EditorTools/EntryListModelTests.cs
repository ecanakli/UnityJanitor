using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Ecanakli.Janitor.EditorTools.Tests
{
    // The details pane's entry list, read from a real lifetime through LifetimeDiagnostics.CaptureEntries.
    [TestFixture]
    public sealed class EntryListModelTests : DiagnosticsTestBase
    {
        private EntryListModel _model;
        private Lifetime _area;

        [SetUp]
        public void SetUp()
        {
            _model = new EntryListModel();
            _area = Scope.App.CreateChild("Named");
        }

        // Contents and order

        [Test]
        public void Update_EntriesOfEveryKind_ListsThemNewestFirstWithTheirLabels()
        {
            Scope.Tree.UseManualClock();
            var events = new OwnedEvent<int>("Coins");
            var source = new PairedSource();
            _area.OnCancel(() => { });
            _area.After(10f, () => { });
            events.Subscribe(_ => { }, _area);
            _area.Subscribe(h => source.Raised += h, h => source.Raised -= h, () => { });
            new Probe().AddTo(_area);

            _model.Update(_area);

            Assert.That(Labels(), Is.EqualTo(new[]
            {
                "IDisposable Probe",
                "Paired<Action>",
                "OwnedEvent<Int32> \"Coins\"",
                "Task.After",
                "OnCancel",
            }));
        }

        [Test]
        public void Update_OwnedEventsOfEveryArity_ShowTheirTypeAndName()
        {
            new OwnedEvent("Plain").Subscribe(() => { }, _area);
            new OwnedEvent<int>().Subscribe(_ => { }, _area);
            new OwnedEvent<int, string>("Pair").Subscribe((a, b) => { }, _area);
            new OwnedEvent<int, string, bool>("Triple").Subscribe((a, b, c) => { }, _area);

            _model.Update(_area);

            Assert.That(Labels(), Is.EqualTo(new[]
            {
                "OwnedEvent<Int32, String, Boolean> \"Triple\"",
                "OwnedEvent<Int32, String> \"Pair\"",
                "OwnedEvent<Int32>",
                "OwnedEvent \"Plain\"",
            }));
        }

        [Test]
        public void Update_Entry_ShowsTheRegistrationCallSite()
        {
            _area.OnCancel(() => { });

            _model.Update(_area);

            Assert.That(_model.Rows[0].SiteText, Does.Match("^" + nameof(Update_Entry_ShowsTheRegistrationCallSite) + @":\d+$"));
        }

        [Test]
        public void Update_Entry_ShowsGenerationStatusAndAge()
        {
            _area.OnCancel(() => { });
            _model.Update(_area);
            var row = _model.Rows[0];

            Assert.That(row.GenerationText, Is.EqualTo(_area.Generation.ToString()));
            Assert.That(row.StatusText, Is.EqualTo("Live"));
            Assert.That(row.IsOutlived, Is.False);
            Assert.That(row.AgeText, Is.EqualTo("0"));

            Frame = StartFrame + 30;
            _model.Update(_area);

            Assert.That(row.AgeText, Is.EqualTo("30"));
        }

        [Test]
        public void Update_TrackingWasOffAtRegistration_ShowsAnUnknownAge()
        {
            LifetimeDiagnostics.TrackingEnabled = false;
            _area.OnCancel(() => { });
            LifetimeDiagnostics.TrackingEnabled = true;

            _model.Update(_area);

            Assert.That(_model.Rows[0].AgeText, Is.EqualTo(CountText.Unknown));
        }

        [Test]
        public void Update_NullLifetime_EmptiesTheList()
        {
            _area.OnCancel(() => { });
            _model.Update(_area);

            _model.Update(null);

            Assert.That(_model.Rows, Is.Empty);
        }

        [Test]
        public void Update_UnchangedEntries_ReusesTheRowObjectsAndTheirLabels()
        {
            var source = new PairedSource();
            _area.Subscribe(h => source.Raised += h, h => source.Raised -= h, () => { });
            _model.Update(_area);
            var row = _model.Rows[0];
            var label = row.ItemText;

            _model.Update(_area);

            Assert.That(_model.Rows[0], Is.SameAs(row));
            Assert.That(row.ItemText, Is.SameAs(label), "a paired label is built by concatenation; an unchanged entry must not build it again");
        }

        [Test]
        public void Update_ListShrinksThenGrows_ReusesTheReleasedRows()
        {
            var first = _area.OnCancel(() => { });
            _area.OnCancel(() => { });
            _model.Update(_area);
            var known = new List<EntryRow>(_model.Rows);

            first.Cancel();
            _model.Update(_area);
            _area.OnCancel(() => { });
            _model.Update(_area);

            Assert.That(_model.Rows.Count, Is.EqualTo(2));
            Assert.That(known, Does.Contain(_model.Rows[0]));
            Assert.That(known, Does.Contain(_model.Rows[1]));
        }

        [Test]
        public void Clear_AfterAnUpdate_EmptiesTheListAndTheSelection()
        {
            _area.OnCancel(() => { });
            _model.Update(_area);
            _model.SelectIndex(0);

            _model.Clear();

            Assert.That(_model.Rows, Is.Empty);
            Assert.That(_model.SelectedIndex, Is.EqualTo(-1));
            Assert.That(_model.TraceText, Is.Null);
        }

        // Outlived tasks

        [Test]
        public void Update_TaskThatOutlivedItsGeneration_IsListedAsOutlivedUntilItFinishes()
        {
            var source = new UniTaskCompletionSource();
            _area.Run(ct => source.Task);
            _area.Cancel();

            Frame = StartFrame + 5;
            _model.Update(_area);

            Assert.That(_model.Rows.Count, Is.EqualTo(1));
            Assert.That(_model.Rows[0].ItemText, Is.EqualTo("Task.Run"));
            Assert.That(_model.Rows[0].StatusText, Is.EqualTo("Outlived"));
            Assert.That(_model.Rows[0].IsOutlived, Is.True);
            Assert.That(_model.Rows[0].AgeText, Is.EqualTo("5"), "the age counts from the end of its generation");

            source.TrySetResult();
            _model.Update(_area);

            Assert.That(_model.Rows, Is.Empty);
        }

        // Selection

        [Test]
        public void SelectIndex_ThenNewEntriesRegister_KeepsTheSameEntrySelected()
        {
            Scope.Tree.UseManualClock();
            _area.After(10f, () => { });
            _area.OnCancel(() => { });
            _model.Update(_area);
            _model.SelectIndex(1);
            Assert.That(_model.Rows[_model.SelectedIndex].ItemText, Is.EqualTo("Task.After"));

            _area.OnCancel(() => { });
            _model.Update(_area);

            Assert.That(_model.SelectedIndex, Is.EqualTo(2), "the list shifted by one");
            Assert.That(_model.Rows[_model.SelectedIndex].ItemText, Is.EqualTo("Task.After"));
        }

        [Test]
        public void Update_SelectedEntryEnds_ClearsTheSelection()
        {
            var registration = _area.OnCancel(() => { });
            _model.Update(_area);
            _model.SelectIndex(0);
            Assert.That(_model.SelectedIndex, Is.EqualTo(0));

            registration.Cancel();
            _model.Update(_area);

            Assert.That(_model.SelectedIndex, Is.EqualTo(-1));
            Assert.That(_model.TraceText, Is.Null);
        }

        [Test]
        public void SelectIndex_OutOfRange_ClearsTheSelection()
        {
            _area.OnCancel(() => { });
            _model.Update(_area);
            _model.SelectIndex(0);

            _model.SelectIndex(7);

            Assert.That(_model.SelectedIndex, Is.EqualTo(-1));
            Assert.That(_model.TraceText, Is.Null);
        }

        // Stack traces

        [Test]
        public void SelectIndex_EntryWithoutATrace_ShowsTheNoTraceHint()
        {
            _area.OnCancel(() => { });
            _model.Update(_area);

            _model.SelectIndex(0);

            Assert.That(_model.Rows[0].HasTrace, Is.False);
            Assert.That(_model.TraceText, Is.EqualTo(EntryListModel.NoTraceText));
        }

        [Test]
        public void SelectIndex_EntryRegisteredWithTracesOn_ShowsItsStackTrace()
        {
            LifetimeDiagnostics.CaptureStackTraces = true;
            _area.OnCancel(() => { });
            LifetimeDiagnostics.CaptureStackTraces = false;
            _model.Update(_area);

            _model.SelectIndex(0);

            Assert.That(_model.Rows[0].HasTrace, Is.True);
            Assert.That(_model.TraceText, Is.Not.EqualTo(EntryListModel.NoTraceText));
            Assert.That(_model.TraceText, Does.Contain(nameof(EntryListModelTests)), "the trace must reach the registering call site");
        }

        private string[] Labels()
        {
            var labels = new string[_model.Rows.Count];
            for (var i = 0; i < labels.Length; i++)
            {
                labels[i] = _model.Rows[i].ItemText;
            }

            return labels;
        }

        private sealed class PairedSource
        {
#pragma warning disable CS0067 // The tests only subscribe and unsubscribe; nothing needs to raise it.
            public event Action Raised;
#pragma warning restore CS0067
        }

        private sealed class Probe : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
