using DG.Tweening;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests.TweenBinding
{
    // The play-again shape: an area below the active lifetime is cancelled, then a Run on it awaits a new tween with the area's token.
    [TestFixture]
    public sealed class AwaitOnAreaUnderActiveLifetimeTests
    {
        private TweenSession _s;
        private TweenTestHost _host;
        private Lifetime _area;
        private Counter _finished;

        [SetUp]
        public void SetUp()
        {
            _s = TweenSession.Begin();
            _host = _s.NewHost();
            _area = _host.GetActiveLifetime().CreateChild("Play");
            _finished = new Counter();
        }

        [TearDown]
        public void TearDown()
        {
            _s.Complete();
        }

        // Cancels the area, then awaits a new tween on it; the counter goes up when the await completes.
        private Tween Play()
        {
            var tween = _s.NewTween(_s.Box(), 10f, 1f);
            var finished = _finished;
            _area.Cancel();
            _area.Run(async token =>
            {
                await tween.AwaitCompletionAsync(token);
                finished.Value++;
            });
            return tween;
        }

        [Test]
        public void PlayAgain_CancellingTheAreaFirst_KillsTheEarlierTweenAndOnlyTheLaterOneFinishes()
        {
            var first = Play();
            _s.Step(0.4f);

            var second = Play();

            Assert.That(first.IsActive(), Is.False, "the cancel killed the earlier tween");
            Assert.That(second.IsActive(), Is.True);
            Assert.That(_area.IsDisposed, Is.False);
            Assert.That(_area.EntryCount, Is.EqualTo(1), "one await, not two");

            _s.Step(2f);

            Assert.That(_finished.Value, Is.EqualTo(1), "the earlier await ended without finishing");
            Assert.That(_s.Errors.Count, Is.Zero);
        }

        [Test]
        public void ObjectDeactivated_WhileTheTweenRuns_KillsItAndTheStatementAfterTheAwaitNeverRuns()
        {
            var tween = Play();
            _s.Step(0.4f);

            _host.gameObject.SetActive(false);

            Assert.That(tween.IsActive(), Is.False, "the deactivation cancelled the area and the token killed the tween");
            Assert.That(_area.EntryCount, Is.Zero);

            _s.Step(2f);

            Assert.That(_finished.Value, Is.Zero);
            Assert.That(_s.Errors.Count, Is.Zero, "the cancelled await is not an error");
        }

        [Test]
        public void ObjectActivatedAgain_ThePlayShapeWorksOnTheSameArea()
        {
            Play();
            _host.gameObject.SetActive(false);
            _host.gameObject.SetActive(true);

            Play();
            _s.Step(2f);

            Assert.That(_finished.Value, Is.EqualTo(1), "the tween ran to its end");
            Assert.That(_area.IsDisposed, Is.False);
            Assert.That(_s.Errors.Count, Is.Zero);
        }

        [Test]
        public void PlayAgain_AfterTheTweenFinished_CountsEachFinishOnce()
        {
            Play();
            _s.Step(1f);
            Assert.That(_finished.Value, Is.EqualTo(1));

            _s.Step(1f);
            Assert.That(_finished.Value, Is.EqualTo(1), "a finished await does not finish again");

            Play();
            _s.Step(1f);

            Assert.That(_finished.Value, Is.EqualTo(2));
            Assert.That(_s.Errors.Count, Is.Zero);
        }
    }
}
