using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using MacBookEco.Core;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace MacBookEco.Platform.Windows
{
    /// <summary>
    /// Read-only discovery of the Apple SMBIOS identity, active internal display,
    /// monitor devnode/EDID, controlling display adapter and current GDI mode.
    /// Discovery deliberately returns an incomplete snapshot instead of guessing
    /// when Windows cannot map the active CCD target to a monitor devnode.
    /// </summary>
    public sealed class HardwareDiscoveryService
    {
        public WindowsHardwareSnapshot Discover()
        {
            WindowsHardwareSnapshot snapshot = new WindowsHardwareSnapshot();
            List<string> warnings = new List<string>();

            SmbiosIdentity identity = SmbiosReader.ReadIdentity();
            snapshot.SystemManufacturer = identity.Manufacturer;
            snapshot.AppleModel = identity.ProductName;
            snapshot.IsAppleHardware =
                string.Equals(identity.Manufacturer, "Apple Inc.", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(identity.ProductName) &&
                 identity.ProductName.StartsWith("MacBook", StringComparison.OrdinalIgnoreCase));

            IList<ActiveDisplayPath> paths;
            try
            {
                paths = DisplayTopologyReader.ReadActivePaths();
                snapshot.ActiveDisplayCount = paths.Count;
            }
            catch (Exception ex)
            {
                paths = new List<ActiveDisplayPath>();
                warnings.Add("CCD discovery failed: " + ex.Message);
            }

            IList<MonitorDeviceRecord> monitorDevices;
            try
            {
                monitorDevices = MonitorDevnodeReader.EnumeratePresent(
                    DisplayTopologyNativeMethods.KEY_READ);
            }
            catch (Exception ex)
            {
                monitorDevices = new List<MonitorDeviceRecord>();
                warnings.Add("Monitor SetupAPI discovery failed: " + ex.Message);
            }

            ActiveDisplayPath internalPath = ChooseInternalPath(paths, warnings);
            if (internalPath != null)
            {
                snapshot.InternalDisplay = BuildMonitorInfo(internalPath, monitorDevices, warnings);
                snapshot.DisplayAdapter = BuildAdapterInfo(internalPath);

                try
                {
                    snapshot.CurrentDisplayMode =
                        DisplayModeService.ReadCurrentMode(internalPath.GdiDeviceName);
                }
                catch (Exception ex)
                {
                    warnings.Add("Current display mode is unavailable: " + ex.Message);
                }
            }

            snapshot.Warnings = warnings.AsReadOnly();
            return snapshot;
        }

        /// <summary>
        /// A read-only snapshot degrades to a warning rather than throwing: an
        /// ambiguous topology must still produce diagnostics. It never becomes
        /// a mutation candidate, because every mutation path re-resolves the
        /// panel itself and refuses the same ambiguity outright.
        /// </summary>
        private static ActiveDisplayPath ChooseInternalPath(
            IList<ActiveDisplayPath> paths,
            IList<string> warnings)
        {
            ActiveDisplayPath selected;
            string detail;
            if (InternalPanelSelector.Select(paths, out selected, out detail) ==
                InternalPanelSelectionResult.Selected)
            {
                return selected;
            }

            warnings.Add(detail);
            return null;
        }

        private static WindowsMonitorInfo BuildMonitorInfo(
            ActiveDisplayPath path,
            IList<MonitorDeviceRecord> devices,
            IList<string> warnings)
        {
            MonitorDeviceRecord matchingDevice = null;
            for (int index = 0; index < devices.Count; index++)
            {
                if (DisplayTopologyReader.DevicePathsEqual(
                    devices[index].InterfacePath,
                    path.MonitorDevicePath))
                {
                    matchingDevice = devices[index];
                    break;
                }
            }

            WindowsMonitorInfo info = new WindowsMonitorInfo();
            info.FriendlyName = path.MonitorFriendlyName;
            info.MonitorDevicePath = path.MonitorDevicePath;
            info.IsInternal = path.IsInternal;
            info.Endpoint = new DisplayEndpoint(
                path.AdapterLuidValue,
                path.SourceId,
                path.TargetId,
                path.GdiDeviceName);

            if (matchingDevice == null)
            {
                warnings.Add(
                    "The active target could not be mapped to a GUID_DEVINTERFACE_MONITOR devnode.");
                return info;
            }

            info.DeviceInstanceId = matchingDevice.DeviceInstanceId;
            info.RegistryDevicePath =
                @"HKLM\SYSTEM\CurrentControlSet\Enum\" +
                matchingDevice.DeviceInstanceId +
                @"\Device Parameters";
            info.HardwareId = matchingDevice.HardwareId;
            info.Manufacturer = matchingDevice.Manufacturer;
            info.FriendlyName = FirstNonEmpty(
                matchingDevice.FriendlyName,
                path.MonitorFriendlyName,
                matchingDevice.Description);
            info.Edid = CloneBytes(matchingDevice.Edid);
            bool overrideReadSucceeded;
            info.ExistingEdidOverride = ReadExistingOverride(
                matchingDevice,
                warnings,
                out overrideReadSucceeded);
            info.ExistingEdidOverrideReadSucceeded = overrideReadSucceeded;

            try
            {
                EdidBaseBlock edid = CreateCoreEdid(matchingDevice.Edid);
                DetailedTiming nativeTiming = edid.PreferredTiming;
                info.EdidManufacturerCode = edid.ManufacturerCode;
                info.EdidProductCode = edid.ProductCode;
                info.NativeWidth = nativeTiming.HorizontalActive;
                info.NativeHeight = nativeTiming.VerticalActive;
            }
            catch (Exception ex)
            {
                warnings.Add("The internal monitor EDID is invalid: " + ex.Message);
            }

            return info;
        }

        /// <summary>
        /// Reads the override for the one identified internal panel. Discovery
        /// used to read it for every present monitor while enumerating them,
        /// which put the override subkey name into the shared enumeration code
        /// that the watchdog also compiles.
        /// </summary>
        private static byte[] ReadExistingOverride(
            MonitorDeviceRecord monitor,
            IList<string> warnings,
            out bool readSucceeded)
        {
            readSucceeded = false;
            try
            {
                using (SafeRegistryHandle deviceKey =
                    MonitorDevnodeAccess.OpenExactDeviceParameters(
                        monitor,
                        DisplayTopologyNativeMethods.KEY_READ))
                {
                    byte[] value = CloneBytes(EdidOverrideRegistry.Read(deviceKey));
                    readSucceeded = true;
                    return value;
                }
            }
            catch (Exception ex)
            {
                warnings.Add(
                    "The existing display override could not be read: " + ex.Message);
                return null;
            }
        }

        private static WindowsDisplayAdapterInfo BuildAdapterInfo(ActiveDisplayPath path)
        {
            WindowsDisplayAdapterInfo result = new WindowsDisplayAdapterInfo();
            result.AdapterLuid = path.AdapterLuid;

            uint deviceIndex = 0;
            DISPLAY_DEVICE device = new DISPLAY_DEVICE();
            device.Cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));

            while (NativeMethods.EnumDisplayDevices(null, deviceIndex, ref device, 0))
            {
                if (string.Equals(
                    device.DeviceName,
                    path.GdiDeviceName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    result.GdiDeviceName = device.DeviceName;
                    result.Description = device.DeviceString;
                    result.DeviceInstanceId = device.DeviceId;
                    result.RegistryDevicePath = device.DeviceKey;
                    result.IsAmd =
                        ContainsIgnoreCase(device.DeviceId, "VEN_1002") ||
                        ContainsIgnoreCase(device.DeviceString, "AMD") ||
                        ContainsIgnoreCase(device.DeviceString, "Radeon");
                    ReadAdapterDriverMetadata(result);
                    return result;
                }

                deviceIndex++;
                device = new DISPLAY_DEVICE();
                device.Cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            }

            result.GdiDeviceName = path.GdiDeviceName;
            return result;
        }

        private static void ReadAdapterDriverMetadata(WindowsDisplayAdapterInfo adapter)
        {
            string deviceKey = adapter.RegistryDevicePath;
            const string prefix = @"\Registry\Machine\";
            if (string.IsNullOrEmpty(deviceKey) ||
                !deviceKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string subKey = deviceKey.Substring(prefix.Length);
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(subKey, false))
                {
                    if (key == null)
                    {
                        return;
                    }

                    adapter.DriverVersion = Convert.ToString(
                        key.GetValue("DriverVersion"),
                        CultureInfo.InvariantCulture);
                }
            }
            catch
            {
                // Driver metadata is diagnostic only. Lack of access must not
                // make the otherwise read-only hardware snapshot fail.
            }
        }

        private static bool ContainsIgnoreCase(string value, string part)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            for (int index = 0; index < values.Length; index++)
            {
                if (!string.IsNullOrWhiteSpace(values[index]))
                {
                    return values[index];
                }
            }

            return null;
        }

        private static byte[] CloneBytes(byte[] bytes)
        {
            return bytes == null ? null : (byte[])bytes.Clone();
        }

        internal static EdidBaseBlock CreateCoreEdid(byte[] bytes)
        {
            if (bytes == null || bytes.Length < EdidBaseBlock.Length)
            {
                throw new FormatException(
                    "The EDID base block is missing or shorter than 128 bytes.");
            }

            byte[] baseBlock = new byte[EdidBaseBlock.Length];
            Buffer.BlockCopy(bytes, 0, baseBlock, 0, baseBlock.Length);
            return new EdidBaseBlock(baseBlock);
        }
    }
}
