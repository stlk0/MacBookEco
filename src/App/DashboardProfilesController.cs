using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using MacBookEco.AppPolicy;
using MacBookEco.Core;
using MacBookEco.Telemetry;

namespace MacBookEco.App
{
    // Keeps profile-selection state and turns telemetry/action state into the
    // view data rendered by DashboardProfilesPage. It deliberately performs no
    // mutations; the dashboard shell remains the action coordinator.
    public sealed class DashboardProfilesController
    {
        private readonly object _customProfileItem;
        private DashboardProfilesPage _page;

        // Never null. Attach runs from the dashboard constructor, long before
        // the first telemetry sample arrives, and every presentation path below
        // asks the profile catalog which reviewed mode Windows reports.
        private DisplayTelemetry _display =
            DisplayTelemetry.Unavailable("Waiting for the first sample.");
        private OptimizationStateSnapshot _optimizationState;
        private bool _synchronizingSelections;
        private bool _profileSelectionDirty;
        private bool _cpuSelectionDirty;
        private bool _displaySelectionDirty;
        private bool _mutationControlsEnabled = true;
        private string _lastModeVisibility;

        public DashboardProfilesController(object customProfileItem)
        {
            if (customProfileItem == null)
            {
                throw new ArgumentNullException(nameof(customProfileItem));
            }

            _customProfileItem = customProfileItem;
        }

        public void Attach(DashboardProfilesPage page)
        {
            if (page == null)
            {
                throw new ArgumentNullException(nameof(page));
            }

            _page = page;
            ApplyDisplaySupportPresentation();
        }

        public void UpdateDisplay(DisplayTelemetry display)
        {
            if (display != null)
            {
                _display = display;
            }

            if (_page == null)
            {
                return;
            }

            string currentModeText = "Current mode: " + DisplayModeText(_display);
            if (!string.Equals(
                _page.DisplayCurrent.Text,
                currentModeText,
                StringComparison.Ordinal))
            {
                _page.DisplayCurrent.Text = currentModeText;
            }
            if (!_displaySelectionDirty && _display.RefreshRateHz.HasValue)
            {
                int? currentRefreshRate = CurrentRefreshRate();
                if (currentRefreshRate.HasValue)
                {
                    SelectDisplayMode(currentRefreshRate.Value);
                }
            }
            ApplyDisplaySupportPresentation();
            UpdateCombinedProfileState();
        }

        public void UpdateOptimizationState(OptimizationStateSnapshot state)
        {
            _optimizationState = state;
            if (_page == null)
            {
                return;
            }

            if (state == null || !state.Available)
            {
                _page.CpuState.Text = "Current plan: unavailable";
                _page.CpuState.ForeColor = DashboardTheme.SecondaryTextColor;
                ApplyDisplaySupportPresentation();
                UpdateCombinedProfileState();
                return;
            }

            if (state.CpuProfileActive && state.ActiveCpuPreset.HasValue)
            {
                PowerPresetDefinition definition = PowerPresetCatalog.Get(
                    state.ActiveCpuPreset.Value);
                _page.CpuState.Text = "Current plan: MacBook Eco "
                    + definition.DisplayName
                    + " (app-owned)";
                _page.CpuState.ForeColor = DashboardTheme.SecondaryTextColor;
                if (!_cpuSelectionDirty)
                {
                    SelectCpuPreset(state.ActiveCpuPreset.Value);
                }
            }
            else
            {
                _page.CpuState.Text =
                    "Current plan: original or user-selected Windows plan";
                _page.CpuState.ForeColor = DashboardTheme.SecondaryTextColor;
                if (!_cpuSelectionDirty)
                {
                    // No app-owned plan is active, so there is nothing for the
                    // list to mirror. Leaving the last applied preset selected
                    // would contradict the line directly above it, which is
                    // what a restore used to look like.
                    ClearCpuSelection();
                }
            }

            ApplyDisplaySupportPresentation();
            UpdateCombinedProfileState();
        }

        public void ResetProfileSelection()
        {
            _profileSelectionDirty = false;
        }

        public void ResetAllSelections()
        {
            _profileSelectionDirty = false;
            _cpuSelectionDirty = false;
            _displaySelectionDirty = false;
        }

        public int? SelectedDisplayRefreshRate()
        {
            DisplayModeChoice selected = _page == null
                ? null
                : _page.DisplayMode.SelectedItem as DisplayModeChoice;
            return selected == null
                ? (int?)null
                : selected.RefreshRateHz;
        }

        public PowerPreset? SelectedCpuPreset()
        {
            if (_page == null)
            {
                return null;
            }

            return _page.CpuPreset.SelectedItem is PowerPreset
                ? (PowerPreset?)_page.CpuPreset.SelectedItem
                : null;
        }

        public OptimizationProfileDefinition SelectedRecommendedProfile()
        {
            return _page == null
                ? null
                : _page.RecommendedProfile.SelectedItem
                    as OptimizationProfileDefinition;
        }

        public bool IsSelectedRefreshRate(int refreshRate)
        {
            DisplayModeDefinition mode = ProfileCatalog.GetMode(
                refreshRate);
            return mode != null &&
                mode.MatchesWindowsSelector(_display.RefreshRateHz);
        }

        public void SetControlsEnabled(bool enabled)
        {
            if (_page == null)
            {
                return;
            }

            _mutationControlsEnabled = enabled;
            ApplyDisplaySupportPresentation();
            _page.CpuApplyButton.Enabled = enabled;
            _page.CpuRestoreButton.Enabled = enabled;
            _page.CpuPreset.Enabled = enabled;
            _page.RecommendedProfile.Enabled = enabled;
        }

        public void OnRecommendedProfileChanged(object sender, EventArgs eventArgs)
        {
            if (_page == null)
            {
                return;
            }

            bool custom = ReferenceEquals(
                _page.RecommendedProfile.SelectedItem,
                _customProfileItem);
            if (!_synchronizingSelections)
            {
                _profileSelectionDirty = !custom;
            }

            OptimizationProfileDefinition profile = SelectedRecommendedProfile();
            if (profile == null)
            {
                _page.RecommendedDescription.Text =
                    "The current display and CPU settings do not exactly match "
                    + "a named profile. Choose one below to replace both.";
                UpdateRecommendedApplyEnabled();
                return;
            }

            if (!_synchronizingSelections)
            {
                SelectCpuPreset(profile.CpuPreset);
                _cpuSelectionDirty = true;
            }

            PowerPresetDefinition cpu = PowerPresetCatalog.Get(profile.CpuPreset);
            DisplayModeDefinition displayMode = ProfileCatalog.GetMode(
                profile.DisplayRefreshRate);
            _page.RecommendedDescription.Text = profile.Description
                + Environment.NewLine
                + "Display: "
                + (displayMode == null
                    ? profile.DisplayRefreshRate + " Hz"
                    : displayMode.DisplayName)
                + ". CPU: "
                + cpu.DisplayName
                + ".";
            UpdateRecommendedApplyEnabled();
        }

        public void OnCpuPresetChanged(object sender, EventArgs eventArgs)
        {
            if (_page == null)
            {
                return;
            }

            if (!_synchronizingSelections)
            {
                _cpuSelectionDirty = true;
            }

            PowerPreset? preset = SelectedCpuPreset();
            if (preset.HasValue)
            {
                _page.CpuDetails.SetPreset(preset.Value);
            }
        }

        public void OnDisplayModeChanged(object sender, EventArgs eventArgs)
        {
            if (!_synchronizingSelections)
            {
                _displaySelectionDirty = true;
            }

            ApplyDisplaySupportPresentation();
        }

        private void SelectCpuPreset(PowerPreset preset)
        {
            for (int index = 0; index < _page.CpuPreset.Items.Count; index++)
            {
                if (_page.CpuPreset.Items[index] is PowerPreset
                    && (PowerPreset)_page.CpuPreset.Items[index] == preset)
                {
                    // The setter raises SelectedIndexChanged synchronously, so
                    // the guard has to survive a handler that throws: leaving
                    // it set would make every later user choice look like a
                    // programmatic sync and be silently overwritten.
                    _synchronizingSelections = true;
                    try
                    {
                        _page.CpuPreset.SelectedIndex = index;
                    }
                    finally
                    {
                        _synchronizingSelections = false;
                    }

                    return;
                }
            }
        }

        /// <summary>
        /// Clears the preset list without marking it as a user choice.  Uses
        /// the same guard as SelectCpuPreset and for the same reason: the
        /// setter raises SelectedIndexChanged synchronously, and a handler
        /// that throws must not leave later user choices looking like a
        /// programmatic sync.
        /// </summary>
        private void ClearCpuSelection()
        {
            _synchronizingSelections = true;
            try
            {
                _page.CpuPreset.SelectedIndex = -1;
            }
            finally
            {
                _synchronizingSelections = false;
            }
        }

        private void ApplyDisplaySupportPresentation()
        {
            if (_page == null)
            {
                return;
            }

            DisplaySupportUiState displayState = CurrentDisplaySupport();
            UpdateDisplayModeChoices(displayState);
            int? selectedRefreshRate = SelectedDisplayRefreshRate();
            bool canApply = selectedRefreshRate.HasValue &&
                displayState.CanSelect(selectedRefreshRate.Value);
            _page.DisplayMode.Enabled = _mutationControlsEnabled &&
                _page.DisplayMode.Items.Count > 0;
            DisplayModeDefinition selectedMode = selectedRefreshRate.HasValue
                ? ProfileCatalog.GetMode(selectedRefreshRate.Value)
                : null;
            _page.DisplayApplyButton.Enabled = canApply &&
                (selectedMode == null ||
                    !selectedMode.MatchesWindowsSelector(
                        _display.RefreshRateHz));
            if (!string.Equals(
                _page.DisplayState.Text,
                displayState.SupportText,
                StringComparison.Ordinal))
            {
                _page.DisplayState.Text = displayState.SupportText;
            }

            if (!string.Equals(
                _page.InstallDisplayButton.Text,
                displayState.InstallText,
                StringComparison.Ordinal))
            {
                _page.InstallDisplayButton.Text = displayState.InstallText;
            }

            _page.InstallDisplayButton.AccessibleDescription =
                displayState.CanInstall
                    ? "Install, refresh, or repair MacBook Eco display support"
                    : "Eco display support cannot be changed in the current state";
            if (_page.InstallDisplayButton.Enabled != displayState.CanInstall)
            {
                _page.InstallDisplayButton.Enabled = displayState.CanInstall;
            }

            if (_page.RemoveDisplayButton.Visible != displayState.ShowRemove)
            {
                _page.RemoveDisplayButton.Visible = displayState.ShowRemove;
            }

            _page.RemoveDisplayButton.AccessibleDescription =
                displayState.CanRemove
                    ? "Remove MacBook Eco-owned display support"
                    : "No MacBook Eco-owned display support can be removed";
            if (_page.RemoveDisplayButton.Enabled != displayState.CanRemove)
            {
                _page.RemoveDisplayButton.Enabled = displayState.CanRemove;
            }

            UpdateRecommendedApplyEnabled(displayState);
        }

        private void UpdateRecommendedApplyEnabled(
            DisplaySupportUiState displayState = null)
        {
            if (_page == null)
            {
                return;
            }

            displayState = displayState ?? CurrentDisplaySupport();
            OptimizationProfileDefinition profile = SelectedRecommendedProfile();
            bool canUseProfileDisplay = profile == null
                || displayState.CanSelect(profile.DisplayRefreshRate);
            _page.ApplyRecommendedButton.Enabled = _mutationControlsEnabled
                && profile != null
                && canUseProfileDisplay;
        }

        private void UpdateCombinedProfileState()
        {
            if (_page == null)
            {
                return;
            }

            OptimizationProfileDefinition active = FindActiveCombinedProfile();
            string display = _display.RefreshRateHz.HasValue
                ? _display.RefreshRateHz.Value.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture) + " Hz"
                : "display N/A";
            string cpu = CurrentCpuProfileText();

            _page.RecommendedCurrent.Text = active == null
                ? "Current: Custom \u2014 " + display + " + " + cpu
                : "Current: " + active.DisplayName + " \u2014 "
                    + display + " + " + cpu;
            _page.RecommendedCurrent.ForeColor = active == null
                ? DashboardTheme.WarningColor
                : DashboardTheme.AccentColor;

            if (!_profileSelectionDirty)
            {
                SelectRecommendedProfile(
                    active == null ? _customProfileItem : (object)active);
            }
        }

        private OptimizationProfileDefinition FindActiveCombinedProfile()
        {
            if (_optimizationState == null
                || !_optimizationState.Available
                || !_optimizationState.CpuProfileActive
                || !_optimizationState.ActiveCpuPreset.HasValue)
            {
                return null;
            }

            foreach (OptimizationProfileDefinition profile
                in OptimizationProfileCatalog.Profiles)
            {
                DisplayModeDefinition mode = ProfileCatalog.GetMode(
                    profile.DisplayRefreshRate);
                if (profile.CpuPreset == _optimizationState.ActiveCpuPreset.Value
                    && mode != null
                    && mode.MatchesWindowsSelector(_display.RefreshRateHz))
                {
                    return profile;
                }
            }

            return null;
        }

        private string CurrentCpuProfileText()
        {
            if (_optimizationState == null || !_optimizationState.Available)
            {
                return "CPU state N/A";
            }

            if (!_optimizationState.CpuProfileActive
                || !_optimizationState.ActiveCpuPreset.HasValue)
            {
                return "Windows power plan";
            }

            return PowerPresetCatalog.SafeDisplayName(
                _optimizationState.ActiveCpuPreset.Value) + " CPU";
        }

        private void SelectRecommendedProfile(object item)
        {
            if (ReferenceEquals(_page.RecommendedProfile.SelectedItem, item))
            {
                return;
            }

            _synchronizingSelections = true;
            try
            {
                _page.RecommendedProfile.SelectedItem = item;
            }
            finally
            {
                _synchronizingSelections = false;
            }
        }

        private void SelectDisplayMode(int refreshRateHz)
        {
            for (var index = 0; index < _page.DisplayMode.Items.Count; index++)
            {
                DisplayModeChoice choice =
                    _page.DisplayMode.Items[index] as DisplayModeChoice;
                if (choice != null && choice.RefreshRateHz == refreshRateHz)
                {
                    _synchronizingSelections = true;
                    try
                    {
                        _page.DisplayMode.SelectedIndex = index;
                    }
                    finally
                    {
                        _synchronizingSelections = false;
                    }

                    return;
                }
            }
        }

        private DisplaySupportUiState CurrentDisplaySupport()
        {
            return DisplaySupportUiPolicy.Evaluate(
                _optimizationState,
                CurrentRefreshRate(),
                _mutationControlsEnabled);
        }

        private void UpdateDisplayModeChoices(DisplaySupportUiState state)
        {
            var visibleRates = new List<string>();
            for (var index = 0; index < state.Modes.Count; index++)
            {
                if (state.Modes[index].Show)
                {
                    visibleRates.Add(
                        state.Modes[index].Mode.WindowsRefreshRate.ToString(
                            CultureInfo.InvariantCulture));
                }
            }

            string visibility = string.Join("|", visibleRates.ToArray());
            if (string.Equals(
                    _lastModeVisibility,
                    visibility,
                    StringComparison.Ordinal))
            {
                return;
            }

            _lastModeVisibility = visibility;
            int? selected = SelectedDisplayRefreshRate();

            _synchronizingSelections = true;
            try
            {
                _page.DisplayMode.Items.Clear();
                for (var index = 0; index < state.Modes.Count; index++)
                {
                    DisplayModeUiState mode = state.Modes[index];
                    if (mode.Show)
                    {
                        _page.DisplayMode.Items.Add(
                            new DisplayModeChoice(
                                mode.Mode.WindowsRefreshRate,
                                mode.Mode.DisplayName));
                    }
                }
            }
            finally
            {
                _synchronizingSelections = false;
            }

            if (!selected.HasValue && _display.RefreshRateHz.HasValue)
            {
                selected = CurrentRefreshRate();
            }

            if (selected.HasValue)
            {
                SelectDisplayMode(selected.Value);
            }
        }

        private int? CurrentRefreshRate()
        {
            if (!_display.RefreshRateHz.HasValue)
            {
                return null;
            }

            DisplayModeDefinition mode =
                ProfileCatalog.GetModeForWindowsSelector(
                    _display.RefreshRateHz);
            if (mode != null)
            {
                return mode.WindowsRefreshRate;
            }

            return (int)Math.Round(_display.RefreshRateHz.Value);
        }

        private static string DisplayModeText(DisplayTelemetry display)
        {
            if (display.Width <= 0)
            {
                return "N/A";
            }

            return display.Width.ToString(CultureInfo.InvariantCulture)
                + "\u00d7"
                + display.Height.ToString(CultureInfo.InvariantCulture)
                + " @ "
                + TelemetryText.Refresh(display.RefreshRateHz);
        }

    }
}
