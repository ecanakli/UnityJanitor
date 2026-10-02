using System.Globalization;

namespace Ecanakli.Janitor.EditorTools
{
    /// <summary>
    /// Cached number strings. Rebinding a visible row asks this class for its text, so a steady refresh allocates nothing.
    /// Main thread only.
    /// </summary>
    internal static class CountText
    {
        /// <summary>Shown for an unknown (negative) value.</summary>
        internal const string Unknown = "-";

        /// <summary>Shown for a frame count of ten million or more.</summary>
        internal const string FramesOverflow = "10000k+";

        private const int NumberLimit = 10000;
        private const int KiloLimit = 10000;

        private static readonly string[] Numbers = new string[NumberLimit];
        private static readonly string[] Kilos = new string[KiloLimit];

        /// <summary>The number as text; a negative number gives <see cref="Unknown"/>.</summary>
        internal static string Get(int value)
        {
            if (value < 0)
            {
                return Unknown;
            }

            if (value >= NumberLimit)
            {
                return value.ToString(CultureInfo.InvariantCulture);
            }

            var text = Numbers[value];
            if (text == null)
            {
                text = value.ToString(CultureInfo.InvariantCulture);
                Numbers[value] = text;
            }

            return text;
        }

        /// <summary>A frame count. From 10000 up it is rounded down to thousands ("12k"), so a growing age keeps hitting the cache.</summary>
        internal static string Frames(int frames)
        {
            if (frames < 0)
            {
                return Unknown;
            }

            if (frames < NumberLimit)
            {
                return Get(frames);
            }

            var kilo = frames / 1000;
            if (kilo >= KiloLimit)
            {
                return FramesOverflow;
            }

            var text = Kilos[kilo];
            if (text == null)
            {
                text = kilo.ToString(CultureInfo.InvariantCulture) + "k";
                Kilos[kilo] = text;
            }

            return text;
        }
    }
}
