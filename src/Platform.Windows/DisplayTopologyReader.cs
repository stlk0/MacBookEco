using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MacBookEco.Platform.Windows
{
    /// <summary>
    /// One active CCD path, flattened into the facts the rest of the code
    /// needs. Everything here is ephemeral: adapter LUID, source and target
    /// IDs and the GDI name can all change when the topology changes, so none
    /// of it belongs in durable state.
    /// </summary>
    internal sealed class ActiveDisplayPath
    {
        internal string GdiDeviceName;
        internal string MonitorFriendlyName;
        internal string MonitorDevicePath;
        internal string AdapterLuid;
        internal ulong AdapterLuidValue;
        internal uint SourceId;
        internal uint TargetId;
        internal uint RefreshRateNumerator;
        internal uint RefreshRateDenominator;
        internal ulong PixelRate;
        internal uint ActiveWidth;
        internal uint ActiveHeight;
        internal uint TotalWidth;
        internal uint TotalHeight;
        internal uint OutputTechnology;
        internal bool IsInternal;
    }

    /// <summary>
    /// Reads the active display topology through CCD. Read-only, and shared by
    /// hardware discovery, the display-action resolvers and the watchdog, so
    /// all of them agree on what "the active internal panel" means.
    /// </summary>
    internal static class DisplayTopologyReader
    {
        private const int TopologyRetryCount = 3;

        /// <summary>
        /// Enumerates every active path. The retry exists because the topology
        /// can change between sizing the buffers and filling them; Windows
        /// reports that as ERROR_INSUFFICIENT_BUFFER.
        /// </summary>
        internal static IList<ActiveDisplayPath> ReadActivePaths()
        {
            for (int attempt = 0; attempt < TopologyRetryCount; attempt++)
            {
                uint pathCount;
                uint modeCount;
                int error = DisplayTopologyNativeMethods.GetDisplayConfigBufferSizes(
                    DisplayTopologyNativeMethods.QDC_ONLY_ACTIVE_PATHS,
                    out pathCount,
                    out modeCount);
                if (error != DisplayTopologyNativeMethods.ERROR_SUCCESS)
                {
                    throw new Win32Exception(
                        error,
                        "GetDisplayConfigBufferSizes failed.");
                }

                DISPLAYCONFIG_PATH_INFO[] paths =
                    new DISPLAYCONFIG_PATH_INFO[pathCount];
                DISPLAYCONFIG_MODE_INFO[] modes =
                    new DISPLAYCONFIG_MODE_INFO[modeCount];

                error = DisplayTopologyNativeMethods.QueryDisplayConfig(
                    DisplayTopologyNativeMethods.QDC_ONLY_ACTIVE_PATHS,
                    ref pathCount,
                    paths,
                    ref modeCount,
                    modes,
                    IntPtr.Zero);
                if (error == DisplayTopologyNativeMethods.ERROR_INSUFFICIENT_BUFFER)
                {
                    continue;
                }

                if (error != DisplayTopologyNativeMethods.ERROR_SUCCESS)
                {
                    throw new Win32Exception(error, "QueryDisplayConfig failed.");
                }

                List<ActiveDisplayPath> result = new List<ActiveDisplayPath>();
                for (uint index = 0; index < pathCount; index++)
                {
                    result.Add(ReadPath(paths[index], modes));
                }

                return result.AsReadOnly();
            }

            throw new InvalidOperationException(
                "The active display topology changed repeatedly during discovery.");
        }

        internal static bool DevicePathsEqual(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            {
                return false;
            }

            return string.Equals(
                left.TrimEnd('\\'),
                right.TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsEmbeddedOutput(uint technology)
        {
            return technology ==
                    DisplayTopologyNativeMethods.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS ||
                technology ==
                    DisplayTopologyNativeMethods.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED ||
                technology ==
                    DisplayTopologyNativeMethods.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED ||
                technology ==
                    DisplayTopologyNativeMethods.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL;
        }

        private static ActiveDisplayPath ReadPath(
            DISPLAYCONFIG_PATH_INFO path,
            DISPLAYCONFIG_MODE_INFO[] modes)
        {
            DISPLAYCONFIG_SOURCE_DEVICE_NAME source =
                new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
            source.Header.Type =
                DisplayTopologyNativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
            source.Header.Size = (uint)Marshal.SizeOf(
                typeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME));
            source.Header.AdapterId = path.SourceInfo.AdapterId;
            source.Header.Id = path.SourceInfo.Id;
            int error = DisplayTopologyNativeMethods.DisplayConfigGetDeviceInfo(
                ref source);
            if (error != DisplayTopologyNativeMethods.ERROR_SUCCESS)
            {
                throw new Win32Exception(
                    error,
                    "Could not resolve the CCD source name.");
            }

            DISPLAYCONFIG_TARGET_DEVICE_NAME target =
                new DISPLAYCONFIG_TARGET_DEVICE_NAME();
            target.Header.Type =
                DisplayTopologyNativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
            target.Header.Size = (uint)Marshal.SizeOf(
                typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME));
            target.Header.AdapterId = path.TargetInfo.AdapterId;
            target.Header.Id = path.TargetInfo.Id;
            error = DisplayTopologyNativeMethods.DisplayConfigGetDeviceInfo(
                ref target);
            if (error != DisplayTopologyNativeMethods.ERROR_SUCCESS)
            {
                throw new Win32Exception(
                    error,
                    "Could not resolve the CCD target name.");
            }

            ActiveDisplayPath result = new ActiveDisplayPath();
            result.GdiDeviceName = source.ViewGdiDeviceName;
            result.MonitorFriendlyName = target.MonitorFriendlyDeviceName;
            result.MonitorDevicePath = target.MonitorDevicePath;
            result.AdapterLuid = path.SourceInfo.AdapterId.ToString();
            result.AdapterLuidValue = path.SourceInfo.AdapterId.ToUInt64();
            result.SourceId = path.SourceInfo.Id;
            result.TargetId = path.TargetInfo.Id;
            result.RefreshRateNumerator = path.TargetInfo.RefreshRate.Numerator;
            result.RefreshRateDenominator = path.TargetInfo.RefreshRate.Denominator;
            PopulateTargetSignal(path, modes, result);
            result.OutputTechnology = path.TargetInfo.OutputTechnology;
            result.IsInternal = IsEmbeddedOutput(path.TargetInfo.OutputTechnology);
            return result;
        }

        internal static void PopulateTargetSignal(
            DISPLAYCONFIG_PATH_INFO path,
            DISPLAYCONFIG_MODE_INFO[] modes,
            ActiveDisplayPath result)
        {
            uint index = (path.Flags &
                    DisplayTopologyNativeMethods
                        .DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE) != 0
                ? path.TargetInfo.ModeInfoIdx >> 16
                : path.TargetInfo.ModeInfoIdx;
            if (modes == null || index >= (uint)modes.Length)
            {
                return;
            }

            DISPLAYCONFIG_MODE_INFO mode = modes[index];
            if (mode.InfoType !=
                    DisplayTopologyNativeMethods.DISPLAYCONFIG_MODE_INFO_TYPE_TARGET ||
                mode.Id != path.TargetInfo.Id ||
                mode.AdapterId.ToUInt64() != path.TargetInfo.AdapterId.ToUInt64())
            {
                return;
            }

            DISPLAYCONFIG_VIDEO_SIGNAL_INFO signal =
                mode.ModeInfo.TargetMode.TargetVideoSignalInfo;
            result.PixelRate = signal.PixelRate;
            result.ActiveWidth = signal.ActiveSize.Cx;
            result.ActiveHeight = signal.ActiveSize.Cy;
            result.TotalWidth = signal.TotalSize.Cx;
            result.TotalHeight = signal.TotalSize.Cy;
        }
    }
}
