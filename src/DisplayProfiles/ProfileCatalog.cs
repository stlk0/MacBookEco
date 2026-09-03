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

        public const string MacBookPro161Appa044235fEcoModesProfileId =
            "macbookpro16-1-appa044-235fb43d-48-60eco-v3";

        private const string LegacyMacBookPro161Appa044EcoModesProfileId =
            "macbookpro16-1-appa044-48-58hz-v2";

        private const string LegacyMacBookPro161Appa044Faf4EcoModesProfileId =
            "macbookpro16-1-appa044-faf4a9c1-48-58hz-v2";

        private const string LegacyMacBookPro161Appa0444b2eEcoModesProfileId =
            "macbookpro16-1-appa044-4b2ea063-48-58hz-v2";

        private const int LegacyExperimentalRefreshRate = 61;

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

        private static readonly DisplayModeDefinition CompatibilityModeValue =
            new DisplayModeDefinition(
                48,
                "48 Hz",
                true,
                false);

        // EnumDisplaySettingsEx exposes 60000/1001 in the integer 59 bucket.
        // CCD supplies the exact rational used for signal read-back.
        private static readonly DisplayModeDefinition EcoModeValue =
            new DisplayModeDefinition(
                59,
                "60 Hz Eco",
                true,
                false);

        private static readonly DisplayModeDefinition NativeModeValue =
            new DisplayModeDefinition(
                60,
                "60 Hz Native",
                false,
                true);

        private static readonly DisplayModeDefinition LegacyEcoModeValue =
            new DisplayModeDefinition(
                58,
                "58 Hz Legacy",
                true,
                false);

        private static readonly ProfileModeTemplate CompatibilityModeTemplate =
            new ProfileModeTemplate(
                CompatibilityModeValue,
                Appa04448HzTiming);

        private static readonly ProfileModeTemplate EcoModeTemplate =
            new ProfileModeTemplate(
                EcoModeValue,
                Appa044Eco60HzTiming,
                60000U,
                1001U);

        private static readonly ProfileModeTemplate LegacyEcoModeTemplate =
            new ProfileModeTemplate(
                LegacyEcoModeValue,
                LegacyAppa04458HzTiming);

        private static readonly ReadOnlyCollection<DisplayModeDefinition>
            ReviewedModes = Array.AsReadOnly(
                new[]
                {
                    CompatibilityModeValue,
                    EcoModeValue,
                    NativeModeValue
                });

        private static readonly ReadOnlyCollection<DisplayProfile> Profiles =
            Array.AsReadOnly(
                new[]
                {
                    CreateAppa044Profile(
                        MacBookPro161Appa044EcoModesProfileId,
                        "MacBook Pro 16-inch 2019 / APPA044",
                        "CDA0E18080DE8CAC744C66A5374A53CBBA1999115FA5FE2DBD949980649AF3F5",
                        "AMD Radeon Pro 5300M",
                        "30.0.13045.22003",
                        false,
                        new[] { CompatibilityModeTemplate, EcoModeTemplate }),
                    CreateAppa044Profile(
                        MacBookPro161Appa044Faf4EcoModesProfileId,
                        "MacBook Pro 16-inch 2019 / APPA044 FAF4A9C1",
                        "FAF4A9C16A6B394896D75DAA3280D84A61744EA07ED2F7CC21E6CFBCF1B4D2DF",
                        "AMD Radeon Pro 5300M",
                        string.Empty,
                        true,
                        new[] { CompatibilityModeTemplate, EcoModeTemplate }),
                    CreateAppa044Profile(
                        MacBookPro161Appa0444b2eEcoModesProfileId,
                        "MacBook Pro 16-inch 2019 / APPA044 4B2EA063",
                        "4B2EA0633F9C80C074E8F06E891B5F179444E0A417CD60AFBD190C732840B7EC",
                        "AMD Radeon Pro 5500M",
                        "26.20.13003.5002",
                        true,
                        new[] { CompatibilityModeTemplate, EcoModeTemplate }),
                    CreateAppa044Profile(
                        MacBookPro161Appa044235fEcoModesProfileId,
                        "MacBook Pro 16-inch 2019 / APPA044 235FB43D",
                        "235FB43D444EEB6055EED98766FBA83F751998DA3F53068F06A2949744AB1EFF",
                        "AMD Radeon Pro 5300M",
                        "30.0.13045.22003",
                        true,
                        new[] { CompatibilityModeTemplate, EcoModeTemplate })
                });

        // Historical app-owned profiles remain compiled only so existing
        // journals can still be verified and safely restored after an update.
        // New installs never select them.
        private static readonly ReadOnlyCollection<DisplayProfile> LegacyProfiles =
            CreateLegacyProfiles();

        public static ReadOnlyCollection<DisplayProfile> All => Profiles;

        public static ReadOnlyCollection<DisplayModeDefinition> Modes =>
            ReviewedModes;

        public static DisplayModeDefinition CompatibilityMode =>
            CompatibilityModeValue;

        public static DisplayModeDefinition EcoMode => EcoModeValue;

        public static DisplayModeDefinition NativeMode => NativeModeValue;

        public static string OwnedSupportDisplayName
        {
            get
            {
                var names = new List<string>();
                for (var index = 0; index < ReviewedModes.Count; index++)
                {
                    if (ReviewedModes[index].RequiresOwnedSupport)
                    {
                        names.Add(ReviewedModes[index].DisplayName);
                    }
                }

                return string.Join(" + ", names.ToArray());
            }
        }

        public static string ReviewedModeDisplayName
        {
            get
            {
                var names = new List<string>();
                for (var index = 0; index < ReviewedModes.Count; index++)
                {
                    names.Add(ReviewedModes[index].DisplayName);
                }

                return string.Join(", ", names.ToArray());
            }
        }

        public static DisplayModeDefinition GetMode(int refreshRate)
        {
            for (var index = 0; index < ReviewedModes.Count; index++)
            {
                if (ReviewedModes[index].WindowsRefreshRate == refreshRate)
                {
                    return ReviewedModes[index];
                }
            }

            return null;
        }

        public static DisplayModeDefinition GetModeForWindowsSelector(
            double? refreshRate)
        {
            for (var index = 0; index < ReviewedModes.Count; index++)
            {
                if (ReviewedModes[index].MatchesWindowsSelector(refreshRate))
                {
                    return ReviewedModes[index];
                }
            }

            return null;
        }

        public static bool IsHistoricalRecoveryMode(int refreshRate)
        {
            return refreshRate == LegacyEcoModeValue.WindowsRefreshRate ||
                refreshRate == LegacyExperimentalRefreshRate;
        }

        private static ReadOnlyCollection<DisplayProfile>
            CreateLegacyAppa044Profiles(
            string primaryProfileId,
            string alternateProfileId,
            string radeon5500ProfileId,
            ProfileModeTemplate[] modes)
        {
            return Array.AsReadOnly(
                new[]
                {
                    CreateAppa044Profile(
                        primaryProfileId,
                        "MacBook Pro 16-inch 2019 / APPA044",
                        "CDA0E18080DE8CAC744C66A5374A53CBBA1999115FA5FE2DBD949980649AF3F5",
                        "AMD Radeon Pro 5300M",
                        "30.0.13045.22003",
                        false,
                        modes),
                    CreateAppa044Profile(
                        alternateProfileId,
                        "MacBook Pro 16-inch 2019 / APPA044 FAF4A9C1",
                        "FAF4A9C16A6B394896D75DAA3280D84A61744EA07ED2F7CC21E6CFBCF1B4D2DF",
                        "AMD Radeon Pro 5300M",
                        string.Empty,
                        true,
                        modes),
                    CreateAppa044Profile(
                        radeon5500ProfileId,
                        "MacBook Pro 16-inch 2019 / APPA044 4B2EA063",
                        "4B2EA0633F9C80C074E8F06E891B5F179444E0A417CD60AFBD190C732840B7EC",
                        "AMD Radeon Pro 5500M",
                        "26.20.13003.5002",
                        true,
                        modes)
                });
        }

        private static ReadOnlyCollection<DisplayProfile> CreateLegacyProfiles()
        {
            var profiles = new List<DisplayProfile>();
            profiles.AddRange(CreateLegacyAppa044Profiles(
                LegacyMacBookPro161Appa044EcoModesProfileId,
                LegacyMacBookPro161Appa044Faf4EcoModesProfileId,
                LegacyMacBookPro161Appa0444b2eEcoModesProfileId,
                new[] { CompatibilityModeTemplate, LegacyEcoModeTemplate }));
            profiles.AddRange(CreateLegacyAppa044Profiles(
                MacBookPro161Appa044ProfileId,
                MacBookPro161Appa044Faf4ProfileId,
                MacBookPro161Appa0444b2eProfileId,
                new[] { CompatibilityModeTemplate }));
            return profiles.AsReadOnly();
        }

        private static DisplayProfile CreateAppa044Profile(
            string id,
            string displayName,
            string normalizedEdidSignature,
            string verifiedGpuName,
            string verifiedDriverVersion,
            bool experimentalAdditionalModes,
            ProfileModeTemplate[] templates)
        {
            var modes = new List<DisplayRefreshMode>();
            for (var index = 0; index < templates.Length; index++)
            {
                ProfileModeTemplate template = templates[index];
                modes.Add(new DisplayRefreshMode(
                    template.Definition,
                    template.Timing,
                    experimentalAdditionalModes && index > 0,
                    template.ExpectedRefreshRateNumerator,
                    template.ExpectedRefreshRateDenominator));
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

        private sealed class ProfileModeTemplate
        {
            internal ProfileModeTemplate(
                DisplayModeDefinition definition,
                DetailedTiming timing,
                uint expectedRefreshRateNumerator = 0U,
                uint expectedRefreshRateDenominator = 0U)
            {
                Definition = definition ?? throw new ArgumentNullException(
                    nameof(definition));
                Timing = timing ?? throw new ArgumentNullException(
                    nameof(timing));
                ExpectedRefreshRateNumerator = expectedRefreshRateNumerator;
                ExpectedRefreshRateDenominator = expectedRefreshRateDenominator;
            }

            internal DisplayModeDefinition Definition { get; private set; }
            internal DetailedTiming Timing { get; private set; }
            internal uint ExpectedRefreshRateNumerator { get; private set; }
            internal uint ExpectedRefreshRateDenominator { get; private set; }
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

        public static bool HasAllOwnedModes(DisplayProfile profile)
        {
            if (profile == null)
            {
                return false;
            }

            for (var index = 0; index < ReviewedModes.Count; index++)
            {
                DisplayModeDefinition mode = ReviewedModes[index];
                if (mode.RequiresOwnedSupport &&
                    profile.GetTargetMode(mode.WindowsRefreshRate) == null)
                {
                    return false;
                }
            }

            return true;
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
            for (var index = 0; index < ReviewedModes.Count; index++)
            {
                DisplayModeDefinition definition = ReviewedModes[index];
                if (!definition.RequiresOwnedSupport)
                {
                    continue;
                }

                DisplayRefreshMode mode = selection.Profile == null
                    ? null
                    : selection.Profile.GetTargetMode(
                        definition.WindowsRefreshRate);
                text.AppendLine(
                    definition.DisplayName
                        + " validation: "
                        + (mode == null
                            ? "Unavailable"
                            : mode.Experimental
                                ? "Experimental"
                                : "Hardware-verified"));
            }

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
