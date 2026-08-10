using System;
using System.Runtime.InteropServices;

namespace MacBookEco.Telemetry
{
    public sealed class CpuTelemetryProvider : ICpuTelemetryProvider
    {
        private readonly object _sync = new object();
        private readonly OptionalCpuSensorProvider _sensorProvider =
            new OptionalCpuSensorProvider();
        private ulong? _previousIdle;
        private ulong? _previousKernel;
        private ulong? _previousUser;

        public CpuTelemetry Capture()
        {
            double? load = CaptureLoad();
            double? currentMhz;
            double? maximumMhz;
            CaptureFrequency(out currentMhz, out maximumMhz);
            CpuSensorSample sensors = _sensorProvider.Capture();

            TelemetryAvailability availability =
                load.HasValue || currentMhz.HasValue || maximumMhz.HasValue
                ? TelemetryAvailability.Available
                : TelemetryAvailability.Unavailable;

            string detail = load.HasValue
                ? "GetSystemTimes and ProcessorInformation"
                : "CPU load needs two samples; processor frequency may be unavailable.";

            return new CpuTelemetry(
                availability,
                load,
                currentMhz,
                maximumMhz,
                sensors.TemperatureCelsius,
                sensors.PowerWatts,
                sensors.Source,
                detail);
        }

        private double? CaptureLoad()
        {
            FileTime idle;
            FileTime kernel;
            FileTime user;
            if (!CpuNativeMethods.GetSystemTimes(out idle, out kernel, out user))
            {
                return null;
            }

            ulong idleValue = idle.ToUInt64();
            ulong kernelValue = kernel.ToUInt64();
            ulong userValue = user.ToUInt64();

            lock (_sync)
            {
                if (!_previousIdle.HasValue)
                {
                    _previousIdle = idleValue;
                    _previousKernel = kernelValue;
                    _previousUser = userValue;
                    return null;
                }

                ulong idleDelta = idleValue - _previousIdle.Value;
                ulong kernelDelta = kernelValue - _previousKernel.Value;
                ulong userDelta = userValue - _previousUser.Value;

                _previousIdle = idleValue;
                _previousKernel = kernelValue;
                _previousUser = userValue;

                ulong total = kernelDelta + userDelta;
                if (total == 0 || idleDelta > total)
                {
                    return null;
                }

                double value = ((double)(total - idleDelta) / total) * 100.0;
                return Math.Max(0.0, Math.Min(100.0, value));
            }
        }

        private static void CaptureFrequency(out double? currentMhz, out double? maximumMhz)
        {
            currentMhz = null;
            maximumMhz = null;

            int processorCount = Math.Max(1, Environment.ProcessorCount);
            int itemSize = Marshal.SizeOf(typeof(ProcessorPowerInformation));
            int bufferSize = itemSize * processorCount;
            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                uint status = CpuNativeMethods.CallNtPowerInformation(
                    11,
                    IntPtr.Zero,
                    0,
                    buffer,
                    (uint)bufferSize);
                if (status != 0)
                {
                    return;
                }

                double currentTotal = 0.0;
                double maximumTotal = 0.0;
                int validCurrent = 0;
                int validMaximum = 0;

                for (int index = 0; index < processorCount; index++)
                {
                    IntPtr itemAddress = new IntPtr(buffer.ToInt64() + (index * itemSize));
                    ProcessorPowerInformation item =
                        (ProcessorPowerInformation)Marshal.PtrToStructure(
                            itemAddress,
                            typeof(ProcessorPowerInformation));

                    if (item.CurrentMhz > 0)
                    {
                        currentTotal += item.CurrentMhz;
                        validCurrent++;
                    }

                    if (item.MaxMhz > 0)
                    {
                        maximumTotal += item.MaxMhz;
                        validMaximum++;
                    }
                }

                if (validCurrent > 0)
                {
                    currentMhz = currentTotal / validCurrent;
                }

                if (validMaximum > 0)
                {
                    maximumMhz = maximumTotal / validMaximum;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    internal static class CpuNativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemTimes(
            out FileTime idleTime,
            out FileTime kernelTime,
            out FileTime userTime);

        [DllImport("powrprof.dll", SetLastError = false)]
        internal static extern uint CallNtPowerInformation(
            int informationLevel,
            IntPtr inputBuffer,
            uint inputBufferLength,
            IntPtr outputBuffer,
            uint outputBufferLength);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileTime
    {
        internal uint LowDateTime;
        internal uint HighDateTime;

        internal ulong ToUInt64()
        {
            return ((ulong)HighDateTime << 32) | LowDateTime;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessorPowerInformation
    {
        internal uint Number;
        internal uint MaxMhz;
        internal uint CurrentMhz;
        internal uint MhzLimit;
        internal uint MaxIdleState;
        internal uint CurrentIdleState;
    }
}
