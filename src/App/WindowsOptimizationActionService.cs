using System;
using MacBookEco.AppPolicy;
using MacBookEco.Core;
using MacBookEco.Platform.Windows;

namespace MacBookEco.App
{
    /// <summary>
    /// Thin application coordinator for display and power use cases. Native
    /// adapters are created only by the App composition root and injected here.
    /// </summary>
    public sealed class WindowsOptimizationActionService : IOptimizationActionService
    {
        private readonly Func<
            int,
            Func<
                DisplayModeConfirmationRequest,
                DisplayModeConfirmationDecision>,
            OptimizationActionResult> _setDisplayRefreshRate;
        private readonly Func<DisplayOverrideStatus> _readEdidStatus;
        private readonly Func<PowerSchemeStatus> _readPowerStatus;
        private readonly Func<string, DisplayProfileCandidateStatus>
            _readDisplayCandidate;
        private readonly Func<
            AdminCommand,
            string,
            OptimizationActionResult> _runAdminCommand;
        private readonly Func<bool> _is48HzModeAvailable;
        private readonly CpuHardwareSupportStatus _cpuHardwareSupport;
        private readonly bool _displayMutationsBlocked;

        internal WindowsOptimizationActionService(
            DisplayRefreshRateUseCase displayRefresh,
            EdidStatusReader edidStatus,
            DisplayProfileCandidateReader displayCandidate,
            PowerStatusReader powerStatus,
            ElevatedAdminHelper adminHelper,
            CpuHardwareSupportStatus cpuHardwareSupport,
            OptimizationActionResult startupRecovery = null)
        {
            if (displayRefresh == null)
            {
                throw new ArgumentNullException(nameof(displayRefresh));
            }

            if (edidStatus == null)
            {
                throw new ArgumentNullException(nameof(edidStatus));
            }

            if (powerStatus == null)
            {
                throw new ArgumentNullException(nameof(powerStatus));
            }

            if (displayCandidate == null)
            {
                throw new ArgumentNullException(nameof(displayCandidate));
            }

            if (adminHelper == null)
            {
                throw new ArgumentNullException(nameof(adminHelper));
            }

            _setDisplayRefreshRate = displayRefresh.SetRefreshRate;
            _readEdidStatus = edidStatus.Read;
            _readDisplayCandidate = displayCandidate.Read;
            _readPowerStatus = powerStatus.Read;
            _runAdminCommand = adminHelper.Run;
            _is48HzModeAvailable = displayRefresh.Is48HzModeAvailable;
            _cpuHardwareSupport = cpuHardwareSupport;
            _displayMutationsBlocked = ShouldBlockDisplayMutations(
                startupRecovery);
        }

        internal WindowsOptimizationActionService(
            Func<
                int,
                Func<
                    DisplayModeConfirmationRequest,
                    DisplayModeConfirmationDecision>,
                OptimizationActionResult> setDisplayRefreshRate,
            Func<DisplayOverrideStatus> readEdidStatus,
            Func<PowerSchemeStatus> readPowerStatus,
            Func<AdminCommand, string, OptimizationActionResult> runAdminCommand,
            CpuHardwareSupportStatus cpuHardwareSupport,
            OptimizationActionResult startupRecovery = null,
            Func<bool> is48HzModeAvailable = null,
            Func<string, DisplayProfileCandidateStatus> readDisplayCandidate = null)
        {
            if (setDisplayRefreshRate == null)
            {
                throw new ArgumentNullException(nameof(setDisplayRefreshRate));
            }

            if (readEdidStatus == null)
            {
                throw new ArgumentNullException(nameof(readEdidStatus));
            }

            if (readPowerStatus == null)
            {
                throw new ArgumentNullException(nameof(readPowerStatus));
            }

            if (runAdminCommand == null)
            {
                throw new ArgumentNullException(nameof(runAdminCommand));
            }

            _setDisplayRefreshRate = setDisplayRefreshRate;
            _readEdidStatus = readEdidStatus;
            _readPowerStatus = readPowerStatus;
            _readDisplayCandidate = readDisplayCandidate
                ?? delegate
                {
                    return new DisplayProfileCandidateStatus();
                };
            _runAdminCommand = runAdminCommand;
            _is48HzModeAvailable = is48HzModeAvailable
                ?? delegate { return false; };
            _cpuHardwareSupport = cpuHardwareSupport;
            _displayMutationsBlocked = ShouldBlockDisplayMutations(
                startupRecovery);
        }

        public OptimizationActionResult SetDisplayRefreshRate(
            int refreshRateHz,
            Func<DisplayModeConfirmationRequest, DisplayModeConfirmationDecision>
                confirmation)
        {
            OptimizationActionResult blocked = BlockedDisplayMutation();
            if (blocked != null)
            {
                return blocked;
            }

            return _setDisplayRefreshRate(refreshRateHz, confirmation);
        }

        public OptimizationActionResult InstallDisplaySupport(
            string experimentalAcknowledgementToken = null)
        {
            OptimizationActionResult blocked = BlockedDisplayMutation();
            if (blocked != null)
            {
                return blocked;
            }

            AdminCommand installCommand = AdminCommand.InstallDisplay;
            string helperAcknowledgementToken = null;
            try
            {
                DisplayOverrideStatus before = _readEdidStatus();
                bool freshInstall =
                    before.State == ManagedResourceState.NotInstalled ||
                    before.State == ManagedResourceState.Restored;
                if (freshInstall)
                {
                    DisplayProfileCandidateStatus candidate =
                        _readDisplayCandidate(string.Empty);
                    if (candidate == null || !candidate.Available ||
                        !candidate.EligibleForInstall)
                    {
                        return OptimizationActionResult.Unsupported(
                            OperationCode.UnsupportedCapability,
                            "No current reviewed or experimental 48 Hz candidate "
                                + "passes every hardware, EDID, topology and "
                                + "ownership gate.");
                    }

                    if (candidate.Experimental &&
                        !AcknowledgementTokensEqual(
                            experimentalAcknowledgementToken,
                            candidate.ExperimentalAcknowledgementToken))
                    {
                        return OptimizationActionResult.Cancelled(
                            OperationCode.UserCancelled,
                            "The experimental 48 Hz candidate was not explicitly "
                                + "acknowledged, or it changed after confirmation. "
                                + "No helper was started.");
                    }

                    if (!candidate.Experimental &&
                        !string.IsNullOrEmpty(
                            experimentalAcknowledgementToken))
                    {
                        return OptimizationActionResult.Cancelled(
                            OperationCode.UserCancelled,
                            "The display candidate changed after confirmation. "
                                + "No helper was started.");
                    }

                    installCommand = candidate.Experimental
                        ? AdminCommand.InstallExperimentalDisplay
                        : AdminCommand.InstallDisplay;
                    helperAcknowledgementToken = candidate.Experimental
                        ? candidate.ExperimentalAcknowledgementToken
                        : null;
                }
                else if (before.ExperimentalProfile)
                {
                    installCommand = AdminCommand.InstallExperimentalDisplay;
                    helperAcknowledgementToken =
                        Experimental48HzProfileGenerator
                            .CreateAcknowledgementToken(before.ProfileId);
                    if (experimentalAcknowledgementToken != null &&
                        !AcknowledgementTokensEqual(
                            experimentalAcknowledgementToken,
                            helperAcknowledgementToken))
                    {
                        return OptimizationActionResult.Cancelled(
                            OperationCode.UserCancelled,
                            "The display state changed after confirmation. "
                                + "No helper was started.");
                    }
                }
                else if (!string.IsNullOrEmpty(
                    experimentalAcknowledgementToken))
                {
                    return OptimizationActionResult.Cancelled(
                        OperationCode.UserCancelled,
                        "The display state changed after confirmation. "
                            + "No helper was started.");
                }
            }
            catch (Exception exception)
            {
                return OptimizationActionResult.Faulted(
                    "Display candidate validation failed",
                    exception);
            }

            OptimizationActionResult helperResult = _runAdminCommand(
                installCommand,
                helperAcknowledgementToken);
            if (!helperResult.Succeeded)
            {
                return helperResult;
            }

            try
            {
                DisplayOverrideStatus status = _readEdidStatus();
                bool installed =
                    status.State == ManagedResourceState.Installed &&
                    status.ExperimentalProfile ==
                        (installCommand ==
                            AdminCommand.InstallExperimentalDisplay) &&
                    (installCommand != AdminCommand.InstallExperimentalDisplay ||
                        Experimental48HzProfileGenerator
                            .AcknowledgementTokenMatches(
                                status.ProfileId,
                                helperAcknowledgementToken));
                return installed
                    ? OptimizationActionResult.Successful(
                        status.ExperimentalProfile
                            ? "The experimental local 48 Hz profile is installed."
                            : "Reviewed 48 Hz support is installed. Profile: "
                                + Safe(status.ProfileId) + ".",
                        OperationCode.None,
                        true)
                    : OptimizationActionResult.Failed(
                        OperationCode.StateVerificationFailed,
                        "The helper finished, but read-back state is "
                            + status.State + " instead of Installed.",
                        string.Empty);
            }
            catch (Exception exception)
            {
                return OptimizationActionResult.Faulted(
                    "Display support read-back failed",
                    exception);
            }
        }

        public OptimizationActionResult RemoveDisplaySupport()
        {
            OptimizationActionResult blocked = BlockedDisplayMutation();
            if (blocked != null)
            {
                return blocked;
            }

            OptimizationActionResult helperResult = _runAdminCommand(
                AdminCommand.RemoveDisplay,
                null);
            if (!helperResult.Succeeded)
            {
                return helperResult;
            }

            try
            {
                DisplayOverrideStatus status = _readEdidStatus();
                bool restored =
                    status.State == ManagedResourceState.Restored;
                return restored
                    ? OptimizationActionResult.Successful(
                        "The original display support was restored.",
                        OperationCode.None,
                        true)
                    : OptimizationActionResult.Failed(
                        OperationCode.StateVerificationFailed,
                        "The helper finished, but read-back state is "
                            + status.State + " instead of Restored.",
                        string.Empty);
            }
            catch (Exception exception)
            {
                return OptimizationActionResult.Faulted(
                    "Display restore read-back failed",
                    exception);
            }
        }

        public OptimizationActionResult ApplyCpuPreset(PowerPreset preset)
        {
            if (_cpuHardwareSupport != CpuHardwareSupportStatus.Supported)
            {
                return OptimizationActionResult.Unsupported(
                    OperationCode.UnsupportedCapability,
                    CpuHardwareSupportPolicy.UserMessage(_cpuHardwareSupport));
            }

            AdminCommand command;
            switch (preset)
            {
                case PowerPreset.Normal:
                    command = AdminCommand.ApplyPowerNormal;
                    break;
                case PowerPreset.Cool:
                    command = AdminCommand.ApplyPowerCool;
                    break;
                case PowerPreset.MaximumBattery:
                    command = AdminCommand.ApplyPowerBattery;
                    break;
                default:
                    return OptimizationActionResult.Failed(
                        OperationCode.InvalidRequest,
                        "Unknown CPU preset.",
                        string.Empty);
            }

            OptimizationActionResult helperResult = _runAdminCommand(
                command,
                null);
            if (!helperResult.Succeeded)
            {
                return helperResult;
            }

            try
            {
                PowerSchemeStatus status = _readPowerStatus();
                bool installed =
                    status.State == ManagedResourceState.Installed;
                bool active = status.ActiveScheme == status.OwnedScheme;
                bool verified = installed && active && status.Preset == preset;
                return verified
                    ? OptimizationActionResult.Successful(
                        "CPU preset " + PowerPresetCatalog.SafeDisplayName(preset)
                            + " is active in an app-owned power scheme.",
                        OperationCode.None,
                        false)
                    : OptimizationActionResult.Failed(
                        OperationCode.StateVerificationFailed,
                        "The helper finished, but power-plan read-back did not match "
                            + "the requested preset.",
                        string.Empty);
            }
            catch (Exception exception)
            {
                return OptimizationActionResult.Faulted(
                    "CPU preset read-back failed",
                    exception);
            }
        }

        public OptimizationStateSnapshot ReadState()
        {
            try
            {
                PowerSchemeStatus power = _readPowerStatus();
                DisplayOverrideStatus display = _readEdidStatus();
                bool freshDisplayInstall =
                    display.State == ManagedResourceState.NotInstalled ||
                    display.State == ManagedResourceState.Restored;
                DisplayProfileCandidateStatus candidate =
                    _readDisplayCandidate(
                        freshDisplayInstall
                            ? string.Empty
                            : display.ProfileId)
                        ?? new DisplayProfileCandidateStatus();
                bool installed =
                    power.State == ManagedResourceState.Installed;
                bool active = installed
                    && power.OwnedScheme != Guid.Empty
                    && power.ActiveScheme == power.OwnedScheme;
                string displayProfileId = freshDisplayInstall
                    ? (candidate.Experimental
                        ? string.Empty
                        : candidate.ReviewedProfileId ?? string.Empty)
                    : (display.ExperimentalProfile
                        ? "Experimental local 48 Hz candidate"
                        : Safe(display.ProfileId));
                bool displayProfileExperimental = freshDisplayInstall
                    ? candidate.Experimental
                    : display.ExperimentalProfile || candidate.Experimental;

                return new OptimizationStateSnapshot(
                    true,
                    active,
                    active ? (PowerPreset?)power.Preset : null,
                    power.State.ToString(),
                    display.State.ToString(),
                    displayProfileId,
                    active
                        ? "The active Windows power plan is owned by MacBook Eco."
                        : "The original or another Windows power plan is active.",
                    display.State == ManagedResourceState.Installed
                        && Read48HzModeAvailability(),
                    candidate.EligibleForInstall,
                    displayProfileExperimental,
                    candidate.SafeSummary,
                    freshDisplayInstall && candidate.Experimental &&
                        candidate.EligibleForInstall
                            ? candidate.ExperimentalAcknowledgementToken
                            : null);
            }
            catch (Exception exception)
            {
                return OptimizationStateSnapshot.Unavailable(
                    "Optimization state could not be read: " + exception.Message);
            }
        }

        public OptimizationActionResult RestoreCpuPower()
        {
            OptimizationActionResult helperResult = _runAdminCommand(
                AdminCommand.RestorePower,
                null);
            if (!helperResult.Succeeded)
            {
                return helperResult;
            }

            try
            {
                PowerSchemeStatus status = _readPowerStatus();
                bool restored =
                    status.State == ManagedResourceState.Restored;
                bool verified = restored
                    && status.ActiveScheme == status.OriginalScheme;
                return verified
                    ? OptimizationActionResult.Successful(
                        "The original Windows power scheme is active again.",
                        OperationCode.None,
                        false)
                    : OptimizationActionResult.Failed(
                        OperationCode.StateVerificationFailed,
                        "The helper finished, but the original power scheme was not verified.",
                        string.Empty);
            }
            catch (Exception exception)
            {
                return OptimizationActionResult.Faulted(
                    "Power-plan restore read-back failed",
                    exception);
            }
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "N/A" : value.Trim();
        }

        private static bool AcknowledgementTokensEqual(
            string first,
            string second)
        {
            Sha256Digest firstDigest;
            Sha256Digest secondDigest;
            return Sha256Digest.TryParseCanonical(first, out firstDigest) &&
                Sha256Digest.TryParseCanonical(second, out secondDigest) &&
                firstDigest.Equals(secondDigest);
        }

        private bool Read48HzModeAvailability()
        {
            try
            {
                return _is48HzModeAvailable();
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool ShouldBlockDisplayMutations(
            OptimizationActionResult startupRecovery)
        {
            return startupRecovery != null && !startupRecovery.Succeeded;
        }

        private OptimizationActionResult BlockedDisplayMutation()
        {
            if (!_displayMutationsBlocked)
            {
                return null;
            }

            return OptimizationActionResult.Indeterminate(
                OperationCode.DisplayRollbackUnverified,
                "Display changes are disabled because startup recovery did not "
                    + "verify every stale watchdog session. Restart MacBook Eco "
                    + "after recovery completes.",
                "startup-display-recovery=unverified");
        }
    }
}
