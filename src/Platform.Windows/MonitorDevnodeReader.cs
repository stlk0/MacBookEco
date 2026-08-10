using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using MacBookEco.Core;
using Microsoft.Win32.SafeHandles;

namespace MacBookEco.Platform.Windows
{
    /// <summary>
    /// One monitor devnode as read through SetupAPI.
    ///
    /// Deliberately no override bytes: this type is compiled into the watchdog,
    /// which must not be able to reach EDID override state at all. Callers that
    /// legitimately need it read it for their one resolved panel through
    /// MonitorDevnodeAccess and EdidOverrideRegistry.
    /// </summary>
    internal sealed class MonitorDeviceRecord
    {
        internal string InterfacePath;
        internal string DeviceInstanceId;
        internal string HardwareId;
        internal string FriendlyName;
        internal string Description;
        internal string Manufacturer;
        internal byte[] Edid;
    }

    /// <summary>
    /// Read-only SetupAPI enumeration of monitor devnodes, plus the registry
    /// value reads that go with them.
    ///
    /// Hardware discovery, the EDID resolver and the watchdog's resolver each
    /// used to carry their own copy of this. They are one implementation now so
    /// that the identity a transaction is journaled against and the identity a
    /// rollback re-proves cannot drift apart.
    /// </summary>
    internal static class MonitorDevnodeReader
    {
        private const int InstanceIdCapacity = 512;
        private const int PropertyBufferBytes = 2048;

        /// <summary>
        /// Enumerates monitors that are present and have an active interface.
        /// </summary>
        internal static IList<MonitorDeviceRecord> EnumeratePresent(
            int registryAccess)
        {
            Guid interfaceGuid = DisplayTopologyNativeMethods.GuidDevInterfaceMonitor;
            IntPtr informationSet = DisplayTopologyNativeMethods.SetupDiGetClassDevs(
                ref interfaceGuid,
                null,
                IntPtr.Zero,
                DisplayTopologyNativeMethods.DIGCF_PRESENT
                    | DisplayTopologyNativeMethods.DIGCF_DEVICEINTERFACE);
            if (DisplayTopologyNativeMethods.IsInvalidHandle(informationSet))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                List<MonitorDeviceRecord> records = new List<MonitorDeviceRecord>();
                uint index = 0;
                while (true)
                {
                    SP_DEVICE_INTERFACE_DATA interfaceData =
                        new SP_DEVICE_INTERFACE_DATA();
                    interfaceData.CbSize =
                        Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));

                    bool found =
                        DisplayTopologyNativeMethods.SetupDiEnumDeviceInterfaces(
                            informationSet,
                            IntPtr.Zero,
                            ref interfaceGuid,
                            index,
                            ref interfaceData);
                    if (!found)
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == DisplayTopologyNativeMethods.ERROR_NO_MORE_ITEMS)
                        {
                            break;
                        }

                        throw new Win32Exception(error);
                    }

                    records.Add(ReadInterfaceRecord(
                        informationSet,
                        ref interfaceData,
                        registryAccess));
                    index++;
                }

                return records.AsReadOnly();
            }
            finally
            {
                DisplayTopologyNativeMethods.SetupDiDestroyDeviceInfoList(
                    informationSet);
            }
        }

        /// <summary>
        /// Enumerates monitor-class devnodes without DIGCF_PRESENT. A durable
        /// EDID transaction is allowed to resolve its original devnode while it
        /// is non-present, but never infers a registry path from a journal
        /// string.
        /// </summary>
        internal static IList<MonitorDeviceRecord> EnumerateInstalled(
            int registryAccess)
        {
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
                List<MonitorDeviceRecord> records = new List<MonitorDeviceRecord>();
                uint index = 0;
                while (true)
                {
                    SP_DEVINFO_DATA deviceInfo = new SP_DEVINFO_DATA();
                    deviceInfo.CbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                    if (!DisplayTopologyNativeMethods.SetupDiEnumDeviceInfo(
                        informationSet,
                        index,
                        ref deviceInfo))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == DisplayTopologyNativeMethods.ERROR_NO_MORE_ITEMS)
                        {
                            break;
                        }

                        throw new Win32Exception(error);
                    }

                    records.Add(ReadDeviceInfoRecord(
                        informationSet,
                        ref deviceInfo,
                        registryAccess,
                        false));
                    index++;
                }

                return records.AsReadOnly();
            }
            finally
            {
                DisplayTopologyNativeMethods.SetupDiDestroyDeviceInfoList(
                    informationSet);
            }
        }

        /// <summary>
        /// Reads a REG_BINARY value, optionally from a subkey. Returns null
        /// when the key or value is absent; a wrong value type is an error,
        /// because silently accepting one would let foreign state look like an
        /// app-owned override.
        /// </summary>
        internal static byte[] ReadBinaryValue(
            SafeRegistryHandle deviceKey,
            string subKey,
            string valueName)
        {
            if (string.IsNullOrEmpty(subKey))
            {
                return ReadBinaryValueCore(deviceKey, valueName);
            }

            SafeRegistryHandle subKeyHandle;
            int openError = DisplayTopologyNativeMethods.RegOpenKeyEx(
                deviceKey,
                subKey,
                0,
                DisplayTopologyNativeMethods.KEY_QUERY_VALUE,
                out subKeyHandle);
            if (openError != DisplayTopologyNativeMethods.ERROR_SUCCESS)
            {
                subKeyHandle.Dispose();
                if (openError == DisplayTopologyNativeMethods.ERROR_FILE_NOT_FOUND)
                {
                    return null;
                }

                throw new Win32Exception(openError);
            }

            using (subKeyHandle)
            {
                return ReadBinaryValueCore(subKeyHandle, valueName);
            }
        }

        /// <summary>
        /// Reads one REG_BINARY value, sizing it first and then re-checking the
        /// type after the data read: a value whose type changes between the two
        /// calls is refused rather than reinterpreted.
        /// </summary>
        private static byte[] ReadBinaryValueCore(
            SafeRegistryHandle key,
            string valueName)
        {
            uint type;
            uint length = 0;
            int error = DisplayTopologyNativeMethods.RegQueryValueEx(
                key,
                valueName,
                IntPtr.Zero,
                out type,
                null,
                ref length);
            if (error == DisplayTopologyNativeMethods.ERROR_FILE_NOT_FOUND)
            {
                return null;
            }

            if (error != DisplayTopologyNativeMethods.ERROR_SUCCESS)
            {
                throw new Win32Exception(error);
            }

            if (type != DisplayTopologyNativeMethods.REG_BINARY)
            {
                throw new InvalidOperationException(
                    "The registry value " + valueName + " is not REG_BINARY.");
            }

            byte[] data = new byte[length];
            error = DisplayTopologyNativeMethods.RegQueryValueEx(
                key,
                valueName,
                IntPtr.Zero,
                out type,
                data,
                ref length);
            if (error != DisplayTopologyNativeMethods.ERROR_SUCCESS)
            {
                throw new Win32Exception(error);
            }

            if (type != DisplayTopologyNativeMethods.REG_BINARY)
            {
                throw new InvalidOperationException(
                    "The registry value " + valueName +
                    " changed to a non-REG_BINARY type while it was read.");
            }

            if (data.Length != length)
            {
                Array.Resize(ref data, (int)length);
            }

            return data;
        }

        private static MonitorDeviceRecord ReadInterfaceRecord(
            IntPtr informationSet,
            ref SP_DEVICE_INTERFACE_DATA interfaceData,
            int registryAccess)
        {
            uint requiredSize;
            SP_DEVINFO_DATA deviceInfo = new SP_DEVINFO_DATA();
            deviceInfo.CbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA));

            DisplayTopologyNativeMethods.SetupDiGetDeviceInterfaceDetail(
                informationSet,
                ref interfaceData,
                IntPtr.Zero,
                0,
                out requiredSize,
                ref deviceInfo);
            int firstError = Marshal.GetLastWin32Error();
            if (requiredSize == 0 ||
                (firstError != DisplayTopologyNativeMethods.ERROR_INSUFFICIENT_BUFFER &&
                 firstError != DisplayTopologyNativeMethods.ERROR_SUCCESS))
            {
                throw new Win32Exception(firstError);
            }

            IntPtr detail = Marshal.AllocHGlobal((int)requiredSize);
            try
            {
                // SP_DEVICE_INTERFACE_DETAIL_DATA.cbSize is the size of the
                // fixed part only, and differs between 32-bit and 64-bit.
                Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                if (!DisplayTopologyNativeMethods.SetupDiGetDeviceInterfaceDetail(
                    informationSet,
                    ref interfaceData,
                    detail,
                    requiredSize,
                    out requiredSize,
                    ref deviceInfo))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                MonitorDeviceRecord record = ReadDeviceInfoRecord(
                    informationSet,
                    ref deviceInfo,
                    registryAccess,
                    true);
                record.InterfacePath = Marshal.PtrToStringUni(
                    IntPtr.Add(detail, sizeof(int)));
                return record;
            }
            finally
            {
                Marshal.FreeHGlobal(detail);
            }
        }

        internal static MonitorDeviceRecord ReadDeviceInfoRecord(
            IntPtr informationSet,
            ref SP_DEVINFO_DATA deviceInfo,
            int registryAccess,
            bool strictEdid)
        {
            MonitorDeviceRecord record = new MonitorDeviceRecord();
            record.DeviceInstanceId = ReadInstanceId(informationSet, ref deviceInfo);
            record.HardwareId = ReadFirstPropertyString(
                informationSet,
                ref deviceInfo,
                DisplayTopologyNativeMethods.SPDRP_HARDWAREID);
            record.FriendlyName = ReadFirstPropertyString(
                informationSet,
                ref deviceInfo,
                DisplayTopologyNativeMethods.SPDRP_FRIENDLYNAME);
            record.Description = ReadFirstPropertyString(
                informationSet,
                ref deviceInfo,
                DisplayTopologyNativeMethods.SPDRP_DEVICEDESC);
            record.Manufacturer = ReadFirstPropertyString(
                informationSet,
                ref deviceInfo,
                DisplayTopologyNativeMethods.SPDRP_MFG);

            using (SafeRegistryHandle deviceKey =
                DisplayTopologyNativeMethods.SetupDiOpenDevRegKey(
                    informationSet,
                    ref deviceInfo,
                    DisplayTopologyNativeMethods.DICS_FLAG_GLOBAL,
                    0,
                    DisplayTopologyNativeMethods.DIREG_DEV,
                    registryAccess))
            {
                if (!deviceKey.IsInvalid)
                {
                    try
                    {
                        record.Edid = ReadBinaryValue(deviceKey, null, "EDID");
                    }
                    catch (InvalidOperationException)
                    {
                        // An unrelated devnode with a malformed EDID value must
                        // not prevent a stored target from being found. Active
                        // enumeration keeps the strict failure, because that
                        // devnode may be the mutation target.
                        if (strictEdid)
                        {
                            throw;
                        }

                        record.Edid = null;
                    }
                }
            }

            return record;
        }

        internal static bool SameDurableDevice(
            MonitorDeviceRecord expected,
            MonitorDeviceRecord actual)
        {
            if (actual == null ||
                !string.Equals(
                    expected.DeviceInstanceId,
                    actual.DeviceInstanceId,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    expected.HardwareId,
                    actual.HardwareId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (expected.Edid == null || actual.Edid == null ||
                expected.Edid.Length < EdidBaseBlock.Length ||
                actual.Edid.Length < EdidBaseBlock.Length)
            {
                return false;
            }

            for (int index = 0; index < EdidBaseBlock.Length; index++)
            {
                if (expected.Edid[index] != actual.Edid[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static string ReadInstanceId(
            IntPtr informationSet,
            ref SP_DEVINFO_DATA deviceInfo)
        {
            int required;
            StringBuilder builder = new StringBuilder(InstanceIdCapacity);
            if (!DisplayTopologyNativeMethods.SetupDiGetDeviceInstanceId(
                informationSet,
                ref deviceInfo,
                builder,
                builder.Capacity,
                out required))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return builder.ToString();
        }

        /// <summary>
        /// Reads the first string of a REG_MULTI_SZ device property. An absent
        /// property is not an error: several of these are optional.
        /// </summary>
        private static string ReadFirstPropertyString(
            IntPtr informationSet,
            ref SP_DEVINFO_DATA deviceInfo,
            uint property)
        {
            uint type;
            uint required;
            byte[] buffer = new byte[PropertyBufferBytes];
            if (!DisplayTopologyNativeMethods.SetupDiGetDeviceRegistryProperty(
                informationSet,
                ref deviceInfo,
                property,
                out type,
                buffer,
                (uint)buffer.Length,
                out required))
            {
                return null;
            }

            int length = (int)Math.Min(required, (uint)buffer.Length);
            string value = Encoding.Unicode.GetString(buffer, 0, length);
            int terminator = value.IndexOf('\0');
            return terminator < 0 ? value : value.Substring(0, terminator);
        }
    }
}
