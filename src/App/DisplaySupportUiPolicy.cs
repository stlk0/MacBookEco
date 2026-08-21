using System;
using MacBookEco.Core;

namespace MacBookEco.App
{
    /// <summary>
    /// One presentation policy for dashboard and tray display actions. It
    /// deliberately distinguishes MacBook Eco-owned support from a mode that
    /// happens to be active because another tool installed it.
    /// </summary>
    internal sealed class DisplaySupportUiState
    {
        internal bool CanSelect48Hz;
        internal bool CanSelect58Hz;
        internal bool CanSelect60Hz;
        internal bool Show48Hz;
        internal bool Show58Hz;
        internal bool Show60Hz;
        internal bool CanInstall;
        internal bool CanRemove;
        internal bool ShowRemove;
        internal string InstallText;
        internal string SupportText;
    }

    internal static class DisplaySupportUiPolicy
    {
        internal static DisplaySupportUiState Evaluate(
            OptimizationStateSnapshot optimizationState,
            bool current48Hz,
            bool current58Hz,
            bool current60Hz,
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
            DisplayRefreshMode mode58 = installedProfile == null
                ? null
                : installedProfile.GetTargetMode(58);
            bool legacy = installed && mode58 == null;
            bool ready48 = installed &&
                optimizationState.Display48HzAvailable;
            bool ready58 = installed && !legacy &&
                optimizationState.Display58HzAvailable;
            bool ready60 = stateAvailable &&
                optimizationState.Display60HzAvailable;

            DisplaySupportUiState result = new DisplaySupportUiState();
            result.CanSelect48Hz = mutationControlsEnabled && ready48;
            result.CanSelect58Hz = mutationControlsEnabled && ready58;
            result.Show48Hz = ready48 || current48Hz;
            result.Show58Hz = ready58 || current58Hz;
            result.Show60Hz = ready60 || current60Hz;
            // Native 60 Hz never depends on EDID ownership, but it must still
            // be enumerated by Windows for the current display configuration.
            result.CanSelect60Hz = mutationControlsEnabled &&
                result.Show60Hz;
            result.ShowRemove = installed;
            result.CanRemove = mutationControlsEnabled && installed;
            result.InstallText = installed || conflict
                ? "Refresh Eco display support"
                : "Install 48 + 58 Hz support";
            // Re-running the install action for an owned profile is a safe
            // read-back repair: the elevated service reconciles the exact
            // owned bytes and never overwrites a foreign override.  A fresh
            // install remains unavailable while an external Eco mode is
            // active, because that is not ours to modify.
            result.CanInstall = mutationControlsEnabled
                && stateAvailable
                && (legacy
                    ? current60Hz
                    : (installed && ready48 && ready58) ||
                        ((conflict || recoverable) && current60Hz));
            result.SupportText = SupportText(
                stateAvailable,
                installed,
                legacy,
                ready48,
                ready58,
                notInstalled,
                restored,
                current48Hz,
                current58Hz,
                current60Hz,
                state);
            return result;
        }

        private static string SupportText(
            bool stateAvailable,
            bool installed,
            bool legacy,
            bool ready48,
            bool ready58,
            bool notInstalled,
            bool restored,
            bool current48Hz,
            bool current58Hz,
            bool current60Hz,
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
                    return current60Hz
                        ? "Existing MacBook Eco display support is installed. "
                            + "Refresh setup to add all modes."
                        : "Return to 60 Hz before refreshing Eco display support.";
                }

                return ready48 && ready58
                    ? "48 + 58 Hz support is installed by MacBook Eco."
                    : "Eco display support is installed, but Windows has not "
                        + "exposed both modes yet. Restart Windows to activate it.";
            }

            if (restored)
            {
                return current48Hz || current58Hz
                    ? "Eco display support was removed. Restart Windows to finish removal."
                    : "Eco display support is not installed.";
            }

            if (notInstalled)
            {
                return current48Hz || current58Hz
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
                return current60Hz
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
