using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MacBookEco.Platform.Windows
{
    /// <summary>
    /// Interop that can change system state, plus the firmware and adapter
    /// queries that only the elevated helper and the tray need.
    ///
    /// Read-only display-topology interop lives in
    /// DisplayTopologyNativeMethods, which every executable compiles. This file
    /// is excluded from the watchdog's source set so that the watchdog provably
    /// cannot write the registry or touch a power scheme.
    /// </summary>
    internal static class NativeMethods
    {
        internal const int ERROR_MORE_DATA = 234;
        internal const int ERROR_FILE_NOT_FOUND = 2;
        internal const int ERROR_NOT_FOUND = 1168;
        internal const int ERROR_SUCCESS = 0;

        internal const int KEY_SET_VALUE = 0x0002;
        internal const int KEY_CREATE_SUB_KEY = 0x0004;
        internal const int KEY_WRITE = 0x20006;
        internal const uint REG_OPTION_NON_VOLATILE = 0;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumDisplayDevices(
            string deviceName,
            uint deviceNumber,
            ref DISPLAY_DEVICE displayDevice,
            uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint GetSystemFirmwareTable(
            uint firmwareTableProviderSignature,
            uint firmwareTableId,
            IntPtr firmwareTableBuffer,
            uint bufferSize);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int RegCreateKeyEx(
            SafeRegistryHandle key,
            string subKey,
            int reserved,
            string keyClass,
            uint options,
            int samDesired,
            IntPtr securityAttributes,
            out SafeRegistryHandle result,
            out uint disposition);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int RegSetValueEx(
            SafeRegistryHandle key,
            string valueName,
            int reserved,
            uint type,
            byte[] data,
            int dataLength);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int RegDeleteValue(
            SafeRegistryHandle key,
            string valueName);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern int RegFlushKey(SafeRegistryHandle key);

        [DllImport("powrprof.dll", SetLastError = true)]
        internal static extern uint PowerGetActiveScheme(
            IntPtr userRootPowerKey,
            out IntPtr activePolicyGuid);

        [DllImport("powrprof.dll", SetLastError = true)]
        internal static extern uint PowerDuplicateScheme(
            IntPtr rootPowerKey,
            ref Guid sourceSchemeGuid,
            ref IntPtr destinationSchemeGuid);

        [DllImport("powrprof.dll", SetLastError = true)]
        internal static extern uint PowerSetActiveScheme(
            IntPtr userRootPowerKey,
            ref Guid schemeGuid);

        [DllImport("powrprof.dll", SetLastError = true)]
        internal static extern uint PowerReadACValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subgroupOfPowerSettingsGuid,
            ref Guid powerSettingGuid,
            out uint acValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        internal static extern uint PowerReadDCValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subgroupOfPowerSettingsGuid,
            ref Guid powerSettingGuid,
            out uint dcValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        internal static extern uint PowerWriteACValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subgroupOfPowerSettingsGuid,
            ref Guid powerSettingGuid,
            uint acValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        internal static extern uint PowerWriteDCValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subgroupOfPowerSettingsGuid,
            ref Guid powerSettingGuid,
            uint dcValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        internal static extern uint PowerWriteFriendlyName(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            IntPtr subgroupOfPowerSettingsGuid,
            IntPtr powerSettingGuid,
            byte[] buffer,
            uint bufferSize);

        [DllImport("powrprof.dll", SetLastError = true)]
        internal static extern uint PowerReadFriendlyName(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            IntPtr subgroupOfPowerSettingsGuid,
            IntPtr powerSettingGuid,
            byte[] buffer,
            ref uint bufferSize);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr LocalFree(IntPtr memory);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DISPLAY_DEVICE
    {
        internal int Cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string DeviceString;

        internal int StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string DeviceKey;
    }
}
