using System;
using System.Diagnostics;
using System.Threading;
using MacBookEco.Core;
using MacBookEco.DisplaySafety;
using MacBookEco.Platform.Windows;

namespace MacBookEco.Watchdog
{
    internal static class Program
    {
        private static int Main(string[] arguments)
        {
            if (arguments == null
                || arguments.Length != 2
                || (!string.Equals(
                        arguments[0],
                        "watch",
                        StringComparison.Ordinal)
                    && !string.Equals(
                        arguments[0],
                        "recover",
                        StringComparison.Ordinal)))
            {
                return DisplayWatchdogExitCodes.UsageOrInvalidState;
            }

            bool recoverImmediately = string.Equals(
                arguments[0],
                "recover",
                StringComparison.Ordinal);
            string token = arguments[1];
            DisplayWatchdogSessionState state;
            try
            {
                DisplayWatchdogProtocol.ValidateToken(token);
                state = DisplayWatchdogProtocol.ReadSession(token);
                if (!recoverImmediately)
                {
                    DisplayWatchdogProtocol.WriteReady(token);
                }
            }
            catch
            {
                return DisplayWatchdogExitCodes.UsageOrInvalidState;
            }

            TimeSpan remaining = state.DeadlineUtc - DateTime.UtcNow;
            if (recoverImmediately || remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            Stopwatch deadlineTimer = Stopwatch.StartNew();
            while (deadlineTimer.Elapsed < remaining)
            {
                DisplayWatchdogSignal signal;
                try
                {
                    signal = DisplayWatchdogProtocol.ReadSignal(token);
                }
                catch
                {
                    signal = DisplayWatchdogSignal.Conflict;
                }

                if (signal == DisplayWatchdogSignal.Commit
                    || signal == DisplayWatchdogSignal.Cancel)
                {
                    DisplayWatchdogProtocol.Cleanup(token);
                    return DisplayWatchdogExitCodes.Completed;
                }

                if (signal == DisplayWatchdogSignal.Conflict)
                {
                    break;
                }

                int remainingMilliseconds = (int)Math.Min(
                    100.0,
                    Math.Max(
                        1.0,
                        (remaining - deadlineTimer.Elapsed).TotalMilliseconds));
                Thread.Sleep(remainingMilliseconds);
            }

            bool cleanupCompletedSession = false;
            int finalExitCode;
            try
            {
                using (System.IO.FileStream persistenceLock =
                    DisplayWatchdogProtocol.AcquirePersistenceLock(
                        token,
                        TimeSpan.FromMinutes(2)))
                {
                    // The deadline check performed before taking the lock is
                    // not authoritative. The tray may have persisted and
                    // committed while this process was waiting.
                    DisplayWatchdogSignal signal =
                        DisplayWatchdogProtocol.ReadSignal(token);
                    if (signal == DisplayWatchdogSignal.Commit
                        || signal == DisplayWatchdogSignal.Cancel)
                    {
                        cleanupCompletedSession = true;
                        finalExitCode = DisplayWatchdogExitCodes.Completed;
                    }
                    else if (signal == DisplayWatchdogSignal.Conflict)
                    {
                        // Conflicting markers are not a safe basis for a
                        // display mutation. Keep the recovery state instead
                        // of guessing which terminal signal won.
                        finalExitCode = DisplayWatchdogExitCodes.RollbackFailed;
                    }
                    else
                    {
                        // A rollback marker is durable recovery intent, not
                        // evidence that a prior watchdog completed the mode
                        // restore. A restarted watchdog must re-resolve,
                        // restore and verify it while holding this lock.
                        bool rollbackMarkerPublished =
                            signal == DisplayWatchdogSignal.Rollback;
                        try
                        {
                            // Publish the winner while still owning the lock.
                            // A resumed tray must observe this marker and may
                            // not persist its target mode afterwards.
                            DisplayWatchdogProtocol.WriteRollback(token);
                            rollbackMarkerPublished = true;
                        }
                        catch
                        {
                        }

                        // The state record carries only durable monitor
                        // identity. Resolve a fresh endpoint immediately
                        // before mutation: a persisted DISPLAYn could have
                        // been renumbered to a different display.
                        StableDisplayTargetResolver resolver =
                            new StableDisplayTargetResolver();
                        StableDisplayTarget target = null;
                        try
                        {
                            target = resolver.ResolveActive(
                                state.TargetIdentity);
                        }
                        catch
                        {
                            // Keep the session and rollback marker for
                            // recovery diagnostics. No mode API was called.
                        }

                        if (target == null)
                        {
                            finalExitCode = DisplayWatchdogExitCodes.RollbackTargetUnresolved;
                        }
                        else
                        {
                            DisplayModeService displayModes =
                                new DisplayModeService();
                            DisplayModeOperationResult result =
                                displayModes.PersistExactMode(
                                    target.Endpoint.GdiDeviceName,
                                    state.OriginalMode);

                            // Re-resolve after the native call and compare the
                            // complete persisted key, including CCD's exact
                            // rational refresh. A topology change after lookup is
                            // never assumed to have restored the intended panel.
                            StableDisplayTarget verifiedTarget = null;
                            try
                            {
                                verifiedTarget = resolver.ResolveActive(
                                    state.TargetIdentity);
                            }
                            catch
                            {
                                // The native call completed, but the live
                                // target can no longer be proven. Retain the
                                // session for manual recovery rather than
                                // claiming that rollback succeeded.
                            }

                            if (verifiedTarget == null)
                            {
                                finalExitCode = DisplayWatchdogExitCodes.RollbackTargetUnresolved;
                            }
                            else
                            {
                                bool restored = result.Succeeded
                                    && state.OriginalMode.Equals(
                                        displayModes.GetCurrentModeKey(
                                            verifiedTarget.Endpoint.GdiDeviceName,
                                            verifiedTarget.RefreshRateNumerator,
                                            verifiedTarget.RefreshRateDenominator));
                                finalExitCode = restored
                                    && rollbackMarkerPublished
                                    ? DisplayWatchdogExitCodes.RollbackPerformed
                                    : DisplayWatchdogExitCodes.RollbackFailed;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Leave the session files in place for post-mortem inspection.
                return DisplayWatchdogExitCodes.RollbackFailed;
            }

            if (cleanupCompletedSession)
            {
                DisplayWatchdogProtocol.Cleanup(token);
            }

            return finalExitCode;
        }
    }
}
