using System;
using System.Globalization;
using System.Text;

namespace MacBookEco.Telemetry
{
    public enum TelemetryAvailability
    {
        Available,
        Unavailable,
        Unsupported,
        Error
    }

    public sealed class BatteryTelemetry
    {
        public BatteryTelemetry(
            TelemetryAvailability availability,
            bool? acOnline,
            bool? charging,
            double? chargePercent,
            string source,
            string detail,
            double? chargeWatts,
            double? dischargeWatts)
        {
            Availability = availability;
            AcOnline = acOnline;
            Charging = charging;
            ChargePercent = chargePercent;
            Source = source ?? string.Empty;
            Detail = detail ?? string.Empty;
            ChargeWatts = chargeWatts;
            DischargeWatts = dischargeWatts;
        }

        public TelemetryAvailability Availability { get; private set; }

        public bool? AcOnline { get; private set; }

        public bool? Charging { get; private set; }

        public double? ChargePercent { get; private set; }

        public string Source { get; private set; }

        public string Detail { get; private set; }

        public double? ChargeWatts { get; private set; }

        public double? DischargeWatts { get; private set; }

        public static BatteryTelemetry Unavailable(string detail)
        {
            return new BatteryTelemetry(
                TelemetryAvailability.Unavailable,
                null,
                null,
                null,
                string.Empty,
                detail,
                null,
                null);
        }

        public static BatteryTelemetry Error(string detail)
        {
            return new BatteryTelemetry(
                TelemetryAvailability.Error,
                null,
                null,
                null,
                string.Empty,
                detail,
                null,
                null);
        }
    }

    public sealed class CpuTelemetry
    {
        public CpuTelemetry(
            TelemetryAvailability availability,
            double? loadPercent,
            double? averageMhz,
            double? maximumMhz,
            double? temperatureCelsius,
            double? powerWatts,
            string sensorSource,
            string detail)
        {
            Availability = availability;
            LoadPercent = loadPercent;
            AverageMhz = averageMhz;
            MaximumMhz = maximumMhz;
            TemperatureCelsius = temperatureCelsius;
            PowerWatts = powerWatts;
            SensorSource = sensorSource ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        public TelemetryAvailability Availability { get; private set; }

        public double? LoadPercent { get; private set; }

        public double? AverageMhz { get; private set; }

        public double? MaximumMhz { get; private set; }

        public double? TemperatureCelsius { get; private set; }

        public double? PowerWatts { get; private set; }

        public string SensorSource { get; private set; }

        public string Detail { get; private set; }

        public static CpuTelemetry Paused()
        {
            return new CpuTelemetry(
                TelemetryAvailability.Unavailable,
                null,
                null,
                null,
                null,
                null,
                string.Empty,
                "Sampling is paused while the dashboard is hidden.");
        }

        public static CpuTelemetry Error(string detail)
        {
            return new CpuTelemetry(
                TelemetryAvailability.Error,
                null,
                null,
                null,
                null,
                null,
                string.Empty,
                detail);
        }
    }

    public sealed class DisplayTelemetry
    {
        public DisplayTelemetry(
            TelemetryAvailability availability,
            string deviceName,
            int width,
            int height,
            double? refreshRateHz,
            string detail)
            : this(
                availability,
                deviceName,
                width,
                height,
                refreshRateHz,
                detail,
                "Unknown",
                string.Empty)
        {
        }

        public DisplayTelemetry(
            TelemetryAvailability availability,
            string deviceName,
            int width,
            int height,
            double? refreshRateHz,
            string detail,
            string targetRole,
            string targetSignature)
        {
            Availability = availability;
            DeviceName = deviceName ?? string.Empty;
            Width = width;
            Height = height;
            RefreshRateHz = refreshRateHz;
            Detail = detail ?? string.Empty;
            TargetRole = targetRole ?? string.Empty;
            TargetSignature = targetSignature ?? string.Empty;
        }

        public TelemetryAvailability Availability { get; private set; }

        public string DeviceName { get; private set; }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public double? RefreshRateHz { get; private set; }

        public string Detail { get; private set; }

        public string TargetRole { get; private set; }

        // Normalized EDID signature. Serial-number and manufacture-date bytes
        // are excluded before hashing, so this can identify the reviewed panel
        // profile without exporting an exact per-device EDID fingerprint.
        public string TargetSignature { get; private set; }

        public bool IsRefreshRate(double expected)
        {
            return RefreshRateHz.HasValue
                && Math.Abs(RefreshRateHz.Value - expected) < 0.75;
        }

        public static DisplayTelemetry Unavailable(string detail)
        {
            return new DisplayTelemetry(
                TelemetryAvailability.Unavailable,
                string.Empty,
                0,
                0,
                null,
                detail);
        }

        public static DisplayTelemetry Error(string detail)
        {
            return new DisplayTelemetry(
                TelemetryAvailability.Error,
                string.Empty,
                0,
                0,
                null,
                detail);
        }
    }

    public sealed class GpuTelemetry
    {
        public GpuTelemetry(
            TelemetryAvailability availability,
            string adapterName,
            double? loadPercent,
            double? coreMhz,
            double? memoryMhz,
            double? powerWatts,
            double? temperatureCelsius,
            string sensorSource,
            string detail)
        {
            Availability = availability;
            AdapterName = adapterName ?? string.Empty;
            LoadPercent = loadPercent;
            CoreMhz = coreMhz;
            MemoryMhz = memoryMhz;
            PowerWatts = powerWatts;
            TemperatureCelsius = temperatureCelsius;
            SensorSource = sensorSource ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        public TelemetryAvailability Availability { get; private set; }

        public string AdapterName { get; private set; }

        public double? LoadPercent { get; private set; }

        public double? CoreMhz { get; private set; }

        public double? MemoryMhz { get; private set; }

        public double? PowerWatts { get; private set; }

        public double? TemperatureCelsius { get; private set; }

        public string SensorSource { get; private set; }

        public string Detail { get; private set; }

        public static GpuTelemetry Paused()
        {
            return new GpuTelemetry(
                TelemetryAvailability.Unavailable,
                string.Empty,
                null,
                null,
                null,
                null,
                null,
                string.Empty,
                "Sampling is paused while the dashboard is hidden.");
        }

        public static GpuTelemetry Unsupported(string detail)
        {
            return new GpuTelemetry(
                TelemetryAvailability.Unsupported,
                string.Empty,
                null,
                null,
                null,
                null,
                null,
                string.Empty,
                detail);
        }

        public static GpuTelemetry Error(string detail)
        {
            return new GpuTelemetry(
                TelemetryAvailability.Error,
                string.Empty,
                null,
                null,
                null,
                null,
                null,
                string.Empty,
                detail);
        }
    }

    public sealed class TelemetrySnapshot
    {
        public TelemetrySnapshot(
            DateTime timestampUtc,
            BatteryTelemetry battery,
            CpuTelemetry cpu,
            DisplayTelemetry display,
            GpuTelemetry gpu,
            bool dashboardSampling)
        {
            TimestampUtc = timestampUtc;
            Battery = battery ?? BatteryTelemetry.Unavailable("No sample.");
            Cpu = cpu ?? CpuTelemetry.Paused();
            Display = display ?? DisplayTelemetry.Unavailable("No sample.");
            Gpu = gpu ?? GpuTelemetry.Paused();
            DashboardSampling = dashboardSampling;
        }

        public DateTime TimestampUtc { get; private set; }

        public BatteryTelemetry Battery { get; private set; }

        public CpuTelemetry Cpu { get; private set; }

        public DisplayTelemetry Display { get; private set; }

        public GpuTelemetry Gpu { get; private set; }

        public bool DashboardSampling { get; private set; }

        public static TelemetrySnapshot Empty()
        {
            return new TelemetrySnapshot(
                DateTime.UtcNow,
                BatteryTelemetry.Unavailable("Waiting for the first sample."),
                CpuTelemetry.Paused(),
                DisplayTelemetry.Unavailable("Waiting for the first sample."),
                GpuTelemetry.Paused(),
                false);
        }
    }

    public sealed class TelemetrySnapshotEventArgs : EventArgs
    {
        public TelemetrySnapshotEventArgs(TelemetrySnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public TelemetrySnapshot Snapshot { get; private set; }
    }

    public static class TelemetryText
    {
        public static string Percent(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%"
                : "N/A";
        }

        public static string Watts(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("0.00", CultureInfo.InvariantCulture) + " W"
                : "N/A";
        }

        public static string Frequency(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("0", CultureInfo.InvariantCulture) + " MHz"
                : "N/A";
        }

        public static string Temperature(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("0", CultureInfo.InvariantCulture) + " \u00b0C"
                : "N/A";
        }

        public static string Refresh(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) + " Hz"
                : "N/A";
        }

        public static string BuildPublicDiagnostics(TelemetrySnapshot snapshot)
        {
            if (snapshot == null)
            {
                return "No telemetry snapshot is available.";
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine(
                "MacBook Eco diagnostics (public-safe, read-only)");
            builder.AppendLine(
                "Captured UTC: "
                + snapshot.TimestampUtc.ToString(
                    "O",
                    CultureInfo.InvariantCulture));
            builder.AppendLine("OS: " + Environment.OSVersion);
            builder.AppendLine("Runtime: " + Environment.Version);
            builder.AppendLine("64-bit process: " + Environment.Is64BitProcess);
            builder.AppendLine();
            builder.AppendLine(
                "Battery: " + snapshot.Battery.Availability
                + ", charge " + Percent(snapshot.Battery.ChargePercent)
                + ", system draw " + Watts(snapshot.Battery.DischargeWatts)
                + ", charge rate " + Watts(snapshot.Battery.ChargeWatts)
                + ", source " + PublicSensorSource(snapshot.Battery.Source));
            builder.AppendLine(
                "CPU: " + snapshot.Cpu.Availability
                + ", load " + Percent(snapshot.Cpu.LoadPercent)
                + ", current " + Frequency(snapshot.Cpu.AverageMhz)
                + ", maximum " + Frequency(snapshot.Cpu.MaximumMhz)
                + ", temperature " + Temperature(snapshot.Cpu.TemperatureCelsius)
                + ", package power " + Watts(snapshot.Cpu.PowerWatts)
                + ", sensors "
                + PublicSensorSource(snapshot.Cpu.SensorSource));
            builder.AppendLine(
                "Display: " + snapshot.Display.Availability
                + ", " + snapshot.Display.Width + "x" + snapshot.Display.Height
                + " @ " + Refresh(snapshot.Display.RefreshRateHz)
                + ", target " + PublicTargetRole(snapshot.Display.TargetRole)
                + ", normalized signature "
                + Safe(snapshot.Display.TargetSignature));
            builder.AppendLine(
                "GPU: " + snapshot.Gpu.Availability
                + ", load " + Percent(snapshot.Gpu.LoadPercent)
                + ", core " + Frequency(snapshot.Gpu.CoreMhz)
                + ", memory " + Frequency(snapshot.Gpu.MemoryMhz)
                + ", power " + Watts(snapshot.Gpu.PowerWatts)
                + ", temperature " + Temperature(snapshot.Gpu.TemperatureCelsius)
                + ", sensors "
                + PublicSensorSource(snapshot.Gpu.SensorSource));
            return builder.ToString();
        }

        private static string PublicSensorSource(string value)
        {
            switch (value)
            {
                case "powrprof":
                case "WMI":
                case "GetSystemPowerStatus":
                case @"root\LibreHardwareMonitor":
                case @"root\OpenHardwareMonitor":
                case "AMD ADL":
                    return value;
                default:
                    return "N/A";
            }
        }

        private static string PublicTargetRole(string value)
        {
            return string.Equals(value, "Internal", StringComparison.Ordinal)
                ? "Internal"
                : "N/A";
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "N/A" : value.Trim();
        }
    }
}
