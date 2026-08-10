using System;
using System.Management;

namespace MacBookEco.Telemetry
{
    internal sealed class CpuSensorSample
    {
        internal double? TemperatureCelsius;
        internal double? PowerWatts;
        internal string Source;
    }

    /// <summary>
    /// Reads an already-running LibreHardwareMonitor/OpenHardwareMonitor WMI
    /// provider when one exists. MacBook Eco never installs or starts a kernel
    /// driver to obtain CPU package sensors.
    /// </summary>
    internal sealed class OptionalCpuSensorProvider
    {
        private static readonly string[] Namespaces =
        {
            @"root\LibreHardwareMonitor",
            @"root\OpenHardwareMonitor"
        };

        private readonly object _sync = new object();
        private DateTime _nextProbeUtc;
        private string _activeNamespace;

        internal CpuSensorSample Capture()
        {
            lock (_sync)
            {
                if (string.IsNullOrEmpty(_activeNamespace)
                    && DateTime.UtcNow < _nextProbeUtc)
                {
                    return new CpuSensorSample();
                }

                if (string.IsNullOrEmpty(_activeNamespace))
                {
                    _activeNamespace = FindProvider();
                    _nextProbeUtc = DateTime.UtcNow.AddMinutes(1);
                }

                if (string.IsNullOrEmpty(_activeNamespace))
                {
                    return new CpuSensorSample();
                }

                try
                {
                    return ReadSensors(_activeNamespace);
                }
                catch
                {
                    _activeNamespace = null;
                    _nextProbeUtc = DateTime.UtcNow.AddMinutes(1);
                    return new CpuSensorSample();
                }
            }
        }

        private static string FindProvider()
        {
            foreach (string candidate in Namespaces)
            {
                try
                {
                    using (ManagementObjectSearcher searcher =
                        new ManagementObjectSearcher(
                            candidate,
                            "SELECT Name FROM Sensor"))
                    using (ManagementObjectCollection results = searcher.Get())
                    {
                        bool found = false;
                        foreach (ManagementObject item in results)
                        {
                            try
                            {
                                found = true;
                            }
                            finally
                            {
                                item.Dispose();
                            }
                        }

                        if (found)
                        {
                            return candidate;
                        }
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static CpuSensorSample ReadSensors(string providerNamespace)
        {
            CpuSensorSample result = new CpuSensorSample();
            result.Source = providerNamespace;

            double? bestTemperature = null;
            int bestTemperatureRank = -1;
            double? bestPower = null;
            int bestPowerRank = -1;

            using (ManagementObjectSearcher searcher =
                new ManagementObjectSearcher(
                    providerNamespace,
                    "SELECT Name, SensorType, Value, Parent FROM Sensor"))
            using (ManagementObjectCollection sensors = searcher.Get())
            {
                foreach (ManagementObject sensor in sensors)
                {
                    try
                    {
                        string parent = Convert.ToString(sensor["Parent"])
                            ?? string.Empty;
                        if (!IsAllowedCpuParent(parent))
                        {
                            continue;
                        }

                        string name = Convert.ToString(sensor["Name"]) ?? string.Empty;
                        string type = Convert.ToString(sensor["SensorType"]) ?? string.Empty;
                        double? value = ReadDouble(sensor["Value"]);
                        if (!value.HasValue)
                        {
                            continue;
                        }

                        if (string.Equals(
                            type,
                            "Temperature",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            int rank = CpuSensorRank(name, "Package", "Core Max");
                            if (rank > bestTemperatureRank
                                && value.Value > 0.0
                                && value.Value < 125.0)
                            {
                                bestTemperatureRank = rank;
                                bestTemperature = value;
                            }
                        }
                        else if (string.Equals(
                            type,
                            "Power",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            int rank = CpuSensorRank(name, "Package", "CPU Package");
                            if (rank > bestPowerRank
                                && value.Value >= 0.0
                                && value.Value < 500.0)
                            {
                                bestPowerRank = rank;
                                bestPower = value;
                            }
                        }
                    }
                    finally
                    {
                        sensor.Dispose();
                    }
                }
            }

            result.TemperatureCelsius = bestTemperature;
            result.PowerWatts = bestPower;
            return result;
        }

        private static int CpuSensorRank(
            string name,
            string preferred,
            string secondary)
        {
            if (name.IndexOf(preferred, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 3;
            }

            if (name.IndexOf(secondary, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 2;
            }

            return 1;
        }

        internal static bool IsAllowedCpuParent(string parent)
        {
            if (string.IsNullOrWhiteSpace(parent))
            {
                return false;
            }

            string normalized = parent.Replace('\\', '/').Trim().ToLowerInvariant();
            if (normalized.IndexOf("gpu", StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            return normalized == "cpu"
                || normalized.StartsWith("cpu/", StringComparison.Ordinal)
                || normalized == "/cpu"
                || normalized.StartsWith("/cpu/", StringComparison.Ordinal)
                || normalized == "intelcpu"
                || normalized.StartsWith("intelcpu/", StringComparison.Ordinal)
                || normalized == "/intelcpu"
                || normalized.StartsWith("/intelcpu/", StringComparison.Ordinal)
                || normalized == "amdcpu"
                || normalized.StartsWith("amdcpu/", StringComparison.Ordinal)
                || normalized == "/amdcpu"
                || normalized.StartsWith("/amdcpu/", StringComparison.Ordinal);
        }

        private static double? ReadDouble(object value)
        {
            if (value == null)
            {
                return null;
            }

            try
            {
                double number = Convert.ToDouble(value);
                return double.IsNaN(number) || double.IsInfinity(number)
                    ? (double?)null
                    : number;
            }
            catch
            {
                return null;
            }
        }
    }
}
