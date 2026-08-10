using System;
using System.Threading;

namespace MacBookEco.Telemetry
{
    /// <summary>
    /// Owns the complete telemetry lifecycle.  The one gate serializes timer
    /// callbacks, public captures, visibility changes and disposal so a
    /// provider cannot capture or publish after Dispose has returned.
    /// </summary>
    public sealed class TelemetryService : IDisposable
    {
        private const int HiddenIntervalMilliseconds = 30000;
        private const int DashboardIntervalMilliseconds = 2000;

        private readonly object _lifecycleGate = new object();
        private readonly IBatteryTelemetryProvider _batteryProvider;
        private readonly ICpuTelemetryProvider _cpuProvider;
        private readonly IDisplayTelemetryProvider _displayProvider;
        private readonly IGpuTelemetryProvider _gpuProvider;
        private readonly Timer _timer;

        private bool _started;
        private bool _disposed;
        private bool _dashboardVisible;
        private TelemetrySnapshot _latestSnapshot;

        public TelemetryService(
            IBatteryTelemetryProvider batteryProvider,
            ICpuTelemetryProvider cpuProvider,
            IDisplayTelemetryProvider displayProvider,
            IGpuTelemetryProvider gpuProvider)
        {
            if (batteryProvider == null)
            {
                throw new ArgumentNullException(nameof(batteryProvider));
            }

            if (cpuProvider == null)
            {
                throw new ArgumentNullException(nameof(cpuProvider));
            }

            if (displayProvider == null)
            {
                throw new ArgumentNullException(nameof(displayProvider));
            }

            if (gpuProvider == null)
            {
                throw new ArgumentNullException(nameof(gpuProvider));
            }

            _batteryProvider = batteryProvider;
            _cpuProvider = cpuProvider;
            _displayProvider = displayProvider;
            _gpuProvider = gpuProvider;
            _latestSnapshot = TelemetrySnapshot.Empty();
            _timer = new Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
        }

        public event EventHandler<TelemetrySnapshotEventArgs> SnapshotAvailable;

        public TelemetrySnapshot LatestSnapshot
        {
            get
            {
                lock (_lifecycleGate)
                {
                    return _latestSnapshot;
                }
            }
        }

        public void Start()
        {
            lock (_lifecycleGate)
            {
                ThrowIfDisposed();
                if (_started)
                {
                    return;
                }

                _started = true;
                _timer.Change(0, Timeout.Infinite);
            }
        }

        public void SetDashboardVisible(bool visible)
        {
            lock (_lifecycleGate)
            {
                ThrowIfDisposed();
                if (_dashboardVisible == visible)
                {
                    return;
                }

                _dashboardVisible = visible;
                if (visible)
                {
                    SafeStartGpu();
                }
                else
                {
                    // Stop synchronously: the hidden state must not leave an
                    // ADL session or performance counters polling until the
                    // next timer turn.
                    SafeStopGpu();
                }

                if (_started)
                {
                    _timer.Change(0, Timeout.Infinite);
                }
            }
        }

        public TelemetrySnapshot CaptureOnce(bool dashboardSampling)
        {
            lock (_lifecycleGate)
            {
                ThrowIfDisposed();
                // Visibility is authoritative.  A public caller cannot force
                // GPU polling while the dashboard is hidden.
                return CaptureCore(dashboardSampling && _dashboardVisible);
            }
        }

        public void Dispose()
        {
            lock (_lifecycleGate)
            {
                if (_disposed)
                {
                    return;
                }

                // Acquiring this gate waits for an in-flight timer/public
                // capture and its event callback. Once set, no entry point
                // may begin another capture.
                _disposed = true;
                _started = false;
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                SafeStopGpu();
                _timer.Dispose();
            }
        }

        private void OnTimer(object state)
        {
            lock (_lifecycleGate)
            {
                if (_disposed || !_started)
                {
                    return;
                }

                TelemetrySnapshot snapshot = CaptureCore(_dashboardVisible);
                EventHandler<TelemetrySnapshotEventArgs> handler = SnapshotAvailable;
                if (handler != null && !_disposed)
                {
                    try
                    {
                        // Event delivery stays inside the lifecycle gate.
                        // Dispose therefore waits for it and no notification
                        // can occur after Dispose returns.
                        handler(this, new TelemetrySnapshotEventArgs(snapshot));
                    }
                    catch
                    {
                        // A UI subscriber must not stop the sampling loop.
                    }
                }

                ScheduleNextLocked();
            }
        }

        private TelemetrySnapshot CaptureCore(bool dashboardSampling)
        {
            BatteryTelemetry battery = SafeBatteryCapture();
            DisplayTelemetry display = SafeDisplayCapture();
            CpuTelemetry cpu = dashboardSampling
                ? SafeCpuCapture()
                : CpuTelemetry.Paused();
            GpuTelemetry gpu = dashboardSampling
                ? SafeGpuCapture()
                : GpuTelemetry.Paused();

            TelemetrySnapshot snapshot = new TelemetrySnapshot(
                DateTime.UtcNow,
                battery,
                cpu,
                display,
                gpu,
                dashboardSampling);
            _latestSnapshot = snapshot;
            return snapshot;
        }

        private void ScheduleNextLocked()
        {
            if (_disposed || !_started)
            {
                return;
            }

            _timer.Change(
                _dashboardVisible
                    ? DashboardIntervalMilliseconds
                    : HiddenIntervalMilliseconds,
                Timeout.Infinite);
        }

        private BatteryTelemetry SafeBatteryCapture()
        {
            try
            {
                return _batteryProvider.Capture()
                    ?? BatteryTelemetry.Unavailable("Battery provider returned no result.");
            }
            catch (Exception exception)
            {
                return BatteryTelemetry.Error(exception.Message);
            }
        }

        private CpuTelemetry SafeCpuCapture()
        {
            try
            {
                return _cpuProvider.Capture()
                    ?? CpuTelemetry.Error("CPU provider returned no result.");
            }
            catch (Exception exception)
            {
                return CpuTelemetry.Error(exception.Message);
            }
        }

        private DisplayTelemetry SafeDisplayCapture()
        {
            try
            {
                return _displayProvider.Capture()
                    ?? DisplayTelemetry.Unavailable("Display provider returned no result.");
            }
            catch (Exception exception)
            {
                return DisplayTelemetry.Error(exception.Message);
            }
        }

        private GpuTelemetry SafeGpuCapture()
        {
            try
            {
                return _gpuProvider.Capture()
                    ?? GpuTelemetry.Error("GPU provider returned no result.");
            }
            catch (Exception exception)
            {
                return GpuTelemetry.Error(exception.Message);
            }
        }

        private void SafeStartGpu()
        {
            try
            {
                _gpuProvider.StartMonitoring();
            }
            catch
            {
            }
        }

        private void SafeStopGpu()
        {
            try
            {
                _gpuProvider.StopMonitoring();
            }
            catch
            {
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("TelemetryService");
            }
        }
    }
}
