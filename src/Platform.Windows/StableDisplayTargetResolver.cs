using System;
using System.Collections.Generic;
using MacBookEco.Core;

namespace MacBookEco.Platform.Windows
{
    /// <summary>
    /// Narrow, read-only live display resolver shared by the application and
    /// the independent watchdog. Its durable input is a MonitorIdentity;
    /// adapter LUID, CCD IDs and DISPLAYn are output-only ephemeral facts.
    /// It deliberately contains no EDID override or power mutation surface.
    /// </summary>
    internal sealed class StableDisplayTargetResolver
    {
        internal StableDisplayTarget ResolveActive()
        {
            ActiveDisplayPath path = ChooseInternalPath(
                DisplayTopologyReader.ReadActivePaths());
            MonitorDeviceRecord record = FindPresentMonitor(path.MonitorDevicePath);
            return CreateTarget(path, CreateIdentity(record));
        }

        internal StableDisplayTarget ResolveActive(MonitorIdentity expectedIdentity)
        {
            if (expectedIdentity == null)
            {
                throw new ArgumentNullException(nameof(expectedIdentity));
            }

            StableDisplayTarget target = ResolveActive();
            if (!target.Identity.Equals(expectedIdentity))
            {
                throw new InvalidOperationException(
                    "The active internal display does not match the durable watchdog target identity.");
            }

            return target;
        }

        /// <summary>
        /// A rollback must never be applied to a display it cannot prove is the
        /// right one, so both "no candidate" and "more than one candidate" are
        /// refusals here.
        /// </summary>
        private static ActiveDisplayPath ChooseInternalPath(
            IList<ActiveDisplayPath> paths)
        {
            ActiveDisplayPath selected;
            string detail;
            if (InternalPanelSelector.Select(paths, out selected, out detail) !=
                InternalPanelSelectionResult.Selected)
            {
                throw new InvalidOperationException(detail);
            }

            return selected;
        }

        private static StableDisplayTarget CreateTarget(
            ActiveDisplayPath path,
            MonitorIdentity identity)
        {
            if (path.RefreshRateNumerator == 0 || path.RefreshRateDenominator == 0)
            {
                throw new InvalidOperationException(
                    "The active internal display has no valid CCD rational refresh rate.");
            }

            return new StableDisplayTarget(
                identity,
                new DisplayEndpoint(
                    path.AdapterLuidValue,
                    path.SourceId,
                    path.TargetId,
                    path.GdiDeviceName),
                path.RefreshRateNumerator,
                path.RefreshRateDenominator,
                path.PixelRate,
                path.ActiveWidth,
                path.ActiveHeight,
                path.TotalWidth,
                path.TotalHeight);
        }

        private static MonitorIdentity CreateIdentity(MonitorDeviceRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.DeviceInstanceId))
            {
                throw new InvalidOperationException(
                    "The active monitor devnode has no durable instance identity.");
            }

            if (string.IsNullOrEmpty(record.HardwareId))
            {
                throw new InvalidOperationException(
                    "The active monitor devnode has no hardware identity.");
            }

            if (record.Edid == null || record.Edid.Length < EdidBaseBlock.Length)
            {
                throw new InvalidOperationException(
                    "The active monitor devnode has no complete base EDID.");
            }

            byte[] baseBlock = new byte[EdidBaseBlock.Length];
            Buffer.BlockCopy(record.Edid, 0, baseBlock, 0, baseBlock.Length);
            return MonitorIdentity.FromExactBaseEdid(
                record.DeviceInstanceId,
                HardwareSnapshot.NormalizePanelHardwareId(record.HardwareId),
                new EdidBaseBlock(baseBlock));
        }

        private static MonitorDeviceRecord FindPresentMonitor(string interfacePath)
        {
            if (string.IsNullOrEmpty(interfacePath))
            {
                throw new InvalidOperationException(
                    "The active CCD target has no monitor interface path.");
            }

            IList<MonitorDeviceRecord> records =
                MonitorDevnodeReader.EnumeratePresent(
                    DisplayTopologyNativeMethods.KEY_READ);

            MonitorDeviceRecord match = null;
            for (int index = 0; index < records.Count; index++)
            {
                if (!DisplayTopologyReader.DevicePathsEqual(
                    records[index].InterfacePath,
                    interfacePath))
                {
                    continue;
                }

                if (match != null)
                {
                    throw new InvalidOperationException(
                        "More than one present monitor devnode matches the active CCD target.");
                }

                match = records[index];
            }

            if (match == null)
            {
                throw new InvalidOperationException(
                    "The active CCD target could not be mapped to a present monitor devnode.");
            }

            return match;
        }
    }

    /// <summary>
    /// Live facts produced after an exact MonitorIdentity proof. None of these
    /// endpoint fields belongs in durable watchdog state.
    /// </summary>
    internal sealed class StableDisplayTarget
    {
        internal StableDisplayTarget(
            MonitorIdentity identity,
            DisplayEndpoint endpoint,
            uint refreshRateNumerator,
            uint refreshRateDenominator,
            ulong pixelRate = 0,
            uint activeWidth = 0,
            uint activeHeight = 0,
            uint totalWidth = 0,
            uint totalHeight = 0)
        {
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            if (refreshRateNumerator == 0 || refreshRateDenominator == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(refreshRateNumerator),
                    "A non-zero CCD rational refresh rate is required.");
            }

            Identity = identity;
            Endpoint = endpoint;
            RefreshRateNumerator = refreshRateNumerator;
            RefreshRateDenominator = refreshRateDenominator;
            PixelRate = pixelRate;
            ActiveWidth = activeWidth;
            ActiveHeight = activeHeight;
            TotalWidth = totalWidth;
            TotalHeight = totalHeight;
        }

        internal MonitorIdentity Identity { get; private set; }

        internal DisplayEndpoint Endpoint { get; private set; }

        internal uint RefreshRateNumerator { get; private set; }

        internal uint RefreshRateDenominator { get; private set; }

        internal ulong PixelRate { get; private set; }

        internal uint ActiveWidth { get; private set; }

        internal uint ActiveHeight { get; private set; }

        internal uint TotalWidth { get; private set; }

        internal uint TotalHeight { get; private set; }
    }
}
