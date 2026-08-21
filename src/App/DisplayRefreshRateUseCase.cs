using System;
using System.IO;
using MacBookEco.Core;
using MacBookEco.Platform.Windows;

namespace MacBookEco.App
{
    /// <summary>
    /// Executes reversible, watchdog-backed 48/58/60 Hz mode transitions.
    /// </summary>
    internal sealed class DisplayRefreshRateUseCase
    {
        private const int ConfirmationSeconds = 20;
        private const int WatchdogTimeoutSeconds = 30;
        private const string EmbeddedWatchdogResourceName =
            "MacBookEco.Watchdog.exe";

        private readonly DisplayRefreshRateValidator _validator;
        private readonly DisplayModeService _displayModes;
        private readonly string _watchdogExecutable;
        private readonly EmbeddedHelperVerifier _helperVerifier;

        public DisplayRefreshRateUseCase(
            DisplayRefreshRateValidator validator,
            DisplayModeService displayModes,
            string watchdogExecutable,
            EmbeddedHelperVerifier helperVerifier)
        {
            if (validator == null)
            {
                throw new ArgumentNullException(nameof(validator));
            }

            if (displayModes == null)
            {
                throw new ArgumentNullException(nameof(displayModes));
            }

            if (string.IsNullOrWhiteSpace(watchdogExecutable))
            {
                throw new ArgumentException(
                    "A Watchdog helper path is required.",
                    nameof(watchdogExecutable));
            }

            if (helperVerifier == null)
            {
                throw new ArgumentNullException(nameof(helperVerifier));
            }

            _validator = validator;
            _displayModes = displayModes;
            _watchdogExecutable = watchdogExecutable;
            _helperVerifier = helperVerifier;
        }

        public OptimizationActionResult SetRefreshRate(
            int refreshRateHz,
            Func<DisplayModeConfirmationRequest, DisplayModeConfirmationDecision>
                confirmation)
        {
            if (!DisplayModeSelectionPolicy.IsReviewedRefreshRate(refreshRateHz))
            {
                return OptimizationActionResult.Unsupported(
                    OperationCode.InvalidRequest,
                    "Only the reviewed 48/58 Hz and native 60 Hz modes are allowed.");
            }

            try
            {
                StableDisplayTarget displayTarget;
                OptimizationActionResult validation = _validator.Validate(
                    refreshRateHz,
                    out displayTarget);
                if (validation != null)
                {
                    return validation;
                }

                WindowsDisplayMode originalMode = _displayModes.GetCurrentMode(
                    displayTarget.Endpoint.GdiDeviceName,
                    displayTarget.RefreshRateNumerator,
                    displayTarget.RefreshRateDenominator);
                DisplayModeKey originalModeKey = originalMode.Key;
                if (!DisplayModeSelectionPolicy.IsReviewedRefreshRate(
                        originalMode.RefreshRate))
                {
                    return OptimizationActionResult.Unsupported(
                        OperationCode.UnsupportedCapability,
                        "The current refresh rate is not a reviewed 48/58/60 Hz "
                        + "watchdog recovery target.");
                }

                if (originalMode.RefreshRate == refreshRateHz)
                {
                    return OptimizationActionResult.Successful(
                        "The internal display is already "
                        + originalMode.RefreshRate
                        + " Hz.",
                        OperationCode.None,
                        false);
                }

                if (!_displayModes.IsExactRefreshOnlyModeAvailable(
                    displayTarget.Endpoint.GdiDeviceName,
                    refreshRateHz))
                {
                    return OptimizationActionResult.Unsupported(
                        OperationCode.UnsupportedCapability,
                        DisplayModeSelectionPolicy.IsEcoRefreshRate(refreshRateHz)
                            ? "Windows has not exposed the requested Eco mode yet. "
                                + "Restart Windows after installing or repairing "
                                + "Eco display support."
                            : "Windows has not exposed an exact native 60 Hz "
                                + "recovery mode for the current display settings.");
                }

                if (!File.Exists(_watchdogExecutable))
                {
                    return OptimizationActionResult.Failed(
                        OperationCode.HelperMissing,
                        "MacBookEco.Watchdog.exe is missing. No temporary display "
                        + "mode was applied.",
                        string.Empty);
                }

                using (FileStream verifiedWatchdog = OpenVerifiedWatchdog())
                using (DisplayWatchdogClient watchdog = DisplayWatchdogClient.Start(
                    _watchdogExecutable,
                    displayTarget.Identity,
                    originalModeKey,
                    TimeSpan.FromSeconds(WatchdogTimeoutSeconds)))
                {
                    DisplayModeLease lease = null;
                    bool persistenceAttempted = false;
                    try
                    {
                        DisplayModeKey targetMode = CreateRefreshOnlyTarget(
                            originalModeKey,
                            refreshRateHz);
                        lease = _displayModes.BeginTemporaryMode(
                            displayTarget.Endpoint.GdiDeviceName,
                            originalModeKey,
                            targetMode,
                            TimeSpan.FromSeconds(ConfirmationSeconds),
                            delegate {
                                return _validator.ResolveActive(
                                    displayTarget.Identity).Endpoint.GdiDeviceName;
                            });
                        if (refreshRateHz == 58)
                        {
                            Verify58HzSignal(displayTarget.Identity);
                        }

                        DisplayModeConfirmationDecision decision =
                            confirmation == null
                                ? DisplayModeConfirmationDecision.Revert
                                : confirmation(new DisplayModeConfirmationRequest(
                                    refreshRateHz,
                                    TimeSpan.FromSeconds(ConfirmationSeconds)));
                        if (decision != DisplayModeConfirmationDecision.Keep)
                        {
                            bool restored = RestoreAndVerifyOriginal(
                                lease,
                                displayTarget.Identity,
                                originalModeKey,
                                false);
                            if (restored)
                            {
                                TryCancelWatchdog(watchdog);
                            }

                            return restored
                                ? OptimizationActionResult.Cancelled(
                                    OperationCode.DisplayReverted,
                                    "The previous display mode was restored.")
                                : OptimizationActionResult.Indeterminate(
                                    OperationCode.DisplayRollbackUnverified,
                                    "The in-process rollback was not verified. "
                                        + "The independent watchdog remains armed.",
                                    string.Empty);
                        }

                        if (lease.IsCompleted)
                        {
                            bool restored = RestoreAndVerifyOriginal(
                                lease,
                                displayTarget.Identity,
                                originalModeKey,
                                false);
                            if (restored)
                            {
                                TryCancelWatchdog(watchdog);
                            }

                            return restored
                                ? OptimizationActionResult.Failed(
                                    OperationCode.DisplayConfirmationTimedOut,
                                    "The previous display mode was restored before "
                                        + "confirmation completed.",
                                    string.Empty)
                                : OptimizationActionResult.Indeterminate(
                                    OperationCode.DisplayRollbackUnverified,
                                    "Rollback was not verified. The independent "
                                        + "watchdog remains armed.",
                                    string.Empty);
                        }

                        DisplayWatchdogPersistenceGuard persistenceGuard = null;
                        PersistWithWatchdog(
                            delegate
                            {
                                persistenceGuard =
                                    watchdog.AcquirePersistenceGuard();
                                return persistenceGuard;
                            },
                            lease.ConfirmAndPersist,
                            delegate { persistenceGuard.Commit(); },
                            ref persistenceAttempted);

                        return CompleteConfirmedTransition(
                            refreshRateHz,
                            originalModeKey,
                            watchdog.WaitForCommitAcknowledgement,
                            delegate
                            {
                                return ReadCurrentModeForTarget(
                                    displayTarget.Identity,
                                    refreshRateHz == 58);
                            },
                            delegate
                            {
                                return RestoreAndVerifyOriginal(
                                    lease,
                                    displayTarget.Identity,
                                    originalModeKey,
                                    persistenceAttempted);
                            });
                    }
                    catch
                    {
                        bool restored = RestoreAndVerifyOriginal(
                            lease,
                            displayTarget.Identity,
                            originalModeKey,
                            persistenceAttempted);
                        if (restored)
                        {
                            TryCancelWatchdog(watchdog);
                        }

                        throw;
                    }
                    finally
                    {
                        if (lease != null)
                        {
                            lease.Dispose();
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                return OptimizationActionResult.Faulted("Display mode change failed", exception);
            }
        }

        internal bool IsRefreshRateModeAvailable(int refreshRateHz)
        {
            if (!DisplayModeSelectionPolicy.IsEcoRefreshRate(refreshRateHz))
            {
                return false;
            }

            try
            {
                StableDisplayTarget displayTarget;
                OptimizationActionResult validation = _validator.Validate(
                    refreshRateHz,
                    out displayTarget);
                return validation == null
                    && _displayModes.IsExactRefreshOnlyModeAvailable(
                        displayTarget.Endpoint.GdiDeviceName,
                        refreshRateHz);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private FileStream OpenVerifiedWatchdog()
        {
            return _helperVerifier.OpenVerifiedHelper(
                _watchdogExecutable,
                EmbeddedWatchdogResourceName,
                "MacBookEco.Watchdog.exe");
        }

        private WindowsDisplayMode ReadCurrentModeForTarget(
            MonitorIdentity identity)
        {
            return ReadCurrentModeForTarget(identity, false);
        }

        private WindowsDisplayMode ReadCurrentModeForTarget(
            MonitorIdentity identity,
            bool verify58HzSignal)
        {
            StableDisplayTarget target = _validator.ResolveActive(identity);
            if (verify58HzSignal)
            {
                Verify58HzSignal(target);
            }

            return _displayModes.GetCurrentMode(
                target.Endpoint.GdiDeviceName,
                target.RefreshRateNumerator,
                target.RefreshRateDenominator);
        }

        private void Verify58HzSignal(MonitorIdentity identity)
        {
            Verify58HzSignal(_validator.ResolveActive(identity));
        }

        private static void Verify58HzSignal(StableDisplayTarget target)
        {
            if (target == null ||
                !IsExpected58HzSignal(
                    target.PixelRate,
                    target.ActiveWidth,
                    target.ActiveHeight,
                    target.TotalWidth,
                    target.TotalHeight))
            {
                throw new InvalidOperationException(
                    "Windows did not read back the exact reviewed 58 Hz signal timing.");
            }
        }

        internal static bool IsExpected58HzSignal(
            ulong pixelRate,
            uint activeWidth,
            uint activeHeight,
            uint totalWidth,
            uint totalHeight)
        {
            return pixelRate == 373510000UL &&
                activeWidth == 3072U &&
                activeHeight == 1920U &&
                totalWidth == 3152U &&
                totalHeight == 2048U;
        }

        private static DisplayModeKey CreateRefreshOnlyTarget(
            DisplayModeKey original,
            int refreshRateHz)
        {
            if (original == null)
            {
                throw new ArgumentNullException(nameof(original));
            }

            return new DisplayModeKey(
                original.Width,
                original.Height,
                original.BitsPerPixel,
                refreshRateHz,
                original.Orientation,
                original.FixedOutput,
                original.DisplayFlags,
                (uint)refreshRateHz,
                1);
        }

        private bool RestoreAndVerifyOriginal(
            DisplayModeLease lease,
            MonitorIdentity targetIdentity,
            DisplayModeKey originalMode,
            bool forcePersistOriginal)
        {
            return RestoreAndVerifyOriginal(
                lease,
                targetIdentity,
                originalMode,
                forcePersistOriginal,
                ReadCurrentModeForTarget,
                delegate(
                    MonitorIdentity identity,
                    DisplayModeKey mode)
                {
                    StableDisplayTarget target = _validator.ResolveActive(
                        identity);
                    return _displayModes.PersistExactMode(
                        target.Endpoint.GdiDeviceName,
                        mode).Succeeded;
                });
        }

        internal static bool RestoreAndVerifyOriginal(
            DisplayModeLease lease,
            MonitorIdentity targetIdentity,
            DisplayModeKey originalMode,
            bool forcePersistOriginal,
            Func<MonitorIdentity, WindowsDisplayMode> readCurrentMode,
            Func<MonitorIdentity, DisplayModeKey, bool> persistOriginalMode)
        {
            if (targetIdentity == null || originalMode == null)
            {
                return false;
            }

            try
            {
                if (lease != null && !lease.IsCompleted)
                {
                    lease.Rollback();
                }
            }
            catch
            {
            }

            try
            {
                WindowsDisplayMode current = readCurrentMode(
                    targetIdentity);
                if (forcePersistOriginal || !current.Key.Equals(originalMode))
                {
                    if (!persistOriginalMode(targetIdentity, originalMode))
                    {
                        return false;
                    }
                }

                return readCurrentMode(targetIdentity).Key.Equals(
                    originalMode);
            }
            catch
            {
                return false;
            }
        }

        internal static void PersistWithWatchdog(
            Func<IDisposable> acquirePersistenceGuard,
            Action confirmAndPersist,
            Action commitPersistenceGuard,
            ref bool persistenceAttempted)
        {
            if (acquirePersistenceGuard == null)
            {
                throw new ArgumentNullException(nameof(acquirePersistenceGuard));
            }

            if (confirmAndPersist == null)
            {
                throw new ArgumentNullException(nameof(confirmAndPersist));
            }

            if (commitPersistenceGuard == null)
            {
                throw new ArgumentNullException(nameof(commitPersistenceGuard));
            }

            // The guard verifies on construction and throws if the watchdog
            // already won the deadline race, so reaching the body means
            // persistence is still permitted.
            using (IDisposable persistenceGuard = acquirePersistenceGuard())
            {
                if (persistenceGuard == null)
                {
                    throw new InvalidOperationException(
                        "The display watchdog returned no persistence guard.");
                }

                persistenceAttempted = true;
                confirmAndPersist();
                commitPersistenceGuard();
            }
        }

        internal static OptimizationActionResult CompleteConfirmedTransition(
            int refreshRateHz,
            DisplayModeKey originalMode,
            Func<bool> waitForCommitAcknowledgement,
            Func<WindowsDisplayMode> readCurrentMode,
            Func<bool> restoreOriginal)
        {
            if (originalMode == null)
            {
                throw new ArgumentNullException(nameof(originalMode));
            }

            if (waitForCommitAcknowledgement == null)
            {
                throw new ArgumentNullException(
                    nameof(waitForCommitAcknowledgement));
            }

            if (readCurrentMode == null)
            {
                throw new ArgumentNullException(nameof(readCurrentMode));
            }

            if (restoreOriginal == null)
            {
                throw new ArgumentNullException(nameof(restoreOriginal));
            }

            if (!waitForCommitAcknowledgement())
            {
                bool restored = restoreOriginal();
                return restored
                    ? OptimizationActionResult.Failed(
                        OperationCode.DisplayReverted,
                        "The watchdog reached its deadline during confirmation; "
                            + "the previous mode was restored.",
                        string.Empty)
                    : OptimizationActionResult.Indeterminate(
                        OperationCode.DisplayRollbackUnverified,
                        "The watchdog did not acknowledge confirmation and "
                            + "rollback could not be verified.",
                        string.Empty);
            }

            WindowsDisplayMode verified = readCurrentMode();
            if (verified.RefreshRate != refreshRateHz ||
                !verified.Key.HasSameDisplayConfiguration(originalMode))
            {
                bool restored = restoreOriginal();
                return restored
                    ? OptimizationActionResult.Failed(
                        OperationCode.StateVerificationFailed,
                        "Windows did not report the confirmed refresh rate "
                            + "after persistence; the previous mode was restored.",
                        string.Empty)
                    : OptimizationActionResult.Indeterminate(
                        OperationCode.DisplayRollbackUnverified,
                        "Windows did not report the confirmed refresh rate and "
                            + "rollback could not be verified.",
                        string.Empty);
            }

            return OptimizationActionResult.Successful(
                "Internal display is now "
                    + verified.Width
                    + "x"
                    + verified.Height
                    + " @ "
                    + verified.RefreshRate
                    + " Hz.",
                OperationCode.None,
                false);
        }

        // Best effort by contract: the rollback path has already decided what
        // to report, and a watchdog that cannot be disarmed is handled by
        // startup recovery rather than by this result.
        private static void TryCancelWatchdog(DisplayWatchdogClient watchdog)
        {
            try
            {
                watchdog.CancelAndWait();
            }
            catch
            {
            }
        }

    }
}
