using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using MacBookEco.AppPolicy;
using MacBookEco.Core;
using MacBookEco.Telemetry;

namespace MacBookEco.App
{
    /// <summary>
    /// Public-safe, read-only diagnostics page and its text presenter.
    /// Clipboard feedback is reported to the dashboard shell through an
    /// injected presentation callback; it has no platform or mutation
    /// dependencies.
    /// </summary>
    internal sealed class DashboardDiagnosticsPage
    {
        private readonly Action<string> _reportStatus;
        private readonly string _profileDiagnostics;
        private readonly TextBox _diagnostics;
        private readonly Control _view;

        public DashboardDiagnosticsPage(
            Action<string> reportStatus,
            string profileDiagnostics)
        {
            if (reportStatus == null)
            {
                throw new ArgumentNullException(nameof(reportStatus));
            }

            _reportStatus = reportStatus;
            _profileDiagnostics = string.IsNullOrWhiteSpace(profileDiagnostics)
                ? "Display profile compatibility (public-safe)"
                    + Environment.NewLine
                    + "Discovery: Unavailable"
                    + Environment.NewLine
                : profileDiagnostics;
            _diagnostics = CreateTextBox();
            _view = BuildView();
        }

        public Control View => _view;

        public void Update(
            TelemetrySnapshot snapshot,
            OptimizationStateSnapshot optimizationState,
            OptimizationActionResult lastActionResult)
        {
            if (snapshot == null)
            {
                return;
            }

            string diagnostics = BuildPublicDiagnostics(
                snapshot,
                optimizationState,
                lastActionResult,
                _profileDiagnostics);
            if (string.Equals(
                _diagnostics.Text,
                diagnostics,
                StringComparison.Ordinal))
            {
                return;
            }

            int selectionStart = _diagnostics.SelectionStart;
            _diagnostics.Text = diagnostics;
            _diagnostics.SelectionStart = Math.Min(
                selectionStart,
                _diagnostics.TextLength);
        }

        internal static string BuildPublicDiagnostics(
            TelemetrySnapshot snapshot,
            OptimizationStateSnapshot optimizationState,
            OptimizationActionResult lastActionResult,
            string profileDiagnostics)
        {
            if (snapshot == null)
            {
                return "No telemetry snapshot is available.";
            }

            StringBuilder text = new StringBuilder();
            text.Append(TelemetryText.BuildPublicDiagnostics(snapshot));
            text.AppendLine();
            string compatibility = string.IsNullOrWhiteSpace(profileDiagnostics)
                ? "Display profile compatibility (public-safe)"
                    + Environment.NewLine
                    + "Discovery: Unavailable"
                    + Environment.NewLine
                : profileDiagnostics;
            text.Append(compatibility);
            if (!compatibility.EndsWith(
                Environment.NewLine,
                StringComparison.Ordinal))
            {
                text.AppendLine();
            }

            text.AppendLine();
            text.AppendLine("Optimization state (read-only)");
            if (optimizationState == null)
            {
                text.AppendLine("State provider returned no result.");
            }
            else
            {
                text.AppendLine("Available: " + optimizationState.Available);
                text.AppendLine(
                    "CPU state: "
                    + PublicManagedState(optimizationState.CpuState));
                text.AppendLine(
                    "Active MacBook Eco CPU preset: "
                    + (optimizationState.ActiveCpuPreset.HasValue
                        ? PowerPresetCatalog.SafeDisplayName(
                            optimizationState.ActiveCpuPreset.Value)
                        : "None"));
                text.AppendLine(
                    "Display support: "
                    + PublicManagedState(
                        optimizationState.DisplaySupportState));
                text.AppendLine(
                    "Display profile: "
                    + PublicProfileId(optimizationState.DisplayProfileId));
                for (var index = 0; index < ProfileCatalog.Modes.Count; index++)
                {
                    DisplayModeDefinition mode = ProfileCatalog.Modes[index];
                    text.AppendLine(
                        mode.DisplayName
                            + " mode exposed by Windows: "
                            + optimizationState.IsDisplayModeAvailable(
                                mode.WindowsRefreshRate));
                }
            }

            text.AppendLine();
            text.AppendLine("Last requested action");
            if (lastActionResult == null)
            {
                text.AppendLine("No action has completed in this session.");
            }
            else
            {
                text.AppendLine("Outcome: " + lastActionResult.Outcome);
                text.AppendLine("Code: " + lastActionResult.Code);
                text.AppendLine(
                    "Restart required: " + lastActionResult.RestartRequired);
            }

            return text.ToString();
        }

        private static string PublicManagedState(string value)
        {
            switch (value)
            {
                case "NotInstalled":
                case "RecoveryRequired":
                case "Installed":
                case "Restored":
                case "Conflict":
                case "Unavailable":
                    return value;
                default:
                    return "Unavailable";
            }
        }

        private static string PublicProfileId(string value)
        {
            DisplayProfile profile = ProfileCatalog.GetById(value);
            return profile == null ? "N/A" : profile.Id;
        }

        private Control BuildView()
        {
            Panel surface = DashboardTheme.CreateSurfacePanel();
            surface.Dock = DockStyle.Fill;
            surface.Padding = new Padding(14);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 3;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label title = DashboardTheme.CreateSectionTitle(
                "Public-safe diagnostics & sensor sources");
            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(_diagnostics, 0, 1);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.Padding = new Padding(0, 2, 0, 0);
            buttons.Controls.Add(DashboardTheme.CreateSecondaryButton(
                "Copy public diagnostics",
                CopyDiagnostics));
            layout.Controls.Add(buttons, 0, 2);
            surface.Controls.Add(layout);
            return surface;
        }

        private static TextBox CreateTextBox()
        {
            TextBox diagnostics = new TextBox();
            diagnostics.Dock = DockStyle.Fill;
            diagnostics.Multiline = true;
            diagnostics.ReadOnly = true;
            diagnostics.ScrollBars = ScrollBars.Both;
            diagnostics.WordWrap = false;
            diagnostics.BorderStyle = BorderStyle.FixedSingle;
            diagnostics.BackColor = Color.White;
            // A theme font: WinForms does not own a Font assigned to a control,
            // and the theme's fonts live for the process instead of leaking one
            // GDI handle per page.
            diagnostics.Font = DashboardTheme.MonospaceFont;
            diagnostics.Text = "Waiting for the first sample...";
            return diagnostics;
        }

        private void CopyDiagnostics(object sender, EventArgs eventArgs)
        {
            if (string.IsNullOrEmpty(_diagnostics.Text))
            {
                return;
            }

            try
            {
                Clipboard.SetText(_diagnostics.Text);
                _reportStatus("Diagnostics copied to the clipboard.");
            }
            catch (Exception exception)
            {
                _reportStatus(
                    "Could not copy diagnostics: " + exception.Message);
            }
        }
    }
}
