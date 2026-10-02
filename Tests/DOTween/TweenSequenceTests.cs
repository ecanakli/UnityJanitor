using DG.Tweening;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.TweenBinding
{
    // Sequences: register the root Sequence only. DOTween ignores Kill on a nested tween, so a
    // nested tween registered on its own survives its owner. These tests pin both halves.
    [TestFixture]
    public sealed class TweenSequenceTests
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
        public void Cancel_ARegisteredRoot_KillsTheWholeSequenceSoNoNestedTweenMovesAgain()
        {
            var area = _s.Area();
            var firstBox = _s.Box();
            var secondBox = _s.Box();
            var completes = 0;
            var sequence = _s.NewSequence(_s.NewNestedTween(firstBox, 10f, 1f), _s.NewNestedTween(secondBox, 20f, 1f));
            sequence.OnComplete(() => completes++);
            sequence.AddTo(area);
            _s.Step(0.5f);
            var reached = firstBox.Value;
            Assert.That(reached, Is.GreaterThan(0f).And.LessThan(10f), "premise: the first nested tween is midway");
            Assert.That(secondBox.Value, Is.Zero, "premise: the second nested tween has not started");

            area.Cancel();
            _s.Step(5f);

            Assert.That(sequence.IsActive(), Is.False);
            Assert.That(firstBox.Value, Is.EqualTo(reached), "the nested tween stopped with its root");
            Assert.That(secondBox.Value, Is.Zero, "and the one that had not started never starts");
            Assert.That(completes, Is.Zero);
        }

        [Test]
        public void Cancel_ARegisteredRootInCompleteMode_LandsEveryNestedTweenOnItsEndValue()
        {
            var area = _s.Area();
            var firstBox = _s.Box();
            var secondBox = _s.Box();
            var completes = 0;
            var sequence = _s.NewSequence(_s.NewNestedTween(firstBox, 10f, 1f), _s.NewNestedTween(secondBox, 20f, 1f));
            sequence.OnComplete(() => completes++);
            sequence.AddTo(area, TweenCancelMode.Complete);
            _s.Step(0.5f);

            area.Cancel();

            Assert.That(sequence.IsActive(), Is.False);
            Assert.That(firstBox.Value, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(secondBox.Value, Is.EqualTo(20f).Within(0.0001f));
            Assert.That(completes, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_ARegisteredRoot_KillsTheSequence()
        {
            var area = _s.Area();
            var sequence = _s.NewSequence(_s.NewNestedTween(_s.Box(), 10f, 1f));
            sequence.AddTo(area);

            area.Dispose();

            Assert.That(sequence.IsActive(), Is.False);
        }

        [Test]
        public void Cancel_ANestedTweenRegisteredOnItsOwn_IsNotKilledBecauseDOTweenIgnoresIt()
        {
            var area = _s.Area();
            var box = _s.Box();
            var nested = _s.NewNestedTween(box, 10f, 1f);
            var sequence = _s.NewSequence(nested, _s.NewNestedTween(_s.Box(), 20f, 1f));
            nested.AddTo(area);
            _s.Step(0.25f);
            var before = box.Value;

            area.Cancel();
            _s.Step(0.25f);

            Assert.That(nested.IsActive(), Is.True, "premise: Kill on a nested tween is ignored, silently");
            Assert.That(sequence.IsActive(), Is.True, "the root keeps playing");
            Assert.That(box.Value, Is.GreaterThan(before), "and keeps driving the nested tween");
            Assert.That(_s.Errors.Count, Is.Zero, "nothing is routed: the lifetime cannot tell");
        }

        [Test]
        public void Destroy_OfTheOwnerOfARegisteredRoot_KillsTheSequence()
        {
            var host = _s.NewHost();
            var box = _s.Box();
            var sequence = _s.NewSequence(_s.NewNestedTween(box, 10f, 1f));
            sequence.AddTo(host);
            _s.Step(0.5f);
            var reached = box.Value;

            Object.DestroyImmediate(host.gameObject);
            _s.Step(5f);

            Assert.That(sequence.IsActive(), Is.False);
            Assert.That(box.Value, Is.EqualTo(reached));
        }
    }
}
