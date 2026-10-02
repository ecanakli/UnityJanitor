using NUnit.Framework;

namespace Ecanakli.Janitor.EditorTools.Tests
{
    // The cached texts of the window's cells: numbers, frame counts and the enum display names.
    [TestFixture]
    public sealed class ViewTextTests
    {
        // CountText

        [TestCase(0, "0")]
        [TestCase(7, "7")]
        [TestCase(9999, "9999")]
        [TestCase(10000, "10000")]
        [TestCase(123456, "123456")]
        public void CountText_Get_FormatsTheNumber(int value, string expected)
        {
            Assert.That(CountText.Get(value), Is.EqualTo(expected));
        }

        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public void CountText_Get_NegativeValueIsUnknown(int value)
        {
            Assert.That(CountText.Get(value), Is.EqualTo(CountText.Unknown));
        }

        [Test]
        public void CountText_Get_RepeatedCallsReturnTheSameString()
        {
            Assert.That(CountText.Get(42), Is.SameAs(CountText.Get(42)));
        }

        [TestCase(0, "0")]
        [TestCase(9999, "9999")]
        [TestCase(10000, "10k")]
        [TestCase(12999, "12k")]
        [TestCase(13000, "13k")]
        [TestCase(9999999, "9999k")]
        [TestCase(10000000, "10000k+")]
        [TestCase(int.MaxValue, "10000k+")]
        public void CountText_Frames_RoundsLargeCountsDownToThousands(int frames, string expected)
        {
            Assert.That(CountText.Frames(frames), Is.EqualTo(expected));
        }

        [Test]
        public void CountText_Frames_NegativeIsUnknown()
        {
            Assert.That(CountText.Frames(-1), Is.EqualTo(CountText.Unknown));
        }

        [Test]
        public void CountText_Frames_GrowingAgeKeepsHittingTheCache()
        {
            Assert.That(CountText.Frames(12000), Is.SameAs(CountText.Frames(12500)), "both are 12k");
        }

        // DisplayNames

        [TestCase(0, "App")]
        [TestCase(1, "Scene")]
        [TestCase(2, "Component")]
        [TestCase(3, "GameObject")]
        [TestCase(4, "Active")]
        [TestCase(5, "Area")]
        [TestCase(6, "Injected")]
        public void DisplayNames_Kind_NamesEveryLifetimeKind(int kind, string expected)
        {
            Assert.That(DisplayNames.Kind((LifetimeKind)kind), Is.EqualTo(expected));
        }

        [Test]
        public void DisplayNames_Kind_UnknownValueHasAFallback()
        {
            Assert.That(DisplayNames.Kind((LifetimeKind)99), Is.EqualTo(DisplayNames.Unknown));
        }

        [TestCase(0, "Active")]
        [TestCase(1, "Cancelling")]
        [TestCase(2, "Disposing")]
        [TestCase(3, "Disposed")]
        public void DisplayNames_State_NamesEveryLifetimeState(int state, string expected)
        {
            Assert.That(DisplayNames.State((LifetimeState)state, false), Is.EqualTo(expected));
        }

        [TestCase(0, "Active (orphan)")]
        [TestCase(1, "Cancelling (orphan)")]
        [TestCase(2, "Disposing (orphan)")]
        [TestCase(3, "Disposed")]
        public void DisplayNames_State_OrphanMarksEveryLiveStateButNotDisposed(int state, string expected)
        {
            Assert.That(DisplayNames.State((LifetimeState)state, true), Is.EqualTo(expected));
        }

        [Test]
        public void DisplayNames_State_UnknownValueHasAFallback()
        {
            Assert.That(DisplayNames.State((LifetimeState)99, false), Is.EqualTo(DisplayNames.Unknown));
            Assert.That(DisplayNames.State((LifetimeState)99, true), Is.EqualTo(DisplayNames.Unknown));
        }

        [Test]
        public void DisplayNames_Status_NamesLiveAndOutlived()
        {
            Assert.That(DisplayNames.Status(EntryStatus.Live), Is.EqualTo("Live"));
            Assert.That(DisplayNames.Status(EntryStatus.Outlived), Is.EqualTo("Outlived"));
        }
    }
}
