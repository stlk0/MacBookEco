using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MacBookEco.Platform.Windows
{
    /// <summary>
    /// Hands out a device-parameters registry key for a monitor devnode that a
    /// resolver has already identified.
    ///
    /// Kept out of MonitorDevnodeReader, and therefore out of the watchdog's
    /// source set, because this is the only entry point that can return a
    /// writable handle. The watchdog restores a display mode and must not be
    /// able to reach EDID state at all.
    /// </summary>
    internal static class MonitorDevnodeAccess
    {
        /// <summary>
        /// Reopens the exact devnode by its durable instance ID and validates
        /// the complete device fingerprint again before returning the key. The
        /// caller owns the returned handle and must dispose it.
        /// </summary>
        internal static SafeRegistryHandle OpenExactDeviceParameters(
            MonitorDeviceRecord expected,
            int registryAccess)
        {
            if (expected == null)
            {
                throw new ArgumentNullException(nameof(expected));
            }

            if (string.IsNullOrEmpty(expected.DeviceInstanceId))
            {
                throw new SecureStateConflictException(
                    "The resolved monitor has no durable device instance identifier.");
            }

            Guid monitorClassGuid = DisplayTopologyNativeMethods.GuidDevClassMonitor;
            IntPtr informationSet = DisplayTopologyNativeMethods.SetupDiGetClassDevs(
                ref monitorClassGuid,
                null,
                IntPtr.Zero,
                0);
            if (DisplayTopologyNativeMethods.IsInvalidHandle(informationSet))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                SP_DEVINFO_DATA deviceInfo = new SP_DEVINFO_DATA();
                deviceInfo.CbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                if (!DisplayTopologyNativeMethods.SetupDiOpenDeviceInfo(
                    informationSet,
                    expected.DeviceInstanceId,
                    IntPtr.Zero,
                    0,
                    ref deviceInfo))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new SecureStateConflictException(
                        "The durable monitor devnode can no longer be resolved (Win32 error " +
                        error.ToString(CultureInfo.InvariantCulture) + ").");
                }

                MonitorDeviceRecord actual = MonitorDevnodeReader.ReadDeviceInfoRecord(
                    informationSet,
                    ref deviceInfo,
                    DisplayTopologyNativeMethods.KEY_READ,
                    false);
                if (!MonitorDevnodeReader.SameDurableDevice(expected, actual))
                {
                    throw new SecureStateConflictException(
                        "The re-resolved monitor devnode does not match the durable EDID identity.");
                }

                SafeRegistryHandle deviceKey =
                    DisplayTopologyNativeMethods.SetupDiOpenDevRegKey(
                        informationSet,
                        ref deviceInfo,
                        DisplayTopologyNativeMethods.DICS_FLAG_GLOBAL,
                        0,
                        DisplayTopologyNativeMethods.DIREG_DEV,
                        registryAccess);
                if (deviceKey.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    deviceKey.Dispose();
                    throw new Win32Exception(error);
                }

                return deviceKey;
            }
            finally
            {
                DisplayTopologyNativeMethods.SetupDiDestroyDeviceInfoList(
                    informationSet);
            }
        }
    }
}
