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
                (installedProfile == null ||
                    installedProfile.GetTargetMode(58) == null);
            bool ready48 = installed &&
                optimizationState.Display48HzAvailable;
            bool ready58 = installed && !legacy &&
                optimizationState.Display58HzAvailable;

            DisplaySupportUiState result = new DisplaySupportUiState();
            result.CanSelect48Hz = mutationControlsEnabled && ready48;
            result.CanSelect58Hz = mutationControlsEnabled && ready58;
            // Native 60 Hz is a recovery action and never depends on EDID
            // ownership. The use case itself still validates the hardware.
            result.CanSelect60Hz = mutationControlsEnabled;
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
                    ? !current48Hz && !current58Hz
                    : (installed && ready48 && ready58) || conflict ||
                        (recoverable && !current48Hz && !current58Hz));
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
                    return "Existing MacBook Eco display support is installed. "
                        + "Return to 60 Hz and refresh setup to add all modes.";
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
                return "Eco display support needs verification. Repair is available "
                    + "only when the live override exactly matches MacBook Eco.";
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
