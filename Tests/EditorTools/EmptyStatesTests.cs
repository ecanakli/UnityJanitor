using NUnit.Framework;

namespace Ecanakli.Janitor.EditorTools.Tests
{
    // The one-line messages the window shows instead of an empty view.
    [TestFixture]
    public sealed class EmptyStatesTests
    {
        // The tree

        [Test]
        public void ForTree_NotPlaying_SaysSo()
        {
            Assert.That(EmptyStates.ForTree(false, true, 0, 0), Is.EqualTo(EmptyStates.NotPlaying));
        }

        [Test]
        public void ForTree_NotPlayingAndTrackingOff_PrefersTheNotPlayingMessage()
        {
            Assert.That(EmptyStates.ForTree(false, false, 5, 5), Is.EqualTo(EmptyStates.NotPlaying));
        }

        [Test]
        public void ForTree_PlayingWithTrackingOff_SaysTrackingIsOff()
        {
            Assert.That(EmptyStates.ForTree(true, false, 5, 5), Is.EqualTo(EmptyStates.TrackingOff));
        }

        [Test]
        public void ForTree_PlayingWithoutLifetimes_SaysThereAreNone()
        {
            Assert.That(EmptyStates.ForTree(true, true, 0, 0), Is.EqualTo(EmptyStates.NoLifetimes));
        }

        [Test]
        public void ForTree_FilterHidesEveryLifetime_SaysNothingMatches()
        {
            Assert.That(EmptyStates.ForTree(true, true, 6, 0), Is.EqualTo(EmptyStates.NoFilterMatch));
        }

        [Test]
        public void ForTree_RowsToShow_HasNoMessage()
        {
            Assert.That(EmptyStates.ForTree(true, true, 6, 3), Is.Null);
        }

        [Test]
        public void Messages_EveryOne_IsASingleShortLine()
        {
            var messages = new[]
            {
                EmptyStates.NotPlaying,
                EmptyStates.TrackingOff,
                EmptyStates.NoLifetimes,
                EmptyStates.NoFilterMatch,
                EmptyStates.DetailsNotPlaying,
                EmptyStates.NoSelection,
                EmptyStates.SelectionGone,
                EmptyStates.NoWarnings,
                EmptyStates.NoWarningsTrackingOff,
                EmptyStates.NoRecent,
                EmptyStates.NoRecentTrackingOff,
            };

            foreach (var message in messages)
            {
                Assert.That(message, Is.Not.Null.And.Not.Empty);
                Assert.That(message, Does.Not.Contain("\n"), message);
                Assert.That(message.Length, Is.LessThan(140), message);
            }
        }

        // The warnings and recently disposed lists

        [Test]
        public void ForWarnings_NoneRecorded_SaysSo()
        {
            Assert.That(EmptyStates.ForWarnings(true, 0), Is.EqualTo(EmptyStates.NoWarnings));
        }

        [Test]
        public void ForWarnings_NoneRecordedAndTrackingOff_SaysTrackingIsOff()
        {
            Assert.That(EmptyStates.ForWarnings(false, 0), Is.EqualTo(EmptyStates.NoWarningsTrackingOff));
        }

        [Test]
        public void ForWarnings_SomeRecorded_HasNoMessageEvenWithTrackingOff()
        {
            Assert.That(EmptyStates.ForWarnings(true, 2), Is.Null);
            Assert.That(EmptyStates.ForWarnings(false, 2), Is.Null);
        }

        [Test]
        public void ForRecent_NothingDisposed_SaysSoAndNamesTheCapacity()
        {
            var message = EmptyStates.ForRecent(true, 0);

            Assert.That(message, Is.EqualTo(EmptyStates.NoRecent));
            Assert.That(message, Does.Contain("200"));
        }

        [Test]
        public void ForRecent_NothingDisposedAndTrackingOff_SaysTrackingIsOff()
        {
            Assert.That(EmptyStates.ForRecent(false, 0), Is.EqualTo(EmptyStates.NoRecentTrackingOff));
        }

        [Test]
        public void ForRecent_SomeDisposed_HasNoMessage()
        {
            Assert.That(EmptyStates.ForRecent(true, 3), Is.Null);
            Assert.That(EmptyStates.ForRecent(false, 3), Is.Null);
        }
    }
}
