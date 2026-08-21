using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MacBookEco.Platform.Windows
{
    /// <summary>
    /// Read-only interop for discovering the display topology: CCD path
    /// enumeration, monitor devnode enumeration through SetupAPI, and registry
    /// queries.
    ///
    /// This surface is compiled into every executable, including the
    /// unelevated watchdog, so its contents are the audit boundary: it must
    /// declare nothing that can change system state. Registry writes, device
    /// restarts, power-scheme APIs and display-mode changes live in
    /// NativeMethods and DisplayModeNativeMethods, which the watchdog does not
    /// compile.
    /// </summary>
    internal static class DisplayTopologyNativeMethods
    {
        internal const int ERROR_SUCCESS = 0;
        internal const int ERROR_INSUFFICIENT_BUFFER = 122;
        internal const int ERROR_NO_MORE_ITEMS = 259;
        internal const int ERROR_FILE_NOT_FOUND = 2;

        internal const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
        internal const uint DISPLAYCONFIG_MODE_INFO_TYPE_TARGET = 2;
        internal const uint DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE = 0x00000008;

        internal const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
        internal const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;

        internal const uint DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS = 6;
        internal const uint DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED = 11;
        internal const uint DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED = 13;
        internal const uint DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL = 0x80000000;

        internal const uint DIGCF_PRESENT = 0x00000002;
        internal const uint DIGCF_DEVICEINTERFACE = 0x00000010;
        internal const uint SPDRP_DEVICEDESC = 0x00000000;
        internal const uint SPDRP_HARDWAREID = 0x00000001;
        internal const uint SPDRP_MFG = 0x0000000B;
        internal const uint SPDRP_FRIENDLYNAME = 0x0000000C;
        internal const uint DICS_FLAG_GLOBAL = 0x00000001;
        internal const uint DIREG_DEV = 0x00000001;

        internal const int KEY_QUERY_VALUE = 0x0001;
        internal const int KEY_READ = 0x20019;
        internal const uint REG_BINARY = 3;

        internal static readonly Guid GuidDevInterfaceMonitor =
            new Guid("E6F07B5F-EE97-4A90-B076-33F57BF4EAA7");

        // GUID_DEVCLASS_MONITOR. Unlike GUID_DEVINTERFACE_MONITOR, the
        // device-setup class includes installed non-present devnodes. That
        // distinction is required for a durable restore path: an EDID journal
        // describes a monitor devnode, never an active CCD route.
        internal static readonly Guid GuidDevClassMonitor =
            new Guid("4D36E96E-E325-11CE-BFC1-08002BE10318");

        internal static bool IsInvalidHandle(IntPtr handle)
        {
            return handle == IntPtr.Zero || handle == new IntPtr(-1);
        }

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int GetDisplayConfigBufferSizes(
            uint flags,
            out uint pathCount,
            out uint modeCount);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int QueryDisplayConfig(
            uint flags,
            ref uint pathCount,
            [Out] DISPLAYCONFIG_PATH_INFO[] paths,
            ref uint modeCount,
            [Out] DISPLAYCONFIG_MODE_INFO[] modes,
            IntPtr currentTopologyId);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int DisplayConfigGetDeviceInfo(
            ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int DisplayConfigGetDeviceInfo(
            ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);


        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid,
            string enumerator,
            IntPtr parentWindow,
            uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr deviceInfoSet,
            IntPtr deviceInfoData,
            ref Guid interfaceClassGuid,
            uint memberIndex,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiEnumDeviceInfo(
            IntPtr deviceInfoSet,
            uint memberIndex,
            ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiOpenDeviceInfo(
            IntPtr deviceInfoSet,
            string deviceInstanceId,
            IntPtr parentWindow,
            uint openFlags,
            ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
            IntPtr deviceInterfaceDetailData,
            uint deviceInterfaceDetailDataSize,
            out uint requiredSize,
            ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceInstanceId(
            IntPtr deviceInfoSet,
            ref SP_DEVINFO_DATA deviceInfoData,
            StringBuilder deviceInstanceId,
            int deviceInstanceIdSize,
            out int requiredSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceRegistryProperty(
            IntPtr deviceInfoSet,
            ref SP_DEVINFO_DATA deviceInfoData,
            uint property,
            out uint propertyRegDataType,
            byte[] propertyBuffer,
            uint propertyBufferSize,
            out uint requiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        internal static extern SafeRegistryHandle SetupDiOpenDevRegKey(
            IntPtr deviceInfoSet,
            ref SP_DEVINFO_DATA deviceInfoData,
            uint scope,
            uint hardwareProfile,
            uint keyType,
            int samDesired);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int RegOpenKeyEx(
            SafeRegistryHandle key,
            string subKey,
            uint options,
            int samDesired,
            out SafeRegistryHandle result);

        // Kept in preference to RegistryKey.GetValue: the second read below
        // rechecks the value type, so a value that changes type between the
        // sizing call and the data call is refused rather than reinterpreted.
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int RegQueryValueEx(
            SafeRegistryHandle key,
            string valueName,
            IntPtr reserved,
            out uint type,
            byte[] data,
            ref uint dataLength);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LUID
    {
        internal uint LowPart;
        internal int HighPart;

        internal ulong ToUInt64()
        {
            return ((ulong)(uint)HighPart << 32) | LowPart;
        }

        public override string ToString()
        {
            return HighPart.ToString("X8") + ":" + LowPart.ToString("X8");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_RATIONAL
    {
        internal uint Numerator;
        internal uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_2DREGION
    {
        internal uint Cx;
        internal uint Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
    {
        internal ulong PixelRate;
        internal DISPLAYCONFIG_RATIONAL HSyncFrequency;
        internal DISPLAYCONFIG_RATIONAL VSyncFrequency;
        internal DISPLAYCONFIG_2DREGION ActiveSize;
        internal DISPLAYCONFIG_2DREGION TotalSize;
        internal uint VideoStandard;
        internal uint ScanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_TARGET_MODE
    {
        internal DISPLAYCONFIG_VIDEO_SIGNAL_INFO TargetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINTL
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_SOURCE_MODE
    {
        internal uint Width;
        internal uint Height;
        internal uint PixelFormat;
        internal POINTL Position;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    internal struct DISPLAYCONFIG_MODE_INFO_UNION
    {
        [FieldOffset(0)]
        internal DISPLAYCONFIG_TARGET_MODE TargetMode;

        [FieldOffset(0)]
        internal DISPLAYCONFIG_SOURCE_MODE SourceMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_MODE_INFO
    {
        internal uint InfoType;
        internal uint Id;
        internal LUID AdapterId;
        internal DISPLAYCONFIG_MODE_INFO_UNION ModeInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        internal LUID AdapterId;
        internal uint Id;
        internal uint ModeInfoIdx;
        internal uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        internal LUID AdapterId;
        internal uint Id;
        internal uint ModeInfoIdx;
        internal uint OutputTechnology;
        internal uint Rotation;
        internal uint Scaling;
        internal DISPLAYCONFIG_RATIONAL RefreshRate;
        internal uint ScanLineOrdering;

        [MarshalAs(UnmanagedType.Bool)]
        internal bool TargetAvailable;

        internal uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_PATH_INFO
    {
        internal DISPLAYCONFIG_PATH_SOURCE_INFO SourceInfo;
        internal DISPLAYCONFIG_PATH_TARGET_INFO TargetInfo;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        internal uint Type;
        internal uint Size;
        internal LUID AdapterId;
        internal uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        internal DISPLAYCONFIG_DEVICE_INFO_HEADER Header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        internal DISPLAYCONFIG_DEVICE_INFO_HEADER Header;
        internal uint Flags;
        internal uint OutputTechnology;
        internal ushort EdidManufactureId;
        internal ushort EdidProductCodeId;
        internal uint ConnectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        internal string MonitorFriendlyDeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string MonitorDevicePath;
    }


    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_DEVICE_INTERFACE_DATA
    {
        internal int CbSize;
        internal Guid InterfaceClassGuid;
        internal int Flags;
        internal IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_DEVINFO_DATA
    {
        internal int CbSize;
        internal Guid ClassGuid;
        internal uint DevInst;
        internal IntPtr Reserved;
    }
}
