#if UNITY_EDITOR
using System.Collections.Generic;
using DG.Tweening;
using Ecanakli.Janitor.Tests.TweenBinding;
using NUnit.Framework;
using UnityEngine;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // JANITOR102: a tween still active right after its termination killed it, most likely nested in a Sequence.
    // The DOTween assembly detects it and reports it through the public LifetimeDiagnostics.Report hook, from inside the item's
    // cancel action, so the core attaches it to the cancelling lifetime and to the AddTo call site. Also the tween entry kind.
    [TestFixture]
    public sealed class TweenDiagnosticsTests
    {
        private TweenSession _s;
        private DiagnosticsRecordingKit _d;
        private DiagnosticsSnapshot _snapshot;

        [SetUp]
        public void SetUp()
        {
            _s = TweenSession.Begin();
            _d = new DiagnosticsRecordingKit();
            _snapshot = new DiagnosticsSnapshot();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                _s.Complete();
            }
            finally
            {
                _d.Dispose();
            }
        }

        // A tween that lives inside a root Sequence, so DOTween ignores a kill on it.
        private Tween NewNestedInASequence()
        {
            var nested = _s.NewNestedTween(_s.Box(), 10f, 1f);
            _s.NewSequence(nested, _s.NewNestedTween(_s.Box(), 20f, 1f));
            return nested;
        }

        [Test]
        public void Cancel_ANestedTweenRegisteredOnItsOwn_RecordsJanitor102AttachedToTheLifetimeAndTheAddToCallSite()
        {
            var area = _s.Area("TweenArea");
            var nested = NewNestedInASequence();
            nested.AddTo(area, TweenCancelMode.Kill, "TweenSite", 88);
            _s.Step(0.25f);

            area.Cancel();

            Assert.That(nested.IsActive(), Is.True, "premise: DOTween ignored the kill");
            var warning = _d.Single(DiagnosticIds.UnkillableTween);
            Assert.That(warning.Lifetime, Is.SameAs(area));
            Assert.That(warning.LifetimeLabel, Is.EqualTo("TweenArea"));
            Assert.That(warning.Member, Is.EqualTo("TweenSite"), "attributed to the registration, not to the cancel");
            Assert.That(warning.Line, Is.EqualTo(88));
            Assert.That(warning.Message, Does.Contain("still active right after it was killed").And.Contain("Sequence"));
            Assert.That(warning.Count, Is.EqualTo(1));
            Assert.That(warning.IsInfo, Is.False);
            Assert.That(_s.Errors.Count, Is.Zero, "nothing is routed as an error");
        }

        [Test]
        public void Cancel_ANestedTweenWithATarget_NamesTheTargetAndKeepsItAsTheContext()
        {
            var area = _s.Area();
            var target = _s.NewObject("TweenTarget");
            var nested = NewNestedInASequence();
            nested.SetTarget(target);
            nested.AddTo(area);

            area.Cancel();

            var warning = _d.Single(DiagnosticIds.UnkillableTween);
            Assert.That(warning.Context, Is.SameAs(target), "the window can ping the target");
            Assert.That(warning.Message, Does.Contain("TweenTarget"));
        }

        [Test]
        public void Cancel_ANestedTweenWithoutAUnityTarget_HasNoContextObject()
        {
            var area = _s.Area();
            var nested = NewNestedInASequence();
            nested.AddTo(area);

            area.Cancel();

            Assert.That(_d.Single(DiagnosticIds.UnkillableTween).Context, Is.Null);
        }

        [Test]
        public void Cancel_TheSameNestedTweenKilledByTwoLifetimes_IsReportedOnce()
        {
            var first = _s.Area("First");
            var second = _s.Area("Second");
            var nested = NewNestedInASequence();
            nested.AddTo(first);
            nested.AddTo(second);

            first.Cancel();
            second.Cancel();

            var warning = _d.Single(DiagnosticIds.UnkillableTween);
            Assert.That(warning.Lifetime, Is.SameAs(first), "once per tween: the first kill that failed");
            Assert.That(warning.Count, Is.EqualTo(1));
        }

        [Test]
        public void Cancel_TwoNestedTweens_AreTwoEntriesEachAttachedToItsOwnLifetime()
        {
            var first = _s.Area("First");
            var second = _s.Area("Second");
            NewNestedInASequence().AddTo(first);
            NewNestedInASequence().AddTo(second);

            first.Cancel();
            second.Cancel();

            var rows = _d.Warnings(DiagnosticIds.UnkillableTween);
            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(rows[0].Lifetime, Is.SameAs(first));
            Assert.That(rows[1].Lifetime, Is.SameAs(second));
        }

        [Test]
        public void Dispose_ANestedTweenRegisteredOnItsOwn_IsReportedLikeACancel()
        {
            var area = _s.Area();
            NewNestedInASequence().AddTo(area);

            area.Dispose();

            Assert.That(_d.Single(DiagnosticIds.UnkillableTween).Lifetime, Is.SameAs(area), "the report keeps the disposed lifetime");
        }

        [Test]
        public void AddTo_ANestedTweenOnAnEndedOwner_IsReportedAttachedToThatLifetimeAndTheInnerCallSite()
        {
            var area = _s.Area();
            var nested = NewNestedInASequence();
            area.OnCancel(() => nested.AddTo(area, TweenCancelMode.Complete, "InnerSite", 5), "OuterSite", 4);

            area.Cancel();

            var warning = _d.Single(DiagnosticIds.UnkillableTween);
            Assert.That(warning.Lifetime, Is.SameAs(area), "a registration on an ending lifetime kills at once, inside the cancel");
            Assert.That(warning.Member, Is.EqualTo("InnerSite"), "the innermost item's call site, not the outer action's");
            Assert.That(warning.Line, Is.EqualTo(5));
        }

        [Test]
        public void Cancel_ARegisteredNormalTween_RecordsNothing()
        {
            var area = _s.Area();
            var box = _s.Box();
            var tween = _s.NewTween(box);
            tween.AddTo(area);
            _s.Step(0.25f);

            area.Cancel();

            Assert.That(tween.IsActive(), Is.False);
            Assert.That(_d.Total, Is.Zero);
        }

        [Test]
        public void Cancel_ARegisteredNormalTweenInCompleteMode_RecordsNothing()
        {
            var area = _s.Area();
            var tween = _s.NewTween(_s.Box());
            tween.AddTo(area, TweenCancelMode.Complete);
            _s.Step(0.25f);

            area.Cancel();

            Assert.That(tween.IsActive(), Is.False);
            Assert.That(_d.Total, Is.Zero);
        }

        [Test]
        public void Cancel_ARegisteredRootSequence_RecordsNothing()
        {
            var area = _s.Area();
            var sequence = _s.NewSequence(_s.NewNestedTween(_s.Box(), 10f, 1f), _s.NewNestedTween(_s.Box(), 20f, 1f));
            sequence.AddTo(area);
            _s.Step(0.25f);

            area.Cancel();

            Assert.That(sequence.IsActive(), Is.False);
            Assert.That(_d.Total, Is.Zero, "registering the root, as the documentation says, is never reported");
        }

        [Test]
        public void Cancel_ANestedTweenWhileTrackingIsOff_RecordsNothing()
        {
            var area = _s.Area();
            NewNestedInASequence().AddTo(area);
            LifetimeDiagnostics.TrackingEnabled = false;

            area.Cancel();

            Assert.That(_d.Total, Is.Zero);
        }

        // A finished tween keeps its entry until the next sweep; the window must not show it as live work.

        [Test]
        public void Capture_ATweenThatFinished_IsNeitherCountedNorListed_WhileItsEntryWaitsForASweep()
        {
            var area = _s.Area("TweenArea");
            _s.NewTween(_s.Box(), 10f, 0.25f).AddTo(area);
            var running = _s.NewTween(_s.Box(), 10f, 5f).AddTo(area);
            _s.Step(0.5f);

            LifetimeDiagnostics.Capture(_s.Tree, _snapshot);
            var entries = new List<EntryView>();
            LifetimeDiagnostics.CaptureEntries(area, entries);

            Assert.That(running.IsActive(), Is.True);
            Assert.That(area.EntryCount, Is.EqualTo(2), "the finished tween's entry is still in the lifetime");
            Assert.That(TweensOf(area), Is.EqualTo(1), "only the running tween is counted");
            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].Kind, Is.EqualTo(EntryKind.Tween));
        }

        [Test]
        public void Capture_ATweenThatIsStillPlaying_IsCountedAndListed()
        {
            var area = _s.Area("TweenArea");
            _s.NewTween(_s.Box(), 10f, 5f).AddTo(area);
            _s.Step(0.5f);

            LifetimeDiagnostics.Capture(_s.Tree, _snapshot);
            var entries = new List<EntryView>();
            LifetimeDiagnostics.CaptureEntries(area, entries);

            Assert.That(TweensOf(area), Is.EqualTo(1));
            Assert.That(entries.Count, Is.EqualTo(1));
        }

        private int TweensOf(Lifetime lifetime)
        {
            for (var i = 0; i < _snapshot.Nodes.Count; i++)
            {
                if (ReferenceEquals(_snapshot.Nodes[i].Lifetime, lifetime))
                {
                    return _snapshot.Nodes[i].Tweens;
                }
            }

            return -1;
        }

        // Entry kind

        [Test]
        public void Capture_ARegisteredTween_IsCountedAsATweenAndLabelled()
        {
            var area = _s.Area("TweenArea");
            var tween = _s.NewTween(_s.Box());
            area.OnCancel(() => { });
            tween.AddTo(area, TweenCancelMode.Kill, "TweenSite", 31);

            LifetimeDiagnostics.Capture(_s.Tree, _snapshot);
            var entries = new List<EntryView>();
            LifetimeDiagnostics.CaptureEntries(area, entries);

            Lifetime found = null;
            var tweens = 0;
            var others = 0;
            for (var i = 0; i < _snapshot.Nodes.Count; i++)
            {
                if (ReferenceEquals(_snapshot.Nodes[i].Lifetime, area))
                {
                    found = area;
                    tweens = _snapshot.Nodes[i].Tweens;
                    others = _snapshot.Nodes[i].Others;
                }
            }

            Assert.That(found, Is.SameAs(area));
            Assert.That(tweens, Is.EqualTo(1));
            Assert.That(others, Is.EqualTo(1));
            Assert.That(entries.Count, Is.EqualTo(2));
            Assert.That(entries[0].Kind, Is.EqualTo(EntryKind.Tween), "newest first");
            Assert.That(entries[0].FormatLabel(), Is.EqualTo("Tween"));
            Assert.That(entries[0].Member, Is.EqualTo("TweenSite"));
            Assert.That(entries[0].Line, Is.EqualTo(31));
            Assert.That(entries[1].Kind, Is.EqualTo(EntryKind.Other));
        }

        [Test]
        public void Capture_ATweenAwaitedThroughTheLifetime_IsCountedAsATweenToo()
        {
            var area = _s.Area();
            var tween = _s.NewTween(_s.Box());
            var task = tween.AwaitCompletionAsync(area);
            var entries = new List<EntryView>();

            LifetimeDiagnostics.CaptureEntries(area, entries);
            _s.Step(2f);

            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].Kind, Is.EqualTo(EntryKind.Tween));
            Assert.That(entries[0].FormatLabel(), Is.EqualTo("Tween"));
            task.GetAwaiter().GetResult();
        }
    }
}
#endif
