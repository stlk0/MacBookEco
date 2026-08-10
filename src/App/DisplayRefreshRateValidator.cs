using System;
using MacBookEco.Core;
using MacBookEco.Platform.Windows;

namespace MacBookEco.App
{
    /// <summary>
    /// Resolves and validates the app-owned internal-panel profile before any
    /// display mode change. This class does not mutate Windows state.
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
            // requires exact app-owned override bytes for this panel.
            if (refreshRateHz == 48)
            {
                try
                {
                    HardwareSnapshot coreHardware = hardware.ToCoreSnapshot();
                    ProfileSelectionResult reviewedSelection =
                        ProfileCatalog.Select(coreHardware);
                    DisplayProfile profile = reviewedSelection.HardwareSupported
                        ? reviewedSelection.Profile
                        : null;
                    if (profile == null)
                    {
                        DisplayOverrideStatus status =
                            new EdidStatusReader().Read();
                        if (status.State != ManagedResourceState.Installed ||
                            !status.ExperimentalProfile)
                        {
                            return OptimizationActionResult.Unsupported(
                                OperationCode.UnsupportedCapability,
                                "MacBook Eco has not verified an installed, "
                                    + "app-owned experimental 48 Hz override "
                                    + "for this panel.");
                        }

                        profile = ResolveInstalledProfile(
                            status.ProfileId,
                            coreHardware,
                            status.SourceEdidSignature);
                        if (profile == null || !profile.IsExperimental ||
                            !profile.Match(coreHardware).HardwareSupported)
                        {
                            return OptimizationActionResult.Unsupported(
                                OperationCode.UnsupportedCapability,
                                "The installed experimental 48 Hz profile no "
                                    + "longer matches this Mac, panel and "
                                    + "controlling adapter.");
                        }

                        if (coreHardware.NormalizedSourceEdidSignature == null &&
                            (hardware.InternalDisplay.Edid == null ||
                             hardware.InternalDisplay.Edid.Length !=
                                EdidBaseBlock.Length ||
                             status.OwnedOverrideHash == null ||
                             !Sha256Digest.Compute(
                                coreHardware.Edid.ToByteArray()).Equals(
                                    status.OwnedOverrideHash)))
                        {
                            return OptimizationActionResult.Unsupported(
                                OperationCode.UnsupportedCapability,
                                "The experimental source EDID identity cannot "
                                    + "be re-proven for this panel.");
                        }

                        if (!MatchesJournalTarget(
                                displayTarget.Identity,
                                status.TargetMonitorIdentity,
                                status.OwnedOverrideHash))
                        {
                            return OptimizationActionResult.Unsupported(
                                OperationCode.UnsupportedCapability,
                                "The active internal panel is not the exact "
                                    + "physical monitor recorded by the "
                                    + "experimental display transaction.");
                        }
                    }

                    if (!hardware.InternalDisplay
                            .ExistingEdidOverrideReadSucceeded)
                    {
                        return OptimizationActionResult.Failed(
                            OperationCode.ReadBackFailed,
                            "The installed display override could not be read safely.",
                            string.Empty);
                    }

                    byte[] currentOverride =
                        hardware.InternalDisplay.ExistingEdidOverride;
                    byte[] expectedOverride = coreHardware.Edid
                        .InsertDetailedTiming(profile.TargetTiming)
                        .ToByteArray();
                    if (!FixedTimeComparer.AreEqual(
                            currentOverride,
                            expectedOverride))
                    {
                        return OptimizationActionResult.Unsupported(
                            OperationCode.UnsupportedCapability,
                            "The installed display override is absent or does not "
                                + "match the exact app-owned 48 Hz profile.");
                    }
                }
                catch (Exception exception)
                {
                    return OptimizationActionResult.Failed(
                        OperationCode.ReadBackFailed,
                        "The app-owned 48 Hz profile could not be verified: "
                            + exception.Message,
                        exception.Message);
                }
            }

            return null;
        }

        private static DisplayProfile ResolveInstalledProfile(
            string profileId,
            HardwareSnapshot hardware,
            Sha256Digest sourceEdidSignature)
        {
            DisplayProfile reviewed = ProfileCatalog.GetById(profileId);
            if (reviewed != null)
            {
                return reviewed;
            }

            if (hardware == null || !string.Equals(
                    hardware.SystemManufacturer,
                    "Apple Inc.",
                    StringComparison.Ordinal))
            {
                return null;
            }

            ExperimentalProfileGenerationResult generated =
                Experimental48HzProfileGenerator.ResolveForRecovery(
                    profileId,
                    hardware.SystemModel,
                    hardware.PanelHardwareId,
                    hardware.Edid,
                    sourceEdidSignature);
            return generated.Succeeded ? generated.Profile : null;
        }

        internal static bool MatchesJournalTarget(
            MonitorIdentity current,
            MonitorIdentity journaled,
            Sha256Digest ownedOverrideHash)
        {
            return current != null && journaled != null &&
                ownedOverrideHash != null &&
                string.Equals(
                    current.MonitorInstanceId,
                    journaled.MonitorInstanceId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    current.PanelHardwareId,
                    journaled.PanelHardwareId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    current.ManufacturerCode,
                    journaled.ManufacturerCode,
                    StringComparison.Ordinal) &&
                (current.EdidFingerprint.Equals(journaled.EdidFingerprint) ||
                    current.EdidFingerprint.Equals(ownedOverrideHash));
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
                target.Endpoint == null || monitor.Endpoint == null ||
                !target.Endpoint.Equals(monitor.Endpoint) ||
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
