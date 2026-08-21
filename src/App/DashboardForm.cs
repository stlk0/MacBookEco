using System;
using System.Drawing;
using System.Windows.Forms;
using MacBookEco.AppPolicy;
using MacBookEco.Core;
using MacBookEco.Telemetry;

namespace MacBookEco.App
{
    public sealed class DashboardForm : Form
    {
        private readonly TelemetryService _telemetry;
        private readonly OptimizationStateMonitor _stateMonitor;
        private readonly OptimizationCommandRunner _runner;
        private readonly string _profileDiagnostics;

        private DashboardOverviewPage _overviewPage;
        private DashboardProfilesPage _profilesPage;
        private DashboardProfilesController _profilesController;
        private TabControl _tabs;
        private TabPage _overviewTab;
        private TabPage _profilesTab;
        private TabPage _diagnosticsTab;
        private DashboardDiagnosticsPage _diagnosticsPage;
        private Label _actionStatus;
        private Label _hardwareSummary;
        private Panel _actionPanel;

        private TelemetrySnapshot _latestSnapshot;
        private OptimizationActionResult _lastActionResult;
        private readonly object _customProfileItem =
            new CustomProfileSelection();
        private bool _allowClose;

        public DashboardForm(
            TelemetryService telemetry,
            OptimizationStateMonitor stateMonitor,
            OptimizationCommandRunner runner,
            OptimizationActionResult startupRecovery,
            string profileDiagnostics)
        {
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            if (stateMonitor == null)
            {
                throw new ArgumentNullException(nameof(stateMonitor));
            }

            if (runner == null)
            {
                throw new ArgumentNullException(nameof(runner));
            }

            _telemetry = telemetry;
            _stateMonitor = stateMonitor;
            _runner = runner;
            _profileDiagnostics = profileDiagnostics;
            _latestSnapshot = TelemetrySnapshot.Empty();
            _lastActionResult = startupRecovery;

            SuspendLayout();
            Text = Application.ProductName;
            Icon = ApplicationIcon.Shared;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(980, 700);
            ClientSize = new Size(1180, 790);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.BackColor = DashboardTheme.CanvasColor;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildTabs(), 0, 1);
            root.Controls.Add(BuildActionPanel(), 0, 2);
            Controls.Add(root);
            // Setting AutoScaleMode schedules scaling for controls that already
            // belong to the form. Keep this after the complete tree is built.
            DashboardTheme.StyleForm(this);
            ResumeLayout(true);

            _telemetry.SnapshotAvailable += OnSnapshotAvailable;
            _stateMonitor.Changed += OnOptimizationStateChanged;
            _runner.StateChanged += OnRunnerStateChanged;
            _runner.Completed += OnCommandCompleted;
            FormClosing += OnFormClosing;
            VisibleChanged += OnVisibleChanged;
            ApplyOptimizationState();
            if (startupRecovery != null)
            {
                DisplayActionResult(startupRecovery);
            }
        }

        public void ShowDashboard()
        {
            if (!Visible)
            {
                Show();
            }

            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            TopMost = true;
            BringToFront();
            Activate();
            TopMost = false;
            UpdateSnapshot(_telemetry.LatestSnapshot);
            ApplyOptimizationState();
        }

        public void ShowDiagnostics()
        {
            ShowDashboard();
            _tabs.SelectedTab = _diagnosticsTab;
        }

        public void Shutdown()
        {
            _allowClose = true;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _telemetry.SnapshotAvailable -= OnSnapshotAvailable;
                _stateMonitor.Changed -= OnOptimizationStateChanged;
                _runner.StateChanged -= OnRunnerStateChanged;
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// One row: the product name, and the hardware this copy is actually
        /// running on.
        ///
        /// The header used to repeat the window caption in 18pt, add a tagline
        /// aimed at someone who had not opened the application yet, and label
        /// every machine "supported Apple hardware" from a static string that
        /// was never updated. That last one was not merely uninformative: on a
        /// Mac with no reviewed profile it claimed support the application had
        /// not established. The right-hand slot now answers the first question
        /// a user actually has, and says so only when it is true.
        /// </summary>
        private Control BuildHeader()
        {
            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.BackColor = DashboardTheme.SurfaceColor;
            header.Padding = new Padding(22, 8, 22, 8);
            header.AutoSize = true;
            header.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.AutoSize = true;
            layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            layout.ColumnCount = 2;
            layout.RowCount = 1;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label title = DashboardTheme.CreatePageTitle(Application.ProductName);
            title.AutoSize = true;
            title.Anchor = AnchorStyles.Left;
            title.Margin = Padding.Empty;
            layout.Controls.Add(title, 0, 0);

            _hardwareSummary = DashboardTheme.CreateCaptionLabel(
                "Identifying hardware\u2026");
            _hardwareSummary.Font = DashboardTheme.CaptionStrongFont;
            _hardwareSummary.AutoSize = true;
            _hardwareSummary.Anchor = AnchorStyles.Right;
            _hardwareSummary.Margin = Padding.Empty;
            layout.Controls.Add(_hardwareSummary, 1, 0);

            header.Controls.Add(layout);
            return header;
        }

        /// <summary>
        /// Names the reviewed profile that matched this machine, or says plainly
        /// that none did. The profile ID is the only hardware fact the
        /// presentation layer is given; the catalog turns it into the reviewed
        /// display name without the dashboard reaching into the platform.
        /// </summary>
        private void UpdateHardwareSummary(OptimizationStateSnapshot state)
        {
            if (_hardwareSummary == null)
            {
                return;
            }

            string summary;
            Color color;
            if (state == null || !state.Available)
            {
                summary = "Hardware status unavailable";
                color = DashboardTheme.WarningColor;
            }
            else
            {
                DisplayProfile profile = ProfileCatalog.GetById(
                    state.DisplayProfileId);
                if (profile == null)
                {
                    summary = "Unsupported hardware \u00b7 diagnostics only";
                    color = DashboardTheme.SecondaryTextColor;
                }
                else
                {
                    summary = profile.DisplayName;
                    color = DashboardTheme.AccentColor;
                }
            }

            if (!string.Equals(_hardwareSummary.Text, summary, StringComparison.Ordinal))
            {
                _hardwareSummary.Text = summary;
            }

            _hardwareSummary.ForeColor = color;
        }

        private Control BuildTabs()
        {
            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;
            _tabs.Font = DashboardTheme.BodyFont;
            _tabs.Padding = new Point(14, 7);

            _overviewTab = CreateTab("Overview");
            // The native tab renderer leaves the visible ink of the final
            // "s" closer to the right edge than the leading "P" is to the
            // left edge. A trailing em space corrects that optical offset
            // without replacing the native tab chrome.
            _profilesTab = CreateTab("Profiles & controls\u2003");
            _profilesTab.AccessibleName = "Profiles & controls";
            _diagnosticsTab = CreateTab("Diagnostics");

            _overviewPage = new DashboardOverviewPage();
            _overviewTab.Controls.Add(_overviewPage.View);
            _profilesController = new DashboardProfilesController(
                _customProfileItem);
            _profilesPage = new DashboardProfilesPage(
                _customProfileItem,
                _profilesController.OnRecommendedProfileChanged,
                _profilesController.OnCpuPresetChanged,
                _profilesController.OnDisplayModeChanged,
                ApplyDisplayRefreshRate,
                delegate { QueueCommand(OptimizationCommand.InstallDisplaySupport()); },
                RemoveDisplaySupport,
                ApplySelectedCpuPreset,
                RestoreOriginalPower,
                ApplyRecommendedProfile);
            _profilesController.Attach(_profilesPage);
            _profilesTab.Controls.Add(_profilesPage.View);
            _diagnosticsPage = new DashboardDiagnosticsPage(
                ReportActionStatus,
                _profileDiagnostics);
            _diagnosticsTab.Controls.Add(_diagnosticsPage.View);
            _tabs.TabPages.Add(_overviewTab);
            _tabs.TabPages.Add(_profilesTab);
            _tabs.TabPages.Add(_diagnosticsTab);
            return _tabs;
        }

        private static TabPage CreateTab(string text)
        {
            TabPage page = new TabPage(text);
            page.BackColor = DashboardTheme.CanvasColor;
            page.Padding = new Padding(14);
            return page;
        }

        private Control BuildActionPanel()
        {
            _actionPanel = new Panel();
            _actionPanel.Dock = DockStyle.Fill;
            _actionPanel.AutoSize = true;
            _actionPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _actionPanel.BackColor = DashboardTheme.MutedSurfaceColor;
            _actionPanel.Padding = new Padding(20, 11, 20, 8);
            _actionStatus = DashboardTheme.CreateBodyLabel(
                "Monitoring is active. No setting changes without an explicit click.");
            _actionStatus.Dock = DockStyle.Top;
            _actionStatus.AutoSize = true;
            _actionStatus.AutoEllipsis = true;
            _actionStatus.TextAlign = ContentAlignment.MiddleLeft;
            _actionPanel.Controls.Add(_actionStatus);
            return _actionPanel;
        }

        private void OnSnapshotAvailable(
            object sender,
            TelemetrySnapshotEventArgs eventArgs)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                BeginInvoke(
                    new Action<TelemetrySnapshot>(UpdateSnapshot),
                    eventArgs.Snapshot);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void UpdateSnapshot(TelemetrySnapshot snapshot)
        {
            if (snapshot == null || IsDisposed)
            {
                return;
            }

            _latestSnapshot = snapshot;
            _overviewPage.Update(snapshot);
            _profilesController.UpdateDisplay(snapshot.Display);
            UpdateDiagnostics();
        }

        private void OnOptimizationStateChanged(
            object sender,
            OptimizationStateChangedEventArgs eventArgs)
        {
            if (!IsDisposed)
            {
                ApplyOptimizationState();
            }
        }

        private void ApplyOptimizationState()
        {
            OptimizationStateSnapshot state = _stateMonitor.Current;
            _profilesController.UpdateOptimizationState(state);
            UpdateHardwareSummary(state);
            UpdateDiagnostics();
        }

        private void UpdateDiagnostics()
        {
            if (_diagnosticsPage == null || _latestSnapshot == null)
            {
                return;
            }

            _diagnosticsPage.Update(
                _latestSnapshot,
                _stateMonitor.Current,
                _lastActionResult);
        }

        private void ApplyDisplayRefreshRate(int refreshRate)
        {
            _profilesController.ResetProfileSelection();
            QueueCommand(OptimizationCommand.SetDisplayRefreshRate(refreshRate));
        }

        private void ApplySelectedCpuPreset()
        {
            PowerPreset? preset = _profilesController.SelectedCpuPreset();
            if (!preset.HasValue)
            {
                return;
            }

            _profilesController.ResetAllSelections();
            QueueCommand(OptimizationCommand.ApplyCpuPreset(preset.Value));
        }

        private void ApplyRecommendedProfile()
        {
            OptimizationProfileDefinition profile =
                _profilesController.SelectedRecommendedProfile();
            if (profile == null)
            {
                DisplayActionResult(
                    OptimizationActionResult.Cancelled(
                        OperationCode.InvalidRequest,
                        "Select a recommended profile first."));
                return;
            }

            _profilesController.ResetAllSelections();
            QueueCommand(OptimizationCommand.ApplyCombinedProfile(
                profile.DisplayRefreshRate,
                profile.CpuPreset,
                !_profilesController.IsSelectedRefreshRate(
                    profile.DisplayRefreshRate),
                profile.DisplayName));
        }

        private void RemoveDisplaySupport()
        {
            if (DestructiveConfirmation.RemoveDisplaySupport(this))
            {
                QueueCommand(OptimizationCommand.RemoveDisplaySupport());
            }
        }

        private void RestoreOriginalPower()
        {
            if (DestructiveConfirmation.RestorePowerPlan(this))
            {
                _profilesController.ResetAllSelections();
                QueueCommand(OptimizationCommand.RestoreCpuPower());
            }
        }

        /// <summary>
        /// Busy is not pre-checked here. Execute rejects a command that arrives
        /// while another is running and publishes a Busy completion for it, so
        /// checking first would only duplicate that decision -- and would do it
        /// from a stale read, since the runner can become busy between the
        /// check and the call.
        /// </summary>
        private void QueueCommand(OptimizationCommand command)
        {
            _runner.Execute(command);
        }

        private void DisplayActionResult(OptimizationActionResult result)
        {
            if (result == null)
            {
                _actionPanel.BackColor = Color.FromArgb(252, 244, 225);
                _actionStatus.ForeColor = DashboardTheme.WarningColor;
                _actionStatus.Text =
                    "The action provider returned no result.";
                return;
            }

            _lastActionResult = result;
            if (result.Outcome == OperationOutcome.Succeeded)
            {
                _actionPanel.BackColor = Color.FromArgb(231, 244, 238);
                _actionStatus.ForeColor = DashboardTheme.SuccessColor;
            }
            else if (result.Outcome == OperationOutcome.Indeterminate)
            {
                _actionPanel.BackColor = Color.FromArgb(252, 232, 232);
                _actionStatus.ForeColor = DashboardTheme.WarningColor;
            }
            else
            {
                _actionPanel.BackColor = Color.FromArgb(252, 244, 225);
                _actionStatus.ForeColor = DashboardTheme.WarningColor;
            }

            string message = result.RestartRequired
                ? result.Message + " Restart Windows to finish this change."
                : result.Message;
            _actionStatus.Text = result.Outcome == OperationOutcome.Succeeded
                || result.Code == OperationCode.None
                    ? message
                    : message + " Code: " + result.Code + ".";
            ApplyOptimizationState();
        }

        /// <summary>
        /// Records the outcome of a command, whichever surface started it. It
        /// deliberately does not raise the window: a command started from the
        /// tray menu already reports through a balloon, and stealing focus from
        /// whatever the user is doing is not the application's call to make.
        ///
        /// One command can complete twice -- see OptimizationCommandRunner.Completed
        /// -- so this treats each completion as the latest word rather than a
        /// one-shot signal, which is what simply redisplaying the result does.
        /// </summary>
        private void OnCommandCompleted(
            object sender,
            OptimizationCommandCompletedEventArgs eventArgs)
        {
            DisplayActionResult(eventArgs == null ? null : eventArgs.Result);
        }

        private void OnRunnerStateChanged(
            object sender,
            OptimizationCommandRunnerStateChangedEventArgs eventArgs)
        {
            bool enabled = eventArgs == null || !eventArgs.IsBusy;
            _profilesController.SetControlsEnabled(enabled);

            if (!enabled && _actionStatus != null)
            {
                _actionPanel.BackColor = DashboardTheme.MutedSurfaceColor;
                _actionStatus.ForeColor = DashboardTheme.SecondaryTextColor;
                _actionStatus.Text = eventArgs.BusyReason;
            }
        }

        private void OnFormClosing(
            object sender,
            FormClosingEventArgs eventArgs)
        {
            if (!_allowClose && eventArgs.CloseReason == CloseReason.UserClosing)
            {
                eventArgs.Cancel = true;
                Hide();
            }
        }

        private void OnVisibleChanged(object sender, EventArgs eventArgs)
        {
            if (IsDisposed)
            {
                return;
            }

            // The form outliving its collaborators is the ordering the tray
            // context maintains, but the guard above only proves this form is
            // alive; the telemetry service has its own lifetime and reports a
            // late call by throwing rather than by returning.
            try
            {
                _telemetry.SetDashboardVisible(Visible);
                _stateMonitor.SetDashboardVisible(Visible);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void ReportActionStatus(string message)
        {
            if (_actionStatus != null)
            {
                _actionStatus.Text = message ?? string.Empty;
            }
        }

        /// <summary>
        /// A reference-identity token for the "no named profile matches" entry
        /// in the recommended-profile list. Its text comes from the list's
        /// Format handler, like every other entry's, so this type deliberately
        /// carries no state and no ToString.
        /// </summary>
        private sealed class CustomProfileSelection
        {
        }
    }
}
