using System;
using System.Collections.Generic;
using System.Text;
using MacBookEco.AppPolicy;
using MacBookEco.Core;
using MacBookEco.Platform.Windows;

namespace MacBookEco.Admin
{
    /// <summary>
    /// One-shot elevated helper. The parser intentionally exposes no generic
    /// file, registry, device-instance, GUID, shell-command or numeric-setting
    /// argument.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            TryConfigureConsole();
            if (args == null || args.Length == 0)
            {
                PrintUsage();
                return AdminHelperExitCodes.Usage;
            }

            try
            {
                string command = args[0].ToLowerInvariant();
                switch (command)
                {
                    case "diagnose":
                        RequireArgumentCount(args, 1);
                        Diagnose();
                        return AdminHelperExitCodes.Success;

                    case "install-display":
                    {
                        RequireArgumentCount(args, 1);
                        EdidOverrideOperationResult installResult =
                            new EdidOverrideService().InstallVerifiedProfile();
                        TryPrint(
                            delegate {
                                PrintDisplayResult(installResult);
                            });
                        int installExit = installResult.Succeeded
                            ? AdminHelperExitCodes.Success
                            : AdminHelperExitCodes.Indeterminate;
                        return installExit;
                    }

                    case "remove-display":
                    {
                        RequireArgumentCount(args, 1);
                        EdidOverrideOperationResult restoreResult =
                            new EdidOverrideService().RestoreOriginal();
                        TryPrint(
                            delegate {
                                PrintDisplayResult(restoreResult);
                            });
                        int restoreDisplayExit = restoreResult.Succeeded
                            ? AdminHelperExitCodes.Success
                            : AdminHelperExitCodes.Indeterminate;
                        return restoreDisplayExit;
                    }

                    case "apply-power":
                    {
                        RequireArgumentCount(args, 2);
                        PowerSchemeOperationResult applyResult =
                            new PowerSchemeService().ApplyPreset(
                                ParsePreset(args[1]));
                        TryPrint(
                            delegate {
                                PrintPowerResult(applyResult);
                            });
                        int applyExit = ExitCodeForPowerResult(applyResult);
                        return applyExit;
                    }

                    case "restore-power":
                    {
                        RequireArgumentCount(args, 1);
                        PowerSchemeOperationResult restorePowerResult =
                            new PowerSchemeService().RestoreOriginal();
                        TryPrint(
                            delegate {
                                PrintPowerResult(restorePowerResult);
                            });
                        int restorePowerExit =
                            ExitCodeForPowerResult(restorePowerResult);
                        return restorePowerExit;
                    }

                    default:
                        PrintUsage();
                        return AdminHelperExitCodes.Usage;
                }
            }
            catch (NotSupportedException ex)
            {
                TryWriteError("unsupported: " + ex.Message);
                return AdminHelperExitCodes.Unsupported;
            }
            catch (Exception ex)
            {
                TryWriteError(
                    "failed: " +
                    ex.GetType().Name +
                    ": " +
                    ex.Message);
                return AdminHelperExitCodes.Failed;
            }
        }

        private static void TryConfigureConsole()
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch
            {
                // The helper is normally launched by a WinForms process with
                // no console. Diagnostic output must never decide whether an
                // already verified privileged transaction is successful.
            }
        }

        private static void TryPrint(Action print)
        {
            try
            {
                print();
            }
            catch
            {
                // stdout is optional when the helper is launched by the GUI.
            }
        }

        private static void TryWriteError(string message)
        {
            try
            {
                Console.Error.WriteLine(message);
            }
            catch
            {
                // The process exit code remains the authoritative GUI channel.
            }
        }

        private static void Diagnose()
        {
            WindowsHardwareSnapshot hardware =
                new HardwareDiscoveryService().Discover();
            Console.WriteLine("manufacturer=" + Safe(hardware.SystemManufacturer));
            Console.WriteLine("appleModel=" + Safe(hardware.AppleModel));
            Console.WriteLine("isApple=" + hardware.IsAppleHardware);

            if (hardware.InternalDisplay == null)
            {
                Console.WriteLine("internalDisplay=unavailable");
            }
            else
            {
                WindowsMonitorInfo monitor = hardware.InternalDisplay;
                Console.WriteLine("monitor.name=" + Safe(monitor.FriendlyName));
                Console.WriteLine("monitor.hardwareId=" + Safe(monitor.HardwareId));
                Console.WriteLine("monitor.instanceId=" + Safe(monitor.DeviceInstanceId));
                Console.WriteLine(
                    "monitor.interfacePath=" + Safe(monitor.MonitorDevicePath));
                Console.WriteLine(
                    "monitor.registryPath=" + Safe(monitor.RegistryDevicePath));
                Console.WriteLine(
                    "monitor.edid=" +
                    Safe(monitor.EdidManufacturerCode) +
                    "/" +
                    monitor.EdidProductCode.ToString("X4"));
                Console.WriteLine(
                    "monitor.native=" +
                    monitor.NativeWidth +
                    "x" +
                    monitor.NativeHeight);
                Console.WriteLine(
                    "monitor.overridePresent=" +
                    (monitor.ExistingEdidOverride != null));
            }

            if (hardware.DisplayAdapter == null)
            {
                Console.WriteLine("adapter=unavailable");
            }
            else
            {
                WindowsDisplayAdapterInfo adapter = hardware.DisplayAdapter;
                Console.WriteLine("adapter.name=" + Safe(adapter.Description));
                Console.WriteLine("adapter.gdiName=" + Safe(adapter.GdiDeviceName));
                Console.WriteLine("adapter.instanceId=" + Safe(adapter.DeviceInstanceId));
                Console.WriteLine("adapter.isAmd=" + adapter.IsAmd);
                Console.WriteLine("adapter.driver=" + Safe(adapter.DriverVersion));
            }

            Console.WriteLine(
                "display.mode=" +
                (hardware.CurrentDisplayMode == null
                    ? "unavailable"
                    : hardware.CurrentDisplayMode.ToString()));

            IList<string> warnings = hardware.Warnings;
            if (warnings != null)
            {
                int index;
                for (index = 0; index < warnings.Count; index++)
                    Console.WriteLine("warning=" + Safe(warnings[index]));
            }

            DisplayOverrideStatus displayStatus =
                new EdidStatusReader().Read();
            Console.WriteLine("displayJournal.state=" + displayStatus.State);
            Console.WriteLine(
                "displayJournal.profile=" + Safe(displayStatus.ProfileId));

            try
            {
                PowerSchemeStatus powerStatus =
                    new PowerStatusReader().Read();
                Console.WriteLine("power.active=" + powerStatus.ActiveScheme);
                Console.WriteLine("powerJournal.state=" + powerStatus.State);
            }
            catch (Exception ex)
            {
                Console.WriteLine("warning=Power status unavailable: " + Safe(ex.Message));
            }
        }

        private static void PrintDisplayResult(EdidOverrideOperationResult result)
        {
            Console.WriteLine("ok=" + result.Succeeded);
            Console.WriteLine("outcome=" + result.Outcome);
            Console.WriteLine("profile=" + Safe(result.ProfileId));
            Console.WriteLine(
                "deviceReloadRequired=" + result.DeviceReloadRequired);
            Console.WriteLine("message=" + Safe(result.Message));
        }

        private static void PrintPowerResult(PowerSchemeOperationResult result)
        {
            Console.WriteLine("ok=" + result.Succeeded);
            Console.WriteLine("outcome=" + result.Outcome);
            Console.WriteLine("originalScheme=" + result.OriginalScheme);
            Console.WriteLine("ownedScheme=" + result.OwnedScheme);
            Console.WriteLine("ownedSchemeRetained=" + result.OwnedSchemeRetained);
            Console.WriteLine("message=" + Safe(result.Message));

            int index;
            for (index = 0; index < result.SettingResults.Count; index++)
            {
                PowerSettingOperationResult setting =
                    result.SettingResults[index];
                Console.WriteLine(
                    "powerSetting=" + Safe(setting.Name) +
                    ";outcome=" + setting.Outcome +
                    ";message=" + Safe(setting.Message));
            }

            for (index = 0; index < result.SkippedSettings.Count; index++)
                Console.WriteLine(
                    "unsupportedSetting=" +
                    Safe(result.SkippedSettings[index]));
        }

        private static int ExitCodeForPowerResult(
            PowerSchemeOperationResult result)
        {
            if (result != null &&
                result.Outcome == PowerSchemeOperationOutcome.Succeeded)
            {
                return AdminHelperExitCodes.Success;
            }

            return result != null &&
                result.Outcome == PowerSchemeOperationOutcome.Indeterminate
                ? AdminHelperExitCodes.Indeterminate
                : AdminHelperExitCodes.Failed;
        }

        private static PowerPreset ParsePreset(string value)
        {
            switch (value.ToLowerInvariant())
            {
                case "normal":
                    return PowerPreset.Normal;
                case "cool":
                    return PowerPreset.Cool;
                case "battery":
                case "maximum-battery":
                    return PowerPreset.MaximumBattery;
                default:
                    throw new ArgumentException(
                        "Power preset must be normal, cool, or battery.");
            }
        }

        private static void RequireArgumentCount(string[] args, int count)
        {
            if (args.Length != count)
                throw new ArgumentException(
                    "The selected command received an invalid argument count.");
        }

        private static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Replace("\r", " ").Replace("\n", " ");
        }

        private static void PrintUsage()
        {
            Console.Error.WriteLine("MacBookEco.Admin commands:");
            Console.Error.WriteLine("  diagnose");
            Console.Error.WriteLine("  install-display");
            Console.Error.WriteLine("  remove-display");
            Console.Error.WriteLine("  apply-power normal|cool|battery");
            Console.Error.WriteLine("  restore-power");
        }
    }
}
