using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MacBookEco.Core
{
    public sealed class DisplayModeDefinition
    {
        public DisplayModeDefinition(
            int refreshRate,
            string displayName,
            bool requiresOwnedSupport,
            bool nativeRecovery)
        {
            if (refreshRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(refreshRate));
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException(
                    "A display-mode name is required.",
                    nameof(displayName));
            }

            WindowsRefreshRate = refreshRate;
            DisplayName = displayName.Trim();
            RequiresOwnedSupport = requiresOwnedSupport;
            NativeRecovery = nativeRecovery;
        }

        // EnumDisplaySettingsEx selects refresh rates through an integer
        // dmDisplayFrequency bucket. A hardware profile carries any exact
        // fractional signal identity alongside its DTD.
        public int WindowsRefreshRate { get; private set; }
        public string DisplayName { get; private set; }
        public bool RequiresOwnedSupport { get; private set; }
        public bool NativeRecovery { get; private set; }
        public bool MatchesWindowsSelector(double? refreshRate)
        {
            return refreshRate.HasValue &&
                refreshRate.Value == WindowsRefreshRate;
        }

    }

    public sealed class DisplayRefreshMode
    {
        public DisplayRefreshMode(
            DisplayModeDefinition definition,
            DetailedTiming timing,
            bool experimental,
            uint expectedRefreshRateNumerator = 0U,
            uint expectedRefreshRateDenominator = 0U)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (timing == null)
            {
                throw new ArgumentNullException(nameof(timing));
            }

            if ((expectedRefreshRateNumerator == 0U) !=
                (expectedRefreshRateDenominator == 0U))
            {
                throw new ArgumentException(
                    "An exact refresh rational must be complete or omitted.");
            }

            Definition = definition;
            Timing = timing;
            Experimental = experimental;
            ExpectedRefreshRateNumerator = expectedRefreshRateNumerator;
            ExpectedRefreshRateDenominator = expectedRefreshRateDenominator;
        }

        public DisplayModeDefinition Definition { get; private set; }

        public int RefreshRateHz => Definition.WindowsRefreshRate;

        public DetailedTiming Timing { get; private set; }

        public bool Experimental { get; private set; }

        public uint ExpectedRefreshRateNumerator { get; private set; }

        public uint ExpectedRefreshRateDenominator { get; private set; }

        public bool RequiresExactSignalValidation =>
            ExpectedRefreshRateNumerator != 0U;

        public bool MatchesSignal(
            uint refreshRateNumerator,
            uint refreshRateDenominator,
            ulong pixelRate,
            uint activeWidth,
            uint activeHeight,
            uint totalWidth,
            uint totalHeight)
        {
            if (!RequiresExactSignalValidation ||
                refreshRateNumerator == 0U ||
                refreshRateDenominator == 0U)
            {
                return false;
            }

            return (ulong)refreshRateNumerator *
                    ExpectedRefreshRateDenominator ==
                    (ulong)refreshRateDenominator *
                    ExpectedRefreshRateNumerator &&
                pixelRate == (ulong)Timing.PixelClock10Khz * 10000UL &&
                activeWidth == (uint)Timing.HorizontalActive &&
                activeHeight == (uint)Timing.VerticalActive &&
                totalWidth == (uint)Timing.HorizontalTotal &&
                totalHeight == (uint)Timing.VerticalTotal;
        }
    }

    public sealed class DisplayProfile
    {
        private readonly string[] _systemModels;
        private readonly ReadOnlyCollection<DisplayRefreshMode> _targetModes;

        public DisplayProfile(
            string id,
            string displayName,
            string[] systemModels,
            string panelHardwareId,
            string normalizedEdidSignature,
            DetailedTiming nativeTiming,
            DisplayRefreshMode[] targetModes,
            string verifiedGpuName,
            string verifiedGpuDeviceIdPrefix,
            string verifiedDriverVersion)
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

            if (targetModes == null || targetModes.Length == 0)
            {
                throw new ArgumentException(
                    "At least one target display mode is required.",
                    nameof(targetModes));
            }

            var reviewedModes = new List<DisplayRefreshMode>();
            var refreshRates = new HashSet<int>();
            for (var index = 0; index < targetModes.Length; index++)
            {
                DisplayRefreshMode mode = targetModes[index];
                if (mode == null)
                {
                    throw new ArgumentException(
                        "Target display modes cannot be null.",
                        nameof(targetModes));
                }

                if (!refreshRates.Add(mode.RefreshRateHz))
                {
                    throw new ArgumentException(
                        "Target display refresh rates must be unique.",
                        nameof(targetModes));
                }

                if (
                    nativeTiming.HorizontalActive != mode.Timing.HorizontalActive ||
                    nativeTiming.VerticalActive != mode.Timing.VerticalActive)
                {
                    throw new ArgumentException(
                        "Native and target timings must use the same active dimensions.",
                        nameof(targetModes));
                }

                reviewedModes.Add(mode);
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
            _targetModes = new ReadOnlyCollection<DisplayRefreshMode>(reviewedModes);
            VerifiedGpuName = NormalizeOptional(verifiedGpuName);
            VerifiedGpuDeviceIdPrefix = NormalizeOptional(verifiedGpuDeviceIdPrefix);
            VerifiedDriverVersion = NormalizeOptional(verifiedDriverVersion);
        }

        public string Id { get; private set; }

        public string DisplayName { get; private set; }

        public string PanelHardwareId { get; private set; }

        public Sha256Digest NormalizedEdidSignature { get; private set; }

        public DetailedTiming NativeTiming { get; private set; }

        public string VerifiedGpuName { get; private set; }

        public string VerifiedGpuDeviceIdPrefix { get; private set; }

        public string VerifiedDriverVersion { get; private set; }

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

            if (!hardware.NativeTiming.Equals(NativeTiming))
            {
                rejectionReasons.Add("The preferred native detailed timing does not match.");
            }

            AddGpuCompatibility(hardware, rejectionReasons, warnings);

            // Descriptor availability is a property of the current base EDID,
            // not a hardware-identity mismatch.  Keep it separate so callers
            // can distinguish a supported panel from one that cannot safely
            // receive this profile's owned override.
            var requiredDescriptors = 0;
            for (var index = 0; index < _targetModes.Count; index++)
            {
                if (!hardware.Edid.ContainsDetailedTiming(
                        _targetModes[index].Timing))
                {
                    requiredDescriptors++;
                }
            }

            if (hardware.Edid.CountFreeDescriptors() < requiredDescriptors)
            {
                installBlockers.Add(
                    "The EDID does not have enough free non-preferred descriptor "
                        + "slots for the owned override.");
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
                    "The hardware does not match the verified display profile.");
            }

            if (!match.CanInstall)
            {
                throw new InvalidOperationException(
                    "The matching EDID cannot safely receive the owned override.");
            }

            return BuildOverride(hardware.Edid);
        }

        public EdidBaseBlock BuildOverride(EdidBaseBlock baseEdid)
        {
            if (baseEdid == null)
            {
                throw new ArgumentNullException(nameof(baseEdid));
            }

            if (!NormalizedEdidSignature.Equals(baseEdid.NormalizedSignature) ||
                !NativeTiming.Equals(baseEdid.PreferredTiming))
            {
                throw new InvalidOperationException(
                    "The EDID does not match this display profile.");
            }

            EdidBaseBlock result = baseEdid;
            for (var index = 0; index < _targetModes.Count; index++)
            {
                result = result.InsertDetailedTiming(_targetModes[index].Timing);
            }

            return result;
        }

        public DisplayRefreshMode GetTargetMode(int refreshRateHz)
        {
            for (var index = 0; index < _targetModes.Count; index++)
            {
                if (_targetModes[index].RefreshRateHz == refreshRateHz)
                {
                    return _targetModes[index];
                }
            }

            return null;
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
                    warnings.Add("GPU name is unavailable; the verified GPU could not be checked.");
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
                        "GPU device ID is unavailable; the verified adapter is required.");
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
