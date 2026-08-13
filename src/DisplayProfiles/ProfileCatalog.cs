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

        private static readonly ReadOnlyCollection<DisplayProfile> Profiles =
            Array.AsReadOnly(
                new[]
                {
                    CreateAppa044Profile(
                        MacBookPro161Appa044ProfileId,
                        "MacBook Pro 16-inch 2019 / APPA044",
                        "CDA0E18080DE8CAC744C66A5374A53CBBA1999115FA5FE2DBD949980649AF3F5",
                        "30.0.13045.22003"),
                    CreateAppa044Profile(
                        MacBookPro161Appa044Faf4ProfileId,
                        "MacBook Pro 16-inch 2019 / APPA044 FAF4A9C1",
                        "FAF4A9C16A6B394896D75DAA3280D84A61744EA07ED2F7CC21E6CFBCF1B4D2DF",
                        string.Empty)
                });

        public static ReadOnlyCollection<DisplayProfile> All => Profiles;

        private static DisplayProfile CreateAppa044Profile(
            string id,
            string displayName,
            string normalizedEdidSignature,
            string verifiedDriverVersion)
        {
            return new DisplayProfile(
                id,
                displayName,
                new[] { "MacBookPro16,1" },
                "APPA044",
                normalizedEdidSignature,
                DetailedTiming.ParseHex(
                    "E7 91 00 50 C0 80 37 70 08 20 98 08 59 D7 10 00 00 1A"),
                DetailedTiming.ParseHex(
                    "DC 91 00 50 C0 80 24 72 08 20 98 08 59 D7 10 00 00 1A"),
                "AMD Radeon Pro 5300M",
                "PCI\\VEN_1002&DEV_7340",
                verifiedDriverVersion);
        }

        public static DisplayProfile GetById(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return null;
            }

            for (var index = 0; index < Profiles.Count; index++)
            {
                if (
                    string.Equals(
                        Profiles[index].Id,
                        profileId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Profiles[index];
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
                "GPU device: " + PublicGpuDevice(hardware.GpuDeviceId));
            text.AppendLine(
                "Display driver: " + SafeVersion(hardware.DriverVersion));
            text.AppendLine(
                "Closest profile: "
                + (closest == null ? "N/A" : closest.Profile.Id));
            text.AppendLine(
                "Hardware supported: "
                + (selection.HardwareSupported ? "True" : "False"));

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
