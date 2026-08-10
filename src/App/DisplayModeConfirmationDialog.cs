using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace MacBookEco.App
{
    /// <summary>
    /// A modal display confirmation prompt that can actually close itself at
    /// the advertised deadline. MessageBox.Show cannot be dismissed by a
    /// WinForms timer, which leaves a stale prompt visible after rollback.
    /// </summary>
    internal sealed class DisplayModeConfirmationDialog : Form
    {
        private readonly TimeSpan _timeout;
        private readonly Stopwatch _stopwatch;
        private readonly Timer _timer;
        private readonly Label _countdown;
        private readonly Button _keepButton;

        private DisplayModeConfirmationDialog(int refreshRateHz, TimeSpan timeout)
        {
            _timeout = timeout <= TimeSpan.Zero
                ? TimeSpan.FromSeconds(1)
                : timeout;
            _stopwatch = new Stopwatch();
            _timer = new Timer();
            _timer.Interval = 100;
            _timer.Tick += OnTimerTick;

            Text = "Confirm display mode";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(470, 244);
            Padding = new Padding(20);
            DashboardTheme.StyleForm(this);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 4;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label title = DashboardTheme.CreateSectionTitle(
                "Windows switched the internal panel to "
                    + refreshRateHz
                    + " Hz.");
            title.Margin = new Padding(0, 0, 0, 12);

            Label explanation = DashboardTheme.CreateBodyLabel(
                "Is the picture clean and stable?\r\n\r\n"
                    + "Keep this mode before the countdown reaches zero. "
                    + "Revert, closing this window, or timeout restores the "
                    + "previous mode. A separate watchdog also restores it if "
                    + "MacBook Eco exits unexpectedly.");
            explanation.AutoSize = false;
            explanation.Dock = DockStyle.Fill;
            explanation.Margin = new Padding(0);

            _countdown = DashboardTheme.CreateCaptionLabel(string.Empty);
            _countdown.ForeColor = DashboardTheme.WarningColor;
            _countdown.Margin = new Padding(0, 12, 0, 12);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.AutoSize = true;
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.RightToLeft;
            actions.WrapContents = false;
            actions.Margin = new Padding(0);

            _keepButton = DashboardTheme.CreatePrimaryButton(
                "Keep this display mode",
                delegate {
                    DialogResult = DialogResult.Yes;
                    Close();
                });
            Button revertButton = DashboardTheme.CreateSecondaryButton(
                "Revert now",
                delegate {
                    DialogResult = DialogResult.No;
                    Close();
                });
            actions.Controls.Add(_keepButton);
            actions.Controls.Add(revertButton);

            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(explanation, 0, 1);
            layout.Controls.Add(_countdown, 0, 2);
            layout.Controls.Add(actions, 0, 3);
            Controls.Add(layout);

            AcceptButton = _keepButton;
            CancelButton = revertButton;
            UpdateCountdown();
        }

        internal static DisplayModeConfirmationDecision ShowConfirmation(
            IWin32Window owner,
            DisplayModeConfirmationRequest request)
        {
            int refreshRateHz = request == null ? 0 : request.RefreshRateHz;
            TimeSpan timeout = request == null
                ? TimeSpan.FromSeconds(20)
                : request.Timeout;
            using (DisplayModeConfirmationDialog dialog =
                new DisplayModeConfirmationDialog(refreshRateHz, timeout))
            {
                return dialog.ShowDialog(owner) == DialogResult.Yes
                    ? DisplayModeConfirmationDecision.Keep
                    : DisplayModeConfirmationDecision.Revert;
            }
        }

        internal static int RemainingWholeSeconds(
            TimeSpan timeout,
            TimeSpan elapsed)
        {
            double remaining = (timeout - elapsed).TotalSeconds;
            return remaining <= 0.0
                ? 0
                : (int)Math.Ceiling(remaining);
        }

        protected override void OnShown(EventArgs eventArgs)
        {
            base.OnShown(eventArgs);
            _stopwatch.Start();
            _timer.Start();
            UpdateCountdown();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Stop();
                _timer.Dispose();
            }

            base.Dispose(disposing);
        }

        private void OnTimerTick(object sender, EventArgs eventArgs)
        {
            if (_stopwatch.Elapsed >= _timeout)
            {
                _timer.Stop();
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            UpdateCountdown();
        }

        private void UpdateCountdown()
        {
            int remaining = RemainingWholeSeconds(
                _timeout,
                _stopwatch.Elapsed);
            _countdown.Text = "Automatic rollback in "
                + remaining
                + (remaining == 1 ? " second." : " seconds.");
            _keepButton.Text = "Keep this display mode ("
                + remaining
                + ")";
        }
    }
}
