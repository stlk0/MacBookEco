using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MacBookEco.Core
{
    public enum DisplayProfileKind
    {
        Verified = 1,
        Experimental = 2
    }

    public sealed class DisplayProfile
    {
        private readonly string[] _systemModels;

        public DisplayProfile(
            string id,
            string displayName,
            string[] systemModels,
            string panelHardwareId,
            string normalizedEdidSignature,
            DetailedTiming nativeTiming,
            DetailedTiming targetTiming,
            string verifiedGpuName,
            string verifiedGpuDeviceIdPrefix,
            string verifiedDriverVersion)
            : this(
                id,
                displayName,
                systemModels,
                panelHardwareId,
                normalizedEdidSignature,
                nativeTiming,
                targetTiming,
                verifiedGpuName,
                verifiedGpuDeviceIdPrefix,
                verifiedDriverVersion,
                DisplayProfileKind.Verified,
                normalizedEdidSignature)
        {
        }

        internal DisplayProfile(
            string id,
            string displayName,
            string[] systemModels,
            string panelHardwareId,
            string normalizedEdidSignature,
            DetailedTiming nativeTiming,
            DetailedTiming targetTiming,
            string verifiedGpuName,
            string verifiedGpuDeviceIdPrefix,
            string verifiedDriverVersion,
            DisplayProfileKind kind,
            string sourceEdidSignature)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A profile ID is required.", nameof(id));
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("A display name is required.", nameof(displayName));
            }

            if (systemModels == null || systemModels.Length == 0)
            {
                throw new ArgumentException(
                    "At least one system model is required.",
                    nameof(systemModels));
            }

            if (string.IsNullOrWhiteSpace(panelHardwareId))
            {
                throw new ArgumentException(
                    "A panel hardware identifier is required.",
                    nameof(panelHardwareId));
            }

            try
            {
                NormalizedEdidSignature =
                    Sha256Digest.ParseHex(normalizedEdidSignature);
                SourceEdidSignature =
                    Sha256Digest.ParseHex(sourceEdidSignature);
            }
            catch (FormatException exception)
            {
                throw new ArgumentException(
                    "The normalized EDID signature is not a 32-byte hexadecimal digest.",
                    "normalizedEdidSignature",
                    exception);
            }

            if (nativeTiming == null)
            {
                throw new ArgumentNullException(nameof(nativeTiming));
            }

            if (targetTiming == null)
            {
                throw new ArgumentNullException(nameof(targetTiming));
            }

            if (kind != DisplayProfileKind.Verified &&
                kind != DisplayProfileKind.Experimental)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            if (
                nativeTiming.HorizontalActive != targetTiming.HorizontalActive ||
                nativeTiming.VerticalActive != targetTiming.VerticalActive)
            {
                throw new ArgumentException(
                    "Native and target timings must use the same active dimensions.",
                    nameof(targetTiming));
            }

            Id = id.Trim();
            DisplayName = displayName.Trim();
            _systemModels = (string[])systemModels.Clone();
            for (var index = 0; index < _systemModels.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(_systemModels[index]))
                {
                    throw new ArgumentException(
                        "System model entries cannot be empty.",
                        nameof(systemModels));
                }

                _systemModels[index] = _systemModels[index].Trim();
            }

            PanelHardwareId = HardwareSnapshot.NormalizePanelHardwareId(panelHardwareId);
            NativeTiming = nativeTiming;
            TargetTiming = targetTiming;
            VerifiedGpuName = NormalizeOptional(verifiedGpuName);
            VerifiedGpuDeviceIdPrefix = NormalizeOptional(verifiedGpuDeviceIdPrefix);
            VerifiedDriverVersion = NormalizeOptional(verifiedDriverVersion);
            Kind = kind;
        }

        public string Id { get; private set; }

        public string DisplayName { get; private set; }

        public string PanelHardwareId { get; private set; }

        public Sha256Digest NormalizedEdidSignature { get; private set; }

        public Sha256Digest SourceEdidSignature { get; private set; }

        public DetailedTiming NativeTiming { get; private set; }

        public DetailedTiming TargetTiming { get; private set; }

        public string VerifiedGpuName { get; private set; }

        public string VerifiedGpuDeviceIdPrefix { get; private set; }

        public string VerifiedDriverVersion { get; private set; }

        public DisplayProfileKind Kind { get; private set; }

        public bool IsExperimental => Kind == DisplayProfileKind.Experimental;

        public DisplayProfileMatch Match(HardwareSnapshot hardware)
        {
            if (hardware == null)
            {
                throw new ArgumentNullException(nameof(hardware));
            }

            var rejectionReasons = new List<string>();
            var warnings = new List<string>();
            var installBlockers = new List<string>();

            if (!hardware.IsInternalDisplay)
            {
                rejectionReasons.Add("The detected display is not the internal panel.");
            }

            // Profile IDs are not a substitute for the platform identity.  This
            // check deliberately remains exact (rather than a fuzzy "Apple"
            // match) because it is repeated by the elevated EDID path before
            // it can mutate HKLM.
            if (!string.Equals(
                    hardware.SystemManufacturer,
                    "Apple Inc.",
                    StringComparison.Ordinal))
            {
                rejectionReasons.Add(
                    "The SMBIOS manufacturer is not exactly Apple Inc.");
            }

            if (!ContainsSystemModel(hardware.SystemModel))
            {
                rejectionReasons.Add("The SMBIOS model is not allowed by this profile.");
            }

            if (
                !string.Equals(
                    hardware.PanelHardwareId,
                    PanelHardwareId,
                    StringComparison.OrdinalIgnoreCase))
            {
                rejectionReasons.Add("The panel hardware identifier does not match.");
            }

            if (!hardware.NormalizedEdidSignature.Equals(
                    NormalizedEdidSignature))
            {
                rejectionReasons.Add("The normalized EDID signature does not match.");
            }

            if (IsExperimental &&
                hardware.NormalizedSourceEdidSignature != null &&
                !hardware.NormalizedSourceEdidSignature.Equals(
                    SourceEdidSignature))
            {
                rejectionReasons.Add(
                    "The normalized complete EDID signature does not match.");
            }

            if (!hardware.NativeTiming.Equals(NativeTiming))
            {
                rejectionReasons.Add("The preferred native detailed timing does not match.");
            }

            AddGpuCompatibility(hardware, rejectionReasons, warnings);

            // Descriptor availability is a property of the current base EDID,
            // not a hardware-identity mismatch.  Keep it separate so callers
            // can distinguish a supported panel from one that cannot safely
            // receive this profile's owned override.
            if (hardware.Edid.FindFreeDescriptor() < 0)
            {
                installBlockers.Add(
                    "The EDID has no free non-preferred descriptor slot for the owned override.");
            }

            return new DisplayProfileMatch(
                this,
                rejectionReasons,
                warnings,
                installBlockers);
        }

        public EdidBaseBlock BuildOverride(HardwareSnapshot hardware)
        {
            var match = Match(hardware);
            if (!match.HardwareSupported)
            {
                throw new InvalidOperationException(
                    "The hardware does not match the selected display profile.");
            }

            if (!match.CanInstall)
            {
                throw new InvalidOperationException(
                    "The matching EDID cannot safely receive the owned override.");
            }

            return CompileOverride(hardware.Edid);
        }

        internal EdidBaseBlock CompileOverride(EdidBaseBlock baseEdid)
        {
            if (baseEdid == null)
            {
                throw new ArgumentNullException(nameof(baseEdid));
            }

            return IsExperimental
                ? baseEdid.InsertOrderedDetailedTiming(TargetTiming)
                : baseEdid.InsertDetailedTiming(TargetTiming);
        }

        private bool ContainsSystemModel(string value)
        {
            for (var index = 0; index < _systemModels.Length; index++)
            {
                if (
                    string.Equals(
                        _systemModels[index],
                        value,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void AddGpuCompatibility(
            HardwareSnapshot hardware,
            IList<string> rejectionReasons,
            IList<string> warnings)
        {
            if (!string.IsNullOrEmpty(VerifiedGpuName))
            {
                if (string.IsNullOrEmpty(hardware.GpuName))
                {
                    warnings.Add(
                        "GPU name is unavailable; the profile GPU could not be checked.");
                }
                else if (
                    hardware.GpuName.IndexOf(
                        VerifiedGpuName,
                        StringComparison.OrdinalIgnoreCase) < 0)
                {
                    warnings.Add("The GPU differs from the device used to verify this profile.");
                }
            }

            if (!string.IsNullOrEmpty(VerifiedGpuDeviceIdPrefix))
            {
                if (string.IsNullOrEmpty(hardware.GpuDeviceId))
                {
                    rejectionReasons.Add(
                        "GPU device ID is unavailable; the profile adapter is required.");
                }
                else if (
                    !hardware.GpuDeviceId.StartsWith(
                        VerifiedGpuDeviceIdPrefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    rejectionReasons.Add(
                        "The GPU device ID differs from the adapter allowed by this profile.");
                }
            }

            if (!string.IsNullOrEmpty(VerifiedDriverVersion))
            {
                if (string.IsNullOrEmpty(hardware.DriverVersion))
                {
                    warnings.Add(
                        "Display-driver version is unavailable; compatibility is unverified.");
                }
                else if (
                    !string.Equals(
                        hardware.DriverVersion,
                        VerifiedDriverVersion,
                        StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add(
                        "The display-driver version differs from the verified version.");
                }
            }
        }

        private static string NormalizeOptional(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }

    public sealed class DisplayProfileMatch
    {
        internal DisplayProfileMatch(
            DisplayProfile profile,
            IList<string> rejectionReasons,
            IList<string> warnings,
            IList<string> installBlockers)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (rejectionReasons == null)
            {
                throw new ArgumentNullException(nameof(rejectionReasons));
            }

            if (warnings == null)
            {
                throw new ArgumentNullException(nameof(warnings));
            }

            if (installBlockers == null)
            {
                throw new ArgumentNullException(nameof(installBlockers));
            }

            Profile = profile;
            RejectionReasons = new ReadOnlyCollection<string>(
                new List<string>(rejectionReasons));
            Warnings = new ReadOnlyCollection<string>(new List<string>(warnings));
            InstallBlockers = new ReadOnlyCollection<string>(
                new List<string>(installBlockers));
        }

        public DisplayProfile Profile { get; private set; }

        /// <summary>
        /// True only when the static, reviewed hardware identity matches the
        /// profile.  It intentionally excludes EDID descriptor availability.
        /// </summary>
        public bool HardwareSupported => RejectionReasons.Count == 0;

        /// <summary>
        /// True only when the hardware matches and the base EDID has the free
        /// descriptor required for this owned override.  It is not by itself
        /// authorization to mutate the platform registry.
        /// </summary>
        public bool CanInstall => HardwareSupported && InstallBlockers.Count == 0;

        public ReadOnlyCollection<string> RejectionReasons { get; private set; }

        public ReadOnlyCollection<string> Warnings { get; private set; }

        /// <summary>
        /// Safe-construction blockers that are separate from static hardware
        /// mismatch reasons.  The platform contributes further blockers such
        /// as a missing active endpoint or a non-pristine registry value.
        /// </summary>
        public ReadOnlyCollection<string> InstallBlockers { get; private set; }
    }

    public sealed class ProfileSelectionResult
    {
        internal ProfileSelectionResult(
            DisplayProfile profile,
            DisplayProfileMatch closestMatch)
        {
            Profile = profile;
            ClosestMatch = closestMatch;
        }

        public DisplayProfile Profile { get; private set; }

        public DisplayProfileMatch ClosestMatch { get; private set; }

        public bool HardwareSupported => Profile != null &&
                    ClosestMatch != null &&
                    ClosestMatch.HardwareSupported;

        public bool CanInstall => Profile != null &&
                    ClosestMatch != null &&
                    ClosestMatch.CanInstall;

    }
}
