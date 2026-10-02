using System;
using DG.Tweening;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests.TweenBinding
{
    // What a lifetime does to the tweens registered with AddTo(Lifetime).
    [TestFixture]
    public sealed class TweenCancelTests
    {
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
        public void Cancel_KillMode_KillsTheTweenAtOnceWithoutMovingOrCompletingIt()
        {
            var area = _s.Area();
            var box = _s.Box();
            var completes = 0;
            var kills = 0;
            var tween = _s.NewTween(box, 10f);
            tween.OnComplete(() => completes++);
            tween.OnKill(() => kills++);
            tween.AddTo(area);
            _s.Step(0.4f);
            var reached = box.Value;
            Assert.That(reached, Is.GreaterThan(0f).And.LessThan(10f), "premise: the tween is midway");

            area.Cancel();

            Assert.That(tween.IsActive(), Is.False);
            Assert.That(box.Value, Is.EqualTo(reached), "a killed tween stays where it was");
            Assert.That(completes, Is.Zero);
            Assert.That(kills, Is.EqualTo(1));
        }

        [Test]
        public void Cancel_CompleteMode_LandsTheEndValueAndRunsOnCompleteOnce()
        {
            var area = _s.Area();
            var box = _s.Box();
            var completes = 0;
            var kills = 0;
            var tween = _s.NewTween(box, 10f);
            tween.OnComplete(() => completes++);
            tween.OnKill(() => kills++);
            tween.AddTo(area, TweenCancelMode.Complete);
            _s.Step(0.4f);
            Assert.That(box.Value, Is.LessThan(10f), "premise: the tween is midway");

            area.Cancel();

            Assert.That(tween.IsActive(), Is.False);
            Assert.That(box.Value, Is.EqualTo(10f).Within(0.0001f), "the visual lands on its end value");
            Assert.That(completes, Is.EqualTo(1));
            Assert.That(kills, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_KillMode_KillsTheTween()
        {
            var area = _s.Area();
            var box = _s.Box();
            var completes = 0;
            var tween = _s.NewTween(box, 10f);
            tween.OnComplete(() => completes++);
            tween.AddTo(area);
            _s.Step(0.4f);
            var reached = box.Value;

            area.Dispose();

            Assert.That(tween.IsActive(), Is.False);
            Assert.That(box.Value, Is.EqualTo(reached));
            Assert.That(completes, Is.Zero);
        }

        [Test]
        public void Dispose_CompleteMode_LandsTheEndValueAndRunsOnCompleteOnce()
        {
            var area = _s.Area();
            var box = _s.Box();
            var completes = 0;
            var tween = _s.NewTween(box, 10f);
            tween.OnComplete(() => completes++);
            tween.AddTo(area, TweenCancelMode.Complete);
            _s.Step(0.4f);

            area.Dispose();

            Assert.That(tween.IsActive(), Is.False);
            Assert.That(box.Value, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(completes, Is.EqualTo(1));
        }

        [Test]
        public void Cancel_OnTheParent_KillsTheTweensOfChildAreas()
        {
            var parent = _s.Area("Parent");
            var child = parent.CreateChild("Child");
            var tween = _s.NewTween(_s.Box());
            tween.AddTo(child);

            parent.Cancel();

            Assert.That(tween.IsActive(), Is.False);
        }

        [Test]
        public void Cancel_TwoTweens_AreEndedNewestFirst()
        {
            var area = _s.Area();
            var log = new CallLog();
            var first = _s.NewTween(_s.Box());
            var second = _s.NewTween(_s.Box());
            first.OnKill(() => log.Add("first"));
            second.OnKill(() => log.Add("second"));
            first.AddTo(area);
            second.AddTo(area);

            area.Cancel();

            Assert.That(log.ToArray(), Is.EqualTo(new[] { "second", "first" }));
        }

        [Test]
        public void Cancel_CalledTwice_TheSecondCallIsANoOp()
        {
            var area = _s.Area();
            var kills = 0;
            var tween = _s.NewTween(_s.Box());
            tween.OnKill(() => kills++);
            tween.AddTo(area);

            area.Cancel();
            area.Cancel();

            Assert.That(kills, Is.EqualTo(1));
        }

        [Test]
        public void Cancel_ThenANewTween_TheLifetimeIsReusableAndTheNewTweenDiesWithTheNextCancel()
        {
            var area = _s.Area();
            var first = _s.NewTween(_s.Box());
            first.AddTo(area);
            area.Cancel();
            var second = _s.NewTween(_s.Box());
            second.AddTo(area);

            Assert.That(second.IsActive(), Is.True, "work registered after Cancel() returns belongs to the new generation");
            Assert.That(area.EntryCount, Is.EqualTo(1));

            area.Cancel();

            Assert.That(second.IsActive(), Is.False);
        }

        [Test]
        public void AddTo_ReturnsTheSameTweenWithItsStaticType()
        {
            var area = _s.Area();
            var box = _s.Box();
            var sequence = _s.Track(DOTween.Sequence());
            var tweener = _s.Track(DOTween.To(() => box.Value, value => box.Value = value, 1f, 1f));

            Sequence sequenceBack = sequence.AddTo(area);
            Tweener tweenerBack = tweener.AddTo(area);

            Assert.That(sequenceBack, Is.SameAs(sequence));
            Assert.That(tweenerBack, Is.SameAs(tweener));
        }

        [Test]
        public void AddTo_AnAlreadyCancellingLifetime_KillsEvenInCompleteMode()
        {
            var area = _s.Area();
            var box = _s.Box();
            var completes = 0;
            var activeInside = true;
            var tween = _s.NewTween(box, 10f);
            tween.OnComplete(() => completes++);
            area.OnCancel(() =>
            {
                tween.AddTo(area, TweenCancelMode.Complete);
                activeInside = tween.IsActive();
            });

            area.Cancel();

            Assert.That(activeInside, Is.False, "registering on a cancelling lifetime ends the tween on the spot");
            Assert.That(completes, Is.Zero, "the tween never ran for this owner, so it is killed, not completed");
            Assert.That(box.Value, Is.Zero);
        }

        [Test]
        public void AddTo_ADisposedLifetime_KillsEvenInCompleteMode()
        {
            var area = _s.Area();
            var box = _s.Box();
            var completes = 0;
            var tween = _s.NewTween(box, 10f);
            tween.OnComplete(() => completes++);
            area.Dispose();

            tween.AddTo(area, TweenCancelMode.Complete);

            Assert.That(tween.IsActive(), Is.False);
            Assert.That(completes, Is.Zero);
            Assert.That(box.Value, Is.Zero);
            Assert.That(area.EntryCount, Is.Zero, "nothing is left registered");
        }

        [Test]
        public void AddTo_ADisposedLifetime_KillMode_AlsoRunsTheUsersOnKill()
        {
            var area = _s.Area();
            var kills = 0;
            var tween = _s.NewTween(_s.Box());
            tween.OnKill(() => kills++);
            area.Dispose();

            tween.AddTo(area);

            Assert.That(tween.IsActive(), Is.False);
            Assert.That(kills, Is.EqualTo(1));
        }

        [Test]
        public void AddTo_NullLifetime_KillsTheTweenThenThrows()
        {
            var tween = _s.NewTween(_s.Box());
            var completing = _s.NewTween(_s.Box());
            var completes = 0;
            completing.OnComplete(() => completes++);

            var failure = Assert.Throws<ArgumentNullException>(() => tween.AddTo((Lifetime)null));
            Assert.Throws<ArgumentNullException>(() => completing.AddTo((Lifetime)null, TweenCancelMode.Complete));

            Assert.That(failure.ParamName, Is.EqualTo("owner"));
            Assert.That(tween.IsActive(), Is.False);
            Assert.That(completing.IsActive(), Is.False);
            Assert.That(completes, Is.Zero, "a null owner is a bug, so the tween is killed whatever the mode");
        }

        [Test]
        public void AddTo_NullTween_Throws()
        {
            var area = _s.Area();
            Tween none = null;

            var failure = Assert.Throws<ArgumentNullException>(() => none.AddTo(area));

            Assert.That(failure.ParamName, Is.EqualTo("tween"));
            Assert.That(area.EntryCount, Is.Zero);
        }

        [Test]
        public void AddTo_OffTheMainThread_ThrowsAndLeavesTheTweenUntouched()
        {
            var area = _s.Area();
            var tween = _s.NewTween(_s.Box());

            var failure = ThreadRunner.Run(() => tween.AddTo(area));

            Assert.That(failure, Is.InstanceOf<InvalidOperationException>());
            Assert.That(tween.IsActive(), Is.True);
            Assert.That(area.EntryCount, Is.Zero);
        }

        [Test]
        public void AddTo_ATweenThatWasCompletedByHand_RegistersNothing()
        {
            var area = _s.Area();
            var tween = _s.NewTween(_s.Box());
            tween.Complete();

            tween.AddTo(area);

            Assert.That(area.EntryCount, Is.Zero, "an inactive tween needs no cleanup");
        }
    }
}
