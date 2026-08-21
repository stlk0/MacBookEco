using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using MacBookEco.Core;

namespace MacBookEco.Platform.Windows
{
    /// <summary>
    /// Switches only modes already enumerated by the display driver. It never
    /// synthesizes timings. Use BeginTemporaryMode for the first Eco-mode test so
    /// an unconfirmed mode is restored automatically.
    /// </summary>
    public sealed class DisplayModeService
    {
        /// <summary>
        /// Builds a full current-mode key using the exact refresh rational
        /// read from CCD for this already-resolved display endpoint.
        /// </summary>
        public WindowsDisplayMode GetCurrentMode(
            string gdiDeviceName,
            uint refreshRateNumerator,
            uint refreshRateDenominator)
        {
            DEVMODE mode = ReadCurrentNativeMode(gdiDeviceName);
            return WindowsDisplayMode.FromNative(
                mode,
                refreshRateNumerator,
                refreshRateDenominator);
        }

        public DisplayModeKey GetCurrentModeKey(
            string gdiDeviceName,
            uint refreshRateNumerator,
            uint refreshRateDenominator)
        {
            return GetCurrentMode(
                gdiDeviceName,
                refreshRateNumerator,
                refreshRateDenominator).Key;
        }

        /// <summary>
        /// Applies a temporary, driver-enumerated exact key.  The caller may
        /// provide a fresh GDI name obtained from stable target resolution;
        /// a stored DISPLAYn is never required for rollback.
        /// </summary>
        public DisplayModeOperationResult ApplyTemporary(
            string gdiDeviceName,
            DisplayModeKey targetMode)
        {
            DEVMODE mode = FindExactMode(gdiDeviceName, targetMode);
            EnsureModeTestSucceeds(gdiDeviceName, ref mode);
            int result = DisplayModeNativeMethods.ChangeDisplaySettingsEx(
                gdiDeviceName,
                ref mode,
                IntPtr.Zero,
                0,
                IntPtr.Zero);
            DisplayModeOperationResult operation =
                DisplayModeOperationResult.FromNative(result);
            if (operation.Succeeded)
            {
                EnsureCurrentModeMatches(gdiDeviceName, targetMode);
            }

            return operation;
        }

        /// <summary>
        /// Persists only a driver-enumerated mode whose complete DEVMODE
        /// configuration matches both the live current mode and target key;
        /// the target may differ solely in refresh rate.
        /// </summary>
        public DisplayModeOperationResult PersistExactMode(
            string gdiDeviceName,
            DisplayModeKey targetMode)
        {
            DEVMODE mode = FindExactMode(gdiDeviceName, targetMode);
            EnsureModeTestSucceeds(gdiDeviceName, ref mode);
            int result = DisplayModeNativeMethods.ChangeDisplaySettingsEx(
                gdiDeviceName,
                ref mode,
                IntPtr.Zero,
                DisplayModeNativeMethods.CDS_UPDATEREGISTRY,
                IntPtr.Zero);
            DisplayModeOperationResult operation =
                DisplayModeOperationResult.FromNative(result);
            if (operation.Succeeded)
            {
                EnsureCurrentModeMatches(gdiDeviceName, targetMode);
            }

            return operation;
        }

        /// <summary>
        /// Reports whether the live driver has exposed a mode that changes
        /// only the current refresh rate. This is the readiness boundary
        /// after installing an EDID override: registry ownership alone does
        /// not mean that the display stack has reinitialized yet.
        /// </summary>
        public bool IsExactRefreshOnlyModeAvailable(
            string gdiDeviceName,
            int refreshRate)
        {
            ValidateRefreshRate(refreshRate);
            DEVMODE current = ReadCurrentNativeMode(gdiDeviceName);
            DEVMODE ignored;
            return TryFindExactMode(
                gdiDeviceName,
                ToDisplayModeKey(current),
                refreshRate,
                out ignored);
        }

        /// <summary>
        /// Begins a temporary transition only after the caller has captured a
        /// durable current key.  The endpoint callback is invoked again for
        /// confirm, rollback and timeout, so a renumbered DISPLAYn can never
        /// receive a mode from the previous topology.
        /// </summary>
        public DisplayModeLease BeginTemporaryMode(
            string gdiDeviceName,
            DisplayModeKey originalMode,
            DisplayModeKey targetMode,
            TimeSpan confirmationTimeout,
            Func<string> resolveCurrentDeviceName)
        {
            if (originalMode == null)
            {
                throw new ArgumentNullException(nameof(originalMode));
            }

            if (targetMode == null)
            {
                throw new ArgumentNullException(nameof(targetMode));
            }

            if (resolveCurrentDeviceName == null)
            {
                throw new ArgumentNullException(nameof(resolveCurrentDeviceName));
            }

            if (!targetMode.HasSameDisplayConfiguration(originalMode))
            {
                throw new ArgumentException(
                    "A temporary display target may differ from the original only by refresh rate.",
                    nameof(targetMode));
            }

            DEVMODE original = ReadCurrentNativeMode(gdiDeviceName);
            DisplayModeKey liveOriginal = ToDisplayModeKey(original);
            if (!liveOriginal.HasSameDisplayConfiguration(originalMode) ||
                liveOriginal.RefreshRate != originalMode.RefreshRate)
            {
                throw new InvalidOperationException(
                    "The current display mode changed before the temporary transition began.");
            }

            DEVMODE target = FindExactMode(gdiDeviceName, targetMode);
            return BeginTemporaryMode(
                gdiDeviceName,
                original,
                target,
                originalMode,
                targetMode,
                resolveCurrentDeviceName,
                confirmationTimeout);
        }

        internal static WindowsDisplayMode ReadCurrentMode(string gdiDeviceName)
        {
            DEVMODE mode = ReadCurrentNativeMode(gdiDeviceName);
            return WindowsDisplayMode.FromNative(mode);
        }

        internal static DEVMODE ReadCurrentNativeMode(string gdiDeviceName)
        {
            ValidateDeviceName(gdiDeviceName);
            DEVMODE mode = DEVMODE.Create();
            if (!DisplayModeNativeMethods.EnumDisplaySettingsEx(
                gdiDeviceName,
                DisplayModeNativeMethods.ENUM_CURRENT_SETTINGS,
                ref mode,
                0))
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "EnumDisplaySettingsEx could not read the current mode.");
            return mode;
        }

        private static DEVMODE FindExactMode(
            string gdiDeviceName,
            DisplayModeKey targetMode)
        {
            if (targetMode == null)
                throw new ArgumentNullException(nameof(targetMode));

            ValidateRefreshRate(targetMode.RefreshRate);
            DEVMODE current = ReadCurrentNativeMode(gdiDeviceName);
            DisplayModeKey currentKey = ToDisplayModeKey(current);
            DEVMODE match;
            if (targetMode.HasSameDisplayConfiguration(currentKey)
                && TryFindExactMode(
                    gdiDeviceName,
                    currentKey,
                    targetMode.RefreshRate,
                    out match))
            {
                return match;
            }

            throw new InvalidOperationException(
                "The driver does not enumerate an exact refresh-only " +
                current.PelsWidth +
                "x" +
                current.PelsHeight +
                " " +
                current.BitsPerPel +
                " bpp @ " +
                targetMode.RefreshRate +
                " Hz mode for " +
                gdiDeviceName +
                ".");
        }

        private static bool TryFindExactMode(
            string gdiDeviceName,
            DisplayModeKey currentMode,
            int refreshRate,
            out DEVMODE match)
        {
            int modeIndex = 0;
            while (true)
            {
                DEVMODE candidate = DEVMODE.Create();
                if (!DisplayModeNativeMethods.EnumDisplaySettingsEx(
                    gdiDeviceName,
                    modeIndex,
                    ref candidate,
                    0))
                {
                    match = DEVMODE.Create();
                    return false;
                }

                DisplayModeKey candidateKey = ToDisplayModeKey(candidate);
                if (DisplayModeSelectionPolicy.IsExactRefreshOnlyCandidate(
                    currentMode,
                    candidateKey,
                    refreshRate))
                {
                    match = candidate;
                    return true;
                }

                modeIndex++;
            }
        }

        private static DisplayModeKey ToDisplayModeKey(DEVMODE mode)
        {
            uint refreshRateNumerator = mode.DisplayFrequency > 0
                ? (uint)mode.DisplayFrequency
                : 1;
            return new DisplayModeKey(
                mode.PelsWidth,
                mode.PelsHeight,
                mode.BitsPerPel,
                mode.DisplayFrequency,
                mode.DisplayOrientation,
                mode.DisplayFixedOutput,
                mode.DisplayFlags,
                refreshRateNumerator,
                1);
        }

        private DisplayModeLease BeginTemporaryMode(
            string gdiDeviceName,
            DEVMODE original,
            DEVMODE target,
            DisplayModeKey originalMode,
            DisplayModeKey targetMode,
            Func<string> resolveCurrentDeviceName,
            TimeSpan confirmationTimeout)
        {
            ValidateConfirmationTimeout(confirmationTimeout);
            EnsureModeTestSucceeds(gdiDeviceName, ref target);

            int result = DisplayModeNativeMethods.ChangeDisplaySettingsEx(
                gdiDeviceName,
                ref target,
                IntPtr.Zero,
                0,
                IntPtr.Zero);
            if (result != DisplayModeNativeMethods.DISP_CHANGE_SUCCESSFUL)
                throw new DisplayModeException(
                    "Windows rejected the temporary display mode.",
                    result);

            EnsureCurrentModeMatches(gdiDeviceName, targetMode);

            return new DisplayModeLease(
                this,
                originalMode,
                targetMode,
                resolveCurrentDeviceName,
                confirmationTimeout);
        }

        internal static void EnsureCurrentModeMatches(
            string gdiDeviceName,
            DisplayModeKey expected)
        {
            if (expected == null)
            {
                throw new ArgumentNullException(nameof(expected));
            }

            DisplayModeKey actual = ToDisplayModeKey(
                ReadCurrentNativeMode(gdiDeviceName));
            if (!actual.HasSameDisplayConfiguration(expected) ||
                actual.RefreshRate != expected.RefreshRate)
            {
                throw new InvalidOperationException(
                    "Windows did not read back the exact display-mode configuration.");
            }
        }

        private static void EnsureModeTestSucceeds(
            string gdiDeviceName,
            ref DEVMODE mode)
        {
            int result = DisplayModeNativeMethods.ChangeDisplaySettingsEx(
                gdiDeviceName,
                ref mode,
                IntPtr.Zero,
                DisplayModeNativeMethods.CDS_TEST,
                IntPtr.Zero);
            if (result != DisplayModeNativeMethods.DISP_CHANGE_SUCCESSFUL)
                throw new DisplayModeException(
                    "CDS_TEST rejected the requested display mode.",
                    result);
        }

        private static void ValidateDeviceName(string gdiDeviceName)
        {
            if (string.IsNullOrWhiteSpace(gdiDeviceName) ||
                !gdiDeviceName.StartsWith(
                    @"\\.\DISPLAY",
                    StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    "A GDI display name discovered through CCD is required.",
                    nameof(gdiDeviceName));
        }

        private static void ValidateRefreshRate(int refreshRate)
        {
            if (!DisplayModeSelectionPolicy.IsReviewedRefreshRate(refreshRate))
                throw new ArgumentOutOfRangeException(
                    nameof(refreshRate),
                    "Only the reviewed 48/58 Hz and native 60 Hz transitions are permitted.");
        }

        private static void ValidateConfirmationTimeout(TimeSpan confirmationTimeout)
        {
            if (confirmationTimeout < TimeSpan.FromSeconds(5) ||
                confirmationTimeout > TimeSpan.FromMinutes(2))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(confirmationTimeout),
                    "The confirmation timeout must be between 5 seconds and 2 minutes.");
            }
        }
    }

    public sealed class WindowsDisplayMode
    {
        private DEVMODE nativeMode;
        private DisplayModeKey key;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int BitsPerPixel { get; private set; }
        public int RefreshRate { get; private set; }
        public int Orientation { get; private set; }

        /// <summary>
        /// A DEVMODE-backed key using the integer display frequency as a
        /// fallback rational.  Call ToKey with CCD's exact rational when a
        /// persisted recovery record requires it.
        /// </summary>
        public DisplayModeKey Key
        {
            get
            {
                if (key == null)
                {
                    throw new InvalidOperationException(
                        "The display driver did not report a usable integer refresh rate. " +
                        "Supply the verified CCD refresh rational with ToKey.");
                }

                return key;
            }
        }

        internal static WindowsDisplayMode FromNative(
            DEVMODE mode)
        {
            WindowsDisplayMode result = new WindowsDisplayMode();
            result.Width = mode.PelsWidth;
            result.Height = mode.PelsHeight;
            result.BitsPerPixel = mode.BitsPerPel;
            result.RefreshRate = mode.DisplayFrequency;
            result.Orientation = mode.DisplayOrientation;
            result.nativeMode = mode;
            if (mode.DisplayFrequency > 0)
            {
                result.key = result.ToKey((uint)mode.DisplayFrequency, 1);
            }
            return result;
        }

        internal static WindowsDisplayMode FromNative(
            DEVMODE mode,
            uint refreshRateNumerator,
            uint refreshRateDenominator)
        {
            WindowsDisplayMode result = FromNative(mode);
            result.key = result.ToKey(
                refreshRateNumerator,
                refreshRateDenominator);
            return result;
        }

        public DisplayModeKey ToKey(
            uint refreshRateNumerator,
            uint refreshRateDenominator)
        {
            return new DisplayModeKey(
                Width,
                Height,
                BitsPerPixel,
                RefreshRate,
                Orientation,
                nativeMode.DisplayFixedOutput,
                nativeMode.DisplayFlags,
                refreshRateNumerator,
                refreshRateDenominator);
        }

        // Diagnostics print this value directly. Without the override the
        // Admin "diagnose" command and the platform diagnostics report the
        // type name instead of the mode.
        public override string ToString()
        {
            return Width + "x" + Height + " @ " + RefreshRate + " Hz";
        }
    }

    public sealed class DisplayModeOperationResult
    {
        public bool Succeeded { get; private set; }
        internal int NativeResult { get; private set; }

        internal static DisplayModeOperationResult FromNative(int result)
        {
            DisplayModeOperationResult operation = new DisplayModeOperationResult();
            operation.NativeResult = result;
            operation.Succeeded = result == DisplayModeNativeMethods.DISP_CHANGE_SUCCESSFUL;
            return operation;
        }
    }

    public sealed class DisplayModeException : InvalidOperationException
    {
        public int NativeResult { get; private set; }

        public DisplayModeException(string message, int nativeResult)
            : base(message + " Native result: " + nativeResult + ".")
        {
            NativeResult = nativeResult;
        }
    }

    /// <summary>
    /// A temporary display-mode transaction. Unless ConfirmAndPersist succeeds,
    /// Dispose or timeout restores the exact mode captured before the test.
    /// </summary>
    public sealed class DisplayModeLease : IDisposable
    {
        private readonly object gate = new object();
        private readonly Func<
            string,
            DisplayModeKey,
            DisplayModeOperationResult> applyTemporary;
        private readonly Func<
            string,
            DisplayModeKey,
            DisplayModeOperationResult> persistExactMode;
        private readonly DisplayModeKey originalMode;
        private readonly DisplayModeKey targetMode;
        private readonly Func<string> resolveCurrentDeviceName;
        private Timer timer;
        private bool completed;

        internal DisplayModeLease(
            DisplayModeService service,
            DisplayModeKey originalMode,
            DisplayModeKey targetMode,
            Func<string> resolveCurrentDeviceName,
            TimeSpan timeout)
            : this(
                service == null
                    ? null
                    : new Func<
                        string,
                        DisplayModeKey,
                        DisplayModeOperationResult>(service.ApplyTemporary),
                service == null
                    ? null
                    : new Func<
                        string,
                        DisplayModeKey,
                        DisplayModeOperationResult>(service.PersistExactMode),
                originalMode,
                targetMode,
                resolveCurrentDeviceName,
                timeout)
        {
        }

        internal DisplayModeLease(
            Func<
                string,
                DisplayModeKey,
                DisplayModeOperationResult> applyTemporary,
            Func<
                string,
                DisplayModeKey,
                DisplayModeOperationResult> persistExactMode,
            DisplayModeKey originalMode,
            DisplayModeKey targetMode,
            Func<string> resolveCurrentDeviceName,
            TimeSpan timeout)
        {
            if (applyTemporary == null)
            {
                throw new ArgumentNullException(nameof(applyTemporary));
            }

            if (persistExactMode == null)
            {
                throw new ArgumentNullException(nameof(persistExactMode));
            }

            if (originalMode == null)
            {
                throw new ArgumentNullException(nameof(originalMode));
            }

            if (targetMode == null)
            {
                throw new ArgumentNullException(nameof(targetMode));
            }

            if (resolveCurrentDeviceName == null)
            {
                throw new ArgumentNullException(nameof(resolveCurrentDeviceName));
            }

            this.applyTemporary = applyTemporary;
            this.persistExactMode = persistExactMode;
            this.originalMode = originalMode;
            this.targetMode = targetMode;
            this.resolveCurrentDeviceName = resolveCurrentDeviceName;
            timer = new Timer(OnTimeout, null, timeout, Timeout.InfiniteTimeSpan);
        }

        public bool IsCompleted
        {
            get
            {
                lock (gate)
                    return completed;
            }
        }

        public void ConfirmAndPersist()
        {
            lock (gate)
            {
                if (completed)
                    throw new InvalidOperationException(
                        "The temporary display-mode transaction is already complete.");

                string gdiDeviceName = ResolveCurrentDeviceName();
                DisplayModeOperationResult result = persistExactMode(
                    gdiDeviceName,
                    targetMode);
                if (!result.Succeeded)
                {
                    throw new DisplayModeException(
                        "Windows could not persist the confirmed display mode.",
                        result.NativeResult);
                }

                completed = true;
                DisposeTimer();
            }
        }

        public void Rollback()
        {
            lock (gate)
            {
                if (completed)
                    return;

                try
                {
                    string gdiDeviceName = ResolveCurrentDeviceName();
                    DisplayModeOperationResult result = applyTemporary(
                        gdiDeviceName,
                        originalMode);
                    if (!result.Succeeded)
                    {
                        throw new DisplayModeException(
                            "Windows could not restore the previous display mode.",
                            result.NativeResult);
                    }

                    completed = true;
                    DisposeTimer();
                }
                catch
                {
                    DisposeTimer();
                    throw;
                }
            }
        }

        public void Dispose()
        {
            try
            {
                Rollback();
            }
            catch
            {
                // Dispose must not hide the original application exception.
                // Call Rollback explicitly when the caller needs the error.
            }
        }

        private void OnTimeout(object state)
        {
            try
            {
                Rollback();
            }
            catch
            {
                // A timeout rollback cannot report through the original caller.
            }
        }

        private void DisposeTimer()
        {
            Timer current = timer;
            timer = null;
            if (current != null)
                current.Dispose();
        }

        private string ResolveCurrentDeviceName()
        {
            string resolved = resolveCurrentDeviceName();
            if (string.IsNullOrWhiteSpace(resolved))
            {
                throw new InvalidOperationException(
                    "The current display endpoint could not be re-resolved.");
            }

            return resolved;
        }
    }
}
