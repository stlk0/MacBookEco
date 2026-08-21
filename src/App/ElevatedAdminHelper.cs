using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using MacBookEco.Core;

namespace MacBookEco.App
{
    internal enum AdminCommand
    {
        InstallDisplay,
        RemoveDisplay,
        ApplyPowerNormal,
        ApplyPowerCool,
        ApplyPowerBattery,
        RestorePower
    }

    /// <summary>
    /// Owns the UAC helper launch protocol: immutable arguments, embedded-byte
    /// verification, and stable result-code mapping.
    ///
    /// The wait is bounded and then deliberately unbounded.  Run waits
    /// TimeoutMilliseconds for a terminal result; if the child is still
    /// running it is never killed, and this thread parks on it indefinitely
    /// so a late finish still produces a real result and a live read-back
    /// that can release the global mutation gate.  Killing an elevated helper
    /// mid-mutation is the one outcome that leaves durable state unrecoverable.
    /// OptimizationCommandRunner publishes its own bounded timeout so the UI
    /// is released on schedule regardless.
    /// </summary>
    internal sealed class ElevatedAdminHelper
    {
        private const int TimeoutMilliseconds = 120000;
        private const string EmbeddedAdminResourceName = "MacBookEco.Admin.exe";
        private readonly string _executablePath;
        private readonly EmbeddedHelperVerifier _verifier;

        public ElevatedAdminHelper(
            string executablePath,
            EmbeddedHelperVerifier verifier)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new ArgumentException(
                    "An Admin helper path is required.",
                    nameof(executablePath));
            }

            if (verifier == null)
            {
                throw new ArgumentNullException(nameof(verifier));
            }

            _executablePath = executablePath;
            _verifier = verifier;
        }

        /// <summary>
        /// Verifies the embedded helper bytes, launches it elevated, and maps
        /// its exit code to a typed result.  Blocks until the child exits; see
        /// the type summary for why that wait has no upper bound.  A UAC
        /// refusal is a Cancelled result, not a failure.
        /// </summary>
        public OptimizationActionResult Run(AdminCommand command)
        {
            if (!File.Exists(_executablePath))
            {
                return OptimizationActionResult.Failed(
                    OperationCode.HelperMissing,
                    "MacBookEco.Admin.exe is missing. Place the matching helper next to "
                    + "MacBookEco.exe and try again.",
                    string.Empty);
            }

            try
            {
                // The handle stays open from verification through exit so the
                // medium-integrity caller cannot swap the UAC target.
                using (FileStream verifiedHelper = _verifier.OpenVerifiedHelper(
                    _executablePath,
                    EmbeddedAdminResourceName,
                    "MacBookEco.Admin.exe"))
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo();
                    startInfo.FileName = _executablePath;
                    startInfo.Arguments = FixedArguments(command);
                    startInfo.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    startInfo.UseShellExecute = true;
                    startInfo.Verb = "runas";
                    startInfo.WindowStyle = ProcessWindowStyle.Hidden;

                    using (Process process = Process.Start(startInfo))
                    {
                        if (process == null)
                        {
                            return OptimizationActionResult.Failed(
                                OperationCode.HelperFailed,
                                "Windows did not start the elevated helper.",
                                string.Empty);
                        }

                        WaitForTerminalExit(
                            process.WaitForExit,
                            process.WaitForExit);

                        if (process.ExitCode != AdminHelperExitCodes.Success)
                        {
                            return HelperFailure(process.ExitCode);
                        }
                    }
                }

                return OptimizationActionResult.Successful(
                    "The elevated helper completed; verifying read-only state.",
                    OperationCode.None,
                    false);
            }
            catch (Win32Exception exception)
            {
                if (exception.NativeErrorCode == 1223)
                {
                    return OptimizationActionResult.Cancelled(
                        OperationCode.UserCancelled,
                        "Administrator approval was cancelled; no requested change was applied.");
                }

                return OptimizationActionResult.Faulted(
                    "Could not start the elevated helper",
                    exception);
            }
            catch (Exception exception)
            {
                return OptimizationActionResult.Faulted(
                    "Elevated helper failed",
                    exception);
            }
        }

        internal static void WaitForTerminalExit(
            Func<int, bool> waitBounded,
            Action waitUnbounded)
        {
            if (waitBounded == null)
            {
                throw new ArgumentNullException(nameof(waitBounded));
            }

            if (waitUnbounded == null)
            {
                throw new ArgumentNullException(nameof(waitUnbounded));
            }

            // The outer runner publishes its own bounded timeout. Keep both
            // verified handles alive here so a child that finishes later still
            // produces a terminal result and live read-back that can release
            // the global mutation gate.
            if (!waitBounded(TimeoutMilliseconds))
            {
                waitUnbounded();
            }
        }

        internal static string FixedArguments(AdminCommand command)
        {
            switch (command)
            {
                case AdminCommand.InstallDisplay:
                    return "install-display";
                case AdminCommand.RemoveDisplay:
                    return "remove-display";
                case AdminCommand.ApplyPowerNormal:
                    return "apply-power normal";
                case AdminCommand.ApplyPowerCool:
                    return "apply-power cool";
                case AdminCommand.ApplyPowerBattery:
                    return "apply-power battery";
                case AdminCommand.RestorePower:
                    return "restore-power";
                default:
                    throw new InvalidOperationException("Unknown fixed helper command.");
            }
        }

        internal static OptimizationActionResult HelperFailure(int exitCode)
        {
            string diagnosticReason =
                AdminHelperExitCodes.DiagnosticReason(exitCode);
            string description = DescribeExitCode(exitCode) +
                DescribeDiagnosticReason(exitCode, diagnosticReason);
            string detail = "helper-exit=" + exitCode;
            if (diagnosticReason != null)
            {
                detail += ";helper-reason=" + diagnosticReason;
            }

            if (exitCode == AdminHelperExitCodes.Unsupported)
            {
                return OptimizationActionResult.Unsupported(
                    OperationCode.HelperUnsupported,
                    description,
                    detail);
            }

            if (AdminHelperExitCodes.IsIndeterminate(exitCode))
            {
                return OptimizationActionResult.Indeterminate(
                    OperationCode.HelperIndeterminate,
                    description,
                    detail);
            }

            return OptimizationActionResult.Failed(
                exitCode == AdminHelperExitCodes.Usage
                    ? OperationCode.HelperRejected
                    : OperationCode.HelperFailed,
                description,
                detail);
        }

        private static string DescribeExitCode(int exitCode)
        {
            if (AdminHelperExitCodes.IsIndeterminate(exitCode))
            {
                return "The helper reached an indeterminate transaction boundary. "
                    + "Recovery must reconcile the durable journal before another "
                    + "privileged change.";
            }

            if (AdminHelperExitCodes.DiagnosticReason(exitCode) != null &&
                exitCode != AdminHelperExitCodes.Unsupported)
            {
                return "The helper could not complete the requested transaction. "
                    + "No unverified change is treated as successful.";
            }

            switch (exitCode)
            {
                case AdminHelperExitCodes.Usage:
                    return "The helper rejected its fixed command.";
                case AdminHelperExitCodes.Unsupported:
                    return "This MacBook, panel, driver, or requested profile is unsupported.";
                case AdminHelperExitCodes.Failed:
                    return "The helper could not complete the requested transaction. "
                        + "No unverified change is treated as successful.";
                default:
                    return "The helper returned exit code " + exitCode + ".";
            }
        }

        private static string DescribeDiagnosticReason(
            int exitCode,
            string reason)
        {
            if (reason == null)
            {
                return string.Empty;
            }

            return " Diagnostic: " + reason + " (" +
                DiagnosticExplanation(exitCode) + ").";
        }

        private static string DiagnosticExplanation(int exitCode)
        {
            switch (exitCode)
            {
                case AdminHelperExitCodes.Unsupported:
                    return "the reviewed hardware profile did not match";
                case AdminHelperExitCodes.RequiresNative60:
                    return "the desktop was not at native 60 Hz";
                case AdminHelperExitCodes.ExternalDisplaysAttached:
                    return "an external display was attached";
                case AdminHelperExitCodes.DescriptorSlotsUnavailable:
                    return "the base EDID did not have two free descriptor slots";
                case AdminHelperExitCodes.ExistingOverride:
                    return "an unowned override blocked a new install";
                case AdminHelperExitCodes.HistoricalJournalState:
                    return "the historical profile could not be reconciled";
                case AdminHelperExitCodes.MonitorIdentityMismatch:
                    return "the stored and live monitor identities did not match";
                case AdminHelperExitCodes.JournalConflict:
                    return "the protected journal or owned override did not match";
                case AdminHelperExitCodes.InstallReconciliation:
                    return "install reconciliation could not prove live state";
                case AdminHelperExitCodes.RestoreReconciliation:
                    return "restore reconciliation could not prove live state";
                case AdminHelperExitCodes.JournalPersistence:
                    return "the final protected journal state could not be saved";
                case AdminHelperExitCodes.NativeFailure:
                    return "a Windows operation failed";
                default:
                    return "an unexpected helper failure occurred";
            }
        }

    }
}
