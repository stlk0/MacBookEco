using System;
using System.Globalization;

namespace MacBookEco.Core
{
    public sealed class DetailedTiming : IEquatable<DetailedTiming>
    {
        public const int EncodedLength = 18;

        public DetailedTiming(
            int pixelClock10Khz,
            int horizontalActive,
            int horizontalBlanking,
            int verticalActive,
            int verticalBlanking,
            int horizontalSyncOffset,
            int horizontalSyncPulseWidth,
            int verticalSyncOffset,
            int verticalSyncPulseWidth,
            int horizontalImageSizeMillimeters,
            int verticalImageSizeMillimeters,
            int horizontalBorderPixels,
            int verticalBorderLines,
            byte flags)
        {
            ValidateRange("pixelClock10Khz", pixelClock10Khz, 1, 65535);
            ValidateRange("horizontalActive", horizontalActive, 1, 4095);
            ValidateRange("horizontalBlanking", horizontalBlanking, 1, 4095);
            ValidateRange("verticalActive", verticalActive, 1, 4095);
            ValidateRange("verticalBlanking", verticalBlanking, 1, 4095);
            ValidateRange("horizontalSyncOffset", horizontalSyncOffset, 0, 1023);
            ValidateRange("horizontalSyncPulseWidth", horizontalSyncPulseWidth, 0, 1023);
            ValidateRange("verticalSyncOffset", verticalSyncOffset, 0, 63);
            ValidateRange("verticalSyncPulseWidth", verticalSyncPulseWidth, 0, 63);
            ValidateRange(
                "horizontalImageSizeMillimeters",
                horizontalImageSizeMillimeters,
                0,
                4095);
            ValidateRange(
                "verticalImageSizeMillimeters",
                verticalImageSizeMillimeters,
                0,
                4095);
            ValidateRange("horizontalBorderPixels", horizontalBorderPixels, 0, 255);
            ValidateRange("verticalBorderLines", verticalBorderLines, 0, 255);

            if (horizontalSyncOffset + horizontalSyncPulseWidth > horizontalBlanking)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(horizontalSyncPulseWidth),
                    "Horizontal sync extends beyond the blanking interval.");
            }

            if (verticalSyncOffset + verticalSyncPulseWidth > verticalBlanking)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(verticalSyncPulseWidth),
                    "Vertical sync extends beyond the blanking interval.");
            }

            PixelClock10Khz = pixelClock10Khz;
            HorizontalActive = horizontalActive;
            HorizontalBlanking = horizontalBlanking;
            VerticalActive = verticalActive;
            VerticalBlanking = verticalBlanking;
            HorizontalSyncOffset = horizontalSyncOffset;
            HorizontalSyncPulseWidth = horizontalSyncPulseWidth;
            VerticalSyncOffset = verticalSyncOffset;
            VerticalSyncPulseWidth = verticalSyncPulseWidth;
            HorizontalImageSizeMillimeters = horizontalImageSizeMillimeters;
            VerticalImageSizeMillimeters = verticalImageSizeMillimeters;
            HorizontalBorderPixels = horizontalBorderPixels;
            VerticalBorderLines = verticalBorderLines;
            Flags = flags;
        }

        public int PixelClock10Khz { get; private set; }

        public double PixelClockMegahertz => PixelClock10Khz / 100.0;

        public int HorizontalActive { get; private set; }

        public int HorizontalBlanking { get; private set; }

        public int HorizontalTotal => HorizontalActive + HorizontalBlanking;

        public int HorizontalSyncOffset { get; private set; }

        public int HorizontalSyncPulseWidth { get; private set; }

        public int HorizontalBackPorch => HorizontalBlanking -
                    HorizontalSyncOffset -
                    HorizontalSyncPulseWidth;

        public int VerticalActive { get; private set; }

        public int VerticalBlanking { get; private set; }

        public int VerticalTotal => VerticalActive + VerticalBlanking;

        public int VerticalSyncOffset { get; private set; }

        public int VerticalSyncPulseWidth { get; private set; }

        public int VerticalBackPorch => VerticalBlanking -
                    VerticalSyncOffset -
                    VerticalSyncPulseWidth;

        public int HorizontalImageSizeMillimeters { get; private set; }

        public int VerticalImageSizeMillimeters { get; private set; }

        public int HorizontalBorderPixels { get; private set; }

        public int VerticalBorderLines { get; private set; }

        public byte Flags { get; private set; }

        public double RefreshRateHertz => (PixelClock10Khz * 10000.0) /
                    (HorizontalTotal * (double)VerticalTotal);

        public static DetailedTiming Parse(byte[] value)
        {
            return Parse(value, 0);
        }

        public static DetailedTiming Parse(byte[] value, int offset)
        {
            ValidateBuffer(value, offset);

            var pixelClock10Khz = ReadLowHigh(value[offset], value[offset + 1]);
            if (pixelClock10Khz == 0)
            {
                throw new FormatException("The descriptor is not a detailed timing descriptor.");
            }

            var horizontalActive =
                value[offset + 2] |
                ((value[offset + 4] & 0xF0) << 4);
            var horizontalBlanking =
                value[offset + 3] |
                ((value[offset + 4] & 0x0F) << 8);
            var verticalActive =
                value[offset + 5] |
                ((value[offset + 7] & 0xF0) << 4);
            var verticalBlanking =
                value[offset + 6] |
                ((value[offset + 7] & 0x0F) << 8);
            var horizontalSyncOffset =
                value[offset + 8] |
                ((value[offset + 11] & 0xC0) << 2);
            var horizontalSyncPulseWidth =
                value[offset + 9] |
                ((value[offset + 11] & 0x30) << 4);
            var verticalSyncOffset =
                ((value[offset + 10] & 0xF0) >> 4) |
                ((value[offset + 11] & 0x0C) << 2);
            var verticalSyncPulseWidth =
                (value[offset + 10] & 0x0F) |
                ((value[offset + 11] & 0x03) << 4);
            var horizontalImageSize =
                value[offset + 12] |
                ((value[offset + 14] & 0xF0) << 4);
            var verticalImageSize =
                value[offset + 13] |
                ((value[offset + 14] & 0x0F) << 8);

            try
            {
                return new DetailedTiming(
                    pixelClock10Khz,
                    horizontalActive,
                    horizontalBlanking,
                    verticalActive,
                    verticalBlanking,
                    horizontalSyncOffset,
                    horizontalSyncPulseWidth,
                    verticalSyncOffset,
                    verticalSyncPulseWidth,
                    horizontalImageSize,
                    verticalImageSize,
                    value[offset + 15],
                    value[offset + 16],
                    value[offset + 17]);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                // The caller supplied bytes, not arguments: an impossible
                // timing here is a malformed descriptor. EdidBaseBlock and the
                // watchdog's target resolver both document FormatException, so
                // an ArgumentOutOfRangeException escaping Parse used to reach
                // the watchdog as an unclassified fault.
                throw new FormatException(
                    "The detailed timing descriptor encodes an impossible timing.",
                    exception);
            }
        }

        public static DetailedTiming ParseHex(string value)
        {
            return Parse(HexCodec.Parse(value));
        }

        public byte[] ToByteArray()
        {
            var result = new byte[EncodedLength];
            WriteTo(result, 0);
            return result;
        }

        public void WriteTo(byte[] destination, int offset)
        {
            ValidateBuffer(destination, offset);

            destination[offset] = (byte)(PixelClock10Khz & 0xFF);
            destination[offset + 1] = (byte)((PixelClock10Khz >> 8) & 0xFF);
            destination[offset + 2] = (byte)(HorizontalActive & 0xFF);
            destination[offset + 3] = (byte)(HorizontalBlanking & 0xFF);
            destination[offset + 4] = (byte)(
                ((HorizontalActive >> 8) << 4) |
                (HorizontalBlanking >> 8));
            destination[offset + 5] = (byte)(VerticalActive & 0xFF);
            destination[offset + 6] = (byte)(VerticalBlanking & 0xFF);
            destination[offset + 7] = (byte)(
                ((VerticalActive >> 8) << 4) |
                (VerticalBlanking >> 8));
            destination[offset + 8] = (byte)(HorizontalSyncOffset & 0xFF);
            destination[offset + 9] = (byte)(HorizontalSyncPulseWidth & 0xFF);
            destination[offset + 10] = (byte)(
                ((VerticalSyncOffset & 0x0F) << 4) |
                (VerticalSyncPulseWidth & 0x0F));
            destination[offset + 11] = (byte)(
                ((HorizontalSyncOffset >> 8) << 6) |
                ((HorizontalSyncPulseWidth >> 8) << 4) |
                ((VerticalSyncOffset >> 4) << 2) |
                (VerticalSyncPulseWidth >> 4));
            destination[offset + 12] = (byte)(HorizontalImageSizeMillimeters & 0xFF);
            destination[offset + 13] = (byte)(VerticalImageSizeMillimeters & 0xFF);
            destination[offset + 14] = (byte)(
                ((HorizontalImageSizeMillimeters >> 8) << 4) |
                (VerticalImageSizeMillimeters >> 8));
            destination[offset + 15] = (byte)HorizontalBorderPixels;
            destination[offset + 16] = (byte)VerticalBorderLines;
            destination[offset + 17] = Flags;
        }

        public bool Equals(DetailedTiming other)
        {
            if (ReferenceEquals(other, null))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            // Compared as bytes. The encoded form is still the definition of
            // equality here, but rendering both sides as hexadecimal first
            // allocated two arrays and two 36-character strings per call, and
            // EdidBaseBlock.ContainsDetailedTiming calls this in a loop.
            return PixelClock10Khz == other.PixelClock10Khz
                && HorizontalActive == other.HorizontalActive
                && HorizontalBlanking == other.HorizontalBlanking
                && VerticalActive == other.VerticalActive
                && VerticalBlanking == other.VerticalBlanking
                && HorizontalSyncOffset == other.HorizontalSyncOffset
                && HorizontalSyncPulseWidth == other.HorizontalSyncPulseWidth
                && VerticalSyncOffset == other.VerticalSyncOffset
                && VerticalSyncPulseWidth == other.VerticalSyncPulseWidth
                && HorizontalImageSizeMillimeters
                    == other.HorizontalImageSizeMillimeters
                && VerticalImageSizeMillimeters
                    == other.VerticalImageSizeMillimeters
                && HorizontalBorderPixels == other.HorizontalBorderPixels
                && VerticalBorderLines == other.VerticalBorderLines
                && Flags == other.Flags;
        }

        public override bool Equals(object value)
        {
            return Equals(value as DetailedTiming);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = PixelClock10Khz;
                hash = (hash * 397) ^ HorizontalActive;
                hash = (hash * 397) ^ VerticalActive;
                hash = (hash * 397) ^ HorizontalBlanking;
                hash = (hash * 397) ^ VerticalBlanking;
                hash = (hash * 397) ^ Flags;
                return hash;
            }
        }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}x{1}@{2:0.###} Hz",
                HorizontalActive,
                VerticalActive,
                RefreshRateHertz);
        }

        private static int ReadLowHigh(byte low, byte high)
        {
            return low | (high << 8);
        }

        private static void ValidateBuffer(byte[] value, int offset)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (offset < 0 || offset > value.Length - EncodedLength)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
        }

        private static void ValidateRange(string name, int value, int minimum, int maximum)
        {
            if (value < minimum || value > maximum)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Value must be between {0} and {1}.",
                        minimum,
                        maximum));
            }
        }
    }
}
