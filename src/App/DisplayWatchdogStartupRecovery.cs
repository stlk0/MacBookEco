using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using MacBookEco.Core;
using MacBookEco.DisplaySafety;

namespace MacBookEco.App
{
    internal static class DisplayWatchdogStartupRecovery
    {
        private const int RecoveryTimeoutMilliseconds = 15000;
        private const int TotalRecoveryBudgetMilliseconds = 30000;
        private const string EmbeddedWatchdogResourceName =
            "MacBookEco.Watchdog.exe";

        internal static OptimizationActionResult Recover(
            string executablePath,
            EmbeddedHelperVerifier verifier)
        {
            try
            {
                IList<string> tokens =
                    DisplayWatchdogProtocol.ListSessionTokens();
                Stopwatch recoveryBudget = Stopwatch.StartNew();
                return Recover(
                    tokens,
                    !string.IsNullOrWhiteSpace(executablePath) &&
                        File.Exists(executablePath),
                    delegate
                    {
                        if (verifier == null)
                        {
                            throw new ArgumentNullException(nameof(verifier));
                        }

                        return verifier.OpenVerifiedHelper(
                            executablePath,
                            EmbeddedWatchdogResourceName,
                            "MacBookEco.Watchdog.exe");
                    },
                    delegate(string token, int waitMilliseconds)
                    {
                        return RecoverSession(
                            executablePath,
                            token,
                            waitMilliseconds);
                    },
                    DisplayWatchdogProtocol.Cleanup,
                    delegate
                    {
                        return recoveryBudget.ElapsedMilliseconds;
                    });
            }
            catch (Exception exception)
            {
                return InvalidRecoveryResult(exception);
            }
        }

        internal static OptimizationActionResult Recover(
            IList<string> tokens,
            bool executableAvailable,
            Func<IDisposable> openVerifiedWatchdog,
            Func<string, int, string> recoverSession,
            Action<string> cleanupSession,
            Func<long> elapsedMilliseconds)
        {
            try
            {
                if (tokens == null)
                {
                    throw new ArgumentNullException(nameof(tokens));
                }

                if (tokens.Count == 0)
                {
                    return null;
                }

                if (!executableAvailable)
                {
                    return OptimizationActionResult.Indeterminate(
                        OperationCode.DisplayRollbackUnverified,
                        "A stale display recovery session exists, but "
                            + "MacBookEco.Watchdog.exe is missing.",
                        "startup-watchdog-sessions=" + tokens.Count);
                }

                if (openVerifiedWatchdog == null)
                {
                    throw new ArgumentNullException(nameof(openVerifiedWatchdog));
                }

                if (recoverSession == null)
                {
                    throw new ArgumentNullException(nameof(recoverSession));
                }

                if (cleanupSession == null)
                {
                    throw new ArgumentNullException(nameof(cleanupSession));
                }

                if (elapsedMilliseconds == null)
                {
                    throw new ArgumentNullException(nameof(elapsedMilliseconds));
                }

                int recovered = 0;
                List<string> failures = new List<string>();
                using (IDisposable verifiedWatchdog = openVerifiedWatchdog())
                {
                    int index;
                    for (index = 0; index < tokens.Count; index++)
                    {
                        int remainingBudget =
                            TotalRecoveryBudgetMilliseconds
                            - (int)Math.Min(
                                int.MaxValue,
                                elapsedMilliseconds());
                        if (remainingBudget <= 0)
                        {
                            failures.Add("budget-exhausted");
                            continue;
                        }

                        int waitMilliseconds = Math.Min(
                            RecoveryTimeoutMilliseconds,
                            remainingBudget);
                        string failure = recoverSession(
                            tokens[index],
                            waitMilliseconds);
                        if (failure == null)
                        {
                            cleanupSession(tokens[index]);
                            recovered++;
                        }
                        else
                        {
                            failures.Add(failure);
                        }
                    }
                }

                if (failures.Count == 0)
                {
                    return OptimizationActionResult.Successful(
                        recovered == 1
                            ? "Recovered one stale display safety session and "
                                + "verified the original mode."
                            : "Recovered "
                                + recovered
                                + " stale display safety sessions and verified "
                                + "their original modes.",
                        OperationCode.DisplayReverted,
                        false);
                }

                return OptimizationActionResult.Indeterminate(
                    OperationCode.DisplayRollbackUnverified,
                    "Startup recovery could not verify every stale display "
                        + "safety session. Recovery state was retained.",
                    "startup-watchdog-sessions=" + tokens.Count
                        + ";recovered=" + recovered
                        + ";failures=" + string.Join(",", failures.ToArray()));
            }
            catch (Exception exception)
            {
                return InvalidRecoveryResult(exception);
            }
        }

        private static string RecoverSession(
            string executablePath,
            string token,
            int waitMilliseconds)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = executablePath;
            startInfo.Arguments = "recover " + token;
            startInfo.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;

            using (Process process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    return ClassifyProcessResult(false, false, 0);
                }

                bool exited = process.WaitForExit(waitMilliseconds);
                int exitCode = exited ? process.ExitCode : 0;
                return ClassifyProcessResult(true, exited, exitCode);
            }
        }

        internal static string ClassifyProcessResult(
            bool started,
            bool exited,
            int exitCode)
        {
            if (!started)
            {
                return "start-failed";
            }

            if (!exited)
            {
                return "timeout";
            }

            // A performed rollback is as good an outcome as a clean exit:
            // both leave no session to recover.
            return exitCode == DisplayWatchdogExitCodes.Completed ||
                exitCode == DisplayWatchdogExitCodes.RollbackPerformed
                    ? null
                    : "exit-" + exitCode;
        }

        private static OptimizationActionResult InvalidRecoveryResult(
            Exception exception)
        {
            return OptimizationActionResult.Indeterminate(
                OperationCode.DisplayRollbackUnverified,
                "Startup display recovery stopped at an invalid or "
                    + "unverifiable watchdog state.",
                exception.GetType().Name + ": " + exception.Message);
        }
    }
}
