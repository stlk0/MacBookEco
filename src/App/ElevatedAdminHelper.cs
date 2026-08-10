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
        InstallExperimentalDisplay,
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
        public OptimizationActionResult Run(
            AdminCommand command,
            string experimentalAcknowledgementToken = null)
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
                    startInfo.Arguments = FixedArguments(
                        command,
                        experimentalAcknowledgementToken);
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

        internal static string FixedArguments(
            AdminCommand command,
            string experimentalAcknowledgementToken = null)
        {
            switch (command)
            {
                case AdminCommand.InstallDisplay:
                    RequireNoAcknowledgementToken(
                        experimentalAcknowledgementToken);
                    return "install-display";
                case AdminCommand.InstallExperimentalDisplay:
                    Sha256Digest parsed;
                    if (!Sha256Digest.TryParseCanonical(
                            experimentalAcknowledgementToken,
                            out parsed))
                    {
                        throw new InvalidOperationException(
                            "The experimental helper command requires one "
                                + "canonical acknowledgement token.");
                    }

                    // Canonical uppercase hex contains no quoting or command-
                    // line metacharacters. It can only restrict the helper's
                    // freshly generated choice; it cannot supply EDID bytes.
                    return "install-experimental-display " +
                        parsed.ToString();
                case AdminCommand.RemoveDisplay:
                    RequireNoAcknowledgementToken(
                        experimentalAcknowledgementToken);
                    return "remove-display";
                case AdminCommand.ApplyPowerNormal:
                    RequireNoAcknowledgementToken(
                        experimentalAcknowledgementToken);
                    return "apply-power normal";
                case AdminCommand.ApplyPowerCool:
                    RequireNoAcknowledgementToken(
                        experimentalAcknowledgementToken);
                    return "apply-power cool";
                case AdminCommand.ApplyPowerBattery:
                    RequireNoAcknowledgementToken(
                        experimentalAcknowledgementToken);
                    return "apply-power battery";
                case AdminCommand.RestorePower:
                    RequireNoAcknowledgementToken(
                        experimentalAcknowledgementToken);
                    return "restore-power";
                default:
                    throw new InvalidOperationException("Unknown fixed helper command.");
            }
        }

        private static void RequireNoAcknowledgementToken(string value)
        {
            if (value != null)
            {
                throw new InvalidOperationException(
                    "This fixed helper command accepts no acknowledgement token.");
            }
        }

        internal static OptimizationActionResult HelperFailure(int exitCode)
        {
            string description = DescribeExitCode(exitCode);
            string detail = "helper-exit=" + exitCode;

            if (exitCode == AdminHelperExitCodes.Unsupported)
            {
                return OptimizationActionResult.Unsupported(
                    OperationCode.HelperUnsupported,
                    description);
            }

            if (exitCode == AdminHelperExitCodes.Indeterminate)
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
            switch (exitCode)
            {
                case AdminHelperExitCodes.Usage:
                    return "The helper rejected its fixed command.";
                case AdminHelperExitCodes.Unsupported:
                    return "This MacBook, panel, driver, or requested profile is unsupported.";
                case AdminHelperExitCodes.Failed:
                    return "The helper could not complete the requested transaction. "
                        + "No unverified change is treated as successful.";
                case AdminHelperExitCodes.Indeterminate:
                    return "The helper reached an indeterminate transaction boundary. "
                        + "Recovery must reconcile the durable journal before another privileged change.";
                default:
                    return "The helper returned exit code " + exitCode + ".";
            }
        }

    }
}
