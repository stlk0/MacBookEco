using System;
using MacBookEco.AppPolicy;
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
        private readonly Func<
            AdminCommand,
            OptimizationActionResult> _runAdminCommand;
        private readonly Func<int, bool> _isRefreshRateModeAvailable;
        private readonly CpuHardwareSupportStatus _cpuHardwareSupport;
        private readonly bool _displayMutationsBlocked;

        internal WindowsOptimizationActionService(
            DisplayRefreshRateUseCase displayRefresh,
            EdidStatusReader edidStatus,
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

            if (adminHelper == null)
            {
                throw new ArgumentNullException(nameof(adminHelper));
            }

            _setDisplayRefreshRate = displayRefresh.SetRefreshRate;
            _readEdidStatus = edidStatus.Read;
            _readPowerStatus = powerStatus.Read;
            _runAdminCommand = adminHelper.Run;
            _isRefreshRateModeAvailable =
                displayRefresh.IsRefreshRateModeAvailable;
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
            Func<AdminCommand, OptimizationActionResult> runAdminCommand,
            CpuHardwareSupportStatus cpuHardwareSupport,
            OptimizationActionResult startupRecovery = null,
            Func<int, bool> isRefreshRateModeAvailable = null)
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
            _runAdminCommand = runAdminCommand;
            _isRefreshRateModeAvailable = isRefreshRateModeAvailable ??
                delegate { return false; };
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

        public OptimizationActionResult InstallDisplaySupport()
        {
            OptimizationActionResult blocked = BlockedDisplayMutation();
            if (blocked != null)
            {
                return blocked;
            }

            OptimizationActionResult helperResult = _runAdminCommand(
                AdminCommand.InstallDisplay);
            if (!helperResult.Succeeded)
            {
                return helperResult;
            }

            try
            {
                DisplayOverrideStatus status = _readEdidStatus();
                bool installed =
                    status.State == ManagedResourceState.Installed;
                return installed
                    ? OptimizationActionResult.Successful(
                        "Eco display support is installed and verified. Profile: "
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
                AdminCommand.RemoveDisplay);
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

            OptimizationActionResult helperResult = _runAdminCommand(command);
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
                bool installed =
                    power.State == ManagedResourceState.Installed;
                bool active = installed
                    && power.OwnedScheme != Guid.Empty
                    && power.ActiveScheme == power.OwnedScheme;

                return new OptimizationStateSnapshot(
                    true,
                    active,
                    active ? (PowerPreset?)power.Preset : null,
                    power.State.ToString(),
                    display.State.ToString(),
                    Safe(display.ProfileId),
                    active
                        ? "The active Windows power plan is owned by MacBook Eco."
                        : "The original or another Windows power plan is active.",
                    display.State == ManagedResourceState.Installed
                        && ReadModeAvailability(48),
                    display.State == ManagedResourceState.Installed
                        && ReadModeAvailability(58),
                    ReadModeAvailability(60));
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
                AdminCommand.RestorePower);
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

        private bool ReadModeAvailability(int refreshRateHz)
        {
            try
            {
                return _isRefreshRateModeAvailable(refreshRateHz);
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
