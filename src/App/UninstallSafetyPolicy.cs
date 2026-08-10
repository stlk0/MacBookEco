using System;

namespace MacBookEco.App
{
    /// <summary>
    /// Pure policy for the tray's fast uninstall probe. Platform readers map
    /// their durable-state detail into these three safety categories; only a
    /// fully restored/absent pair permits ordinary uninstall.
    /// </summary>
    public enum UninstallSafetyState
    {
        Safe,
        RecoveryRequired,
        Unknown
    }

    public static class UninstallSafetyPolicy
    {
        public static int GetExitCode(
            UninstallSafetyState display,
            UninstallSafetyState power)
        {
            if (display == UninstallSafetyState.Safe &&
                power == UninstallSafetyState.Safe)
            {
                return 0;
            }

            if (display == UninstallSafetyState.Unknown ||
                power == UninstallSafetyState.Unknown)
            {
                return 2;
            }

            return 1;
        }

        internal static bool IsSafeStateName(string state)
        {
            return string.Equals(
                    state,
                    "NotInstalled",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    state,
                    "Restored",
                    StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsRecoverableStateName(string state)
        {
            return string.Equals(
                    state,
                    "Installed",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    state,
                    "RecoveryRequired",
                    StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsConflictStateName(string state)
        {
            return string.Equals(
                state,
                "Conflict",
                StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Runs the same fail-closed recovery actions exposed by the dashboard,
    /// but in the fixed order required before uninstall. The elevated helper
    /// still proves exact ownership and performs read-back for every mutation.
    /// </summary>
    internal sealed class UninstallRecoveryCoordinator
    {
        private readonly IOptimizationActionService _actions;

        internal UninstallRecoveryCoordinator(IOptimizationActionService actions)
        {
            if (actions == null)
            {
                throw new ArgumentNullException(nameof(actions));
            }

            _actions = actions;
        }

        internal OptimizationActionResult Recover(
            Func<
                DisplayModeConfirmationRequest,
                DisplayModeConfirmationDecision> displayConfirmation)
        {
            OptimizationStateSnapshot state = ReadState();
            OptimizationActionResult unavailable = RequireAvailable(state);
            if (unavailable != null)
            {
                return unavailable;
            }

            bool restartRequired = false;
            string displayState = state.DisplaySupportState;
            if (UninstallSafetyPolicy.IsConflictStateName(displayState))
            {
                OptimizationActionResult repair = _actions.InstallDisplaySupport();
                if (!Succeeded(repair))
                {
                    return Stopped("48 Hz support repair", repair);
                }

                restartRequired = repair.RestartRequired;
                displayState = "Installed";
            }

            if (UninstallSafetyPolicy.IsRecoverableStateName(displayState))
            {
                OptimizationActionResult nativeMode =
                    _actions.SetDisplayRefreshRate(60, displayConfirmation);
                if (!Succeeded(nativeMode))
                {
                    return Stopped("the switch to native 60 Hz", nativeMode);
                }

                OptimizationActionResult remove = _actions.RemoveDisplaySupport();
                if (!Succeeded(remove))
                {
                    return Stopped("48 Hz support removal", remove);
                }

                restartRequired = restartRequired || remove.RestartRequired;
            }
            else if (!UninstallSafetyPolicy.IsSafeStateName(displayState))
            {
                return UnknownState("display support", displayState);
            }

            state = ReadState();
            unavailable = RequireAvailable(state);
            if (unavailable != null)
            {
                return unavailable;
            }

            string powerState = state.CpuState;
            if (UninstallSafetyPolicy.IsRecoverableStateName(powerState)
                || UninstallSafetyPolicy.IsConflictStateName(powerState))
            {
                OptimizationActionResult restore = _actions.RestoreCpuPower();
                if (!Succeeded(restore))
                {
                    return Stopped("power-plan restoration", restore);
                }

                restartRequired = restartRequired || restore.RestartRequired;
            }
            else if (!UninstallSafetyPolicy.IsSafeStateName(powerState))
            {
                return UnknownState("power plan", powerState);
            }

            state = ReadState();
            unavailable = RequireAvailable(state);
            if (unavailable != null)
            {
                return unavailable;
            }

            if (!UninstallSafetyPolicy.IsSafeStateName(
                    state.DisplaySupportState)
                || !UninstallSafetyPolicy.IsSafeStateName(state.CpuState))
            {
                return OptimizationActionResult.Failed(
                    OperationCode.StateVerificationFailed,
                    "Automatic uninstall recovery finished, but the final "
                        + "read-back did not verify both resources as restored.",
                    string.Empty);
            }

            return OptimizationActionResult.Successful(
                "Display and power recovery were verified. Uninstall can continue.",
                OperationCode.None,
                restartRequired);
        }

        private OptimizationStateSnapshot ReadState()
        {
            try
            {
                return _actions.ReadState();
            }
            catch (Exception exception)
            {
                return OptimizationStateSnapshot.Unavailable(
                    "Uninstall recovery state could not be read: "
                        + exception.Message);
            }
        }

        private static OptimizationActionResult RequireAvailable(
            OptimizationStateSnapshot state)
        {
            if (state != null && state.Available)
            {
                return null;
            }

            return OptimizationActionResult.Failed(
                OperationCode.ReadBackFailed,
                state == null || string.IsNullOrWhiteSpace(state.Detail)
                    ? "MacBook Eco could not read the state required for safe uninstall."
                    : state.Detail,
                string.Empty);
        }

        private static bool Succeeded(OptimizationActionResult result)
        {
            return result != null && result.Succeeded;
        }

        private static OptimizationActionResult Stopped(
            string step,
            OptimizationActionResult result)
        {
            if (result == null)
            {
                return OptimizationActionResult.Failed(
                    OperationCode.StateVerificationFailed,
                    "Automatic uninstall recovery stopped during " + step
                        + " because the action returned no result.",
                    string.Empty);
            }

            return result.WithMessage(
                "Automatic uninstall recovery stopped during " + step + ". "
                    + result.Message,
                result.Code);
        }

        private static OptimizationActionResult UnknownState(
            string resource,
            string state)
        {
            return OptimizationActionResult.Failed(
                OperationCode.StateVerificationFailed,
                "Automatic uninstall recovery stopped because the " + resource
                    + " state is not recognized as safely recoverable: "
                    + (string.IsNullOrWhiteSpace(state) ? "Unavailable" : state)
                    + ".",
                string.Empty);
        }
    }
}
