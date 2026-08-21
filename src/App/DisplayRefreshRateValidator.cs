using System;
using MacBookEco.Core;
using MacBookEco.Platform.Windows;

namespace MacBookEco.App
{
    /// <summary>
    /// Resolves and validates the reviewed internal panel before any display
    /// mode change. This class does not mutate Windows state.
    /// </summary>
    internal sealed class DisplayRefreshRateValidator
    {
        private readonly HardwareDiscoveryService _discovery;
        private readonly StableDisplayTargetResolver _stableDisplayTargets;

        public DisplayRefreshRateValidator(
            HardwareDiscoveryService discovery,
            StableDisplayTargetResolver stableDisplayTargets)
        {
            if (discovery == null)
            {
                throw new ArgumentNullException(nameof(discovery));
            }

            if (stableDisplayTargets == null)
            {
                throw new ArgumentNullException(nameof(stableDisplayTargets));
            }

            _discovery = discovery;
            _stableDisplayTargets = stableDisplayTargets;
        }

        public OptimizationActionResult Validate(
            int refreshRateHz,
            out StableDisplayTarget displayTarget)
        {
            displayTarget = null;
            WindowsHardwareSnapshot hardware = _discovery.Discover();
            if (hardware == null)
            {
                return OptimizationActionResult.Unsupported(
                    OperationCode.UnsupportedCapability,
                    "Hardware discovery returned no result.");
            }

            if (!hardware.IsAppleHardware)
            {
                return OptimizationActionResult.Unsupported(
                    OperationCode.UnsupportedCapability,
                    "Display optimization is enabled only on recognized Apple hardware.");
            }

            if (hardware.InternalDisplay == null || !hardware.InternalDisplay.IsInternal)
            {
                return OptimizationActionResult.Unsupported(
                    OperationCode.UnsupportedCapability,
                    "The active internal panel could not be identified.");
            }

            try
            {
                displayTarget = _stableDisplayTargets.ResolveActive();
                if (!MatchesSnapshotIdentity(displayTarget, hardware.InternalDisplay))
                {
                    displayTarget = null;
                    return OptimizationActionResult.Failed(
                        OperationCode.StateVerificationFailed,
                        "The active internal panel changed before its durable identity could be verified.",
                        string.Empty);
                }
            }
            catch (Exception exception)
            {
                return OptimizationActionResult.Failed(
                    OperationCode.ReadBackFailed,
                    "The active internal panel could not be re-resolved: "
                        + exception.Message,
                    exception.Message);
            }

            // Native 60 Hz is a recovery action. Entering either Eco mode
            // additionally requires exact compiled override bytes for this
            // panel. This accepts the recovery-only legacy profile for 48 Hz
            // but never treats it as authorization for 58 Hz.
            if (refreshRateHz == 48 || refreshRateHz == 58)
            {
                try
                {
                    HardwareSnapshot coreHardware = hardware.ToCoreSnapshot();
                    byte[] currentOverride =
                        hardware.InternalDisplay.ExistingEdidOverride;
                    DisplayProfile profile = FindExactInstalledProfile(
                        coreHardware,
                        currentOverride,
                        refreshRateHz);
                    if (profile == null)
                    {
                        return OptimizationActionResult.Unsupported(
                            OperationCode.UnsupportedCapability,
                            "No exact installed Eco display profile matches this "
                                + "Mac, panel, and requested refresh rate.");
                    }
                }
                catch (Exception exception)
                {
                    return OptimizationActionResult.Failed(
                        OperationCode.ReadBackFailed,
                        "The installed Eco display profile could not be verified: "
                            + exception.Message,
                        exception.Message);
                }
            }

            return null;
        }

        private static DisplayProfile FindExactInstalledProfile(
            HardwareSnapshot hardware,
            byte[] currentOverride,
            int refreshRateHz)
        {
            if (hardware == null || currentOverride == null)
            {
                return null;
            }

            for (var index = 0; index < ProfileCatalog.All.Count; index++)
            {
                DisplayProfile profile = ProfileCatalog.All[index];
                if (profile.GetTargetMode(refreshRateHz) != null &&
                    profile.Match(hardware).HardwareSupported &&
                    FixedTimeComparer.AreEqual(
                        currentOverride,
                        profile.BuildOverride(hardware).ToByteArray()))
                {
                    return profile;
                }
            }

            // Old 48-only profile IDs remain available by ID for journal
            // recovery, but they are intentionally not part of All. Match the
            // known legacy variant selected by the same hardware signature.
            if (refreshRateHz == 48)
            {
                string[] legacyIds =
                {
                    ProfileCatalog.MacBookPro161Appa044ProfileId,
                    ProfileCatalog.MacBookPro161Appa044Faf4ProfileId,
                    ProfileCatalog.MacBookPro161Appa0444b2eProfileId
                };
                for (var index = 0; index < legacyIds.Length; index++)
                {
                    DisplayProfile profile = ProfileCatalog.GetById(
                        legacyIds[index]);
                    if (profile != null &&
                        profile.Match(hardware).HardwareSupported &&
                        FixedTimeComparer.AreEqual(
                            currentOverride,
                            profile.BuildOverride(hardware).ToByteArray()))
                    {
                        return profile;
                    }
                }
            }

            return null;
        }

        public StableDisplayTarget ResolveActive(MonitorIdentity identity)
        {
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            return _stableDisplayTargets.ResolveActive(identity);
        }

        private static bool MatchesSnapshotIdentity(
            StableDisplayTarget target,
            WindowsMonitorInfo monitor)
        {
            if (target == null || target.Identity == null || monitor == null ||
                string.IsNullOrWhiteSpace(monitor.DeviceInstanceId) ||
                string.IsNullOrWhiteSpace(monitor.HardwareId))
            {
                return false;
            }

            try
            {
                EdidBaseBlock edid = HardwareDiscoveryService.CreateCoreEdid(
                    monitor.Edid);
                MonitorIdentity snapshotIdentity = new MonitorIdentity(
                    monitor.DeviceInstanceId,
                    monitor.HardwareId,
                    edid.ManufacturerCode,
                    Sha256Digest.Compute(edid.ToByteArray()));
                return target.Identity.Equals(snapshotIdentity);
            }
            catch (Exception)
            {
                return false;
            }
        }

    }
}
