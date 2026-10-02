using Cysharp.Threading.Tasks;
using DG.Tweening;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests.TweenBinding
{
    // DOTween's own global operations end tweens that are registered or awaited on a lifetime. The awaits end as
    // cancelled, the entries are reclaimed by the sweep, and the later Cancel() neither throws nor routes anything.
    [TestFixture]
    public sealed class TweenGlobalOperationTests
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
        public void KillAll_EndsRegisteredAndAwaitedTweens_AndALaterCancelThrowsAndRoutesNothing()
        {
            var area = _s.Area();
            var registered = _s.NewTween(_s.Box(), 10f);
            registered.AddTo(area);
            var awaited = _s.NewTween(_s.Box(), 10f);
            var task = awaited.AwaitCompletionAsync(area);

            var killed = DOTween.KillAll();

            Assert.That(killed, Is.GreaterThanOrEqualTo(2));
            Assert.That(registered.IsActive(), Is.False);
            Assert.That(awaited.IsActive(), Is.False);
            TweenAwaits.AssertCancelled(task);
            AssertDeadEntriesAreSweptAndCancelIsQuiet(area);
        }

        [Test]
        public void Kill_ByTarget_EndsOnlyTheTweensOfThatTarget()
        {
            var area = _s.Area();
            var target = new object();
            var registered = _s.NewTween(_s.Box(), 10f).SetTarget(target);
            registered.AddTo(area);
            var awaited = _s.NewTween(_s.Box(), 10f).SetTarget(target);
            var task = awaited.AwaitCompletionAsync(area);
            var other = _s.NewTween(_s.Box(), 10f);
            other.AddTo(area);

            var killed = DOTween.Kill(target);

            Assert.That(killed, Is.EqualTo(2));
            Assert.That(registered.IsActive(), Is.False);
            Assert.That(awaited.IsActive(), Is.False);
            Assert.That(other.IsActive(), Is.True, "a tween of another target is left alone");
            TweenAwaits.AssertCancelled(task);

            area.Cancel();

            Assert.That(other.IsActive(), Is.False, "the lifetime still kills the survivor");
            Assert.That(_s.Errors.Count, Is.Zero);
        }

        [Test]
        public void Clear_EndsRegisteredAndAwaitedTweens_AndTheAwaitIsCancelledAtTheLatestWhenTheLifetimeEnds()
        {
            var area = _s.Area();
            var registered = _s.NewTween(_s.Box(), 10f);
            registered.AddTo(area);
            var awaited = _s.NewTween(_s.Box(), 10f);
            var task = awaited.AwaitCompletionAsync(area);

            DOTween.Clear();

            Assert.That(registered.IsActive(), Is.False);
            Assert.That(awaited.IsActive(), Is.False);
            TestContext.WriteLine("Await status right after DOTween.Clear: " + task.Status);

            Assert.That(() => area.Cancel(), Throws.Nothing);

            TweenAwaits.AssertCancelled(task);
            Assert.That(_s.Errors.Count, Is.Zero);
        }

        // DOTween documents a capacity change as a call for when nothing runs; raising it is what its automatic growth does.
        [Test]
        public void SetTweensCapacity_RaisedWhileTweensAreRegisteredAndAwaited_KeepsThemRunning()
        {
            var area = _s.Area();
            var registered = _s.NewTween(_s.Box(), 10f, 2f);
            registered.AddTo(area);
            var awaited = _s.NewTween(_s.Box(), 10f, 2f);
            var task = awaited.AwaitCompletionAsync(area);

            try
            {
                DOTween.SetTweensCapacity(600, 100);
                _s.Step(1f);

                Assert.That(registered.IsActive(), Is.True);
                Assert.That(awaited.IsActive(), Is.True);
                TweenAwaits.AssertStatus(task, UniTaskStatus.Pending);

                _s.Step(1f);

                TweenAwaits.AssertStatus(task, UniTaskStatus.Succeeded);
                TweenAwaits.Consume(task);
                Assert.That(registered.IsActive(), Is.False);
            }
            finally
            {
                DOTween.SetTweensCapacity(200, 50);
            }

            AssertDeadEntriesAreSweptAndCancelIsQuiet(area);
        }

        // Fourteen more items bring the two dead tween entries to the sweep threshold of 16.
        private void AssertDeadEntriesAreSweptAndCancelIsQuiet(Lifetime area)
        {
            for (var i = 0; i < 14; i++)
            {
                area.OnCancel(() => { });
            }

            Assert.That(area.EntryCount, Is.EqualTo(14), "the entries of the two dead tweens were reclaimed");
            Assert.That(() => area.Cancel(), Throws.Nothing);
            Assert.That(area.EntryCount, Is.Zero);
            Assert.That(_s.Errors.Count, Is.Zero);
        }
    }
}
