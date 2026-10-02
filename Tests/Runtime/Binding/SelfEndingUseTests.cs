using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.Binding
{
    // A task on the active lifetime that turns its own object off: the cancel actions run inside the SetActive call, and the task goes on after it.
    [TestFixture]
    public sealed class SelfEndingUseTests
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

        [UnityTest]
        public IEnumerator TaskThatTurnsItsOwnObjectOff_RunsTheCancelActionInsideTheCall_AndTheStatementAfterTheCallStillRuns()
        {
            var go = _s.NewInactiveObject("Use");
            var use = go.AddComponent<SelfEndingUse>();
            go.SetActive(true);
            Assert.That(use.Uses, Is.EqualTo(1));
            Assert.That(use.Cancelled, Is.Zero);

            yield return BindingScenes.Frames(3);

            Assert.That(go.activeSelf, Is.False, "the task turned the object off");
            Assert.That(use.Cancelled, Is.EqualTo(1), "the cancel action ran once");
            Assert.That(use.CancelledInsideTheCall, Is.EqualTo(1), "and it ran before SetActive returned");
            Assert.That(use.AfterTheCall, Is.EqualTo(1), "the task was not interrupted, it ran on after the call");
            Assert.That(use.TokenCancelledAfterTheCall, Is.True, "with a token that is cancelled by then");
            Assert.That(use.GetActiveLifetime().EntryCount, Is.Zero);
            Assert.That(_s.Errors.Count, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator TaskThatTurnsItsOwnObjectOff_RepeatsTheSameWayOnTheNextUse()
        {
            var go = _s.NewInactiveObject("Use");
            var use = go.AddComponent<SelfEndingUse>();
            go.SetActive(true);
            yield return BindingScenes.Frames(3);

            go.SetActive(true);
            Assert.That(use.Uses, Is.EqualTo(2));
            Assert.That(use.Cancelled, Is.EqualTo(1), "nothing ran at the activation");

            yield return BindingScenes.Frames(3);

            Assert.That(go.activeSelf, Is.False);
            Assert.That(use.Cancelled, Is.EqualTo(2));
            Assert.That(use.CancelledInsideTheCall, Is.EqualTo(2));
            Assert.That(use.AfterTheCall, Is.EqualTo(2));
            Assert.That(use.TokenCancelledAfterTheCall, Is.True);
            Assert.That(_s.Errors.Count, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
