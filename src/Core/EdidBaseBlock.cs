using System;
using System.Collections.Generic;
using System.Globalization;

namespace MacBookEco.Core
{
    public sealed class EdidBaseBlock
    {
        public const int Length = 128;
        public const int DescriptorCount = 4;
        public const int FirstDescriptorOffset = 54;

        private static readonly byte[] ExpectedHeader =
        {
            0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00
        };

        private readonly byte[] _bytes;
        private Sha256Digest _normalizedSignature;

        public EdidBaseBlock(byte[] value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value.Length != Length)
            {
                throw new ArgumentException(
                    "An EDID base block must contain exactly 128 bytes.",
                    nameof(value));
            }

            _bytes = (byte[])value.Clone();
            ValidateHeader(_bytes);
            ValidateChecksum(_bytes);
            ValidateDetailedTimingDescriptors(_bytes);
        }

        public string ManufacturerCode
        {
            get
            {
                var encoded = (_bytes[8] << 8) | _bytes[9];
                return new string(
                    new[]
                    {
                        DecodeManufacturerCharacter((encoded >> 10) & 0x1F),
                        DecodeManufacturerCharacter((encoded >> 5) & 0x1F),
                        DecodeManufacturerCharacter(encoded & 0x1F)
                    });
            }
        }

        public ushort ProductCode => (ushort)(_bytes[10] | (_bytes[11] << 8));

        public string HardwareId => ManufacturerCode +
                    ProductCode.ToString("X4", CultureInfo.InvariantCulture);

        public byte ExtensionBlockCount => _bytes[126];

        /// <summary>
        /// These bytes were validated when this block was constructed and are
        /// never mutated afterwards, so the signature is computed once. It
        /// used to be recomputed on every read, and each read rebuilt an
        /// entire EdidBaseBlock, re-running the header, checksum and all four
        /// descriptor validations before hashing. A single profile match reads
        /// it several times.
        /// </summary>
        public Sha256Digest NormalizedSignature
        {
            get
            {
                if (_normalizedSignature == null)
                {
                    _normalizedSignature = NormalizeAndHash(ToByteArray());
                }

                return _normalizedSignature;
            }
        }

        public DetailedTiming PreferredTiming
        {
            get
            {
                if (!IsDetailedTimingDescriptor(0))
                {
                    throw new InvalidOperationException(
                        "The preferred descriptor is not a detailed timing descriptor.");
                }

                return GetDetailedTiming(0);
            }
        }

        public static EdidBaseBlock ParseHex(string value)
        {
            return new EdidBaseBlock(HexCodec.Parse(value));
        }

        public byte[] ToByteArray()
        {
            return (byte[])_bytes.Clone();
        }

        public byte[] ToPublicProfileFixture()
        {
            byte[] sanitized = ToByteArray();
            ClearProfileVariantData(sanitized);
            UpdateChecksum(sanitized);
            return sanitized;
        }

        public bool IsDetailedTimingDescriptor(int descriptorIndex)
        {
            var offset = GetDescriptorOffset(descriptorIndex);
            return _bytes[offset] != 0 || _bytes[offset + 1] != 0;
        }

        public DetailedTiming GetDetailedTiming(int descriptorIndex)
        {
            var offset = GetDescriptorOffset(descriptorIndex);
            if (!IsDetailedTimingDescriptor(descriptorIndex))
            {
                throw new InvalidOperationException(
                    "The selected descriptor is not a detailed timing descriptor.");
            }

            return DetailedTiming.Parse(_bytes, offset);
        }

        public bool IsFreeDescriptor(int descriptorIndex)
        {
            if (descriptorIndex == 0)
            {
                return false;
            }

            var offset = GetDescriptorOffset(descriptorIndex);
            var allZero = true;
            for (var index = 0; index < DetailedTiming.EncodedLength; index++)
            {
                if (_bytes[offset + index] != 0)
                {
                    allZero = false;
                    break;
                }
            }

            if (allZero)
            {
                return true;
            }

            if (
                _bytes[offset] != 0 ||
                _bytes[offset + 1] != 0 ||
                _bytes[offset + 2] != 0 ||
                _bytes[offset + 3] != 0x10 ||
                _bytes[offset + 4] != 0)
            {
                return false;
            }

            for (var index = 5; index < DetailedTiming.EncodedLength; index++)
            {
                if (_bytes[offset + index] != 0)
                {
                    return false;
                }
            }

            return true;
        }

        public int FindFreeDescriptor()
        {
            for (var descriptorIndex = 1; descriptorIndex < DescriptorCount; descriptorIndex++)
            {
                if (IsFreeDescriptor(descriptorIndex))
                {
                    return descriptorIndex;
                }
            }

            return -1;
        }

        public bool ContainsDetailedTiming(DetailedTiming timing)
        {
            if (timing == null)
            {
                throw new ArgumentNullException(nameof(timing));
            }

            for (var descriptorIndex = 0; descriptorIndex < DescriptorCount; descriptorIndex++)
            {
                if (
                    IsDetailedTimingDescriptor(descriptorIndex) &&
                    GetDetailedTiming(descriptorIndex).Equals(timing))
                {
                    return true;
                }
            }

            return false;
        }

        public EdidBaseBlock InsertDetailedTiming(DetailedTiming timing)
        {
            if (timing == null)
            {
                throw new ArgumentNullException(nameof(timing));
            }

            if (ContainsDetailedTiming(timing))
            {
                return new EdidBaseBlock(_bytes);
            }

            var descriptorIndex = FindFreeDescriptor();
            if (descriptorIndex < 0)
            {
                throw new InvalidOperationException(
                    "The EDID has no free non-preferred descriptor slot.");
            }

            var result = (byte[])_bytes.Clone();
            timing.WriteTo(result, GetDescriptorOffset(descriptorIndex));
            UpdateChecksum(result);
            return new EdidBaseBlock(result);
        }

        public static bool HasValidChecksum(byte[] value)
        {
            if (value == null || value.Length != Length)
            {
                return false;
            }

            var sum = 0;
            for (var index = 0; index < value.Length; index++)
            {
                sum = (sum + value[index]) & 0xFF;
            }

            return sum == 0;
        }

        public static void UpdateChecksum(byte[] value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value.Length != Length)
            {
                throw new ArgumentException(
                    "An EDID base block must contain exactly 128 bytes.",
                    nameof(value));
            }

            value[127] = 0;
            var sum = 0;
            for (var index = 0; index < 127; index++)
            {
                sum = (sum + value[index]) & 0xFF;
            }

            value[127] = (byte)((256 - sum) & 0xFF);
        }

        private static Sha256Digest NormalizeAndHash(byte[] normalized)
        {
            ClearProfileVariantData(normalized);
            normalized[127] = 0;
            return Sha256Digest.Compute(normalized);
        }

        private static void ClearProfileVariantData(byte[] value)
        {
            // Serial number and manufacturing week/year vary between otherwise
            // identical panels and are not part of a reviewed timing profile.
            for (var index = 12; index <= 17; index++)
            {
                value[index] = 0;
            }

            // Only the preferred native DTD participates in the signature.
            // Text descriptors and the application-added secondary DTD do not.
            for (
                var index = FirstDescriptorOffset + DetailedTiming.EncodedLength;
                index < 126;
                index++)
            {
                value[index] = 0;
            }
        }

        private static int GetDescriptorOffset(int descriptorIndex)
        {
            if (descriptorIndex < 0 || descriptorIndex >= DescriptorCount)
            {
                throw new ArgumentOutOfRangeException(nameof(descriptorIndex));
            }

            return FirstDescriptorOffset +
                (descriptorIndex * DetailedTiming.EncodedLength);
        }

        private static void ValidateHeader(byte[] value)
        {
            for (var index = 0; index < ExpectedHeader.Length; index++)
            {
                if (value[index] != ExpectedHeader[index])
                {
                    throw new FormatException("The EDID header is invalid.");
                }
            }
        }

        private static void ValidateChecksum(byte[] value)
        {
            if (!HasValidChecksum(value))
            {
                throw new FormatException("The EDID base-block checksum is invalid.");
            }
        }

        private static void ValidateDetailedTimingDescriptors(byte[] value)
        {
            for (var descriptorIndex = 0; descriptorIndex < DescriptorCount; descriptorIndex++)
            {
                var offset = GetDescriptorOffset(descriptorIndex);
                if (value[offset] != 0 || value[offset + 1] != 0)
                {
                    DetailedTiming.Parse(value, offset);
                }
            }
        }

        private static char DecodeManufacturerCharacter(int value)
        {
            if (value < 1 || value > 26)
            {
                throw new FormatException("The EDID manufacturer identifier is invalid.");
            }

            return (char)('A' + value - 1);
        }
    }
}
