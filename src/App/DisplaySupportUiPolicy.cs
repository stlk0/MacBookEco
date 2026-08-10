using System;

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
            bool ready = installed
                && optimizationState.Display48HzAvailable;

            DisplaySupportUiState result = new DisplaySupportUiState();
            result.CanSelect48Hz = mutationControlsEnabled && ready;
            // Native 60 Hz is a recovery action and never depends on EDID
            // ownership. The use case itself still validates the hardware.
            result.CanSelect60Hz = mutationControlsEnabled;
            result.ShowRemove = installed;
            result.CanRemove = mutationControlsEnabled && installed;
            result.InstallText = installed || conflict
                ? "Repair 48 Hz support"
                : "Install 48 Hz support";
            // Re-running the install action for an owned profile is a safe
            // read-back repair: the elevated service reconciles the exact
            // owned bytes and never overwrites a foreign override.  A fresh
            // install remains unavailable while an external 48 Hz mode is
            // active, because that is not ours to modify.
            result.CanInstall = mutationControlsEnabled
                && stateAvailable
                && (ready || conflict || (recoverable && !current48Hz));
            result.SupportText = SupportText(
                stateAvailable,
                installed,
                ready,
                notInstalled,
                restored,
                current48Hz,
                state);
            return result;
        }

        private static string SupportText(
            bool stateAvailable,
            bool installed,
            bool ready,
            bool notInstalled,
            bool restored,
            bool current48Hz,
            string state)
        {
            if (!stateAvailable)
            {
                return "48 Hz support status is unavailable.";
            }

            if (installed)
            {
                return ready
                    ? "48 Hz support is installed by MacBook Eco."
                    : "48 Hz support is installed, but Windows has not exposed "
                        + "the mode yet. Restart Windows to activate 48 Hz.";
            }

            if (restored)
            {
                return current48Hz
                    ? "48 Hz support was removed. Restart Windows to finish removal."
                    : "48 Hz support is not installed.";
            }

            if (notInstalled)
            {
                return current48Hz
                    ? "48 Hz is active from external support. MacBook Eco will not modify it."
                    : "48 Hz support is not installed.";
            }

            if (IsState(state, "RecoveryRequired"))
            {
                return "48 Hz support needs recovery before it can be changed.";
            }

            if (IsState(state, "Conflict"))
            {
                return "48 Hz support needs verification. Repair is available "
                    + "only when the live override exactly matches MacBook Eco.";
            }

            return "48 Hz support status is unavailable.";
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
