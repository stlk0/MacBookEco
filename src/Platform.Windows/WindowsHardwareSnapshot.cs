using System;
using System.Collections.Generic;
using MacBookEco.Core;

namespace MacBookEco.Platform.Windows
{
    /// <summary>
    /// Result of one read-only discovery pass. Fields are nullable on purpose:
    /// an incomplete snapshot is reported as such rather than being filled with
    /// a guess, and Warnings explains every gap.
    /// </summary>
    public sealed class WindowsHardwareSnapshot
    {
        public string SystemManufacturer { get; internal set; }
        public string AppleModel { get; internal set; }
        public bool IsAppleHardware { get; internal set; }
        public int ActiveDisplayCount { get; internal set; }
        public WindowsMonitorInfo InternalDisplay { get; internal set; }
        public WindowsDisplayAdapterInfo DisplayAdapter { get; internal set; }
        public WindowsDisplayMode CurrentDisplayMode { get; internal set; }
        public IList<string> Warnings { get; internal set; }

        public HardwareSnapshot ToCoreSnapshot()
        {
            if (InternalDisplay == null)
            {
                throw new InvalidOperationException(
                    "The internal monitor is unavailable.");
            }

            EdidBaseBlock edid =
                HardwareDiscoveryService.CreateCoreEdid(InternalDisplay.Edid);
            WindowsDisplayAdapterInfo adapter = DisplayAdapter;
            return new HardwareSnapshot(
                SystemManufacturer,
                AppleModel,
                InternalDisplay.IsInternal,
                InternalDisplay.HardwareId,
                edid,
                adapter == null ? null : adapter.Description,
                adapter == null ? null : adapter.DeviceInstanceId,
                adapter == null ? null : adapter.DriverVersion);
        }
    }

    public sealed class WindowsMonitorInfo
    {
        public bool IsInternal { get; internal set; }
        public string FriendlyName { get; internal set; }
        public string HardwareId { get; internal set; }
        public string Manufacturer { get; internal set; }
        public string DeviceInstanceId { get; internal set; }
        public string MonitorDevicePath { get; internal set; }
        public DisplayEndpoint Endpoint { get; internal set; }
        public string RegistryDevicePath { get; internal set; }
        public byte[] Edid { get; internal set; }
        public byte[] ExistingEdidOverride { get; internal set; }
        public string EdidManufacturerCode { get; internal set; }
        public ushort EdidProductCode { get; internal set; }
        public int NativeWidth { get; internal set; }
        public int NativeHeight { get; internal set; }
    }

    public sealed class WindowsDisplayAdapterInfo
    {
        public string GdiDeviceName { get; internal set; }
        public string Description { get; internal set; }
        public string DeviceInstanceId { get; internal set; }
        public string RegistryDevicePath { get; internal set; }
        public string AdapterLuid { get; internal set; }
        public string DriverVersion { get; internal set; }
        public bool IsAmd { get; internal set; }
    }
}
