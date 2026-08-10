using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using MacBookEco.AppPolicy;
using MacBookEco.Core;

namespace MacBookEco.Platform.Windows
{
    internal delegate bool TryReadPowerSchemeName(
        Guid scheme,
        out string name);

    internal delegate bool TryReadPowerSettingValues(
        Guid scheme,
        Guid setting,
        out uint ac,
        out uint dc);

    internal sealed class DesiredPowerSetting
    {
        internal DesiredPowerSetting(
            string name,
            Guid settingGuid,
            uint acValue,
            uint dcValue)
        {
            Name = name;
            SettingGuid = settingGuid;
            AcValue = acValue;
            DcValue = dcValue;
        }

        internal string Name;
        internal Guid SettingGuid;
        internal uint AcValue;
        internal uint DcValue;
    }

    /// <summary>
    /// Compiles and verifies the exact portion of an app-owned power scheme.
    /// The elevated writer and unelevated status reader share this definition
    /// so a terminal journal state cannot use a weaker ownership check.
    /// </summary>
    internal static class PowerManagedSettings
    {
        private static readonly Guid ProcessorSubgroupId =
            new Guid("54533251-82BE-4824-96C1-47B60B740D00");
        private static readonly Guid MinimumProcessorState =
            new Guid("893DEE8E-2BEF-41E0-89C6-B55D0929964C");
        private static readonly Guid MaximumProcessorState =
            new Guid("BC5038F7-23E0-4960-96DA-33ABAF5935EC");
        private static readonly Guid PerformanceBoostMode =
            new Guid("BE337238-0D82-4146-A960-4F3749D470C7");
        private static readonly Guid EnergyPerformancePreference =
            new Guid("36687F9E-E3A5-4DBF-B1DC-15EB381C6863");
        private static readonly Guid SystemCoolingPolicy =
            new Guid("94D3A615-A899-4AC5-AE2B-E4D8F634367F");

        internal static Guid ProcessorSubgroup
        {
            get { return ProcessorSubgroupId; }
        }

        internal static IList<DesiredPowerSetting> BuildPreset(
            PowerPreset preset)
        {
            PowerPresetDefinition definition = PowerPresetCatalog.Get(preset);
            List<DesiredPowerSetting> result =
                new List<DesiredPowerSetting>();
            result.Add(new DesiredPowerSetting(
                "Minimum processor state",
                MinimumProcessorState,
                definition.MinimumProcessorAc,
                definition.MinimumProcessorDc));
            result.Add(new DesiredPowerSetting(
                "Maximum processor state",
                MaximumProcessorState,
                definition.MaximumProcessorAc,
                definition.MaximumProcessorDc));
            result.Add(new DesiredPowerSetting(
                "Processor performance boost mode",
                PerformanceBoostMode,
                (uint)definition.BoostModeAc,
                (uint)definition.BoostModeDc));
            result.Add(new DesiredPowerSetting(
                "Energy performance preference",
                EnergyPerformancePreference,
                definition.EnergyPreferenceAc,
                definition.EnergyPreferenceDc));
            result.Add(new DesiredPowerSetting(
                "System cooling policy",
                SystemCoolingPolicy,
                definition.CoolingPolicyAc,
                definition.CoolingPolicyDc));
            return result.AsReadOnly();
        }

        internal static Sha256Digest ComputeManagedSettingsHash(
            PowerPreset preset)
        {
            IList<DesiredPowerSetting> settings = BuildPreset(preset);
            StringBuilder canonical = new StringBuilder();
            canonical.Append(ToJournalPreset(preset).ToString());
            canonical.Append('|');
            int index;
            for (index = 0; index < settings.Count; index++)
            {
                canonical.Append(settings[index].SettingGuid.ToString("D"));
                canonical.Append(':');
                canonical.Append(settings[index].AcValue.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                canonical.Append(':');
                canonical.Append(settings[index].DcValue.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                canonical.Append(';');
            }

            return Sha256Digest.Compute(Encoding.UTF8.GetBytes(
                canonical.ToString()));
        }

        internal static bool TryReadValues(
            Guid scheme,
            Guid setting,
            out uint ac,
            out uint dc)
        {
            Guid subgroup = ProcessorSubgroupId;
            uint acError = NativeMethods.PowerReadACValueIndex(
                IntPtr.Zero,
                ref scheme,
                ref subgroup,
                ref setting,
                out ac);
            if (PowerSchemeNative.IsDocumentedNotFound(acError))
            {
                dc = 0;
                return false;
            }
            if (acError != NativeMethods.ERROR_SUCCESS)
            {
                throw new Win32Exception(
                    (int)acError,
                    "PowerReadACValueIndex failed.");
            }

            uint dcError = NativeMethods.PowerReadDCValueIndex(
                IntPtr.Zero,
                ref scheme,
                ref subgroup,
                ref setting,
                out dc);
            if (PowerSchemeNative.IsDocumentedNotFound(dcError))
            {
                return false;
            }
            if (dcError != NativeMethods.ERROR_SUCCESS)
            {
                throw new Win32Exception(
                    (int)dcError,
                    "PowerReadDCValueIndex failed.");
            }

            return true;
        }

        internal static PowerOwnedSchemeState ClassifyOwnedScheme(
            PowerTargetIdentity target)
        {
            return ClassifyOwnedScheme(
                target,
                PowerSchemeNative.TryReadFriendlyName,
                TryReadValues);
        }

        internal static PowerOwnedSchemeState ClassifyOwnedScheme(
            PowerTargetIdentity target,
            TryReadPowerSchemeName tryReadName,
            TryReadPowerSettingValues tryReadSetting)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            if (tryReadName == null)
            {
                throw new ArgumentNullException(nameof(tryReadName));
            }
            if (tryReadSetting == null)
            {
                throw new ArgumentNullException(nameof(tryReadSetting));
            }

            PowerOwnedSchemeState marker = ClassifyOwnedMarker(
                target,
                tryReadName);
            if (marker != PowerOwnedSchemeState.ExactOwned)
            {
                return marker;
            }

            PowerPreset preset = FromJournalPreset(target.Preset);
            IList<DesiredPowerSetting> desired = BuildPreset(preset);
            int index;
            for (index = 0; index < desired.Count; index++)
            {
                uint actualAc;
                uint actualDc;
                if (!tryReadSetting(
                        target.OwnedSchemeId,
                        desired[index].SettingGuid,
                        out actualAc,
                        out actualDc))
                {
                    continue;
                }

                if (actualAc != desired[index].AcValue ||
                    actualDc != desired[index].DcValue)
                {
                    return PowerOwnedSchemeState.ForeignOrDiverged;
                }
            }

            return PowerOwnedSchemeState.ExactOwned;
        }

        internal static PowerOwnedSchemeState ClassifyOwnedMarker(
            PowerTargetIdentity target)
        {
            return ClassifyOwnedMarker(
                target,
                PowerSchemeNative.TryReadFriendlyName);
        }

        internal static PowerOwnedSchemeState ClassifyOwnedMarker(
            PowerTargetIdentity target,
            TryReadPowerSchemeName tryReadName)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            if (tryReadName == null)
            {
                throw new ArgumentNullException(nameof(tryReadName));
            }

            string actualName;
            if (!tryReadName(target.OwnedSchemeId, out actualName))
            {
                return PowerOwnedSchemeState.Missing;
            }

            PowerPreset preset = FromJournalPreset(target.Preset);
            if (!ComputeManagedSettingsHash(preset).Equals(
                    target.ManagedSettingsHash) ||
                !string.Equals(
                    actualName,
                    PowerSchemeNaming.OwnedFriendlyName(
                        preset,
                        target.OwnedSchemeId),
                    StringComparison.Ordinal))
            {
                return PowerOwnedSchemeState.ForeignOrDiverged;
            }

            return PowerOwnedSchemeState.ExactOwned;
        }

        private static PowerPreset FromJournalPreset(PowerPresetId preset)
        {
            switch (preset)
            {
                case PowerPresetId.Normal:
                    return PowerPreset.Normal;
                case PowerPresetId.Cool:
                    return PowerPreset.Cool;
                case PowerPresetId.MaximumBattery:
                    return PowerPreset.MaximumBattery;
                default:
                    throw new SecureStateConflictException(
                        "The trusted power journal has an unknown preset.");
            }
        }

        private static PowerPresetId ToJournalPreset(PowerPreset preset)
        {
            switch (preset)
            {
                case PowerPreset.Normal:
                    return PowerPresetId.Normal;
                case PowerPreset.Cool:
                    return PowerPresetId.Cool;
                case PowerPreset.MaximumBattery:
                    return PowerPresetId.MaximumBattery;
                default:
                    throw new ArgumentOutOfRangeException(nameof(preset));
            }
        }
    }
}
