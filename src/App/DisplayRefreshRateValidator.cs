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

            // Native 60 Hz is a recovery action. Entering 48 Hz additionally
            // requires exact reviewed override bytes for this panel.
            if (refreshRateHz == 48)
            {
                try
                {
                    HardwareSnapshot coreHardware = hardware.ToCoreSnapshot();
                    ProfileSelectionResult selection =
                        ProfileCatalog.Select(coreHardware);
                    if (!selection.HardwareSupported)
                    {
                        return OptimizationActionResult.Unsupported(
                            OperationCode.UnsupportedCapability,
                            "No reviewed 48 Hz profile matches this Mac and panel.");
                    }

                    byte[] currentOverride =
                        hardware.InternalDisplay.ExistingEdidOverride;
                    byte[] reviewedOverride = selection.Profile
                        .BuildOverride(coreHardware)
                        .ToByteArray();
                    if (!FixedTimeComparer.AreEqual(currentOverride, reviewedOverride))
                    {
                        return OptimizationActionResult.Unsupported(
                            OperationCode.UnsupportedCapability,
                            "The installed display override is absent or does not "
                                + "match the reviewed 48 Hz profile.");
                    }
                }
                catch (Exception exception)
                {
                    return OptimizationActionResult.Failed(
                        OperationCode.ReadBackFailed,
                        "The reviewed 48 Hz profile could not be verified: "
                            + exception.Message,
                        exception.Message);
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
