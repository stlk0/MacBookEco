using System;
using System.Drawing;
using System.Globalization;
using System.Collections.Generic;
using System.Windows.Forms;
using MacBookEco.AppPolicy;
using MacBookEco.Core;
using MacBookEco.Telemetry;

namespace MacBookEco.App
{
    public sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly TelemetryService _telemetry;
        private readonly IOptimizationActionService _actions;
        private readonly OptimizationStateMonitor _stateMonitor;
        private readonly OptimizationCommandRunner _runner;
        private readonly DashboardForm _dashboard;
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _statusItem;
        private readonly Dictionary<int, ToolStripMenuItem> _refreshItems;
        private readonly ToolStripSeparator _displayModesSeparator;
        private readonly ToolStripMenuItem _installDisplayItem;
        private readonly ToolStripMenuItem _removeDisplayItem;
        private readonly ToolStripMenuItem _cpuEverydayItem;
        private readonly ToolStripMenuItem _cpuCoolItem;
        private readonly ToolStripMenuItem _cpuBatteryItem;
        private readonly ToolStripMenuItem _startWithWindowsItem;
        private readonly Timer _uiTimer;
        // WinForms does not own a Font that is assigned to a ToolStripItem.
        private readonly Font _openItemFont;
        private readonly List<ToolStripItem> _mutationItems =
            new List<ToolStripItem>();
        private readonly System.Threading.EventWaitHandle _showDashboardRequest;
        private readonly System.Threading.RegisteredWaitHandle _showDashboardWait;
        private readonly System.Threading.SynchronizationContext _uiContext;
        private readonly WindowsFormsSynchronizationContext _ownedUiContext;
        private bool _mutationControlsEnabled = true;
        private bool _exiting;

        public TrayApplicationContext(
            TelemetryService telemetry,
            IOptimizationActionService actions,
            bool showDashboardAtStartup,
            OptimizationActionResult startupRecovery,
            string profileDiagnostics)
        {
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            if (actions == null)
            {
                throw new ArgumentNullException(nameof(actions));
            }

            _telemetry = telemetry;
            _actions = actions;

            // WinForms installs its own context when the message loop starts,
            // which is after this constructor runs. Installing it here is what
            // makes SynchronizationContext.Current equal to the captured
            // context on the UI thread: without that, every "already on the UI
            // thread" fast path misses and each publish takes a needless hop.
            System.Threading.SynchronizationContext uiContext =
                System.Threading.SynchronizationContext.Current;
            if (!(uiContext is WindowsFormsSynchronizationContext))
            {
                _ownedUiContext = new WindowsFormsSynchronizationContext();
                uiContext = _ownedUiContext;
                System.Threading.SynchronizationContext.SetSynchronizationContext(
                    uiContext);
            }

            _uiContext = uiContext;
            _stateMonitor = new OptimizationStateMonitor(_actions, uiContext);
            _stateMonitor.Changed += OnOptimizationStateChanged;
            _runner = new OptimizationCommandRunner(_actions, uiContext);
            _runner.SetDisplayConfirmationHandler(ConfirmDisplayMode);
            _runner.StateChanged += OnRunnerStateChanged;
            _runner.Completed += OnCommandCompleted;
            _dashboard = new DashboardForm(
                _telemetry,
                _stateMonitor,
                _runner,
                startupRecovery,
                profileDiagnostics);
            _menu = new ContextMenuStrip();

            ToolStripMenuItem openItem = new ToolStripMenuItem("Open dashboard");
            _openItemFont = new Font(openItem.Font, FontStyle.Bold);
            openItem.Font = _openItemFont;
            openItem.Click += delegate { _dashboard.ShowDashboard(); };
            _menu.Items.Add(openItem);

            _statusItem = new ToolStripMenuItem("Waiting for telemetry...");
            _statusItem.Enabled = false;
            _menu.Items.Add(_statusItem);
            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem displayMenu =
                new ToolStripMenuItem("Display refresh rate");
            _refreshItems = new Dictionary<int, ToolStripMenuItem>();
            for (var index = 0; index < ProfileCatalog.Modes.Count; index++)
            {
                DisplayModeDefinition mode = ProfileCatalog.Modes[index];
                int refreshRate = mode.WindowsRefreshRate;
                ToolStripMenuItem item = TrackMutation(
                    new ToolStripMenuItem(mode.DisplayName));
                item.Click += delegate {
                    RunCommand(OptimizationCommand.SetDisplayRefreshRate(
                        refreshRate));
                };
                _refreshItems.Add(refreshRate, item);
                displayMenu.DropDownItems.Add(item);
            }
            _displayModesSeparator = new ToolStripSeparator();
            displayMenu.DropDownItems.Add(_displayModesSeparator);

            _installDisplayItem = TrackMutation(
                new ToolStripMenuItem(
                    "Install "
                        + ProfileCatalog.OwnedSupportDisplayName
                        + " support..."));
            _installDisplayItem.Click += delegate {
                RunCommand(OptimizationCommand.InstallDisplaySupport());
            };
            displayMenu.DropDownItems.Add(_installDisplayItem);

            _removeDisplayItem = TrackMutation(
                new ToolStripMenuItem("Remove Eco display support and restore..."));
            _removeDisplayItem.Visible = false;
            _removeDisplayItem.Enabled = false;
            _removeDisplayItem.Click += delegate {
                if (RefuseWhenBusy())
                {
                    return;
                }

                if (DestructiveConfirmation.RemoveDisplaySupport(_dashboard))
                {
                    RunCommand(OptimizationCommand.RemoveDisplaySupport());
                }
            };
            displayMenu.DropDownItems.Add(_removeDisplayItem);
            _menu.Items.Add(displayMenu);

            ToolStripMenuItem cpuMenu = new ToolStripMenuItem("CPU preset");
            _cpuEverydayItem = CreateCpuItem("Everyday", PowerPreset.Normal);
            _cpuCoolItem = CreateCpuItem("Cool & quiet", PowerPreset.Cool);
            _cpuBatteryItem = CreateCpuItem(
                "Battery saver",
                PowerPreset.MaximumBattery);
            cpuMenu.DropDownItems.Add(_cpuEverydayItem);
            cpuMenu.DropDownItems.Add(_cpuCoolItem);
            cpuMenu.DropDownItems.Add(_cpuBatteryItem);
            cpuMenu.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem restorePowerItem = TrackMutation(
                new ToolStripMenuItem("Restore original power plan..."));
            restorePowerItem.Click += delegate {
                if (RefuseWhenBusy())
                {
                    return;
                }

                if (DestructiveConfirmation.RestorePowerPlan(_dashboard))
                {
                    RunCommand(OptimizationCommand.RestoreCpuPower());
                }
            };
            cpuMenu.DropDownItems.Add(restorePowerItem);
            _menu.Items.Add(cpuMenu);
            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem diagnosticsItem =
                new ToolStripMenuItem("Diagnostics");
            diagnosticsItem.Click += delegate { _dashboard.ShowDiagnostics(); };
            _menu.Items.Add(diagnosticsItem);

            _startWithWindowsItem =
                new ToolStripMenuItem("Start with Windows");
            _startWithWindowsItem.Checked = StartupRegistration.IsEnabled();
            _startWithWindowsItem.Click += delegate { ToggleStartWithWindows(); };
            _menu.Items.Add(_startWithWindowsItem);

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += delegate { ExitApplication(); };
            _menu.Items.Add(exitItem);

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = ApplicationIcon.Shared;
            _notifyIcon.Text = Application.ProductName;
            _notifyIcon.ContextMenuStrip = _menu;
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += delegate { _dashboard.ShowDashboard(); };

            // Telemetry and optimization state both arrive as events now. This
            // timer only redraws the tray text from whatever was last cached,
            // so it never touches the registry or a native power API.
            _uiTimer = new Timer();
            _uiTimer.Interval = 5000;
            _uiTimer.Tick += delegate { RefreshTrayStatus(); };
            _uiTimer.Start();

            // A second launch signals this instead of showing its own window.
            _showDashboardRequest = new System.Threading.EventWaitHandle(
                false,
                System.Threading.EventResetMode.AutoReset,
                Program.ShowDashboardEventName);
            _showDashboardWait =
                System.Threading.ThreadPool.RegisterWaitForSingleObject(
                    _showDashboardRequest,
                    OnShowDashboardRequested,
                    null,
                    System.Threading.Timeout.Infinite,
                    false);

            _telemetry.Start();
            _stateMonitor.Start();
            RefreshTrayStatus();
            if (showDashboardAtStartup)
            {
                // The ApplicationContext is constructed just before the
                // WinForms message loop starts. Defer the initial window until
                // the first idle turn so a real foreground handle is created
                // reliably on every supported .NET Framework build.
                Application.Idle += ShowDashboardOnFirstIdle;
            }
        }

        protected override void ExitThreadCore()
        {
            DisposeResources();
            base.ExitThreadCore();
        }

        private ToolStripMenuItem CreateCpuItem(
            string text,
            PowerPreset preset)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += delegate {
                RunCommand(OptimizationCommand.ApplyCpuPreset(preset));
            };
            return TrackMutation(item);
        }

        private void OnOptimizationStateChanged(
            object sender,
            OptimizationStateChangedEventArgs eventArgs)
        {
            RefreshTrayStatus();
        }

        private void RefreshTrayStatus()
        {
            TelemetrySnapshot snapshot = _telemetry.LatestSnapshot;
            OptimizationStateSnapshot state = _stateMonitor.Current;
            if (snapshot == null)
            {
                ApplyDisplayMenuPolicy(state, null);
                return;
            }

            // Values are formatted by the same helpers the dashboard uses, so
            // the tray and the overview never disagree about a number. Only
            // the absent-value text differs: a tray line has no column labels
            // to say which reading is missing.
            string charge = snapshot.Battery.ChargePercent.HasValue
                ? TelemetryText.Percent(snapshot.Battery.ChargePercent)
                : "battery N/A";
            string power = snapshot.Battery.DischargeWatts.HasValue
                ? TelemetryText.Watts(snapshot.Battery.DischargeWatts)
                : "power N/A";
            string refresh = snapshot.Display.RefreshRateHz.HasValue
                ? TelemetryText.Refresh(snapshot.Display.RefreshRateHz)
                : "display N/A";
            string cpuProfile = CpuProfileLabel(state);

            _statusItem.Text = charge + " \u00b7 " + power + " \u00b7 "
                + refresh + " \u00b7 " + cpuProfile;
            ApplyDisplayMenuPolicy(
                state,
                CurrentRefreshRate(snapshot.Display));
            _cpuEverydayItem.Checked = IsCpuPreset(state, PowerPreset.Normal);
            _cpuCoolItem.Checked = IsCpuPreset(state, PowerPreset.Cool);
            _cpuBatteryItem.Checked =
                IsCpuPreset(state, PowerPreset.MaximumBattery);

            string tooltip = Application.ProductName + " \u00b7 " + charge + " \u00b7 "
                + power + " \u00b7 " + refresh;
            _notifyIcon.Text = tooltip.Length <= 63
                ? tooltip
                : tooltip.Substring(0, 63);
        }

        private void ApplyDisplayMenuPolicy(
            OptimizationStateSnapshot state,
            int? currentRefreshRate)
        {
            DisplaySupportUiState display = DisplaySupportUiPolicy.Evaluate(
                state,
                currentRefreshRate,
                _mutationControlsEnabled);
            bool anyVisible = false;
            for (var index = 0; index < display.Modes.Count; index++)
            {
                DisplayModeUiState mode = display.Modes[index];
                ToolStripMenuItem item = _refreshItems[mode.Mode.WindowsRefreshRate];
                item.Checked = mode.Current;
                item.Visible = mode.Show;
                SetMenuItemEnabled(item, mode.CanSelect);
                anyVisible |= mode.Show;
            }

            _displayModesSeparator.Visible = anyVisible;
            string installText = display.InstallText + "...";
            if (!string.Equals(
                _installDisplayItem.Text,
                installText,
                StringComparison.Ordinal))
            {
                _installDisplayItem.Text = installText;
            }

            SetMenuItemEnabled(_installDisplayItem, display.CanInstall);
            if (_removeDisplayItem.Visible != display.ShowRemove)
            {
                _removeDisplayItem.Visible = display.ShowRemove;
            }

            SetMenuItemEnabled(_removeDisplayItem, display.CanRemove);
        }

        private static int? CurrentRefreshRate(DisplayTelemetry display)
        {
            if (display == null || !display.RefreshRateHz.HasValue)
            {
                return null;
            }

            DisplayModeDefinition mode =
                ProfileCatalog.GetModeForWindowsSelector(
                    display.RefreshRateHz);
            if (mode != null)
            {
                return mode.WindowsRefreshRate;
            }

            return (int)Math.Round(display.RefreshRateHz.Value);
        }

        private static void SetMenuItemEnabled(
            ToolStripMenuItem item,
            bool enabled)
        {
            if (item.Enabled != enabled)
            {
                item.Enabled = enabled;
            }
        }

        private static bool IsCpuPreset(
            OptimizationStateSnapshot state,
            PowerPreset expected)
        {
            return state != null
                && state.CpuProfileActive
                && state.ActiveCpuPreset.HasValue
                && state.ActiveCpuPreset.Value == expected;
        }

        private static string CpuProfileLabel(OptimizationStateSnapshot state)
        {
            if (state == null || !state.CpuProfileActive
                || !state.ActiveCpuPreset.HasValue)
            {
                return "Windows power plan";
            }

            return PowerPresetCatalog.SafeDisplayName(state.ActiveCpuPreset.Value);
        }

        private ToolStripMenuItem TrackMutation(ToolStripMenuItem item)
        {
            _mutationItems.Add(item);
            return item;
        }

        private void RunCommand(OptimizationCommand command)
        {
            _runner.Execute(command);
        }

        private void OnRunnerStateChanged(
            object sender,
            OptimizationCommandRunnerStateChangedEventArgs eventArgs)
        {
            bool enabled = eventArgs == null || !eventArgs.IsBusy;
            _mutationControlsEnabled = enabled;
            foreach (ToolStripItem item in _mutationItems)
            {
                item.Enabled = enabled;
            }

            RefreshTrayStatus();
        }

        private void OnCommandCompleted(
            object sender,
            OptimizationCommandCompletedEventArgs eventArgs)
        {
            OptimizationActionResult result = eventArgs == null
                ? null
                : eventArgs.Result;
            OptimizationStateSnapshot state = eventArgs == null
                ? null
                : eventArgs.State;

            // Adopt the read-back the command already performed, then ask for
            // a fresh one: a restart-required change only becomes visible on
            // the next poll.  The dashboard subscribes to Completed itself and
            // is not told the result from here.
            _stateMonitor.Publish(state);
            _stateMonitor.RequestRefresh();
            RefreshTrayStatus();

            if (result == null)
            {
                ShowBalloon("Action failed", "The action provider returned no result.");
                return;
            }

            ShowBalloon(
                result.Outcome == OperationOutcome.Succeeded
                    ? "MacBook Eco"
                    : (result.Outcome == OperationOutcome.Indeterminate
                        ? "Recovery required"
                        : "Action unavailable"),
                result.Message);
        }

        private DisplayModeConfirmationDecision ConfirmDisplayMode(
            DisplayModeConfirmationRequest request)
        {
            return DisplayModeConfirmationDialog.ShowConfirmation(
                _dashboard,
                request);
        }

        /// <summary>
        /// Reports a busy runner through a balloon and returns true so the
        /// caller stops. The tray asks before showing a destructive prompt
        /// rather than after, because a modal dialog raised from a menu the
        /// user cannot see the state of is worse than a balloon that says why.
        /// </summary>
        private bool RefuseWhenBusy()
        {
            if (!_runner.IsBusy)
            {
                return false;
            }

            ShowBalloon("Action unavailable", _runner.BusyReason);
            return true;
        }

        private void OnShowDashboardRequested(object state, bool timedOut)
        {
            if (timedOut || _exiting)
            {
                return;
            }

            // The wait runs on a pool thread; the dashboard is WinForms. This
            // marshals through the UI context rather than Control.BeginInvoke
            // because after --background the window has never been shown and
            // therefore has no handle to invoke through.
            try
            {
                _uiContext.Post(
                    delegate {
                        if (!_exiting && !_dashboard.IsDisposed)
                        {
                            _dashboard.ShowDashboard();
                        }
                    },
                    null);
            }
            catch (ObjectDisposedException)
            {
                // The UI context was torn down while this callback was queued.
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void ShowDashboardOnFirstIdle(object sender, EventArgs eventArgs)
        {
            Application.Idle -= ShowDashboardOnFirstIdle;
            if (!_exiting)
            {
                _dashboard.ShowDashboard();
            }
        }

        private void ShowBalloon(string title, string message)
        {
            string safeMessage = string.IsNullOrWhiteSpace(message)
                ? "No details are available."
                : message;
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = safeMessage;
            _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(4000);
        }

        private void ToggleStartWithWindows()
        {
            bool enable = !_startWithWindowsItem.Checked;
            string error;
            if (StartupRegistration.TrySetEnabled(enable, out error))
            {
                _startWithWindowsItem.Checked = enable;
                return;
            }

            _startWithWindowsItem.Checked = StartupRegistration.IsEnabled();
            ShowBalloon(
                "Startup setting unavailable",
                string.IsNullOrWhiteSpace(error)
                    ? "Windows did not accept the startup setting."
                    : error);
        }

        private void ExitApplication()
        {
            if (_exiting)
            {
                return;
            }

            if (_runner.IsBusy)
            {
                MessageBox.Show(
                    _dashboard,
                    "An optimization command is still running or requires recovery "
                    + "read-back. MacBook Eco will remain in the notification area; "
                    + "do not end it while durable recovery state is active.\r\n\r\n"
                    + "Wait for the result, then choose Exit again.",
                    "Keep MacBook Eco in tray",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _dashboard.Hide();
                return;
            }

            _exiting = true;
            _notifyIcon.Visible = false;
            _dashboard.Shutdown();
            ExitThread();
        }

        private void DisposeResources()
        {
            Application.Idle -= ShowDashboardOnFirstIdle;
            if (!_exiting)
            {
                _exiting = true;
                if (!_dashboard.IsDisposed)
                {
                    _dashboard.Shutdown();
                }
            }

            if (_showDashboardWait != null)
            {
                _showDashboardWait.Unregister(null);
            }

            if (_showDashboardRequest != null)
            {
                _showDashboardRequest.Close();
            }

            _uiTimer.Stop();
            _uiTimer.Dispose();
            _runner.StateChanged -= OnRunnerStateChanged;
            _runner.Completed -= OnCommandCompleted;
            _runner.Dispose();
            _stateMonitor.Changed -= OnOptimizationStateChanged;
            _stateMonitor.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
            _openItemFont.Dispose();
            _telemetry.Dispose();

            // Last: everything that could still post to the UI is disposed by
            // the time this runs.
            if (_ownedUiContext != null)
            {
                _ownedUiContext.Dispose();
            }
        }
    }
}
