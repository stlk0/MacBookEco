using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using MacBookEco.AppPolicy;
using MacBookEco.Platform.Windows;
using MacBookEco.Telemetry;

namespace MacBookEco.App
{
    internal static class Program
    {
        private const string SingleInstanceMutexName =
            @"Local\MacBookEco.Tray.2E6FB97C-78E6-4DFB-AB6E-A8BE8E5B4DBA";

        // Session-local, so it cannot be signalled from another logon session.
        internal const string ShowDashboardEventName =
            @"Local\MacBookEco.Tray.Show.2E6FB97C-78E6-4DFB-AB6E-A8BE8E5B4DBA";

        [STAThread]
        private static int Main(string[] args)
        {
            if (IsUninstallSafetyCheck(args))
            {
                return CheckUninstallSafety();
            }

            if (IsUninstallRecoveryRequest(args))
            {
                return RecoverForUninstall();
            }

            if (IsMalformedMaintenanceRequest(args))
            {
                return 2;
            }

            bool startHidden = args != null
                && Array.Exists(
                    args,
                    value => string.Equals(
                        value,
                        "--background",
                        StringComparison.OrdinalIgnoreCase));
            bool ownsMutex;
            using (Mutex singleInstance = new Mutex(
                true,
                SingleInstanceMutexName,
                out ownsMutex))
            {
                if (!ownsMutex)
                {
                    // Bring the running instance forward instead of telling the
                    // user it exists and leaving them to find it in the tray.
                    ActivateRunningInstance();
                    return 0;
                }

                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);

                    IDisplayTelemetryProvider displayTelemetry =
                        new DisplayTelemetryProvider();
                    TelemetryService telemetry = new TelemetryService(
                        new BatteryTelemetryProvider(),
                        new CpuTelemetryProvider(),
                        displayTelemetry,
                        new WindowsGpuTelemetryProvider());

                    OptimizationActionResult startupRecovery;
                    IOptimizationActionService actions = CreateActions(
                        out startupRecovery);

                    Application.Run(
                        new TrayApplicationContext(
                            telemetry,
                            actions,
                            !startHidden,
                            startupRecovery,
                            CaptureProfileDiagnostics()));
                    return 0;
                }
                finally
                {
                    singleInstance.ReleaseMutex();
                }
            }
        }

        private static bool IsUninstallSafetyCheck(string[] args)
        {
            return args != null && args.Length == 1 && string.Equals(
                args[0],
                "--check-uninstall-safe",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUninstallRecoveryRequest(string[] args)
        {
            return args != null && args.Length == 1 && string.Equals(
                args[0],
                "--recover-for-uninstall",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMalformedMaintenanceRequest(string[] args)
        {
            return args != null && args.Length > 0 && Array.Exists(
                args,
                value => string.Equals(
                        value,
                        "--check-uninstall-safe",
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        value,
                        "--recover-for-uninstall",
                        StringComparison.OrdinalIgnoreCase));
        }

        private static int RecoverForUninstall()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            OptimizationActionResult startupRecovery;
            IOptimizationActionService actions = CreateActions(
                out startupRecovery);
            if (startupRecovery != null && !startupRecovery.Succeeded)
            {
                MessageBox.Show(
                    startupRecovery.Message,
                    "MacBook Eco uninstall recovery",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return 1;
            }

            OptimizationActionResult result =
                new UninstallRecoveryCoordinator(actions).Recover(
                    delegate(DisplayModeConfirmationRequest request)
                    {
                        return DisplayModeConfirmationDialog.ShowConfirmation(
                            null,
                            request);
                    });
            if (result == null || !result.Succeeded)
            {
                MessageBox.Show(
                    result == null
                        ? "MacBook Eco returned no uninstall recovery result."
                        : result.Message,
                    "MacBook Eco uninstall recovery",
                    MessageBoxButtons.OK,
                    result != null
                            && result.Outcome == OperationOutcome.Cancelled
                        ? MessageBoxIcon.Information
                        : MessageBoxIcon.Warning);
                return 1;
            }

            return result.RestartRequired ? 3 : 0;
        }

        private static int CheckUninstallSafety()
        {
            int exitCode = 2;
            using (ManualResetEvent completed = new ManualResetEvent(false))
            {
                Thread worker = new Thread(new ThreadStart(
                    delegate {
                        try
                        {
                            DisplayOverrideStatus display =
                                new EdidStatusReader().Read();
                            PowerSchemeStatus power = new PowerStatusReader().Read();
                            exitCode = UninstallSafetyPolicy.GetExitCode(
                                ToUninstallSafetyState(display == null
                                    ? ManagedResourceState.Conflict
                                    : display.State),
                                ToUninstallSafetyState(power == null
                                    ? ManagedResourceState.Conflict
                                    : power.State));
                        }
                        catch
                        {
                            exitCode = 2;
                        }
                        finally
                        {
                            completed.Set();
                        }
                    }));
                worker.IsBackground = true;
                worker.Start();
                return completed.WaitOne(TimeSpan.FromSeconds(10))
                    ? exitCode
                    : 2;
            }
        }

        private static UninstallSafetyState ToUninstallSafetyState(
            ManagedResourceState state)
        {
            switch (state)
            {
                case ManagedResourceState.NotInstalled:
                case ManagedResourceState.Restored:
                    return UninstallSafetyState.Safe;
                case ManagedResourceState.Installed:
                case ManagedResourceState.RecoveryRequired:
                    return UninstallSafetyState.RecoveryRequired;
                default:
                    return UninstallSafetyState.Unknown;
            }
        }

        private static string CaptureProfileDiagnostics()
        {
            try
            {
                WindowsHardwareSnapshot snapshot =
                    new HardwareDiscoveryService().Discover();
                if (snapshot == null)
                {
                    return BuildProfileDiscoveryFailure(
                        "Hardware discovery returned no result.");
                }

                if (snapshot.InternalDisplay == null)
                {
                    return BuildProfileDiscoveryFailure(
                        "The active internal panel could not be resolved.");
                }

                if (string.IsNullOrWhiteSpace(snapshot.AppleModel))
                {
                    return BuildProfileDiscoveryFailure(
                        "The SMBIOS model is unavailable.");
                }

                if (string.IsNullOrWhiteSpace(
                    snapshot.InternalDisplay.HardwareId))
                {
                    return BuildProfileDiscoveryFailure(
                        "The panel hardware identifier is unavailable.");
                }

                if (snapshot.InternalDisplay.Edid == null)
                {
                    return BuildProfileDiscoveryFailure(
                        "The base EDID is unavailable.");
                }

                return ProfileCatalog.BuildPublicDiagnostics(
                    snapshot.ToCoreSnapshot());
            }
            catch
            {
                // Discovery failures stay categorical. Exception messages can
                // contain device-instance paths and do not belong in a report
                // explicitly intended for public sharing.
                return BuildProfileDiscoveryFailure(
                    "The discovered hardware data could not be validated.");
            }
        }

        private static string BuildProfileDiscoveryFailure(string reason)
        {
            return "Display profile compatibility (public-safe)"
                + Environment.NewLine
                + "Discovery: Incomplete"
                + Environment.NewLine
                + "Mismatch: "
                + reason
                + Environment.NewLine;
        }

        private static IOptimizationActionService CreateActions(
            out OptimizationActionResult startupRecovery)
        {
            startupRecovery = null;
            try
            {
                string applicationDirectory = AppDomain.CurrentDomain.BaseDirectory;
                EmbeddedHelperVerifier helperVerifier =
                    new EmbeddedHelperVerifier();
                string watchdogPath = Path.Combine(
                    applicationDirectory,
                    "MacBookEco.Watchdog.exe");
                startupRecovery = DisplayWatchdogStartupRecovery.Recover(
                    watchdogPath,
                    helperVerifier);
                DisplayRefreshRateValidator displayValidator =
                    new DisplayRefreshRateValidator(
                        new HardwareDiscoveryService(),
                        new StableDisplayTargetResolver());
                DisplayRefreshRateUseCase displayRefresh =
                    new DisplayRefreshRateUseCase(
                        displayValidator,
                        new DisplayModeService(),
                        watchdogPath,
                        helperVerifier);
                ElevatedAdminHelper adminHelper = new ElevatedAdminHelper(
                    Path.Combine(
                        applicationDirectory,
                        "MacBookEco.Admin.exe"),
                    helperVerifier);
                SmbiosIdentity smbios = SmbiosReader.ReadIdentity();
                CpuHardwareSupportStatus cpuHardwareSupport =
                    CpuHardwareSupportPolicy.Classify(
                        smbios == null ? null : smbios.Manufacturer,
                        smbios == null ? null : smbios.ProductName);
                return new WindowsOptimizationActionService(
                    displayRefresh,
                    new EdidStatusReader(),
                    new PowerStatusReader(),
                    adminHelper,
                    cpuHardwareSupport,
                    startupRecovery);
            }
            catch (Exception exception)
            {
                return new ReadOnlyOptimizationActionService(
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static void ActivateRunningInstance()
        {
            EventWaitHandle showDashboard;
            if (EventWaitHandle.TryOpenExisting(
                ShowDashboardEventName,
                out showDashboard))
            {
                using (showDashboard)
                {
                    showDashboard.Set();
                    return;
                }
            }

            // The mutex is held but the running instance is not listening yet,
            // or is shutting down. Say so rather than exiting silently.
            MessageBox.Show(
                "MacBook Eco is already running in the notification area.",
                "MacBook Eco",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
