using System;
using System.Globalization;
using System.Text;

namespace MacBookEco.Core
{
    public static class HexCodec
    {
        public static byte[] Parse(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var compact = new StringBuilder(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                var current = value[index];
                if (Uri.IsHexDigit(current))
                {
                    compact.Append(current);
                }
                else if (!char.IsWhiteSpace(current) && current != '-' && current != ':')
                {
                    throw new FormatException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Unexpected character '{0}' in hexadecimal input.",
                            current));
                }
            }

            if ((compact.Length & 1) != 0)
            {
                throw new FormatException("Hexadecimal input must contain a whole number of bytes.");
            }

            var result = new byte[compact.Length / 2];
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = (byte)(
                    (ParseHexDigit(compact[index * 2]) << 4)
                    | ParseHexDigit(compact[(index * 2) + 1]));
            }

            return result;
        }

        // Every character reaching this has already passed Uri.IsHexDigit.
        // The obvious alternative, byte.Parse over a two-character substring,
        // allocates a string per byte, which for a 128-byte EDID is 128
        // throwaway strings on a path the display resolver runs repeatedly.
        private static int ParseHexDigit(char value)
        {
            if (value >= '0' && value <= '9')
            {
                return value - '0';
            }

            if (value >= 'A' && value <= 'F')
            {
                return (value - 'A') + 10;
            }

            return (value - 'a') + 10;
        }

        public static string Format(byte[] value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var result = new StringBuilder(value.Length * 2);
            for (var index = 0; index < value.Length; index++)
            {
                result.Append(value[index].ToString("X2", CultureInfo.InvariantCulture));
            }

            return result.ToString();
        }
    }
}
