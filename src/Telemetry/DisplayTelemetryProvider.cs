using System;
using System.Runtime.InteropServices;
using MacBookEco.Core;
using MacBookEco.Platform.Windows;

namespace MacBookEco.Telemetry
{
    /// <summary>
    /// Samples only the exact active internal target resolved through the same
    /// durable identity path as display actions. It never substitutes Windows'
    /// primary monitor when topology is ambiguous or unavailable.
    /// </summary>
    public sealed class DisplayTelemetryProvider : IDisplayTelemetryProvider
    {
        /// <summary>
        /// How long a resolved target may be reused.
        ///
        /// Resolving costs a full CCD query, a SetupAPI enumeration of every
        /// present monitor, a registry EDID read and a SHA-256, and this
        /// provider is sampled every two seconds while the dashboard is open.
        /// Paying that each time contradicts the rule that monitoring must not
        /// materially change what it measures.
        ///
        /// Staleness is bounded and harmless here: the worst case is a briefly
        /// wrong read-only readout after a topology change, and the cache is
        /// dropped immediately when the cached endpoint stops answering. No
        /// mutation trusts this cache. Display actions and the watchdog
        /// re-resolve the panel themselves before they touch anything.
        /// </summary>
        private static readonly TimeSpan TargetCacheLifetime =
            TimeSpan.FromSeconds(30.0);

        private readonly InternalDisplayTargetResolver _targetResolver;
        private readonly object _sync = new object();
        private ResolvedMonitorTarget _cachedTarget;
        private DateTime _cachedAtUtc;

        public DisplayTelemetryProvider()
            : this(new InternalDisplayTargetResolver())
        {
        }

        internal DisplayTelemetryProvider(
            InternalDisplayTargetResolver targetResolver)
        {
            if (targetResolver == null)
            {
                throw new ArgumentNullException(nameof(targetResolver));
            }

            _targetResolver = targetResolver;
        }

        public DisplayTelemetry Capture()
        {
            try
            {
                ResolvedMonitorTarget target = GetTarget(false);
                DisplayTelemetry sample = TrySample(target);
                if (sample != null)
                {
                    return sample;
                }

                // The cached endpoint stopped answering, which is what a
                // topology change looks like from here. Resolve once more
                // before reporting the display as unavailable.
                target = GetTarget(true);
                return TrySample(target)
                    ?? DisplayTelemetry.Unavailable(
                        "EnumDisplaySettingsEx failed for the verified internal "
                        + "display with error "
                        + Marshal.GetLastWin32Error()
                        + ".");
            }
            catch (Exception exception)
            {
                InvalidateTarget();
                return DisplayTelemetry.Unavailable(
                    "The internal display target is unavailable: "
                        + exception.Message);
            }
        }

        private ResolvedMonitorTarget GetTarget(bool forceResolve)
        {
            lock (_sync)
            {
                bool expired =
                    DateTime.UtcNow - _cachedAtUtc >= TargetCacheLifetime;
                if (!forceResolve && _cachedTarget != null && !expired)
                {
                    return _cachedTarget;
                }

                _cachedTarget = _targetResolver.ResolveActive();
                _cachedAtUtc = DateTime.UtcNow;
                return _cachedTarget;
            }
        }

        private void InvalidateTarget()
        {
            lock (_sync)
            {
                _cachedTarget = null;
            }
        }

        private DisplayTelemetry TrySample(ResolvedMonitorTarget target)
        {
            if (target.Endpoint == null
                || string.IsNullOrWhiteSpace(target.Endpoint.GdiDeviceName))
            {
                InvalidateTarget();
                return DisplayTelemetry.Unavailable(
                    "The verified internal display has no current GDI endpoint.");
            }

            DEVMODE mode = DEVMODE.Create();
            if (!DisplayModeNativeMethods.EnumDisplaySettingsEx(
                target.Endpoint.GdiDeviceName,
                DisplayModeNativeMethods.ENUM_CURRENT_SETTINGS,
                ref mode,
                0))
            {
                InvalidateTarget();
                return null;
            }

            double? refreshRate = mode.DisplayFrequency <= 1
                ? (double?)null
                : mode.DisplayFrequency;
            return new DisplayTelemetry(
                TelemetryAvailability.Available,
                target.Endpoint.GdiDeviceName,
                mode.PelsWidth,
                mode.PelsHeight,
                refreshRate,
                "Current internal CCD display mode (read-only).",
                "Internal",
                new EdidBaseBlock(
                    target.BaseEdid).NormalizedSignature.ToString());
        }
    }
}
