using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace MacBookEco.Core
{
    public static class ProfileCatalog
    {
        public const string MacBookPro161Appa044ProfileId =
            "macbookpro16-1-appa044-48hz";

        public const string MacBookPro161Appa044Faf4ProfileId =
            "macbookpro16-1-appa044-faf4a9c1-48hz";

        public const string MacBookPro161Appa0444b2eProfileId =
            "macbookpro16-1-appa044-4b2ea063-48hz";

        public const string MacBookPro161Appa044EcoModesProfileId =
            "macbookpro16-1-appa044-48-60eco-v3";

        public const string MacBookPro161Appa044Faf4EcoModesProfileId =
            "macbookpro16-1-appa044-faf4a9c1-48-60eco-v3";

        public const string MacBookPro161Appa0444b2eEcoModesProfileId =
            "macbookpro16-1-appa044-4b2ea063-48-60eco-v3";

        private const string LegacyMacBookPro161Appa044EcoModesProfileId =
            "macbookpro16-1-appa044-48-58hz-v2";

        private const string LegacyMacBookPro161Appa044Faf4EcoModesProfileId =
            "macbookpro16-1-appa044-faf4a9c1-48-58hz-v2";

        private const string LegacyMacBookPro161Appa0444b2eEcoModesProfileId =
            "macbookpro16-1-appa044-4b2ea063-48-58hz-v2";

        private static readonly DetailedTiming NativeAppa044Timing =
            DetailedTiming.ParseHex(
                "E7 91 00 50 C0 80 37 70 08 20 98 08 59 D7 10 00 00 1A");

        private static readonly DetailedTiming Appa04448HzTiming =
            DetailedTiming.ParseHex(
                "DC 91 00 50 C0 80 24 72 08 20 98 08 59 D7 10 00 00 1A");

        private static readonly DetailedTiming Appa044Eco60HzTiming =
            DetailedTiming.ParseHex(
                "20 92 00 50 C0 80 3C 70 08 20 98 08 59 D7 10 00 00 1A");

        private static readonly DetailedTiming LegacyAppa04458HzTiming =
            DetailedTiming.ParseHex(
                "E7 91 00 50 C0 80 80 70 08 20 98 08 59 D7 10 00 00 1A");

        private static readonly ReadOnlyCollection<DisplayProfile> Profiles =
            CreateAppa044Profiles(true, false);

        // Historical app-owned profiles remain compiled only so existing
        // journals can still be verified and safely restored after an update.
        // New installs never select them.
        private static readonly ReadOnlyCollection<DisplayProfile> LegacyProfiles =
            CreateLegacyProfiles();

        public static ReadOnlyCollection<DisplayProfile> All => Profiles;

        private static ReadOnlyCollection<DisplayProfile> CreateAppa044Profiles(
            bool includeEcoMode,
            bool useLegacy58Hz)
        {
            return Array.AsReadOnly(
                new[]
                {
                    CreateAppa044Profile(
                        includeEcoMode
                            ? useLegacy58Hz
                                ? LegacyMacBookPro161Appa044EcoModesProfileId
                                : MacBookPro161Appa044EcoModesProfileId
                            : MacBookPro161Appa044ProfileId,
                        "MacBook Pro 16-inch 2019 / APPA044",
                        "CDA0E18080DE8CAC744C66A5374A53CBBA1999115FA5FE2DBD949980649AF3F5",
                        "AMD Radeon Pro 5300M",
                        "30.0.13045.22003",
                        false,
                        includeEcoMode,
                        useLegacy58Hz),
                    CreateAppa044Profile(
                        includeEcoMode
                            ? useLegacy58Hz
                                ? LegacyMacBookPro161Appa044Faf4EcoModesProfileId
                                : MacBookPro161Appa044Faf4EcoModesProfileId
                            : MacBookPro161Appa044Faf4ProfileId,
                        "MacBook Pro 16-inch 2019 / APPA044 FAF4A9C1",
                        "FAF4A9C16A6B394896D75DAA3280D84A61744EA07ED2F7CC21E6CFBCF1B4D2DF",
                        "AMD Radeon Pro 5300M",
                        string.Empty,
                        includeEcoMode,
                        includeEcoMode,
                        useLegacy58Hz),
                    CreateAppa044Profile(
                        includeEcoMode
                            ? useLegacy58Hz
                                ? LegacyMacBookPro161Appa0444b2eEcoModesProfileId
                                : MacBookPro161Appa0444b2eEcoModesProfileId
                            : MacBookPro161Appa0444b2eProfileId,
                        "MacBook Pro 16-inch 2019 / APPA044 4B2EA063",
                        "4B2EA0633F9C80C074E8F06E891B5F179444E0A417CD60AFBD190C732840B7EC",
                        "AMD Radeon Pro 5500M",
                        "26.20.13003.5002",
                        includeEcoMode,
                        includeEcoMode,
                        useLegacy58Hz)
                });
        }

        private static ReadOnlyCollection<DisplayProfile> CreateLegacyProfiles()
        {
            var profiles = new List<DisplayProfile>();
            profiles.AddRange(CreateAppa044Profiles(true, true));
            profiles.AddRange(CreateAppa044Profiles(false, false));
            return profiles.AsReadOnly();
        }

        private static DisplayProfile CreateAppa044Profile(
            string id,
            string displayName,
            string normalizedEdidSignature,
            string verifiedGpuName,
            string verifiedDriverVersion,
            bool experimentalEcoMode,
            bool includeEcoMode,
            bool useLegacy58Hz)
        {
            var modes = new List<DisplayRefreshMode>
            {
                new DisplayRefreshMode(
                    48,
                    Appa04448HzTiming,
                    false)
            };
            if (includeEcoMode)
            {
                modes.Add(new DisplayRefreshMode(
                    useLegacy58Hz ? 58 : 59,
                    useLegacy58Hz
                        ? LegacyAppa04458HzTiming
                        : Appa044Eco60HzTiming,
                    experimentalEcoMode));
            }

            return new DisplayProfile(
                id,
                displayName,
                new[] { "MacBookPro16,1" },
                "APPA044",
                normalizedEdidSignature,
                NativeAppa044Timing,
                modes.ToArray(),
                verifiedGpuName,
                "PCI\\VEN_1002&DEV_7340",
                verifiedDriverVersion);
        }

        public static DisplayProfile GetById(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return null;
            }

            DisplayProfile profile = FindById(Profiles, profileId);
            if (profile != null)
            {
                return profile;
            }

            return FindById(LegacyProfiles, profileId);
        }

        internal static bool ShouldRefreshInstalledProfile(
            string installedProfileId,
            string selectedProfileId)
        {
            return !string.IsNullOrWhiteSpace(installedProfileId) &&
                !string.IsNullOrWhiteSpace(selectedProfileId) &&
                !string.Equals(
                    installedProfileId,
                    selectedProfileId,
                    StringComparison.OrdinalIgnoreCase);
        }

        internal static DisplayProfile FindExactInstalledProfile(
            HardwareSnapshot hardware,
            byte[] currentOverride,
            int refreshRateHz)
        {
            if (hardware == null || currentOverride == null)
            {
                return null;
            }

            DisplayProfile profile = FindExactInstalledProfile(
                Profiles,
                hardware,
                currentOverride,
                refreshRateHz);
            return profile ?? FindExactInstalledProfile(
                LegacyProfiles,
                hardware,
                currentOverride,
                refreshRateHz);
        }

        private static DisplayProfile FindExactInstalledProfile(
            ReadOnlyCollection<DisplayProfile> profiles,
            HardwareSnapshot hardware,
            byte[] currentOverride,
            int refreshRateHz)
        {
            for (var index = 0; index < profiles.Count; index++)
            {
                DisplayProfile profile = profiles[index];
                if (profile.GetTargetMode(refreshRateHz) != null &&
                    profile.Match(hardware).HardwareSupported &&
                    FixedTimeComparer.AreEqual(
                        currentOverride,
                        profile.BuildOverride(hardware).ToByteArray()))
                {
                    return profile;
                }
            }

            return null;
        }

        private static DisplayProfile FindById(
            ReadOnlyCollection<DisplayProfile> profiles,
            string profileId)
        {
            for (var index = 0; index < profiles.Count; index++)
            {
                if (string.Equals(
                        profiles[index].Id,
                        profileId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return profiles[index];
                }
            }

            return null;
        }

        public static ProfileSelectionResult Select(HardwareSnapshot hardware)
        {
            if (hardware == null)
            {
                throw new ArgumentNullException(nameof(hardware));
            }

            DisplayProfileMatch closest = null;
            for (var index = 0; index < Profiles.Count; index++)
            {
                var match = Profiles[index].Match(hardware);
                if (match.HardwareSupported)
                {
                    return new ProfileSelectionResult(Profiles[index], match);
                }

                if (
                    closest == null ||
                    match.RejectionReasons.Count < closest.RejectionReasons.Count)
                {
                    closest = match;
                }
            }

            return new ProfileSelectionResult(null, closest);
        }

        public static string BuildPublicDiagnostics(HardwareSnapshot hardware)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("Display profile compatibility (public-safe)");
            if (hardware == null)
            {
                text.AppendLine("Discovery: Unavailable");
                return text.ToString();
            }

            ProfileSelectionResult selection = Select(hardware);
            DisplayProfileMatch closest = selection.ClosestMatch;
            bool isApple = string.Equals(
                hardware.SystemManufacturer,
                "Apple Inc.",
                StringComparison.Ordinal);
            text.AppendLine("Discovery: Available");
            text.AppendLine(
                "System model: "
                + (isApple ? SafeSystemModel(hardware.SystemModel) : "N/A"));
            text.AppendLine(
                "Apple SMBIOS manufacturer: " + isApple);
            text.AppendLine("Internal display: " + hardware.IsInternalDisplay);
            text.AppendLine(
                "Panel hardware ID: " + SafePanelId(hardware.PanelHardwareId));
            text.AppendLine(
                "EDID product: "
                + SafeToken(hardware.Edid.ManufacturerCode)
                + " / 0x"
                + hardware.Edid.ProductCode.ToString(
                    "X4",
                    CultureInfo.InvariantCulture));
            text.AppendLine(
                "Normalized EDID signature: "
                + hardware.NormalizedEdidSignature);
            text.AppendLine(
                "Sanitized EDID profile fixture: "
                + HexCodec.Format(hardware.Edid.ToPublicProfileFixture()));
            text.AppendLine(
                "Native DTD: "
                + HexCodec.Format(hardware.NativeTiming.ToByteArray()));
            text.AppendLine(
                "Native mode: "
                + hardware.NativeTiming.HorizontalActive
                    .ToString(CultureInfo.InvariantCulture)
                + "x"
                + hardware.NativeTiming.VerticalActive
                    .ToString(CultureInfo.InvariantCulture)
                + " @ "
                + hardware.NativeTiming.RefreshRateHertz.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture)
                + " Hz");
            int freeDescriptor = hardware.Edid.FindFreeDescriptor();
            text.AppendLine(
                "Free EDID descriptor: "
                + (freeDescriptor >= 0
                    ? freeDescriptor.ToString(CultureInfo.InvariantCulture)
                    : "None"));
            text.AppendLine(
                "Free EDID descriptor count: "
                + hardware.Edid.CountFreeDescriptors().ToString(
                    CultureInfo.InvariantCulture));
            text.AppendLine(
                "GPU device: " + PublicGpuDevice(hardware.GpuDeviceId));
            text.AppendLine(
                "Display driver: " + SafeVersion(hardware.DriverVersion));
            text.AppendLine(
                "Closest profile: "
                + (closest == null ? "N/A" : closest.Profile.Id));
            text.AppendLine(
                "Hardware supported: "
                + (selection.HardwareSupported ? "True" : "False"));
            DisplayRefreshMode ecoMode = selection.Profile == null
                ? null
                : selection.Profile.GetTargetMode(59);
            text.AppendLine(
                "60 Hz Eco validation: "
                + (ecoMode == null
                    ? "Unavailable"
                    : ecoMode.Experimental
                        ? "Experimental"
                        : "Hardware-verified"));

            AppendFindings(text, "Mismatch", closest == null
                ? null
                : closest.RejectionReasons);
            AppendFindings(text, "Warning", closest == null
                ? null
                : closest.Warnings);
            AppendFindings(text, "Install blocker", closest == null
                ? null
                : closest.InstallBlockers);
            text.AppendLine("Raw EDID: omitted because it may identify the device");
            return text.ToString();
        }

        private static void AppendFindings(
            StringBuilder text,
            string label,
            IList<string> findings)
        {
            if (findings == null || findings.Count == 0)
            {
                text.AppendLine(label + ": None");
                return;
            }

            for (int index = 0; index < findings.Count; index++)
            {
                text.AppendLine(label + ": " + findings[index]);
            }
        }

        private static string PublicGpuDevice(string value)
        {
            string normalized = value == null
                ? string.Empty
                : value.ToUpperInvariant();
            string vendor = PublicPciComponent(normalized, "VEN_");
            string device = PublicPciComponent(normalized, "DEV_");
            if (vendor == null || device == null)
            {
                return "N/A";
            }

            return "PCI\\" + vendor + "&" + device;
        }

        private static string PublicPciComponent(
            string value,
            string prefix)
        {
            int start = value.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0 || start + 8 > value.Length)
            {
                return null;
            }

            for (int index = start + 4; index < start + 8; index++)
            {
                char current = value[index];
                if (!char.IsDigit(current)
                    && (current < 'A' || current > 'F'))
                {
                    return null;
                }
            }

            return value.Substring(start, 8);
        }

        private static string SafeVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
            {
                return "N/A";
            }

            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (!char.IsDigit(current) && current != '.')
                {
                    return "N/A";
                }
            }

            return value;
        }

        private static string SafeSystemModel(string value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.Length > 32
                || !value.StartsWith("MacBook", StringComparison.Ordinal))
            {
                return "N/A";
            }

            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (!char.IsLetterOrDigit(current)
                    && current != ' '
                    && current != ','
                    && current != '.'
                    && current != '-'
                    && current != '_')
                {
                    return "N/A";
                }
            }

            return value;
        }

        private static string SafePanelId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 7)
            {
                return "N/A";
            }

            for (int index = 0; index < value.Length; index++)
            {
                if (!char.IsLetterOrDigit(value[index]))
                {
                    return "N/A";
                }
            }

            return value.ToUpperInvariant();
        }

        private static string SafeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 16)
            {
                return "N/A";
            }

            for (int index = 0; index < value.Length; index++)
            {
                if (!char.IsLetterOrDigit(value[index]))
                {
                    return "N/A";
                }
            }

            return value;
        }
    }
}
