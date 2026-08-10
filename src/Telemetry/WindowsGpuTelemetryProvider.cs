using System;
using System.Runtime.InteropServices;

namespace MacBookEco.Telemetry
{
    /// <summary>
    /// Uses the AMD Display Library already shipped by the display driver for
    /// sensors. Monitoring exists only while the dashboard is visible.
    /// </summary>
    public sealed class WindowsGpuTelemetryProvider : IGpuTelemetryProvider
    {
        private readonly object _sync = new object();
        private readonly AmdAdlSession _amd = new AmdAdlSession();
        private bool _started;

        public void StartMonitoring()
        {
            lock (_sync)
            {
                if (_started)
                {
                    return;
                }

                _amd.Start();
                _started = true;
            }
        }

        public GpuTelemetry Capture()
        {
            lock (_sync)
            {
                if (!_started)
                {
                    return GpuTelemetry.Paused();
                }

                AmdGpuSample amd = _amd.Capture();
                double? load = amd.LoadPercent;

                bool available =
                    load.HasValue
                    || amd.CoreMhz.HasValue
                    || amd.MemoryMhz.HasValue
                    || amd.PowerWatts.HasValue
                    || amd.TemperatureCelsius.HasValue
                    || !string.IsNullOrWhiteSpace(amd.AdapterName);

                if (!available)
                {
                    return GpuTelemetry.Unsupported(
                        "AMD ADL sensors are unavailable. " + _amd.Detail);
                }

                return new GpuTelemetry(
                    TelemetryAvailability.Available,
                    string.IsNullOrWhiteSpace(amd.AdapterName)
                        ? "Windows display adapter"
                        : amd.AdapterName,
                    load,
                    amd.CoreMhz,
                    amd.MemoryMhz,
                    amd.PowerWatts,
                    amd.TemperatureCelsius,
                    amd.HasAnySensor ? "AMD ADL" : string.Empty,
                    _amd.Detail);
            }
        }

        public void StopMonitoring()
        {
            lock (_sync)
            {
                if (!_started)
                {
                    return;
                }

                _amd.Stop();
                _started = false;
            }
        }
    }

    internal sealed class AmdGpuSample
    {
        internal string AdapterName;
        internal double? LoadPercent;
        internal double? CoreMhz;
        internal double? MemoryMhz;
        internal double? PowerWatts;
        internal double? TemperatureCelsius;

        internal bool HasAnySensor => LoadPercent.HasValue
                    || CoreMhz.HasValue
                    || MemoryMhz.HasValue
                    || PowerWatts.HasValue
                    || TemperatureCelsius.HasValue;
    }

    internal sealed class AmdAdlSession
    {
        private const int AdlOk = 0;
        private const int AmdVendorId = 0x1002;
        private const int OverdriveNVersion = 8;
        private const int TemperatureEdge = 1;
        private const int TotalGpuPower = 0;

        // Offsets into ADLPMLogData.ulValues, matching the ADL_PMLOG_SENSORS
        // enumeration in AMD's Display Library headers. The array is indexed
        // by sensor ID, so these are positions and not arbitrary constants.
        private const int PmLogClockGfx = 1;
        private const int PmLogClockMemory = 2;
        private const int PmLogTemperatureEdge = 8;
        private const int PmLogInfoActivityGfx = 19;
        private const int PmLogAsicPowerBoard = 23;
        private const int PmLogAsicPowerTotal = 30;

        // Sanity bounds. A reading outside them is a driver reporting an
        // uninitialised slot, not a measurement, and is dropped so the graph
        // shows N/A rather than a fabricated value.
        private const double MaximumPlausibleMhz = 10000.0;
        private const double MaximumPlausibleCelsius = 125.0;
        private const double MinimumPlausibleWatts = 0.05;
        private const double MaximumPlausibleWatts = 500.0;

        private AdlMemoryAllocator _allocator;
        private IntPtr _context;
        private int _adapterIndex = -1;
        private int _overdriveVersion;
        private string _adapterName;
        private string _detail;
        private string _sensorApi;

        internal string Detail => _detail ?? string.Empty;

        internal void Start()
        {
            Stop();
            _allocator = Allocate;

            try
            {
                int result = AmdAdlNative.ADL2_Main_Control_Create(
                    _allocator,
                    1,
                    out _context);
                if (result != AdlOk || _context == IntPtr.Zero)
                {
                    _detail = "ADL initialization returned " + result + ".";
                    _context = IntPtr.Zero;
                    return;
                }

                FindAdapter();
                if (_adapterIndex < 0)
                {
                    _detail = "ADL found no active AMD adapter.";
                    return;
                }

                int supported;
                int enabled;
                int version;
                result = AmdAdlNative.ADL2_Overdrive_Caps(
                    _context,
                    _adapterIndex,
                    out supported,
                    out enabled,
                    out version);
                if (result == AdlOk && supported != 0)
                {
                    _overdriveVersion = version;
                    _detail = "AMD ADL Overdrive "
                        + version
                        + " sensors; writes are never used.";
                }
                else
                {
                    _detail = "AMD adapter identified; probing read-only "
                        + "ADL sensor generations. Writes are never used.";
                }
            }
            catch (DllNotFoundException)
            {
                _detail = "The AMD ADL driver library is not installed.";
                Stop();
            }
            catch (EntryPointNotFoundException)
            {
                _detail = "The installed AMD ADL library lacks the required read-only APIs.";
                Stop();
            }
            catch (BadImageFormatException)
            {
                _detail = "The AMD ADL driver library has an incompatible architecture.";
                Stop();
            }
            catch (Exception exception)
            {
                _detail = "AMD ADL initialization failed: " + exception.Message;
                Stop();
            }
        }

        internal AmdGpuSample Capture()
        {
            AmdGpuSample sample = new AmdGpuSample();
            sample.AdapterName = _adapterName;

            if (_context == IntPtr.Zero || _adapterIndex < 0)
            {
                return sample;
            }

            // Attribution belongs to this sample, not to an earlier one that
            // happened to succeed.
            _sensorApi = null;
            TryCapturePmLog(sample);

            if (_overdriveVersion >= OverdriveNVersion)
            {
                try
                {
                    AdlOdnPerformanceStatus status;
                    int result = AmdAdlNative.ADL2_OverdriveN_PerformanceStatus_Get(
                        _context,
                        _adapterIndex,
                        out status);
                    if (result == AdlOk)
                    {
                        // Fill gaps only. OverdriveN commonly returns ADL_OK
                        // with zeroed clocks on the Boot Camp driver while the
                        // GPU is idle; overwriting here would discard a valid
                        // PMLog sample, including the memory clock this whole
                        // application exists to observe.
                        bool used = FillMissing(
                            sample,
                            ClockToMhz(status.CoreClock),
                            ClockToMhz(status.MemoryClock),
                            Percent(status.GpuActivityPercent));
                        if (used)
                        {
                            _sensorApi = "OverdriveN";
                        }
                    }
                }
                catch (EntryPointNotFoundException)
                {
                }

                try
                {
                    int rawTemperature;
                    int result = AmdAdlNative.ADL2_OverdriveN_Temperature_Get(
                        _context,
                        _adapterIndex,
                        TemperatureEdge,
                        out rawTemperature);
                    if (result == AdlOk)
                    {
                        double temperature = rawTemperature / 1000.0;
                        if (temperature > 0.0 && temperature < MaximumPlausibleCelsius)
                        {
                            sample.TemperatureCelsius = temperature;
                            _sensorApi = "OverdriveN";
                        }
                    }
                }
                catch (EntryPointNotFoundException)
                {
                }
            }

            // Boot Camp drivers frequently report Overdrive_Caps as disabled
            // while still exposing older read-only status calls. Probe those
            // generations regardless of tuning capability; no Set API is
            // imported anywhere in this process.
            if (!sample.CoreMhz.HasValue || !sample.MemoryMhz.HasValue)
            {
                try
                {
                    AdlOd6CurrentStatus status;
                    int result = AmdAdlNative.ADL2_Overdrive6_CurrentStatus_Get(
                        _context,
                        _adapterIndex,
                        out status);
                    if (result == AdlOk && FillMissing(
                            sample,
                            ClockToMhz(status.EngineClock),
                            ClockToMhz(status.MemoryClock),
                            Percent(status.ActivityPercent)))
                    {
                        _sensorApi = "Overdrive6";
                    }
                }
                catch (EntryPointNotFoundException)
                {
                }
            }

            if (!sample.CoreMhz.HasValue || !sample.MemoryMhz.HasValue)
            {
                try
                {
                    AdlPmActivity activity = new AdlPmActivity();
                    activity.Size = Marshal.SizeOf(typeof(AdlPmActivity));
                    int result = AmdAdlNative.ADL2_Overdrive5_CurrentActivity_Get(
                        _context,
                        _adapterIndex,
                        ref activity);
                    if (result == AdlOk && FillMissing(
                            sample,
                            ClockToMhz(activity.EngineClock),
                            ClockToMhz(activity.MemoryClock),
                            Percent(activity.ActivityPercent)))
                    {
                        _sensorApi = "Overdrive5";
                    }
                }
                catch (EntryPointNotFoundException)
                {
                }
            }

            if (!sample.TemperatureCelsius.HasValue)
            {
                try
                {
                    int rawTemperature;
                    int result = AmdAdlNative.ADL2_Overdrive6_Temperature_Get(
                        _context,
                        _adapterIndex,
                        out rawTemperature);
                    if (result == AdlOk)
                    {
                        sample.TemperatureCelsius =
                            TemperatureFromMilliCelsius(rawTemperature);
                        if (sample.TemperatureCelsius.HasValue)
                        {
                            _sensorApi = "Overdrive6";
                        }
                    }
                }
                catch (EntryPointNotFoundException)
                {
                }
            }

            if (!sample.TemperatureCelsius.HasValue)
            {
                try
                {
                    AdlTemperature temperature = new AdlTemperature();
                    temperature.Size = Marshal.SizeOf(typeof(AdlTemperature));
                    int result = AmdAdlNative.ADL2_Overdrive5_Temperature_Get(
                        _context,
                        _adapterIndex,
                        0,
                        ref temperature);
                    if (result == AdlOk)
                    {
                        sample.TemperatureCelsius =
                            TemperatureFromMilliCelsius(
                                temperature.Temperature);
                        if (sample.TemperatureCelsius.HasValue)
                        {
                            _sensorApi = "Overdrive5";
                        }
                    }
                }
                catch (EntryPointNotFoundException)
                {
                }
            }

            try
            {
                int rawPower;
                int result = AmdAdlNative.ADL2_Overdrive6_CurrentPower_Get(
                    _context,
                    _adapterIndex,
                    TotalGpuPower,
                    out rawPower);
                if (result == AdlOk)
                {
                    double power = rawPower / 256.0;
                    if (power > MinimumPlausibleWatts && power < MaximumPlausibleWatts)
                    {
                        sample.PowerWatts = power;
                        _sensorApi = string.IsNullOrEmpty(_sensorApi)
                            ? "Overdrive6"
                            : _sensorApi;
                    }
                }
            }
            catch (EntryPointNotFoundException)
            {
            }

            _detail = string.IsNullOrEmpty(_sensorApi)
                ? "No AMD ADL read-only sensor answered this sample."
                : "AMD ADL " + _sensorApi
                    + " read-only sensors are active; writes are never used.";

            return sample;
        }

        /// <summary>
        /// Copies each supplied reading into the sample only where the sample
        /// has no value yet, and reports whether anything was taken. An ADL
        /// generation that answers successfully but has nothing valid to say
        /// must not claim credit for the sample or erase an earlier one.
        /// </summary>
        internal static bool FillMissing(
            AmdGpuSample sample,
            double? coreMhz,
            double? memoryMhz,
            double? loadPercent)
        {
            bool used = false;
            if (!sample.CoreMhz.HasValue && coreMhz.HasValue)
            {
                sample.CoreMhz = coreMhz;
                used = true;
            }

            if (!sample.MemoryMhz.HasValue && memoryMhz.HasValue)
            {
                sample.MemoryMhz = memoryMhz;
                used = true;
            }

            if (!sample.LoadPercent.HasValue && loadPercent.HasValue)
            {
                sample.LoadPercent = loadPercent;
                used = true;
            }

            return used;
        }

        private void TryCapturePmLog(AmdGpuSample sample)
        {
            try
            {
                AdlPmLogDataOutput output = new AdlPmLogDataOutput();
                output.Sensors = new AdlSingleSensorData[256];
                output.Size = Marshal.SizeOf(typeof(AdlPmLogDataOutput));
                int result = AmdAdlNative.ADL2_New_QueryPMLogData_Get(
                    _context,
                    _adapterIndex,
                    ref output);
                // The array is marshalled at a fixed length, so its size says
                // nothing about what the driver filled in. Each reading is
                // gated on the per-sensor Supported flag and a plausibility
                // range instead; that is the only usable version check here.
                if (result != AdlOk || output.Sensors == null)
                {
                    return;
                }

                sample.CoreMhz = PmLogValue(
                    output, PmLogClockGfx, 0.0, MaximumPlausibleMhz);
                sample.MemoryMhz = PmLogValue(
                    output, PmLogClockMemory, 0.0, MaximumPlausibleMhz);
                sample.TemperatureCelsius = PmLogValue(
                    output,
                    PmLogTemperatureEdge,
                    0.0,
                    MaximumPlausibleCelsius);
                sample.LoadPercent = PmLogLoadPercent(output);
                sample.PowerWatts = PmLogValue(
                    output,
                    PmLogAsicPowerBoard,
                    MinimumPlausibleWatts,
                    MaximumPlausibleWatts)
                    ?? PmLogValue(
                        output,
                        PmLogAsicPowerTotal,
                        MinimumPlausibleWatts,
                        MaximumPlausibleWatts);

                if (sample.HasAnySensor)
                {
                    _sensorApi = "PMLog";
                }
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        // A supported PMLog load reading of zero is a real idle sample. Keep
        // it distinct from an unsupported sensor so the graph remains
        // continuous while the GPU is idle.
        internal static double? PmLogLoadPercent(AdlPmLogDataOutput output)
        {
            return PmLogValue(output, PmLogInfoActivityGfx, 0.0, 100.0, true);
        }

        private static double? PmLogValue(
            AdlPmLogDataOutput output,
            int sensorIndex,
            double minimumExclusive,
            double maximumExclusive)
        {
            return PmLogValue(
                output,
                sensorIndex,
                minimumExclusive,
                maximumExclusive,
                false);
        }

        private static double? PmLogValue(
            AdlPmLogDataOutput output,
            int sensorIndex,
            double minimum,
            double maximumExclusive,
            bool minimumInclusive)
        {
            if (output.Sensors == null
                || sensorIndex < 0
                || sensorIndex >= output.Sensors.Length
                || output.Sensors[sensorIndex].Supported == 0)
            {
                return null;
            }

            double value = output.Sensors[sensorIndex].Value;
            bool aboveMinimum = minimumInclusive
                ? value >= minimum
                : value > minimum;
            return aboveMinimum && value < maximumExclusive
                ? value
                : (double?)null;
        }

        internal void Stop()
        {
            if (_context != IntPtr.Zero)
            {
                try
                {
                    AmdAdlNative.ADL2_Main_Control_Destroy(_context);
                }
                catch
                {
                }
            }

            _context = IntPtr.Zero;
            _adapterIndex = -1;
            _overdriveVersion = 0;
            _adapterName = null;
            _allocator = null;
            _sensorApi = null;
        }

        private void FindAdapter()
        {
            int count;
            int result = AmdAdlNative.ADL2_Adapter_NumberOfAdapters_Get(
                _context,
                out count);
            if (result != AdlOk || count <= 0 || count > 64)
            {
                return;
            }

            int itemSize = Marshal.SizeOf(typeof(AdlAdapterInfo));
            IntPtr buffer = Marshal.AllocCoTaskMem(itemSize * count);
            try
            {
                for (int index = 0; index < count; index++)
                {
                    IntPtr address = new IntPtr(buffer.ToInt64() + (index * itemSize));
                    AdlAdapterInfo initial = new AdlAdapterInfo();
                    initial.Size = itemSize;
                    Marshal.StructureToPtr(initial, address, false);
                }

                result = AmdAdlNative.ADL2_Adapter_AdapterInfo_Get(
                    _context,
                    buffer,
                    itemSize * count);
                if (result != AdlOk)
                {
                    return;
                }

                for (int index = 0; index < count; index++)
                {
                    IntPtr address = new IntPtr(buffer.ToInt64() + (index * itemSize));
                    AdlAdapterInfo info =
                        (AdlAdapterInfo)Marshal.PtrToStructure(
                            address,
                            typeof(AdlAdapterInfo));
                    if (info.Present == 0 || !IsAmd(info))
                    {
                        continue;
                    }

                    _adapterIndex = info.AdapterIndex;
                    _adapterName = string.IsNullOrWhiteSpace(info.AdapterName)
                        ? "AMD Radeon"
                        : info.AdapterName.Trim();
                    return;
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(buffer);
            }
        }

        private static bool IsAmd(AdlAdapterInfo info)
        {
            return info.VendorId == AmdVendorId
                || (!string.IsNullOrEmpty(info.PnpString)
                    && info.PnpString.IndexOf(
                        "VEN_1002",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                || (!string.IsNullOrEmpty(info.AdapterName)
                    && (info.AdapterName.IndexOf(
                            "Radeon",
                            StringComparison.OrdinalIgnoreCase) >= 0
                        || info.AdapterName.IndexOf(
                            "AMD",
                            StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static IntPtr Allocate(int size)
        {
            return size <= 0 ? IntPtr.Zero : Marshal.AllocCoTaskMem(size);
        }

        private static double? ClockToMhz(int value)
        {
            if (value <= 0)
            {
                return null;
            }

            double mhz = value / 100.0;
            return mhz > 0.0 && mhz < MaximumPlausibleMhz ? mhz : (double?)null;
        }

        private static double? Percent(int value)
        {
            return value >= 0 && value <= 100 ? value : (double?)null;
        }

        private static double? TemperatureFromMilliCelsius(int value)
        {
            double temperature = value / 1000.0;
            return temperature > 0.0 && temperature < MaximumPlausibleCelsius
                ? temperature
                : (double?)null;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate IntPtr AdlMemoryAllocator(int size);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct AdlAdapterInfo
    {
        internal int Size;
        internal int AdapterIndex;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        internal string Udid;

        internal int BusNumber;
        internal int DeviceNumber;
        internal int FunctionNumber;
        internal int VendorId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        internal string AdapterName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        internal string DisplayName;

        internal int Present;
        internal int Exists;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        internal string DriverPath;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        internal string DriverPathExtended;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        internal string PnpString;

        internal int OsDisplayIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AdlOdnPerformanceStatus
    {
        internal int CoreClock;
        internal int MemoryClock;
        internal int DcefClock;
        internal int GfxClock;
        internal int UvdClock;
        internal int VceClock;
        internal int GpuActivityPercent;
        internal int CurrentCorePerformanceLevel;
        internal int CurrentMemoryPerformanceLevel;
        internal int CurrentDcefPerformanceLevel;
        internal int CurrentGfxPerformanceLevel;
        internal int UvdPerformanceLevel;
        internal int VcePerformanceLevel;
        internal int CurrentBusSpeed;
        internal int CurrentBusLanes;
        internal int MaximumBusLanes;
        internal int Vddc;
        internal int Vddci;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AdlOd6CurrentStatus
    {
        internal int EngineClock;
        internal int MemoryClock;
        internal int ActivityPercent;
        internal int CurrentPerformanceLevel;
        internal int CurrentBusSpeed;
        internal int CurrentBusLanes;
        internal int MaximumBusLanes;
        internal int ExtensionValue;
        internal int ExtensionMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AdlPmActivity
    {
        internal int Size;
        internal int EngineClock;
        internal int MemoryClock;
        internal int Vddc;
        internal int ActivityPercent;
        internal int CurrentPerformanceLevel;
        internal int CurrentBusSpeed;
        internal int CurrentBusLanes;
        internal int MaximumBusLanes;
        internal int Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AdlTemperature
    {
        internal int Size;
        internal int Temperature;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AdlSingleSensorData
    {
        internal int Supported;
        internal int Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AdlPmLogDataOutput
    {
        internal int Size;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        internal AdlSingleSensorData[] Sensors;
    }

    internal static class AmdAdlNative
    {
        private const string Library = "atiadlxx.dll";

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ADL2_Main_Control_Create(
            AdlMemoryAllocator callback,
            int enumerateConnectedAdapters,
            out IntPtr context);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ADL2_Main_Control_Destroy(IntPtr context);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ADL2_Adapter_NumberOfAdapters_Get(
            IntPtr context,
            out int numberOfAdapters);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ADL2_Adapter_AdapterInfo_Get(
            IntPtr context,
            IntPtr information,
            int inputSize);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ADL2_Overdrive_Caps(
            IntPtr context,
            int adapterIndex,
            out int supported,
            out int enabled,
            out int version);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ADL2_OverdriveN_PerformanceStatus_Get(
            IntPtr context,
            int adapterIndex,
            out AdlOdnPerformanceStatus status);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ADL2_OverdriveN_Temperature_Get(
            IntPtr context,
            int adapterIndex,
            int temperatureType,
            out int temperature);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ADL2_Overdrive6_CurrentStatus_Get(
            IntPtr context,
            int adapterIndex,
            out AdlOd6CurrentStatus status);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ADL2_Overdrive6_Temperature_Get(
            IntPtr context,
            int adapterIndex,
            out int temperature);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ADL2_Overdrive5_CurrentActivity_Get(
            IntPtr context,
            int adapterIndex,
            ref AdlPmActivity activity);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ADL2_Overdrive5_Temperature_Get(
            IntPtr context,
            int adapterIndex,
            int thermalControllerIndex,
            ref AdlTemperature temperature);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ADL2_New_QueryPMLogData_Get(
            IntPtr context,
            int adapterIndex,
            ref AdlPmLogDataOutput dataOutput);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ADL2_Overdrive6_CurrentPower_Get(
            IntPtr context,
            int adapterIndex,
            int powerType,
            out int currentPower);
    }
}
