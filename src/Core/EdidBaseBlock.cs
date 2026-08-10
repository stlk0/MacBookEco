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

        public bool DeclaresPreferredTiming => (_bytes[24] & 0x02) != 0;

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

        internal EdidBaseBlock InsertOrderedDetailedTiming(
            DetailedTiming timing)
        {
            if (timing == null)
            {
                throw new ArgumentNullException(nameof(timing));
            }

            if (ContainsDetailedTiming(timing))
            {
                return new EdidBaseBlock(_bytes);
            }

            if (!IsDetailedTimingDescriptor(0))
            {
                throw new InvalidOperationException(
                    "The preferred EDID descriptor is not a detailed timing.");
            }

            int freeDescriptorIndex = FindFreeDescriptor();
            if (freeDescriptorIndex < 0)
            {
                throw new InvalidOperationException(
                    "The EDID has no free non-preferred descriptor slot.");
            }

            int insertionIndex = -1;
            for (int descriptorIndex = 1;
                descriptorIndex < DescriptorCount;
                descriptorIndex++)
            {
                if (IsDetailedTimingDescriptor(descriptorIndex))
                {
                    if (insertionIndex >= 0)
                    {
                        throw new InvalidOperationException(
                            "Detailed timings must precede monitor descriptors.");
                    }

                    continue;
                }

                if (insertionIndex < 0)
                {
                    insertionIndex = descriptorIndex;
                }
            }

            if (insertionIndex < 0 || freeDescriptorIndex < insertionIndex)
            {
                throw new InvalidOperationException(
                    "The EDID descriptor layout cannot receive an ordered timing.");
            }

            var result = (byte[])_bytes.Clone();
            for (int descriptorIndex = freeDescriptorIndex;
                descriptorIndex > insertionIndex;
                descriptorIndex--)
            {
                Buffer.BlockCopy(
                    result,
                    GetDescriptorOffset(descriptorIndex - 1),
                    result,
                    GetDescriptorOffset(descriptorIndex),
                    DetailedTiming.EncodedLength);
            }

            timing.WriteTo(result, GetDescriptorOffset(insertionIndex));
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

        public static bool HasValidCompleteDocument(byte[] value)
        {
            if (value == null || value.Length < Length || value.Length % Length != 0)
            {
                return false;
            }

            int extensionCount = value[126];
            if (value.Length != (extensionCount + 1) * Length)
            {
                return false;
            }

            byte[] baseBlock = new byte[Length];
            Buffer.BlockCopy(value, 0, baseBlock, 0, baseBlock.Length);
            // EDID 1.3 requires descriptor forms (including range limits)
            // that this deliberately small validator does not parse. A
            // checksum-correct but only partially understood document is not
            // sufficient evidence for experimental timing generation.
            if (baseBlock[18] != 1 || baseBlock[19] != 4)
            {
                return false;
            }

            if (!HasSupportedBaseEncoding(baseBlock))
            {
                return false;
            }

            // EDID 1.4 requires descriptor zero to be a detailed timing.
            // The generator additionally requires feature bit one to declare
            // that timing as native/preferred for this panel.
            if (baseBlock[FirstDescriptorOffset] == 0 &&
                baseBlock[FirstDescriptorOffset + 1] == 0)
            {
                return false;
            }

            try
            {
                EdidBaseBlock parsedBase = new EdidBaseBlock(baseBlock);
                // ManufacturerCode is decoded lazily. Force every semantic
                // field consumed by the generator to be validated here.
                if (string.IsNullOrEmpty(parsedBase.HardwareId))
                {
                    return false;
                }

                if (!HasValidMonitorDescriptors(baseBlock))
                {
                    return false;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            int ctaCapabilityFlags = -1;
            for (int blockIndex = 1; blockIndex <= extensionCount; blockIndex++)
            {
                int sum = 0;
                int blockOffset = blockIndex * Length;
                for (int index = 0; index < Length; index++)
                {
                    sum = (sum + value[blockOffset + index]) & 0xFF;
                }

                if (sum != 0)
                {
                    return false;
                }

                if (!HasSupportedExtensionStructure(value, blockOffset))
                {
                    return false;
                }

                int currentCtaCapabilityFlags = value[blockOffset + 3];
                if (ctaCapabilityFlags >= 0 &&
                    ctaCapabilityFlags != currentCtaCapabilityFlags)
                {
                    return false;
                }

                ctaCapabilityFlags = currentCtaCapabilityFlags;
            }

            return true;
        }

        internal static bool HasValidCompleteDocumentWithReplacementBase(
            byte[] sourceDocument,
            byte[] replacementBase)
        {
            if (!HasValidCompleteDocument(sourceDocument) ||
                replacementBase == null ||
                replacementBase.Length != Length)
            {
                return false;
            }

            byte[] candidate = (byte[])sourceDocument.Clone();
            Buffer.BlockCopy(
                replacementBase,
                0,
                candidate,
                0,
                replacementBase.Length);
            return HasValidCompleteDocument(candidate);
        }

        public static Sha256Digest ComputeNormalizedDocumentSignature(
            byte[] value)
        {
            if (!HasValidCompleteDocument(value))
            {
                throw new FormatException(
                    "The complete EDID document is invalid or unsupported.");
            }

            byte[] normalized = (byte[])value.Clone();
            NormalizeBaseIdentity(normalized);
            return Sha256Digest.Compute(normalized);
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
            NormalizeBaseIdentity(normalized);
            return Sha256Digest.Compute(normalized);
        }

        private static void NormalizeBaseIdentity(byte[] normalized)
        {
            // Serial number and manufacturing week/year vary between otherwise
            // identical panels and are not part of a reviewed timing profile.
            for (var index = 12; index <= 17; index++)
            {
                normalized[index] = 0;
            }

            // Only the preferred base-block DTD participates from the base
            // descriptor area. Text descriptors and the application-added
            // secondary DTD do not; complete-document extension bytes remain
            // part of the document signature.
            for (
                var index = FirstDescriptorOffset + DetailedTiming.EncodedLength;
                index < 126;
                index++)
            {
                normalized[index] = 0;
            }

            normalized[127] = 0;
        }

        private static bool HasSupportedExtensionStructure(
            byte[] document,
            int offset)
        {
            // The restricted generator accepts only CTA extension blocks. An
            // unfamiliar checksum-correct extension is not enough evidence
            // that the complete source document was parsed correctly.
            if (document[offset] != 0x02)
            {
                return false;
            }

            int revision = document[offset + 1];
            int detailedTimingOffset = document[offset + 2];
            // CTA revisions 1 and 2 have legacy/deprecated semantics that are
            // unnecessary for the exact modern-panel allowlist. Accept only
            // revision 3 rather than interpreting their shared-looking fields
            // under revision-3 rules.
            if (revision != 3)
            {
                return false;
            }

            if (detailedTimingOffset == 0)
            {
                return (document[offset + 3] & 0x0F) == 0 &&
                    IsZeroRange(document, offset + 4, offset + 127);
            }

            if (detailedTimingOffset < 4 || detailedTimingOffset > 127)
            {
                return false;
            }

            int detailedTimingStart = offset + detailedTimingOffset;
            // CTA data blocks have many revision- and tag-specific semantic
            // constraints. The restricted generator does not need them, so a
            // nonempty collection remains unsupported instead of receiving a
            // shallow structural check that could bless malformed payloads.
            if (detailedTimingStart != offset + 4)
            {
                return false;
            }

            int descriptorIndex = detailedTimingStart;
            int descriptorEnd = offset + 127;
            int detailedTimingCount = 0;
            while (descriptorIndex + DetailedTiming.EncodedLength <=
                descriptorEnd)
            {
                if (document[descriptorIndex] == 0 &&
                    document[descriptorIndex + 1] == 0)
                {
                    return (document[offset + 3] & 0x0F) <=
                            detailedTimingCount &&
                        IsZeroRange(
                            document,
                            descriptorIndex,
                            descriptorEnd);
                }

                try
                {
                    DetailedTiming.Parse(document, descriptorIndex);
                }
                catch (FormatException)
                {
                    return false;
                }

                detailedTimingCount++;
                descriptorIndex += DetailedTiming.EncodedLength;
            }

            int declaredNativeTimingCount = document[offset + 3] & 0x0F;
            return declaredNativeTimingCount <= detailedTimingCount &&
                IsZeroRange(document, descriptorIndex, descriptorEnd);
        }

        private static bool IsZeroRange(byte[] value, int start, int endExclusive)
        {
            for (int index = start; index < endExclusive; index++)
            {
                if (value[index] != 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasValidMonitorDescriptors(byte[] baseBlock)
        {
            bool monitorDescriptorSeen = false;
            for (int descriptorIndex = 0;
                descriptorIndex < DescriptorCount;
                descriptorIndex++)
            {
                int offset = GetDescriptorOffset(descriptorIndex);
                if (baseBlock[offset] != 0 || baseBlock[offset + 1] != 0)
                {
                    // Base-block DTDs must precede monitor descriptors.
                    if (monitorDescriptorSeen)
                    {
                        return false;
                    }

                    continue;
                }

                if (IsZeroRange(
                        baseBlock,
                        offset,
                        offset + DetailedTiming.EncodedLength))
                {
                    // EDID data-fill must use the defined 10h dummy
                    // descriptor. An all-zero base descriptor is not valid.
                    return false;
                }

                monitorDescriptorSeen = true;

                if (baseBlock[offset + 2] != 0)
                {
                    return false;
                }

                byte tag = baseBlock[offset + 3];
                if (tag == 0x10)
                {
                    if (!IsZeroRange(
                            baseBlock,
                            offset + 4,
                            offset + DetailedTiming.EncodedLength))
                    {
                        return false;
                    }

                    continue;
                }

                if (baseBlock[offset + 4] != 0 ||
                    !IsSupportedMonitorDescriptorTag(tag))
                {
                    return false;
                }

                if (tag == 0xFC || tag == 0xFE || tag == 0xFF)
                {
                    bool terminated = false;
                    for (int index = offset + 5;
                        index < offset + DetailedTiming.EncodedLength;
                        index++)
                    {
                        byte value = baseBlock[index];
                        if (terminated)
                        {
                            if (value != 0x20)
                            {
                                return false;
                            }

                            continue;
                        }

                        if (value == 0x0A)
                        {
                            terminated = true;
                        }
                        else if (value < 0x20 || value > 0x7E)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private static bool HasSupportedBaseEncoding(byte[] baseBlock)
        {
            // Bit 15 of the big-endian PNP manufacturer word is reserved.
            if ((baseBlock[8] & 0x80) != 0)
            {
                return false;
            }

            byte manufactureWeek = baseBlock[16];
            if ((manufactureWeek > 0x36 && manufactureWeek != 0xFF) ||
                baseBlock[17] < 0x10)
            {
                return false;
            }

            byte videoInput = baseBlock[20];
            bool digital = (videoInput & 0x80) != 0;
            // The runtime allowlist contains only digital internal panels. An
            // analog declaration is a valid EDID form but is unsupported here
            // rather than receiving a partial analog-signal validation.
            if (!digital)
            {
                return false;
            }

            int colorBitDepth = (videoInput >> 4) & 0x07;
            int interfaceType = videoInput & 0x0F;
            if (colorBitDepth == 0x07 || interfaceType > 0x05)
            {
                return false;
            }

            // FF delegates gamma to an extension type that this validator does
            // not accept. Likewise, validating the standard-sRGB declaration
            // requires an exact chromaticity tuple; keep that form outside the
            // deliberately narrow generator subset for now.
            if (baseBlock[23] == 0xFF || (baseBlock[24] & 0x04) != 0)
            {
                return false;
            }

            // EDID 1.4 feature bit zero declares a continuous-frequency
            // display and requires a range-limits descriptor. Range limits
            // remain outside this restricted parser, so that form fails
            // closed instead of being only partially validated.
            if ((baseBlock[24] & 0x01) != 0)
            {
                return false;
            }

            // Bit seven is the defined Apple Macintosh II 1152x870 timing.
            // The lower manufacturer-specific timing bits have no portable
            // semantics for this restricted parser, so they fail closed.
            if ((baseBlock[37] & 0x7F) != 0)
            {
                return false;
            }

            for (int offset = 38; offset < 54; offset += 2)
            {
                byte horizontalCode = baseBlock[offset];
                byte shapeAndRefresh = baseBlock[offset + 1];
                bool unused = horizontalCode == 0x01 &&
                    shapeAndRefresh == 0x01;
                if (unused)
                {
                    continue;
                }

                // 00 is reserved. 01 is also the minimum valid 256-pixel
                // code when it is not the canonical 01/01 unused pair.
                if (horizontalCode == 0x00)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSupportedMonitorDescriptorTag(byte tag)
        {
            switch (tag)
            {
                case 0xFC:
                case 0xFE:
                case 0xFF:
                    return true;
                default:
                    return false;
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
