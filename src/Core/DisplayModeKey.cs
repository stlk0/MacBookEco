using System;

namespace MacBookEco.Core
{
    /// <summary>
    /// The DEVMODE values that identify a refresh-only display transition.
    /// Width, height, colour depth, orientation, fixed-output policy and
    /// display flags are all part of the key: changing any of them is a
    /// different display-mode operation, not a refresh-rate switch.
    /// </summary>
    public sealed class DisplayModeKey : IEquatable<DisplayModeKey>
    {
        public DisplayModeKey(
            int width,
            int height,
            int bitsPerPixel,
            int refreshRate,
            int orientation,
            int fixedOutput,
            int displayFlags)
            : this(
                width,
                height,
                bitsPerPixel,
                refreshRate,
                orientation,
                fixedOutput,
                displayFlags,
                RefreshRateToRationalNumerator(refreshRate),
                1)
        {
        }

        public DisplayModeKey(
            int width,
            int height,
            int bitsPerPixel,
            int refreshRate,
            int orientation,
            int fixedOutput,
            int displayFlags,
            uint refreshRateNumerator,
            uint refreshRateDenominator)
        {
            if (refreshRate < 0)
                throw new ArgumentOutOfRangeException(nameof(refreshRate));

            if (refreshRateNumerator == 0)
                throw new ArgumentOutOfRangeException(nameof(refreshRateNumerator));

            if (refreshRateDenominator == 0)
                throw new ArgumentOutOfRangeException(nameof(refreshRateDenominator));

            uint divisor = GreatestCommonDivisor(
                refreshRateNumerator,
                refreshRateDenominator);
            Width = width;
            Height = height;
            BitsPerPixel = bitsPerPixel;
            RefreshRate = refreshRate;
            Orientation = orientation;
            FixedOutput = fixedOutput;
            DisplayFlags = displayFlags;
            RefreshRateNumerator = refreshRateNumerator / divisor;
            RefreshRateDenominator = refreshRateDenominator / divisor;
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int BitsPerPixel { get; private set; }
        public int RefreshRate { get; private set; }
        public int Orientation { get; private set; }
        public int FixedOutput { get; private set; }

        /// <summary>
        /// Includes the native interlace bit.  It is intentionally compared
        /// as an exact value rather than treated as a preference.
        /// </summary>
        public int DisplayFlags { get; private set; }

        /// <summary>
        /// Canonical rational refresh reported by CCD.  This is the
        /// authoritative value for persistence and exact current-mode
        /// verification; RefreshRate remains the integer DEVMODE value used
        /// only to request a driver-enumerated 48/58/60 Hz mode.
        /// </summary>
        public uint RefreshRateNumerator { get; private set; }
        public uint RefreshRateDenominator { get; private set; }

        public DisplayModeKey WithRefreshRateRational(
            uint refreshRateNumerator,
            uint refreshRateDenominator)
        {
            return new DisplayModeKey(
                Width,
                Height,
                BitsPerPixel,
                RefreshRate,
                Orientation,
                FixedOutput,
                DisplayFlags,
                refreshRateNumerator,
                refreshRateDenominator);
        }

        public bool HasSameDisplayConfiguration(DisplayModeKey other)
        {
            return other != null &&
                Width == other.Width &&
                Height == other.Height &&
                BitsPerPixel == other.BitsPerPixel &&
                Orientation == other.Orientation &&
                FixedOutput == other.FixedOutput &&
                DisplayFlags == other.DisplayFlags;
        }

        public bool Equals(DisplayModeKey other)
        {
            return other != null &&
                RefreshRate == other.RefreshRate &&
                RefreshRateNumerator == other.RefreshRateNumerator &&
                RefreshRateDenominator == other.RefreshRateDenominator &&
                HasSameDisplayConfiguration(other);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as DisplayModeKey);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Width;
                hash = (hash * 397) ^ Height;
                hash = (hash * 397) ^ BitsPerPixel;
                hash = (hash * 397) ^ RefreshRate;
                hash = (hash * 397) ^ Orientation;
                hash = (hash * 397) ^ FixedOutput;
                hash = (hash * 397) ^ DisplayFlags;
                hash = (hash * 397) ^ (int)RefreshRateNumerator;
                hash = (hash * 397) ^ (int)RefreshRateDenominator;
                return hash;
            }
        }

        private static uint RefreshRateToRationalNumerator(int refreshRate)
        {
            if (refreshRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(refreshRate));

            return (uint)refreshRate;
        }

        private static uint GreatestCommonDivisor(uint left, uint right)
        {
            while (right != 0)
            {
                uint remainder = left % right;
                left = right;
                right = remainder;
            }

            return left;
        }
    }

    /// <summary>
    /// Pure, fail-closed selection predicate for refresh-only mode changes.
    /// Native code remains responsible for enumerating and applying modes;
    /// this policy prevents it from silently changing another mode property
    /// when two enumerated candidates share a resolution and refresh rate.
    /// </summary>
    public static class DisplayModeSelectionPolicy
    {
        public static bool IsExactRefreshOnlyCandidate(
            DisplayModeKey current,
            DisplayModeKey candidate,
            int requestedRefreshRate)
        {
            if (current == null)
                throw new ArgumentNullException(nameof(current));

            if (candidate == null)
                return false;

            return candidate.RefreshRate == requestedRefreshRate &&
                candidate.Width == current.Width &&
                candidate.Height == current.Height &&
                candidate.BitsPerPixel == current.BitsPerPixel &&
                candidate.Orientation == current.Orientation &&
                candidate.FixedOutput == current.FixedOutput &&
                candidate.DisplayFlags == current.DisplayFlags;
        }

        public static bool IsExactRefreshOnlyCandidate(
            DisplayModeKey current,
            DisplayModeKey candidate,
            DisplayModeKey requested)
        {
            if (current == null)
                throw new ArgumentNullException(nameof(current));

            if (requested == null || candidate == null ||
                !requested.HasSameDisplayConfiguration(current))
            {
                return false;
            }

            // EnumDisplaySettingsEx exposes only dmDisplayFrequency for an
            // enumerated target.  The persisted rational remains part of the
            // full key, but it must not be guessed from this integer API.
            return IsExactRefreshOnlyCandidate(
                current,
                candidate,
                requested.RefreshRate);
        }
    }
}
