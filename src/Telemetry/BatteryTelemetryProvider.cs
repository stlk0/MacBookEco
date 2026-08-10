using System;
using System.Management;
using System.Runtime.InteropServices;

namespace MacBookEco.Telemetry
{
    public sealed class BatteryTelemetryProvider : IBatteryTelemetryProvider
    {
        private readonly IBatteryTelemetryProvider[] _providers;

        public BatteryTelemetryProvider()
        {
            _providers = new IBatteryTelemetryProvider[]
            {
                new NativeBatteryTelemetryProvider(),
                new WmiBatteryTelemetryProvider(),
                new SystemPowerStatusBatteryTelemetryProvider()
            };
        }

        public BatteryTelemetry Capture()
        {
            string lastDetail = "No battery provider returned a sample.";

            foreach (IBatteryTelemetryProvider provider in _providers)
            {
                BatteryTelemetry sample;
                try
                {
                    sample = provider.Capture();
                }
                catch (Exception exception)
                {
                    lastDetail = provider.GetType().Name + ": " + exception.Message;
                    continue;
                }

                if (sample != null && sample.Availability == TelemetryAvailability.Available)
                {
                    return sample;
                }

                if (sample != null && !string.IsNullOrWhiteSpace(sample.Detail))
                {
                    lastDetail = sample.Detail;
                }
            }

            return BatteryTelemetry.Unavailable(lastDetail);
        }
    }

    internal sealed class NativeBatteryTelemetryProvider : IBatteryTelemetryProvider
    {
        public BatteryTelemetry Capture()
        {
            int size = Marshal.SizeOf(typeof(SystemBatteryState));
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                uint status = BatteryNativeMethods.CallNtPowerInformation(
                    5,
                    IntPtr.Zero,
                    0,
                    buffer,
                    (uint)size);

                if (status != 0)
                {
                    return BatteryTelemetry.Unavailable(
                        "CallNtPowerInformation returned status " + status + ".");
                }

                SystemBatteryState state =
                    (SystemBatteryState)Marshal.PtrToStructure(buffer, typeof(SystemBatteryState));
                if (state.BatteryPresent == 0)
                {
                    return BatteryTelemetry.Unavailable("Windows reports no system battery.");
                }

                double? chargePercent = null;
                if (state.MaxCapacity > 0 && state.MaxCapacity != uint.MaxValue)
                {
                    chargePercent = ClampPercent(
                        ((double)state.RemainingCapacity / state.MaxCapacity) * 100.0);
                }

                double? dischargeWatts = null;
                double? chargeWatts = null;
                if (state.Rate != uint.MaxValue)
                {
                    // SYSTEM_BATTERY_STATE declares Rate as an unsigned
                    // storage field, but Windows explicitly requires callers
                    // to interpret it as LONG. A negative value represents
                    // discharge and therefore has the high bit set.
                    int signedRate = unchecked((int)state.Rate);
                    if (state.Discharging != 0 && signedRate < 0)
                    {
                        dischargeWatts = Math.Abs((double)signedRate) / 1000.0;
                    }
                    else if (state.Charging != 0 && signedRate > 0)
                    {
                        chargeWatts = signedRate / 1000.0;
                    }
                }

                return new BatteryTelemetry(
                    TelemetryAvailability.Available,
                    state.AcOnLine != 0,
                    state.Charging != 0,
                    chargePercent,
                    "powrprof",
                    "SystemBatteryState",
                    chargeWatts,
                    dischargeWatts);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static double ClampPercent(double value)
        {
            return Math.Max(0.0, Math.Min(100.0, value));
        }
    }

    internal sealed class WmiBatteryTelemetryProvider : IBatteryTelemetryProvider
    {
        public BatteryTelemetry Capture()
        {
            bool? acOnline = null;
            bool? charging = null;
            double? dischargeWatts = null;
            double? chargeWatts = null;
            double? chargePercent = null;
            bool found = false;

            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT PowerOnline, Charging, Discharging, ChargeRate, DischargeRate "
                + "FROM BatteryStatus"))
            using (ManagementObjectCollection results = searcher.Get())
            {
                bool selectedStatus = false;
                foreach (ManagementObject item in results)
                {
                    try
                    {
                        if (selectedStatus)
                        {
                            continue;
                        }

                        selectedStatus = true;
                        found = true;
                        acOnline = ReadBoolean(item, "PowerOnline");
                        charging = ReadBoolean(item, "Charging");
                        bool? discharging = ReadBoolean(item, "Discharging");
                        uint? discharge = ReadUInt32(item, "DischargeRate");
                        uint? charge = ReadUInt32(item, "ChargeRate");

                        if (discharging == true && IsValidRate(discharge))
                        {
                            dischargeWatts = discharge.Value / 1000.0;
                        }

                        if (charging == true && IsValidRate(charge))
                        {
                            chargeWatts = charge.Value / 1000.0;
                        }
                    }
                    finally
                    {
                        item.Dispose();
                    }

                }
            }

            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                @"root\CIMV2",
                "SELECT EstimatedChargeRemaining FROM Win32_Battery"))
            using (ManagementObjectCollection results = searcher.Get())
            {
                bool selectedBattery = false;
                foreach (ManagementObject item in results)
                {
                    try
                    {
                        if (selectedBattery)
                        {
                            continue;
                        }

                        selectedBattery = true;
                        found = true;
                        uint? percent = ReadUInt32(item, "EstimatedChargeRemaining");
                        if (percent.HasValue && percent.Value <= 100)
                        {
                            chargePercent = percent.Value;
                        }

                    }
                    finally
                    {
                        item.Dispose();
                    }

                }
            }

            if (!found)
            {
                return BatteryTelemetry.Unavailable("WMI returned no battery instances.");
            }

            return new BatteryTelemetry(
                TelemetryAvailability.Available,
                acOnline,
                charging,
                chargePercent,
                "WMI",
                "BatteryStatus / Win32_Battery fallback",
                chargeWatts,
                dischargeWatts);
        }

        private static bool? ReadBoolean(ManagementBaseObject item, string name)
        {
            object value = item[name];
            return value == null ? (bool?)null : Convert.ToBoolean(value);
        }

        private static uint? ReadUInt32(ManagementBaseObject item, string name)
        {
            object value = item[name];
            return value == null ? (uint?)null : Convert.ToUInt32(value);
        }

        private static bool IsValidRate(uint? value)
        {
            return value.HasValue && value.Value != uint.MaxValue;
        }
    }

    internal sealed class SystemPowerStatusBatteryTelemetryProvider : IBatteryTelemetryProvider
    {
        public BatteryTelemetry Capture()
        {
            SystemPowerStatus status;
            if (!BatteryNativeMethods.GetSystemPowerStatus(out status))
            {
                return BatteryTelemetry.Unavailable(
                    "GetSystemPowerStatus failed with error "
                    + Marshal.GetLastWin32Error()
                    + ".");
            }

            if (status.BatteryFlag == 128)
            {
                return BatteryTelemetry.Unavailable("Windows reports no system battery.");
            }

            return BatteryTelemetrySemantics.FromSystemPowerStatus(status);
        }
    }

    internal static class BatteryTelemetrySemantics
    {
        internal static BatteryTelemetry FromSystemPowerStatus(
            SystemPowerStatus status)
        {
            double? percent = status.BatteryLifePercent == 255
                ? (double?)null
                : status.BatteryLifePercent;
            bool? charging = status.BatteryFlag == 255
                ? (bool?)null
                : (status.BatteryFlag & 8) != 0;
            return new BatteryTelemetry(
                TelemetryAvailability.Available,
                status.AcLineStatus == 255 ? (bool?)null : status.AcLineStatus == 1,
                charging,
                percent,
                "GetSystemPowerStatus",
                "Basic battery fallback; instantaneous watts are unavailable.",
                null,
                null);
        }
    }

    internal static class BatteryNativeMethods
    {
        [DllImport("powrprof.dll", SetLastError = false)]
        internal static extern uint CallNtPowerInformation(
            int informationLevel,
            IntPtr inputBuffer,
            uint inputBufferLength,
            IntPtr outputBuffer,
            uint outputBufferLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct SystemBatteryState
    {
        internal byte AcOnLine;
        internal byte BatteryPresent;
        internal byte Charging;
        internal byte Discharging;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        internal byte[] Spare;

        internal uint MaxCapacity;
        internal uint RemainingCapacity;
        internal uint Rate;
        internal uint EstimatedTime;
        internal uint DefaultAlert1;
        internal uint DefaultAlert2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemPowerStatus
    {
        internal byte AcLineStatus;
        internal byte BatteryFlag;
        internal byte BatteryLifePercent;
        internal byte SystemStatusFlag;
        internal uint BatteryLifeTime;
        internal uint BatteryFullLifeTime;
    }
}
