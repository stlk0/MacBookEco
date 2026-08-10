using System;
using System.Collections.Generic;

namespace MacBookEco.AppPolicy
{
    /// <summary>
    /// Reviewed CPU policies exposed by the application. These values are
    /// deliberately platform-neutral; the Windows adapter translates them to
    /// its native power-setting writes.
    /// </summary>
    public enum PowerPreset
    {
        Normal,
        Cool,
        MaximumBattery
    }

    public enum CpuHardwareSupportStatus
    {
        Supported,
        IdentityUnavailable,
        Unsupported
    }

    /// <summary>
    /// The alpha CPU contract is intentionally narrower than display
    /// discovery: it is based on SMBIOS system identity, not panel, EDID, or
    /// GPU identity. Keeping it pure lets both the unelevated UI and elevated
    /// helper apply exactly the same fail-closed rule.
    /// </summary>
    public static class CpuHardwareSupportPolicy
    {
        public static bool IsSupported(string manufacturer, string productName)
        {
            return Classify(manufacturer, productName) ==
                CpuHardwareSupportStatus.Supported;
        }

        public static CpuHardwareSupportStatus Classify(
            string manufacturer,
            string productName)
        {
            if (string.IsNullOrWhiteSpace(manufacturer) ||
                string.IsNullOrWhiteSpace(productName))
            {
                return CpuHardwareSupportStatus.IdentityUnavailable;
            }

            return string.Equals(
                    manufacturer.Trim(),
                    "Apple Inc.",
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    productName.Trim(),
                    "MacBookPro16,1",
                    StringComparison.OrdinalIgnoreCase)
                ? CpuHardwareSupportStatus.Supported
                : CpuHardwareSupportStatus.Unsupported;
        }

        public static string UserMessage(CpuHardwareSupportStatus status)
        {
            switch (status)
            {
                case CpuHardwareSupportStatus.IdentityUnavailable:
                    return "CPU presets are unavailable because the hardware identity could not be read.";
                case CpuHardwareSupportStatus.Unsupported:
                    return "CPU presets in this alpha are supported only on Apple Inc. MacBookPro16,1.";
                case CpuHardwareSupportStatus.Supported:
                    return string.Empty;
                default:
                    return "CPU presets are unavailable because hardware support is unknown.";
            }
        }
    }

    /// <summary>
    /// Values accepted by Windows for PERFBOOSTMODE
    /// (be337238-0d82-4146-a960-4f3749d470c7).
    /// </summary>
    public enum ProcessorPerformanceBoostMode : uint
    {
        Disabled = 0,
        Enabled = 1,
        Aggressive = 2,
        EfficientEnabled = 3,
        EfficientAggressive = 4,
        AggressiveAtGuaranteed = 5,
        EfficientAggressiveAtGuaranteed = 6
    }

    /// <summary>
    /// Immutable application policy for one CPU preset. The same object is
    /// returned for every lookup so UI and platform writers consume one
    /// reviewed definition rather than independently-created copies.
    /// </summary>
    public sealed class PowerPresetDefinition
    {
        internal PowerPresetDefinition(
            PowerPreset preset,
            string displayName,
            string shortDescription,
            uint minimumProcessorAc,
            uint minimumProcessorDc,
            uint maximumProcessorAc,
            uint maximumProcessorDc,
            ProcessorPerformanceBoostMode boostModeAc,
            ProcessorPerformanceBoostMode boostModeDc,
            uint energyPreferenceAc,
            uint energyPreferenceDc,
            uint coolingPolicyAc,
            uint coolingPolicyDc)
        {
            Preset = preset;
            DisplayName = displayName;
            ShortDescription = shortDescription;
            MinimumProcessorAc = minimumProcessorAc;
            MinimumProcessorDc = minimumProcessorDc;
            MaximumProcessorAc = maximumProcessorAc;
            MaximumProcessorDc = maximumProcessorDc;
            BoostModeAc = boostModeAc;
            BoostModeDc = boostModeDc;
            EnergyPreferenceAc = energyPreferenceAc;
            EnergyPreferenceDc = energyPreferenceDc;
            CoolingPolicyAc = coolingPolicyAc;
            CoolingPolicyDc = coolingPolicyDc;
        }

        public PowerPreset Preset { get; private set; }
        public string DisplayName { get; private set; }
        public string ShortDescription { get; private set; }
        public uint MinimumProcessorAc { get; private set; }
        public uint MinimumProcessorDc { get; private set; }
        public uint MaximumProcessorAc { get; private set; }
        public uint MaximumProcessorDc { get; private set; }
        public ProcessorPerformanceBoostMode BoostModeAc { get; private set; }
        public ProcessorPerformanceBoostMode BoostModeDc { get; private set; }
        public uint EnergyPreferenceAc { get; private set; }
        public uint EnergyPreferenceDc { get; private set; }
        public uint CoolingPolicyAc { get; private set; }
        public uint CoolingPolicyDc { get; private set; }

        public static string BoostLabel(ProcessorPerformanceBoostMode value)
        {
            return PowerPresetCatalog.GetBoostModeLabel(value);
        }

        public static string CoolingLabel(uint value)
        {
            return value == 0 ? "Passive" : "Active";
        }
    }

    /// <summary>
    /// Immutable catalog of reviewed CPU policies. It belongs to Application,
    /// not to a Windows adapter, because both UI and writer use these values.
    /// </summary>
    public static class PowerPresetCatalog
    {
        private static readonly IList<PowerPresetDefinition> DefinitionsValue =
            new List<PowerPresetDefinition>
            {
                new PowerPresetDefinition(
                    PowerPreset.Normal,
                    "Everyday",
                    "Responsive on AC, gentler on battery.",
                    5,
                    5,
                    100,
                    90,
                    ProcessorPerformanceBoostMode.Enabled,
                    ProcessorPerformanceBoostMode.Enabled,
                    35,
                    60,
                    1,
                    0),
                new PowerPresetDefinition(
                    PowerPreset.Cool,
                    "Cool & quiet",
                    "No turbo; lower battery ceiling and quieter cooling.",
                    5,
                    5,
                    99,
                    80,
                    ProcessorPerformanceBoostMode.Disabled,
                    ProcessorPerformanceBoostMode.Disabled,
                    45,
                    75,
                    1,
                    0),
                new PowerPresetDefinition(
                    PowerPreset.MaximumBattery,
                    "Battery saver",
                    "No turbo; strongest CPU and cooling limits.",
                    5,
                    5,
                    85,
                    65,
                    ProcessorPerformanceBoostMode.Disabled,
                    ProcessorPerformanceBoostMode.Disabled,
                    65,
                    85,
                    0,
                    0)
            }.AsReadOnly();

        public static IList<PowerPresetDefinition> All => DefinitionsValue;

        /// <summary>
        /// The reviewed name of a preset, or its enum name if the value is not
        /// one this catalog knows. Display paths use this rather than each
        /// deciding for itself whether Get is allowed to throw at them: a value
        /// read back from a durable journal is not guaranteed to be in range.
        /// </summary>
        public static string SafeDisplayName(PowerPreset preset)
        {
            return IsKnown(preset) ? Get(preset).DisplayName : preset.ToString();
        }

        public static bool IsKnown(PowerPreset preset)
        {
            return preset == PowerPreset.Normal
                || preset == PowerPreset.Cool
                || preset == PowerPreset.MaximumBattery;
        }

        public static PowerPresetDefinition Get(PowerPreset preset)
        {
            switch (preset)
            {
                case PowerPreset.Normal:
                    return DefinitionsValue[0];
                case PowerPreset.Cool:
                    return DefinitionsValue[1];
                case PowerPreset.MaximumBattery:
                    return DefinitionsValue[2];
                default:
                    throw new ArgumentOutOfRangeException(nameof(preset));
            }
        }

        public static string GetBoostModeLabel(
            ProcessorPerformanceBoostMode value)
        {
            switch (value)
            {
                case ProcessorPerformanceBoostMode.Disabled:
                    return "Disabled";
                case ProcessorPerformanceBoostMode.Enabled:
                    return "Enabled";
                case ProcessorPerformanceBoostMode.Aggressive:
                    return "Aggressive";
                case ProcessorPerformanceBoostMode.EfficientEnabled:
                    return "Efficient enabled";
                case ProcessorPerformanceBoostMode.EfficientAggressive:
                    return "Efficient aggressive";
                case ProcessorPerformanceBoostMode.AggressiveAtGuaranteed:
                    return "Aggressive at guaranteed";
                case ProcessorPerformanceBoostMode.EfficientAggressiveAtGuaranteed:
                    return "Efficient aggressive at guaranteed";
                default:
                    throw new ArgumentOutOfRangeException(nameof(value));
            }
        }
    }
}
