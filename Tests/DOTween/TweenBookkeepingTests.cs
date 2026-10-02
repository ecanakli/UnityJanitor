using System;
using DG.Tweening;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests.TweenBinding
{
    // A tween stopped by someone else is reclaimed by the probe sweep, so a busy lifetime stays bounded.
    // Also the shape of the registered entry, and the rule that AddTo leaves the user's callbacks alone.
    [TestFixture]
    public sealed class TweenBookkeepingTests
    {
        private const int Cycles = 200;
        private const int LiveTweens = 3;

        private TweenSession _s;

        [SetUp]
        public void SetUp()
        {
            _s = TweenSession.Begin();
        }

        [TearDown]
        public void TearDown()
        {
            _s.Complete();
        }

        [Test]
        public void Sweep_TweensThatCompleteByThemselves_AreReclaimedAndTheEntryCountStaysBounded()
        {
            var area = _s.Area();
            var live = RegisterLiveTweens(area);
            var highest = 0;

            for (var i = 0; i < Cycles; i++)
            {
                var tween = _s.NewTween(_s.Box(), 1f, 1f);
                tween.AddTo(area);
                _s.Step(1f);
                highest = Math.Max(highest, area.EntryCount);
            }

            Assert.That(highest, Is.LessThanOrEqualTo(2 * LiveTweens + 16), "finished tweens must not pile up");
            AssertAllAlive(live);
            area.Cancel();
            AssertAllDead(live);
        }

        [Test]
        public void Sweep_TweensKilledFromOutside_AreReclaimedAndTheEntryCountStaysBounded()
        {
            var area = _s.Area();
            var live = RegisterLiveTweens(area);
            var highest = 0;

            for (var i = 0; i < Cycles; i++)
            {
                var tween = _s.NewTween(_s.Box(), 1f, 1f);
                tween.AddTo(area);
                tween.Kill();
                highest = Math.Max(highest, area.EntryCount);
            }

            Assert.That(highest, Is.LessThanOrEqualTo(2 * LiveTweens + 16));
            AssertAllAlive(live);
            area.Cancel();
            AssertAllDead(live);
        }

        [Test]
        public void TweenKill_BeforeTheLifetimeEnds_IsSafeAndLeavesNothingRouted()
        {
            var area = _s.Area();
            var kills = 0;
            var tween = _s.NewTween(_s.Box());
            tween.OnKill(() => kills++);
            tween.AddTo(area);

            tween.Kill();
            area.Cancel();
            area.Dispose();

            Assert.That(kills, Is.EqualTo(1), "only the early kill ran the user's callback");
            Assert.That(_s.Errors.Count, Is.Zero);
        }

        [Test]
        public void Cancel_AfterTheTweenCompletedOnItsOwn_DoesNothingMore()
        {
            var area = _s.Area();
            var completes = 0;
            var kills = 0;
            var tween = _s.NewTween(_s.Box());
            tween.OnComplete(() => completes++);
            tween.OnKill(() => kills++);
            tween.AddTo(area);
            _s.Step(2f);

            area.Cancel();

            Assert.That(completes, Is.EqualTo(1));
            Assert.That(kills, Is.EqualTo(1));
        }

        [Test]
        public void Cancel_AfterTheTweenCompletedWithAutoKillOff_KillsTheStillAliveTween()
        {
            var area = _s.Area();
            var completes = 0;
            var kills = 0;
            var tween = _s.NewTween(_s.Box());
            tween.SetAutoKill(false);
            tween.OnComplete(() => completes++);
            tween.OnKill(() => kills++);
            tween.AddTo(area);
            _s.Step(2f);
            Assert.That(tween.IsActive(), Is.True, "premise: a finished tween with auto kill off stays alive");

            area.Cancel();

            Assert.That(tween.IsActive(), Is.False, "a finished but alive tween is not 'finished' for the probe");
            Assert.That(completes, Is.EqualTo(1), "it is killed, not completed again");
            Assert.That(kills, Is.EqualTo(1));
        }

        [Test]
        public void AddTo_AnInactiveTween_RegistersNothing()
        {
            var area = _s.Area();
            var tween = _s.NewTween(_s.Box());
            tween.Kill();

            tween.AddTo(area);

            Assert.That(area.EntryCount, Is.Zero);
        }

        [Test]
        public void AddTo_KillMode_RegistersOneEntryWhoseStateIsTheTween()
        {
            var area = _s.Area();
            var tween = _s.NewTween(_s.Box());

            tween.AddTo(area);

            Assert.That(area.EntryCount, Is.EqualTo(1));
            ref var entry = ref area.EntryAt(area.NewestEntryId);
            Assert.That(entry.A, Is.SameAs(tween), "diagnostics recognise a tween entry by its state");
            Assert.That(entry.Fn, Is.SameAs(TweenTermination.KillAction));
            Assert.That(entry.Probe, Is.SameAs(TweenTermination.IsFinishedProbe));
        }

        [Test]
        public void AddTo_CompleteMode_RegistersTheCompleteAction()
        {
            var area = _s.Area();
            var tween = _s.NewTween(_s.Box());

            tween.AddTo(area, TweenCancelMode.Complete);

            ref var entry = ref area.EntryAt(area.NewestEntryId);
            Assert.That(entry.A, Is.SameAs(tween));
            Assert.That(entry.Fn, Is.SameAs(TweenTermination.CompleteAction));
        }

        [Test]
        public void AddTo_DoesNotReplaceTheUsersCallbacks_AndTheyRunOnceEach()
        {
            var area = _s.Area();
            var completes = 0;
            var kills = 0;
            TweenCallback onComplete = () => completes++;
            TweenCallback onKill = () => kills++;
            var tween = _s.NewTween(_s.Box());
            tween.OnComplete(onComplete);
            tween.OnKill(onKill);

            tween.AddTo(area);

            Assert.That(tween.onComplete, Is.SameAs(onComplete), "registration never touches onComplete");
            Assert.That(tween.onKill, Is.SameAs(onKill), "registration never touches onKill");

            _s.Step(2f);

            Assert.That(completes, Is.EqualTo(1));
            Assert.That(kills, Is.EqualTo(1));
        }

        [Test]
        public void AddTo_ATweenWithoutCallbacks_LeavesBothFieldsNull()
        {
            var area = _s.Area();
            var tween = _s.NewTween(_s.Box());

            tween.AddTo(area);
            tween.AddTo(area, TweenCancelMode.Complete);

            Assert.That(tween.onComplete, Is.Null);
            Assert.That(tween.onKill, Is.Null);
        }

        private Tween[] RegisterLiveTweens(Lifetime area)
        {
            var live = new Tween[LiveTweens];
            for (var i = 0; i < live.Length; i++)
            {
                live[i] = _s.NewTween(_s.Box(), 10f, 100000f);
                live[i].AddTo(area);
            }

            return live;
        }

        private static void AssertAllAlive(Tween[] tweens)
        {
            for (var i = 0; i < tweens.Length; i++)
            {
                Assert.That(tweens[i].IsActive(), Is.True, "the sweep never drops a live tween (" + i + ")");
            }
        }

        private static void AssertAllDead(Tween[] tweens)
        {
            for (var i = 0; i < tweens.Length; i++)
            {
                Assert.That(tweens[i].IsActive(), Is.False, "Cancel kills the tweens that survived the sweeps (" + i + ")");
            }
        }
    }
}
