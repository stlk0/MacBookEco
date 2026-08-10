using System;
using System.Security.Cryptography;

namespace MacBookEco.Core
{
    /// <summary>
    /// Immutable SHA-256 value with one wire representation: 64 upper-case
    /// hexadecimal characters without separators.  Journal code deliberately
    /// does not accept the more permissive display-oriented hex formats.
    /// </summary>
    public sealed class Sha256Digest : IEquatable<Sha256Digest>
    {
        public const int ByteLength = 32;
        public const int CanonicalHexLength = ByteLength * 2;

        private readonly byte[] _bytes;
        private readonly string _canonicalHex;

        private Sha256Digest(byte[] bytes)
        {
            _bytes = (byte[])bytes.Clone();
            _canonicalHex = HexCodec.Format(_bytes);
        }

        public static Sha256Digest Compute(byte[] value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            using (SHA256 algorithm = SHA256.Create())
            {
                return FromBytes(algorithm.ComputeHash(value));
            }
        }

        public static Sha256Digest FromBytes(byte[] value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value.Length != ByteLength)
            {
                throw new ArgumentException(
                    "A SHA-256 digest must contain exactly 32 bytes.",
                    nameof(value));
            }

            return new Sha256Digest(value);
        }

        public static Sha256Digest ParseHex(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new FormatException("A SHA-256 digest is required.");

            byte[] bytes = HexCodec.Parse(value);
            if (bytes.Length != ByteLength)
            {
                throw new FormatException(
                    "A SHA-256 digest must contain exactly 32 bytes.");
            }

            return FromBytes(bytes);
        }

        public static Sha256Digest ParseCanonical(string value)
        {
            Sha256Digest result;
            if (!TryParseCanonical(value, out result))
            {
                throw new FormatException(
                    "A SHA-256 digest must be 64 upper-case hexadecimal characters.");
            }

            return result;
        }

        public static bool TryParseCanonical(string value, out Sha256Digest result)
        {
            result = null;
            if (value == null || value.Length != CanonicalHexLength)
            {
                return false;
            }

            var bytes = new byte[ByteLength];
            for (var index = 0; index < ByteLength; index++)
            {
                int high = ParseUpperHexNibble(value[index * 2]);
                int low = ParseUpperHexNibble(value[(index * 2) + 1]);
                if (high < 0 || low < 0)
                {
                    return false;
                }

                bytes[index] = (byte)((high << 4) | low);
            }

            result = new Sha256Digest(bytes);
            return true;
        }

        public bool Equals(Sha256Digest other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (ReferenceEquals(other, null))
            {
                return false;
            }

            return FixedTimeComparer.AreEqual(_bytes, other._bytes);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Sha256Digest);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                for (var index = 0; index < 4; index++)
                {
                    hash = (hash * 31) + _bytes[index];
                }

                return hash;
            }
        }

        public override string ToString()
        {
            return _canonicalHex;
        }

        // Load-bearing beyond value comparison: several journal validators are
        // written as "hash == null", so these operators define what that null
        // check means. Changing them changes those checks.
        public static bool operator ==(Sha256Digest left, Sha256Digest right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (ReferenceEquals(left, null) || ReferenceEquals(right, null))
            {
                return false;
            }

            return left.Equals(right);
        }

        public static bool operator !=(Sha256Digest left, Sha256Digest right)
        {
            return !(left == right);
        }

        private static int ParseUpperHexNibble(char value)
        {
            if (value >= '0' && value <= '9')
            {
                return value - '0';
            }

            if (value >= 'A' && value <= 'F')
            {
                return (value - 'A') + 10;
            }

            return -1;
        }
    }
}
