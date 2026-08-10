using System;
using System.Collections.Generic;

namespace MacBookEco.Platform.Windows
{
    internal enum InternalPanelSelectionResult
    {
        Selected,
        NotFound,
        Ambiguous
    }

    /// <summary>
    /// The one rule for "which active CCD path is the built-in panel".
    ///
    /// It was previously implemented three times, in hardware discovery, in the
    /// watchdog's resolver and in the EDID resolver. Three copies of a
    /// fail-closed hardware-matching rule is three chances for them to disagree
    /// about which display is about to be modified.
    ///
    /// The rule itself never guesses: exactly one embedded target wins, and
    /// more than one candidate is an ambiguity rather than a reason to take the
    /// first. Callers choose how to report that, because a read-only snapshot
    /// degrades to a warning while a mutation path must refuse outright.
    /// </summary>
    internal static class InternalPanelSelector
    {
        internal static InternalPanelSelectionResult Select(
            IList<ActiveDisplayPath> paths,
            out ActiveDisplayPath selected,
            out string detail)
        {
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            selected = null;
            detail = null;

            ActiveDisplayPath embedded = null;
            for (int index = 0; index < paths.Count; index++)
            {
                if (!paths[index].IsInternal)
                {
                    continue;
                }

                if (embedded != null)
                {
                    detail =
                        "More than one active embedded/internal CCD target was found.";
                    return InternalPanelSelectionResult.Ambiguous;
                }

                embedded = paths[index];
            }

            if (embedded != null)
            {
                selected = embedded;
                return InternalPanelSelectionResult.Selected;
            }

            // Some Boot Camp drivers report OTHER for the built-in panel. An
            // APP vendor code in the monitor interface path identifies an Apple
            // display, which is a usable fallback here; mutation code still
            // requires a verified EDID and profile match afterwards.
            ActiveDisplayPath appleFallback = null;
            for (int index = 0; index < paths.Count; index++)
            {
                string path = paths[index].MonitorDevicePath;
                if (string.IsNullOrEmpty(path) ||
                    path.IndexOf(
                        "DISPLAY#APP",
                        StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (appleFallback != null)
                {
                    detail =
                        "More than one active APP monitor path could be the internal panel.";
                    return InternalPanelSelectionResult.Ambiguous;
                }

                appleFallback = paths[index];
            }

            if (appleFallback != null)
            {
                selected = appleFallback;
                return InternalPanelSelectionResult.Selected;
            }

            detail = "No active embedded/internal display target was found.";
            return InternalPanelSelectionResult.NotFound;
        }
    }
}
