using System;
using DG.Tweening;
using NUnit.Framework;

namespace Ecanakli.Janitor.Tests.TweenBinding
{
    // With DOTween's safe mode off, an exception thrown by a tween callback reaches the caller of Kill(true). In Complete mode
    // that caller is a lifetime teardown, which must route it once and still run every other entry.
    [TestFixture]
    public sealed class TweenSafeModeTests
    {
        private TweenSession _s;
        private bool _previousSafeMode;

        [SetUp]
        public void SetUp()
        {
            _s = TweenSession.Begin();
            _previousSafeMode = DOTween.useSafeMode;
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
                DOTween.useSafeMode = _previousSafeMode;
            }
        }

        [Test]
        public void Cancel_WithSafeModeOff_AThrowingOnCompleteInCompleteMode_IsRoutedOnceAndTheOtherEntriesStillRun()
        {
            DOTween.useSafeMode = false;
            var area = _s.Area();
            var log = new CallLog();
            var failure = new InvalidOperationException("onComplete in Complete mode");
            area.Record(log, "before");
            var tween = _s.NewTween(_s.Box(), 10f);
            tween.OnComplete(() => throw failure);
            tween.AddTo(area, TweenCancelMode.Complete);
            area.Record(log, "after");
            _s.Step(0.4f);

            Assert.DoesNotThrow(() => area.Cancel());

            Assert.That(log.ToArray(), Is.EqualTo(new[] { "after", "before" }), "the entries on both sides of the failing one ran, newest first");
            Assert.That(_s.Errors.Count, Is.EqualTo(1), "the failure is routed once");
            Assert.That(_s.Errors[0].Exception, Is.SameAs(failure));
            Assert.That(_s.Errors[0].Context.Source, Is.EqualTo(LifetimeErrorSource.CancelAction));
            Assert.That(area.EntryCount, Is.Zero);
            Assert.That(area.IsDisposed, Is.False, "a Cancel keeps the lifetime usable");
            Assert.That(area.Record(log, "again").IsActive, Is.True);
            _s.Errors.Clear();
        }

        [Test]
        public void Dispose_WithSafeModeOff_AThrowingOnCompleteInCompleteMode_IsRoutedOnceAndTheOtherEntriesStillRun()
        {
            DOTween.useSafeMode = false;
            var area = _s.Area();
            var log = new CallLog();
            var failure = new InvalidOperationException("onComplete in Complete mode");
            area.Record(log, "before");
            var tween = _s.NewTween(_s.Box(), 10f);
            tween.OnComplete(() => throw failure);
            tween.AddTo(area, TweenCancelMode.Complete);
            area.Record(log, "after");
            _s.Step(0.4f);

            Assert.DoesNotThrow(() => area.Dispose());

            Assert.That(log.ToArray(), Is.EqualTo(new[] { "after", "before" }));
            Assert.That(_s.Errors.Count, Is.EqualTo(1));
            Assert.That(_s.Errors[0].Exception, Is.SameAs(failure));
            Assert.That(area.IsDisposed, Is.True);
            _s.Errors.Clear();
        }
    }
}
