using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace MacBookEco.Core
{
    /// <summary>
    /// The result of applying every pure hardware, EDID, and timing gate for an
    /// experimental 48 Hz profile. Platform gates such as topology ambiguity and
    /// registry ownership are deliberately enforced by the Windows layer.
    /// </summary>
    public sealed class ExperimentalProfileGenerationResult
    {
        internal ExperimentalProfileGenerationResult(
            DisplayProfile profile,
            string hardwareKey,
            IList<string> rejectionReasons)
        {
            if (rejectionReasons == null)
            {
                throw new ArgumentNullException(nameof(rejectionReasons));
            }

            Profile = profile;
            HardwareKey = hardwareKey ?? string.Empty;
            RejectionReasons = new ReadOnlyCollection<string>(
                new List<string>(rejectionReasons));
        }

        public DisplayProfile Profile { get; private set; }

        /// <summary>
        /// Short allowlist key included in the deterministic profile ID. It is
        /// an identifier, not authorization supplied by an unelevated caller.
        /// </summary>
        public string HardwareKey { get; private set; }

        public ReadOnlyCollection<string> RejectionReasons { get; private set; }

        public bool Succeeded => Profile != null && RejectionReasons.Count == 0;
    }

    /// <summary>
    /// Generates only the narrowly allowlisted experimental 48 Hz candidate.
    /// It does not authorize an install and never accepts caller-provided timing
    /// bytes. The Admin helper can repeat the same calculation from fresh state.
    /// </summary>
    public static class Experimental48HzProfileGenerator
    {
        private const string ProfileVersion = "exp48-v1";
        private const string ProfileIdPrefix = ProfileVersion + "-";
        private const string AcknowledgementDomain =
            "MacBookEco experimental 48 Hz acknowledgement v1";
        private const int TargetRefreshHertz = 48;
        private const int MinimumNativeRefreshHertz = 59;
        private const int MaximumNativeRefreshHertz = 61;
        private const int MaximumProfileIdLength = 96;
        private const int MaximumHardwareKeyLength = 22;

        // Keep this runtime list intentionally smaller than the research
        // catalog. A row belongs here only when the current discovery path can
        // identify both the exact SMBIOS model and controlling discrete GPU.
        private static readonly HardwareDefinition[] _hardwareAllowlist =
        {
            new HardwareDefinition(
                "mbp161-7340",
                "MacBookPro16,1",
                "1002",
                "7340",
                "AMD Radeon Pro"),
            new HardwareDefinition(
                "mbp164-7360",
                "MacBookPro16,4",
                "1002",
                "7360",
                "AMD Radeon Pro")
        };

        public static ExperimentalProfileGenerationResult Generate(
            HardwareSnapshot hardware)
        {
            if (hardware == null)
            {
                throw new ArgumentNullException(nameof(hardware));
            }

            var rejectionReasons = new List<string>();

            if (!string.Equals(
                    hardware.SystemManufacturer,
                    "Apple Inc.",
                    StringComparison.Ordinal))
            {
                rejectionReasons.Add(
                    "The SMBIOS manufacturer is not exactly Apple Inc.");
            }

            if (!hardware.IsInternalDisplay)
            {
                rejectionReasons.Add(
                    "The detected display is not the unique internal panel.");
            }

            if (!hardware.CompleteEdidIsValid)
            {
                rejectionReasons.Add(
                    "The complete EDID document was not validated.");
            }

            if (hardware.NormalizedSourceEdidSignature == null)
            {
                rejectionReasons.Add(
                    "The normalized complete EDID signature is unavailable.");
            }

            string canonicalGpuDeviceId;
            HardwareDefinition definition = FindHardware(
                hardware.SystemModel,
                hardware.GpuDeviceId,
                rejectionReasons,
                out canonicalGpuDeviceId);

            string panelHardwareId = hardware.Edid.HardwareId;
            if (!string.Equals(
                    hardware.PanelHardwareId,
                    panelHardwareId,
                    StringComparison.OrdinalIgnoreCase))
            {
                rejectionReasons.Add(
                    "The detected panel ID does not match the EDID identity.");
            }

            DetailedTiming nativeTiming = ReadPreferredTiming(
                hardware.Edid,
                rejectionReasons);
            DetailedTiming targetTiming = CreateTargetTiming(
                nativeTiming,
                rejectionReasons);

            if (hardware.Edid.FindFreeDescriptor() < 0)
            {
                rejectionReasons.Add(
                    "The base EDID has no free non-preferred descriptor slot.");
            }

            if (
                targetTiming != null &&
                hardware.Edid.ContainsDetailedTiming(targetTiming))
            {
                rejectionReasons.Add(
                    "The generated 48 Hz target already exists in the base EDID.");
            }

            if (
                rejectionReasons.Count != 0 ||
                definition == null ||
                nativeTiming == null ||
                targetTiming == null)
            {
                return Rejected(definition, rejectionReasons);
            }

            DisplayProfile profile = CreateProfile(
                definition,
                panelHardwareId,
                hardware.Edid.NormalizedSignature,
                hardware.NormalizedSourceEdidSignature,
                nativeTiming,
                targetTiming,
                canonicalGpuDeviceId);
            return Accepted(profile, definition.Key);
        }

        /// <summary>
        /// Production fallback entry point. Platform callers invoke this only
        /// after their topology and ownership gates, and a matching reviewed
        /// catalog profile always suppresses local generation.
        /// </summary>
        public static ExperimentalProfileGenerationResult GenerateFallback(
            HardwareSnapshot hardware)
        {
            if (hardware == null)
            {
                throw new ArgumentNullException(nameof(hardware));
            }

            if (hardware.Edid.IsDetailedTimingDescriptor(0) &&
                ProfileCatalog.Select(hardware).HardwareSupported)
            {
                return Rejected(
                    null,
                    "A reviewed display profile has priority over generation.");
            }

            return Generate(hardware);
        }

        /// <summary>
        /// Recognizes only the canonical ID shape. It does not establish that
        /// the key is allowlisted or that the digest matches current hardware.
        /// </summary>
        public static bool IsExperimentalProfileId(string profileId)
        {
            string hardwareKey;
            return TryParseProfileId(profileId, out hardwareKey);
        }

        /// <summary>
        /// Creates a bounded, comparison-only token for explicit consent to
        /// one generated profile. The token cannot choose a timing, monitor,
        /// registry path, or executable in the elevated helper.
        /// </summary>
        public static string CreateAcknowledgementToken(string profileId)
        {
            if (!IsExperimentalProfileId(profileId))
            {
                throw new ArgumentException(
                    "A canonical experimental profile ID is required.",
                    nameof(profileId));
            }

            var payload = new List<byte>();
            AppendCanonicalField(
                payload,
                Encoding.UTF8.GetBytes(AcknowledgementDomain));
            AppendCanonicalField(payload, Encoding.UTF8.GetBytes(profileId));
            return Sha256Digest.Compute(payload.ToArray()).ToString();
        }

        public static bool AcknowledgementTokenMatches(
            string profileId,
            string acknowledgementToken)
        {
            Sha256Digest supplied;
            if (!IsExperimentalProfileId(profileId) ||
                !Sha256Digest.TryParseCanonical(
                    acknowledgementToken,
                    out supplied))
            {
                return false;
            }

            Sha256Digest expected = Sha256Digest.ParseCanonical(
                CreateAcknowledgementToken(profileId));
            return expected.Equals(supplied);
        }

        /// <summary>
        /// Recreates an experimental profile for recovery without relying on an
        /// active GPU endpoint or the discovery-time complete-EDID flag. The
        /// allowlist key is recovered from the untrusted ID, then the complete
        /// ID is recomputed and compared before a profile is returned.
        /// </summary>
        public static ExperimentalProfileGenerationResult ResolveForRecovery(
            string profileId,
            string systemModel,
            string panelHardwareId,
            EdidBaseBlock baseEdid,
            Sha256Digest sourceEdidSignature)
        {
            string hardwareKey;
            if (!TryParseProfileId(profileId, out hardwareKey))
            {
                return Rejected(
                    null,
                    "The experimental profile ID is not canonical.");
            }

            return ResolveForRecovery(
                profileId,
                hardwareKey,
                systemModel,
                panelHardwareId,
                baseEdid,
                sourceEdidSignature);
        }

        /// <summary>
        /// Recovery overload for callers that persisted the short allowlist key
        /// separately from the profile ID. Both values must agree.
        /// </summary>
        public static ExperimentalProfileGenerationResult ResolveForRecovery(
            string profileId,
            string hardwareKey,
            string systemModel,
            string panelHardwareId,
            EdidBaseBlock baseEdid,
            Sha256Digest sourceEdidSignature)
        {
            var rejectionReasons = new List<string>();
            string parsedHardwareKey;
            if (!TryParseProfileId(profileId, out parsedHardwareKey))
            {
                rejectionReasons.Add(
                    "The experimental profile ID is not canonical.");
            }
            else if (!string.Equals(
                    parsedHardwareKey,
                    hardwareKey,
                    StringComparison.Ordinal))
            {
                rejectionReasons.Add(
                    "The persisted hardware key does not match the profile ID.");
            }

            HardwareDefinition definition = FindHardwareByKey(parsedHardwareKey);
            if (definition == null)
            {
                rejectionReasons.Add(
                    "The experimental hardware key is not allowlisted.");
            }
            else if (!string.Equals(
                    definition.SystemModel,
                    systemModel,
                    StringComparison.Ordinal))
            {
                rejectionReasons.Add(
                    "The SMBIOS model does not match the experimental profile.");
            }

            if (baseEdid == null)
            {
                rejectionReasons.Add("The recovery base EDID is unavailable.");
                return Rejected(definition, rejectionReasons);
            }

            if (sourceEdidSignature == null)
            {
                rejectionReasons.Add(
                    "The normalized source EDID signature is unavailable.");
            }

            string normalizedPanelHardwareId =
                HardwareSnapshot.NormalizePanelHardwareId(panelHardwareId);
            if (!string.Equals(
                    normalizedPanelHardwareId,
                    baseEdid.HardwareId,
                    StringComparison.OrdinalIgnoreCase))
            {
                rejectionReasons.Add(
                    "The recovery panel ID does not match the EDID identity.");
            }

            DetailedTiming nativeTiming = ReadPreferredTiming(
                baseEdid,
                rejectionReasons);
            DetailedTiming targetTiming = CreateTargetTiming(
                nativeTiming,
                rejectionReasons);

            bool targetAlreadyPresent =
                targetTiming != null &&
                baseEdid.ContainsDetailedTiming(targetTiming);
            if (!targetAlreadyPresent && baseEdid.FindFreeDescriptor() < 0)
            {
                rejectionReasons.Add(
                    "The recovery EDID has neither the exact target nor a free slot.");
            }

            if (
                definition == null ||
                sourceEdidSignature == null ||
                nativeTiming == null ||
                targetTiming == null)
            {
                return Rejected(definition, rejectionReasons);
            }

            DisplayProfile profile = CreateProfile(
                definition,
                baseEdid.HardwareId,
                baseEdid.NormalizedSignature,
                sourceEdidSignature,
                nativeTiming,
                targetTiming,
                definition.CanonicalGpuDeviceId);
            if (!string.Equals(profile.Id, profileId, StringComparison.Ordinal))
            {
                rejectionReasons.Add(
                    "The recomputed experimental profile ID does not match.");
            }

            if (rejectionReasons.Count != 0)
            {
                return Rejected(definition, rejectionReasons);
            }

            return Accepted(profile, definition.Key);
        }

        private static HardwareDefinition FindHardware(
            string systemModel,
            string gpuDeviceId,
            IList<string> rejectionReasons,
            out string canonicalGpuDeviceId)
        {
            canonicalGpuDeviceId = string.Empty;
            bool modelIsAllowlisted = false;
            for (int index = 0; index < _hardwareAllowlist.Length; index++)
            {
                if (string.Equals(
                        _hardwareAllowlist[index].SystemModel,
                        systemModel,
                        StringComparison.Ordinal))
                {
                    modelIsAllowlisted = true;
                    break;
                }
            }

            if (!modelIsAllowlisted)
            {
                rejectionReasons.Add(
                    "The exact SMBIOS model is not in the experimental allowlist.");
                return null;
            }

            if (!TryCanonicalizePciDeviceId(
                    gpuDeviceId,
                    out canonicalGpuDeviceId))
            {
                rejectionReasons.Add(
                    "The controlling GPU does not have a canonical PCI VEN/DEV ID.");
                return null;
            }

            for (int index = 0; index < _hardwareAllowlist.Length; index++)
            {
                HardwareDefinition candidate = _hardwareAllowlist[index];
                if (
                    string.Equals(
                        candidate.SystemModel,
                        systemModel,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        candidate.CanonicalGpuDeviceId,
                        canonicalGpuDeviceId,
                        StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            rejectionReasons.Add(
                "The controlling discrete GPU is not allowlisted for this model.");
            return null;
        }

        private static HardwareDefinition FindHardwareByKey(string hardwareKey)
        {
            if (string.IsNullOrEmpty(hardwareKey))
            {
                return null;
            }

            for (int index = 0; index < _hardwareAllowlist.Length; index++)
            {
                if (string.Equals(
                        _hardwareAllowlist[index].Key,
                        hardwareKey,
                        StringComparison.Ordinal))
                {
                    return _hardwareAllowlist[index];
                }
            }

            return null;
        }

        private static DetailedTiming ReadPreferredTiming(
            EdidBaseBlock edid,
            IList<string> rejectionReasons)
        {
            if (!edid.DeclaresPreferredTiming)
            {
                rejectionReasons.Add(
                    "The EDID does not declare descriptor zero as preferred.");
            }

            if (!edid.IsDetailedTimingDescriptor(0))
            {
                rejectionReasons.Add(
                    "The preferred EDID descriptor is not a native DTD.");
                return null;
            }

            return edid.GetDetailedTiming(0);
        }

        private static DetailedTiming CreateTargetTiming(
            DetailedTiming nativeTiming,
            IList<string> rejectionReasons)
        {
            if (nativeTiming == null)
            {
                return null;
            }

            if ((nativeTiming.Flags & 0x80) != 0)
            {
                rejectionReasons.Add(
                    "The preferred native timing is interlaced.");
            }

            if (
                nativeTiming.HorizontalSyncPulseWidth <= 0 ||
                nativeTiming.VerticalSyncPulseWidth <= 0 ||
                nativeTiming.HorizontalSyncOffset +
                    nativeTiming.HorizontalSyncPulseWidth >
                    nativeTiming.HorizontalBlanking ||
                nativeTiming.VerticalSyncOffset +
                    nativeTiming.VerticalSyncPulseWidth >
                    nativeTiming.VerticalBlanking)
            {
                rejectionReasons.Add(
                    "The preferred native timing has invalid sync geometry.");
            }

            long nativePixelClockHertz;
            long nativeTotalPixels;
            long targetVerticalTotal;
            long targetVerticalBlanking;
            long targetPixelClock10Khz;
            try
            {
                checked
                {
                    nativePixelClockHertz =
                        nativeTiming.PixelClock10Khz * 10000L;
                    nativeTotalPixels =
                        nativeTiming.HorizontalTotal *
                        (long)nativeTiming.VerticalTotal;

                    long minimumNativeClock =
                        nativeTotalPixels * MinimumNativeRefreshHertz;
                    long maximumNativeClock =
                        nativeTotalPixels * MaximumNativeRefreshHertz;
                    if (
                        nativePixelClockHertz < minimumNativeClock ||
                        nativePixelClockHertz > maximumNativeClock)
                    {
                        rejectionReasons.Add(
                            "The preferred native refresh is outside 59-61 Hz.");
                        return null;
                    }

                    long targetDenominator =
                        nativeTiming.HorizontalTotal *
                        (long)TargetRefreshHertz;
                    targetVerticalTotal =
                        nativePixelClockHertz / targetDenominator;
                    targetVerticalBlanking =
                        targetVerticalTotal - nativeTiming.VerticalActive;

                    long targetClockNumerator =
                        nativeTiming.HorizontalTotal *
                        targetVerticalTotal *
                        TargetRefreshHertz;
                    targetPixelClock10Khz =
                        (targetClockNumerator + 5000L) / 10000L;
                }
            }
            catch (OverflowException)
            {
                rejectionReasons.Add(
                    "The 48 Hz timing calculation overflowed.");
                return null;
            }

            if (
                targetVerticalTotal <= nativeTiming.VerticalTotal ||
                targetVerticalBlanking <= nativeTiming.VerticalBlanking)
            {
                rejectionReasons.Add(
                    "The 48 Hz candidate would not increase only the " +
                    "vertical back porch.");
                return null;
            }

            if (targetVerticalBlanking < 1 || targetVerticalBlanking > 4095)
            {
                rejectionReasons.Add(
                    "The 48 Hz vertical blanking is outside the DTD range.");
                return null;
            }

            if (targetPixelClock10Khz < 1 || targetPixelClock10Khz > 65535)
            {
                rejectionReasons.Add(
                    "The 48 Hz pixel clock is outside the DTD range.");
                return null;
            }

            if (targetPixelClock10Khz > nativeTiming.PixelClock10Khz)
            {
                rejectionReasons.Add(
                    "The 48 Hz pixel clock would exceed the native pixel clock.");
                return null;
            }

            DetailedTiming targetTiming;
            try
            {
                targetTiming = new DetailedTiming(
                    (int)targetPixelClock10Khz,
                    nativeTiming.HorizontalActive,
                    nativeTiming.HorizontalBlanking,
                    nativeTiming.VerticalActive,
                    (int)targetVerticalBlanking,
                    nativeTiming.HorizontalSyncOffset,
                    nativeTiming.HorizontalSyncPulseWidth,
                    nativeTiming.VerticalSyncOffset,
                    nativeTiming.VerticalSyncPulseWidth,
                    nativeTiming.HorizontalImageSizeMillimeters,
                    nativeTiming.VerticalImageSizeMillimeters,
                    nativeTiming.HorizontalBorderPixels,
                    nativeTiming.VerticalBorderLines,
                    nativeTiming.Flags);
            }
            catch (ArgumentOutOfRangeException)
            {
                rejectionReasons.Add(
                    "The 48 Hz candidate cannot be encoded as a valid DTD.");
                return null;
            }

            if (targetTiming.VerticalBackPorch <= nativeTiming.VerticalBackPorch)
            {
                rejectionReasons.Add(
                    "The 48 Hz candidate does not increase the vertical back porch.");
                return null;
            }

            long encodedTotalPixels =
                targetTiming.HorizontalTotal *
                (long)targetTiming.VerticalTotal;
            long encodedClockHertz = targetTiming.PixelClock10Khz * 10000L;
            long encodedErrorNumerator = Math.Abs(
                encodedClockHertz -
                (TargetRefreshHertz * encodedTotalPixels));
            if (encodedErrorNumerator * 100L > encodedTotalPixels)
            {
                rejectionReasons.Add(
                    "The encoded refresh differs from 48 Hz by more than 0.01 Hz.");
                return null;
            }

            return targetTiming;
        }

        private static DisplayProfile CreateProfile(
            HardwareDefinition definition,
            string panelHardwareId,
            Sha256Digest normalizedEdidSignature,
            Sha256Digest sourceEdidSignature,
            DetailedTiming nativeTiming,
            DetailedTiming targetTiming,
            string canonicalGpuDeviceId)
        {
            string profileId = CreateProfileId(
                definition,
                panelHardwareId,
                sourceEdidSignature,
                nativeTiming,
                canonicalGpuDeviceId);

            return new DisplayProfile(
                profileId,
                "Experimental 48 Hz / " +
                    definition.SystemModel +
                    " / " +
                    panelHardwareId,
                new[] { definition.SystemModel },
                panelHardwareId,
                normalizedEdidSignature.ToString(),
                nativeTiming,
                targetTiming,
                definition.GpuDisplayName,
                definition.CanonicalGpuDeviceId,
                string.Empty,
                DisplayProfileKind.Experimental,
                sourceEdidSignature.ToString());
        }

        private static string CreateProfileId(
            HardwareDefinition definition,
            string panelHardwareId,
            Sha256Digest sourceEdidSignature,
            DetailedTiming nativeTiming,
            string canonicalGpuDeviceId)
        {
            var payload = new List<byte>();
            AppendCanonicalField(payload, Encoding.UTF8.GetBytes(ProfileVersion));
            AppendCanonicalField(payload, Encoding.UTF8.GetBytes(definition.Key));
            AppendCanonicalField(
                payload,
                Encoding.UTF8.GetBytes(definition.SystemModel));
            AppendCanonicalField(
                payload,
                Encoding.UTF8.GetBytes(canonicalGpuDeviceId));
            AppendCanonicalField(
                payload,
                Encoding.UTF8.GetBytes(panelHardwareId.ToUpperInvariant()));
            AppendCanonicalField(
                payload,
                Encoding.ASCII.GetBytes(sourceEdidSignature.ToString()));
            AppendCanonicalField(payload, nativeTiming.ToByteArray());

            string digest = Sha256Digest.Compute(payload.ToArray())
                .ToString()
                .ToLowerInvariant();
            string profileId = ProfileIdPrefix + definition.Key + "-" + digest;
            if (profileId.Length > MaximumProfileIdLength)
            {
                throw new InvalidOperationException(
                    "The generated profile ID exceeds its bounded wire format.");
            }

            return profileId;
        }

        private static void AppendCanonicalField(
            IList<byte> destination,
            byte[] value)
        {
            int length = value.Length;
            destination.Add((byte)((length >> 24) & 0xFF));
            destination.Add((byte)((length >> 16) & 0xFF));
            destination.Add((byte)((length >> 8) & 0xFF));
            destination.Add((byte)(length & 0xFF));
            for (int index = 0; index < value.Length; index++)
            {
                destination.Add(value[index]);
            }
        }

        private static bool TryCanonicalizePciDeviceId(
            string value,
            out string canonicalValue)
        {
            canonicalValue = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.Trim().ToUpperInvariant();
            const string prefix = "PCI\\VEN_";
            const string deviceSeparator = "&DEV_";
            int canonicalLength =
                prefix.Length + 4 + deviceSeparator.Length + 4;
            if (
                normalized.Length < canonicalLength ||
                !normalized.StartsWith(prefix, StringComparison.Ordinal) ||
                !string.Equals(
                    normalized.Substring(prefix.Length + 4, deviceSeparator.Length),
                    deviceSeparator,
                    StringComparison.Ordinal) ||
                (normalized.Length > canonicalLength &&
                    normalized[canonicalLength] != '&'))
            {
                return false;
            }

            for (int index = prefix.Length; index < prefix.Length + 4; index++)
            {
                if (!IsUpperHex(normalized[index]))
                {
                    return false;
                }
            }

            int deviceOffset = prefix.Length + 4 + deviceSeparator.Length;
            for (int index = deviceOffset; index < deviceOffset + 4; index++)
            {
                if (!IsUpperHex(normalized[index]))
                {
                    return false;
                }
            }

            canonicalValue = normalized.Substring(0, canonicalLength);
            return true;
        }

        private static bool TryParseProfileId(
            string profileId,
            out string hardwareKey)
        {
            hardwareKey = string.Empty;
            if (
                string.IsNullOrEmpty(profileId) ||
                profileId.Length > MaximumProfileIdLength ||
                !profileId.StartsWith(ProfileIdPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            int digestSeparator = profileId.Length -
                Sha256Digest.CanonicalHexLength -
                1;
            if (
                digestSeparator <= ProfileIdPrefix.Length ||
                profileId[digestSeparator] != '-')
            {
                return false;
            }

            hardwareKey = profileId.Substring(
                ProfileIdPrefix.Length,
                digestSeparator - ProfileIdPrefix.Length);
            if (
                hardwareKey.Length == 0 ||
                hardwareKey.Length > MaximumHardwareKeyLength ||
                hardwareKey[0] == '-' ||
                hardwareKey[hardwareKey.Length - 1] == '-')
            {
                hardwareKey = string.Empty;
                return false;
            }

            for (int index = 0; index < hardwareKey.Length; index++)
            {
                char value = hardwareKey[index];
                if (
                    (value < 'a' || value > 'z') &&
                    (value < '0' || value > '9') &&
                    value != '-')
                {
                    hardwareKey = string.Empty;
                    return false;
                }
            }

            int digestOffset = digestSeparator + 1;
            for (int index = digestOffset; index < profileId.Length; index++)
            {
                char value = profileId[index];
                if (
                    (value < '0' || value > '9') &&
                    (value < 'a' || value > 'f'))
                {
                    hardwareKey = string.Empty;
                    return false;
                }
            }

            return true;
        }

        private static bool IsUpperHex(char value)
        {
            return
                (value >= '0' && value <= '9') ||
                (value >= 'A' && value <= 'F');
        }

        private static ExperimentalProfileGenerationResult Accepted(
            DisplayProfile profile,
            string hardwareKey)
        {
            return new ExperimentalProfileGenerationResult(
                profile,
                hardwareKey,
                new string[0]);
        }

        private static ExperimentalProfileGenerationResult Rejected(
            HardwareDefinition definition,
            params string[] rejectionReasons)
        {
            return new ExperimentalProfileGenerationResult(
                null,
                definition == null ? string.Empty : definition.Key,
                rejectionReasons);
        }

        private static ExperimentalProfileGenerationResult Rejected(
            HardwareDefinition definition,
            IList<string> rejectionReasons)
        {
            return new ExperimentalProfileGenerationResult(
                null,
                definition == null ? string.Empty : definition.Key,
                rejectionReasons);
        }

        private sealed class HardwareDefinition
        {
            public HardwareDefinition(
                string key,
                string systemModel,
                string pciVendorId,
                string pciDeviceId,
                string gpuDisplayName)
            {
                Key = key;
                SystemModel = systemModel;
                CanonicalGpuDeviceId =
                    "PCI\\VEN_" + pciVendorId + "&DEV_" + pciDeviceId;
                GpuDisplayName = gpuDisplayName;
            }

            public string Key { get; private set; }

            public string SystemModel { get; private set; }

            public string CanonicalGpuDeviceId { get; private set; }

            public string GpuDisplayName { get; private set; }
        }
    }
}
