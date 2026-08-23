using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using MacBookEco.Core;

namespace MacBookEco.App
{
    /// <summary>
    /// One presentation policy for dashboard and tray display actions. It
    /// deliberately distinguishes MacBook Eco-owned support from a mode that
    /// happens to be active because another tool installed it.
    /// </summary>
    internal sealed class DisplayModeUiState
    {
        internal DisplayModeDefinition Mode;
        internal bool CanSelect;
        internal bool Show;
        internal bool Current;
    }

    internal sealed class DisplaySupportUiState
    {
        private readonly ReadOnlyCollection<DisplayModeUiState> _modes;

        internal DisplaySupportUiState(IList<DisplayModeUiState> modes)
        {
            _modes = new ReadOnlyCollection<DisplayModeUiState>(modes);
        }

        internal ReadOnlyCollection<DisplayModeUiState> Modes => _modes;
        internal bool CanInstall;
        internal bool CanRemove;
        internal bool ShowRemove;
        internal string InstallText;
        internal string SupportText;

        internal DisplayModeUiState GetMode(int refreshRate)
        {
            for (var index = 0; index < _modes.Count; index++)
            {
                if (_modes[index].Mode.WindowsRefreshRate == refreshRate)
                {
                    return _modes[index];
                }
            }

            return null;
        }

        internal bool CanSelect(int refreshRate)
        {
            DisplayModeUiState mode = GetMode(refreshRate);
            return mode != null && mode.CanSelect;
        }
    }

    internal static class DisplaySupportUiPolicy
    {
        internal static DisplaySupportUiState Evaluate(
            OptimizationStateSnapshot optimizationState,
            int? currentRefreshRate,
            bool mutationControlsEnabled)
        {
            string state = optimizationState == null
                ? string.Empty
                : optimizationState.DisplaySupportState;
            bool stateAvailable = optimizationState != null
                && optimizationState.Available;
            bool installed = IsState(state, "Installed");
            bool notInstalled = IsState(state, "NotInstalled");
            bool restored = IsState(state, "Restored");
            bool conflict = IsState(state, "Conflict");
            bool recoverable = notInstalled || restored;
            DisplayProfile installedProfile = optimizationState == null
                ? null
                : ProfileCatalog.GetById(optimizationState.DisplayProfileId);
            bool legacy = installed &&
                !ProfileCatalog.HasAllOwnedModes(installedProfile);
            bool allOwnedModesReady = true;
            var modeStates = new List<DisplayModeUiState>();
            for (var index = 0; index < ProfileCatalog.Modes.Count; index++)
            {
                DisplayModeDefinition mode = ProfileCatalog.Modes[index];
                bool profileContainsMode = installedProfile != null &&
                    installedProfile.GetTargetMode(
                        mode.WindowsRefreshRate) != null;
                bool ready = stateAvailable &&
                    optimizationState.IsDisplayModeAvailable(
                        mode.WindowsRefreshRate) &&
                    (!mode.RequiresOwnedSupport ||
                        (installed && profileContainsMode));
                bool current = currentRefreshRate.HasValue &&
                    currentRefreshRate.Value == mode.WindowsRefreshRate;
                var modeState = new DisplayModeUiState();
                modeState.Mode = mode;
                modeState.Current = current;
                modeState.Show = ready || current;
                modeState.CanSelect = mutationControlsEnabled &&
                    (mode.RequiresOwnedSupport ? ready : modeState.Show);
                modeStates.Add(modeState);
                if (mode.RequiresOwnedSupport && !ready)
                {
                    allOwnedModesReady = false;
                }
            }

            DisplayModeDefinition currentMode = currentRefreshRate.HasValue
                ? ProfileCatalog.GetMode(currentRefreshRate.Value)
                : null;
            bool currentNative = currentMode != null &&
                currentMode.NativeRecovery;
            bool currentOwnedOrHistorical =
                (currentMode != null && currentMode.RequiresOwnedSupport) ||
                (currentRefreshRate.HasValue &&
                    ProfileCatalog.IsHistoricalRecoveryMode(
                        currentRefreshRate.Value));

            DisplaySupportUiState result =
                new DisplaySupportUiState(modeStates);
            result.ShowRemove = installed;
            result.CanRemove = mutationControlsEnabled && installed;
            result.InstallText = installed || conflict
                ? "Refresh Eco display support"
                : "Install " + ProfileCatalog.OwnedSupportDisplayName +
                    " support";
            // Re-running the install action for an owned profile is a safe
            // read-back repair: the elevated service reconciles the exact
            // owned bytes and never overwrites a foreign override.  A fresh
            // install remains unavailable while an external Eco mode is
            // active, because that is not ours to modify.
            result.CanInstall = mutationControlsEnabled
                && stateAvailable
                && (legacy
                    ? currentNative
                    : (installed && allOwnedModesReady) ||
                        ((conflict || recoverable) && currentNative));
            result.SupportText = SupportText(
                stateAvailable,
                installed,
                legacy,
                allOwnedModesReady,
                notInstalled,
                restored,
                currentOwnedOrHistorical,
                currentNative,
                state);
            return result;
        }

        private static string SupportText(
            bool stateAvailable,
            bool installed,
            bool legacy,
            bool allOwnedModesReady,
            bool notInstalled,
            bool restored,
            bool currentOwnedOrHistorical,
            bool currentNative,
            string state)
        {
            if (!stateAvailable)
            {
                return "Eco display support status is unavailable.";
            }

            if (installed)
            {
                if (legacy)
                {
                    return currentNative
                        ? "Existing MacBook Eco display support is installed. "
                            + "Refresh setup to add all modes."
                        : "Return to 60 Hz before refreshing Eco display support.";
                }

                return allOwnedModesReady
                    ? ProfileCatalog.OwnedSupportDisplayName
                        + " support is installed by MacBook Eco."
                    : "Eco display support is installed, but Windows has not "
                        + "exposed every configured mode yet. Restart Windows "
                        + "to activate it.";
            }

            if (restored)
            {
                return currentOwnedOrHistorical
                    ? "Eco display support was removed. Restart Windows to finish removal."
                    : "Eco display support is not installed.";
            }

            if (notInstalled)
            {
                return currentOwnedOrHistorical
                    ? "An Eco refresh rate is active from external support. "
                        + "MacBook Eco will not modify it."
                    : "Eco display support is not installed.";
            }

            if (IsState(state, "RecoveryRequired"))
            {
                return "Eco display support needs recovery before it can be changed.";
            }

            if (IsState(state, "Conflict"))
            {
                return currentNative
                    ? "Eco display support needs verification. Repair is available "
                        + "only when the live override exactly matches MacBook Eco."
                    : "Return to 60 Hz before repairing Eco display support.";
            }

            return "Eco display support status is unavailable.";
        }

        private static bool IsState(string value, string expected)
        {
            return string.Equals(
                value,
                expected,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
