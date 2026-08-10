using System;
using System.Collections.Generic;
using System.IO;
using MacBookEco.Core;
using Microsoft.Win32.SafeHandles;

namespace MacBookEco.Platform.Windows
{
    /// <summary>
    /// Resolves a durable monitor identity to a live Windows devnode.  CCD
    /// routes are intentionally used only by ResolveActive: a journal stores
    /// no adapter, DISPLAYn name, or registry path, so restore can reopen the
    /// original installed devnode even when it is currently non-present.
    /// </summary>
    internal sealed class InternalDisplayTargetResolver
    {
        internal ResolvedMonitorTarget ResolveActive()
        {
            ActiveDisplayPath internalPath = ChooseUniqueActiveInternalPath(
                DisplayTopologyReader.ReadActivePaths());

            IList<MonitorDeviceRecord> records =
                MonitorDevnodeReader.EnumeratePresent(
                    DisplayTopologyNativeMethods.KEY_READ);
            MonitorDeviceRecord match = null;
            int index;
            for (index = 0; index < records.Count; index++)
            {
                MonitorDeviceRecord candidate = records[index];
                if (!DisplayTopologyReader.DevicePathsEqual(
                    candidate.InterfacePath,
                    internalPath.MonitorDevicePath))
                {
                    continue;
                }

                if (match != null)
                {
                    throw new SecureStateConflictException(
                        "More than one present monitor devnode matches the active internal CCD target.");
                }

                match = candidate;
            }

            if (match == null)
            {
                throw new InvalidOperationException(
                    "The active internal CCD target could not be mapped to a monitor devnode.");
            }

            return ResolvedMonitorTarget.FromRecord(
                match,
                new DisplayEndpoint(
                    internalPath.AdapterLuidValue,
                    internalPath.SourceId,
                    internalPath.TargetId,
                    internalPath.GdiDeviceName),
                internalPath.RefreshRateNumerator,
                internalPath.RefreshRateDenominator);
        }

        /// <summary>
        /// Re-resolves the current embedded target and proves that it is the
        /// same physical monitor recorded by a prior display transaction.
        /// DISPLAYn, adapter LUIDs and CCD source/target IDs are deliberately
        /// not comparison inputs: all of them may change while the watchdog
        /// is armed.
        /// </summary>
        internal ResolvedMonitorTarget ResolveActive(
            MonitorIdentity expectedIdentity)
        {
            if (expectedIdentity == null)
            {
                throw new ArgumentNullException(nameof(expectedIdentity));
            }

            ResolvedMonitorTarget target = ResolveActive();
            if (!target.MatchesIdentity(expectedIdentity))
            {
                throw new SecureStateConflictException(
                    "The currently active internal display does not match the watchdog target identity.");
            }

            return target;
        }

        /// <summary>
        /// Resolves a previously journaled target across all installed monitor
        /// devnodes, including non-present ones.  An active CCD path is not
        /// consulted here.
        /// </summary>
        internal ResolvedMonitorTarget ResolveStoredForRestore(
            EdidTargetIdentity identity)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));

            return ResolveStoredForRestore(identity.Monitor, null);
        }

        /// <summary>
        /// Resolves a durable monitor identity directly.  The profile ID is
        /// intentionally not an OS lookup input; profile policy is checked by
        /// the transaction coordinator after this exact devnode proof.
        /// </summary>
        internal ResolvedMonitorTarget ResolveStoredForRestore(
            MonitorIdentity identity)
        {
            return ResolveStoredForRestore(identity, null);
        }

        /// <summary>
        /// Windows may expose the installed EDID override as the devnode EDID
        /// after a restart. The journal-owned override digest is therefore an
        /// acceptable alternate fingerprint, but only for the same canonical
        /// instance, hardware ID and manufacturer. No other alternate value
        /// is ever inferred or adopted.
        /// </summary>
        internal ResolvedMonitorTarget ResolveStoredForRestore(
            MonitorIdentity identity,
            Sha256Digest ownedOverrideHash)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));

            IList<MonitorDeviceRecord> records =
                MonitorDevnodeReader.EnumerateInstalled(
                    DisplayTopologyNativeMethods.KEY_READ);
            MonitorDeviceRecord match = null;
            bool foundSameInstance = false;
            int index;
            for (index = 0; index < records.Count; index++)
            {
                MonitorDeviceRecord candidate = records[index];
                if (string.Equals(
                    NormalizeInstanceId(candidate.DeviceInstanceId),
                    identity.MonitorInstanceId,
                    StringComparison.Ordinal))
                {
                    foundSameInstance = true;
                }

                if (!MatchesIdentity(candidate, identity, ownedOverrideHash))
                    continue;

                if (match != null)
                {
                    throw new SecureStateConflictException(
                        "More than one installed monitor devnode matches the durable EDID identity.");
                }

                match = candidate;
            }

            if (match == null)
            {
                throw new SecureStateConflictException(
                    foundSameInstance
                        ? "The journaled monitor devnode no longer matches its durable EDID identity."
                        : "The journaled monitor devnode was not found among installed present or non-present monitors.");
            }

            return ResolvedMonitorTarget.FromRecord(match, null, 0, 0);
        }

        private static bool MatchesIdentity(
            MonitorDeviceRecord record,
            MonitorIdentity identity,
            Sha256Digest ownedOverrideHash)
        {
            if (record == null ||
                !string.Equals(
                    NormalizeInstanceId(record.DeviceInstanceId),
                    identity.MonitorInstanceId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    NormalizeHardwareId(record.HardwareId),
                    identity.PanelHardwareId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                EdidBaseBlock baseBlock = HardwareDiscoveryService.CreateCoreEdid(
                    record.Edid);
                return string.Equals(
                        baseBlock.HardwareId,
                        identity.PanelHardwareId,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        baseBlock.ManufacturerCode,
                        identity.ManufacturerCode,
                        StringComparison.Ordinal) &&
                    MatchesFingerprint(
                        Sha256Digest.Compute(baseBlock.ToByteArray()),
                        identity.EdidFingerprint,
                        ownedOverrideHash);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static bool MatchesFingerprint(
            Sha256Digest actual,
            Sha256Digest original,
            Sha256Digest ownedOverride)
        {
            return actual != null &&
                (actual.Equals(original) ||
                 (ownedOverride != null && actual.Equals(ownedOverride)));
        }

        private static string NormalizeInstanceId(string value)
        {
            return string.IsNullOrEmpty(value)
                ? null
                : value.Trim().ToUpperInvariant();
        }

        private static string NormalizeHardwareId(string value)
        {
            return string.IsNullOrEmpty(value)
                ? null
                : HardwareSnapshot.NormalizePanelHardwareId(value);
        }

        /// <summary>
        /// This resolver feeds EDID mutation, so an ambiguous topology is a
        /// conflict rather than a warning: two candidate panels means the
        /// wrong one could receive an override.
        /// </summary>
        private static ActiveDisplayPath ChooseUniqueActiveInternalPath(
            IList<ActiveDisplayPath> paths)
        {
            ActiveDisplayPath selected;
            string detail;
            switch (InternalPanelSelector.Select(paths, out selected, out detail))
            {
                case InternalPanelSelectionResult.Selected:
                    return selected;
                case InternalPanelSelectionResult.Ambiguous:
                    throw new SecureStateConflictException(detail);
                default:
                    throw new InvalidOperationException(detail);
            }
        }
    }

    /// <summary>
    /// A current, verified view of one monitor devnode.  It retains durable
    /// identity facts plus an optional ephemeral endpoint.  Each registry
    /// operation reopens the devnode through SetupAPI and rechecks the
    /// fingerprint before receiving an HKEY; no journal-supplied path is ever
    /// opened directly.
    /// </summary>
    internal sealed class ResolvedMonitorTarget
    {
        private readonly MonitorDeviceRecord expectedRecord;
        private readonly byte[] baseEdid;

        private ResolvedMonitorTarget(
            MonitorDeviceRecord record,
            EdidBaseBlock parsedEdid,
            DisplayEndpoint endpoint,
            uint refreshRateNumerator,
            uint refreshRateDenominator)
        {
            expectedRecord = CopyRecord(record);
            baseEdid = parsedEdid.ToByteArray();
            DeviceInstanceId = record.DeviceInstanceId.Trim().ToUpperInvariant();
            HardwareId = HardwareSnapshot.NormalizePanelHardwareId(
                record.HardwareId);
            ManufacturerCode = parsedEdid.ManufacturerCode;
            MonitorIdentity identity = Core.MonitorIdentity.FromExactBaseEdid(
                DeviceInstanceId,
                HardwareId,
                parsedEdid);
            MonitorIdentity = identity;

            // Taken from the identity rather than hashed a second time, so the
            // wire field and the identity cannot disagree.
            BaseEdidHash = identity.EdidFingerprint;
            Endpoint = endpoint;
            RefreshRateNumerator = refreshRateNumerator;
            RefreshRateDenominator = refreshRateDenominator;
        }

        internal string DeviceInstanceId { get; private set; }

        internal string HardwareId { get; private set; }

        internal string ManufacturerCode { get; private set; }

        /// <summary>
        /// This is the immutable base-block hash used by the journal wire
        /// field. It intentionally preserves that persisted meaning.
        /// </summary>
        internal Sha256Digest BaseEdidHash { get; private set; }

        internal MonitorIdentity MonitorIdentity { get; private set; }

        internal DisplayEndpoint Endpoint { get; private set; }

        /// <summary>
        /// Current CCD rational refresh for an active endpoint.  Stored-only
        /// restore resolution has no active path and therefore exposes zeroes;
        /// a caller that needs a live mode key must require a non-zero pair.
        /// </summary>
        internal uint RefreshRateNumerator { get; private set; }

        internal uint RefreshRateDenominator { get; private set; }

        internal byte[] BaseEdid => (byte[])baseEdid.Clone();

        internal static ResolvedMonitorTarget FromRecord(
            MonitorDeviceRecord record,
            DisplayEndpoint endpoint,
            uint refreshRateNumerator,
            uint refreshRateDenominator)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrEmpty(record.DeviceInstanceId) ||
                string.IsNullOrEmpty(record.HardwareId))
            {
                throw new SecureStateConflictException(
                    "The resolved monitor lacks a complete durable device identity.");
            }

            try
            {
                EdidBaseBlock parsedEdid = HardwareDiscoveryService.CreateCoreEdid(
                    record.Edid);
                string hardwareId = HardwareSnapshot.NormalizePanelHardwareId(
                    record.HardwareId);
                if (!string.Equals(
                    parsedEdid.HardwareId,
                    hardwareId,
                    StringComparison.Ordinal))
                {
                    throw new SecureStateConflictException(
                        "The monitor SetupAPI hardware ID and EDID hardware ID do not agree.");
                }

                return new ResolvedMonitorTarget(
                    record,
                    parsedEdid,
                    endpoint,
                    refreshRateNumerator,
                    refreshRateDenominator);
            }
            catch (SecureStateConflictException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new SecureStateConflictException(
                    "The resolved monitor has no valid EDID base block.",
                    ex);
            }
        }

        internal EdidTargetIdentity CreateIdentity(string profileId)
        {
            return new EdidTargetIdentity(profileId, MonitorIdentity);
        }

        internal bool MatchesIdentity(EdidTargetIdentity identity)
        {
            return identity != null && MatchesIdentity(identity.Monitor);
        }

        internal bool MatchesIdentity(MonitorIdentity identity)
        {
            return MatchesIdentity(identity, null);
        }

        internal bool MatchesIdentity(
            MonitorIdentity identity,
            Sha256Digest ownedOverrideHash)
        {
            return identity != null &&
                string.Equals(
                    DeviceInstanceId,
                    identity.MonitorInstanceId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    HardwareId,
                    identity.PanelHardwareId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    ManufacturerCode,
                    identity.ManufacturerCode,
                    StringComparison.Ordinal) &&
                (BaseEdidHash.Equals(identity.EdidFingerprint) ||
                 (ownedOverrideHash != null &&
                  BaseEdidHash.Equals(ownedOverrideHash)));
        }

        internal byte[] ReadOverride()
        {
            try
            {
                using (SafeRegistryHandle deviceKey =
                    OpenParameters(DisplayTopologyNativeMethods.KEY_READ))
                {
                    return CloneBytes(EdidOverrideRegistry.Read(deviceKey));
                }
            }
            catch (InvalidOperationException ex)
            {
                throw new SecureStateConflictException(
                    "The EDID override registry value has an unexpected type.",
                    ex);
            }
        }

        /// <summary>
        /// Classifies the live override against independently compiled owned
        /// bytes.  The coordinator must separately prove that those bytes
        /// match the journaled ownership digest before it treats ExactOwned
        /// as authority for a state transition.
        /// </summary>
        internal EdidLiveOverrideState ClassifyOverride(
            byte[] expectedOwnedOverride)
        {
            RequireOverrideBytes(expectedOwnedOverride, "expectedOwnedOverride");
            byte[] current = ReadOverride();
            if (current == null)
                return EdidLiveOverrideState.Absent;

            return FixedTimeComparer.AreEqual(
                    current,
                    expectedOwnedOverride)
                ? EdidLiveOverrideState.ExactOwned
                : EdidLiveOverrideState.ForeignOrInvalid;
        }

        /// <summary>
        /// Installs only into an absent override slot, then proves the exact
        /// bytes survived the registry flush.  Reconciliation is responsible
        /// for treating an already-owned value as Installed rather than
        /// retrying this mutation.
        /// </summary>
        internal void WriteOwnedOverride(byte[] expectedOverride)
        {
            RequireOverrideBytes(expectedOverride, "expectedOverride");
            try
            {
                using (SafeRegistryHandle deviceKey = OpenParameters(
                    DisplayTopologyNativeMethods.KEY_READ |
                        NativeMethods.KEY_WRITE))
                {
                    byte[] current = EdidOverrideRegistry.Read(deviceKey);
                    if (current != null)
                    {
                        throw new SecureStateConflictException(
                            "An EDID override appeared before the app-owned value could be written.");
                    }

                    EdidOverrideRegistry.Write(deviceKey, expectedOverride);
                    byte[] readBack = EdidOverrideRegistry.Read(deviceKey);
                    if (!FixedTimeComparer.AreEqual(
                            expectedOverride,
                            readBack))
                    {
                        throw new IOException(
                            "The EDID override read-back did not match the expected app-owned bytes.");
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                throw new SecureStateConflictException(
                    "The EDID override registry value has an unexpected type.",
                    ex);
            }
        }

        /// <summary>
        /// Deletes only the exact journal-owned value and proves that value is
        /// absent afterwards.  A missing or foreign value is never deleted.
        /// </summary>
        internal void DeleteExactOwnedOverride(byte[] expectedOverride)
        {
            RequireOverrideBytes(expectedOverride, "expectedOverride");
            try
            {
                using (SafeRegistryHandle deviceKey = OpenParameters(
                    DisplayTopologyNativeMethods.KEY_READ |
                        NativeMethods.KEY_WRITE))
                {
                    EdidOverrideRegistry.DeleteExact(deviceKey, expectedOverride);
                }
            }
            catch (InvalidOperationException ex)
            {
                throw new SecureStateConflictException(
                    "The EDID override registry value has an unexpected type.",
                    ex);
            }
        }

        private SafeRegistryHandle OpenParameters(int registryAccess)
        {
            return MonitorDevnodeAccess.OpenExactDeviceParameters(
                expectedRecord,
                registryAccess);
        }

        private static MonitorDeviceRecord CopyRecord(MonitorDeviceRecord source)
        {
            MonitorDeviceRecord result = new MonitorDeviceRecord();
            result.DeviceInstanceId = source.DeviceInstanceId.Trim().ToUpperInvariant();
            // Keep the raw SetupAPI property for the later exact devnode
            // re-open.  The durable MonitorIdentity uses the separately
            // normalized HardwareId property above, while SPDRP_HARDWAREID is
            // commonly reported as MONITOR\\APPA044 rather than APPA044.
            result.HardwareId = source.HardwareId.Trim();
            result.Edid = CopyBaseBlock(source.Edid);
            return result;
        }

        private static byte[] CopyBaseBlock(byte[] source)
        {
            if (source == null || source.Length < EdidBaseBlock.Length)
            {
                throw new ArgumentException(
                    "A complete EDID base block is required.",
                    nameof(source));
            }

            byte[] result = new byte[EdidBaseBlock.Length];
            Buffer.BlockCopy(source, 0, result, 0, result.Length);
            return result;
        }

        private static void RequireOverrideBytes(byte[] value, string parameterName)
        {
            if (value == null || value.Length != EdidBaseBlock.Length)
            {
                throw new ArgumentException(
                    "An EDID override base block must contain exactly 128 bytes.",
                    parameterName);
            }
        }

        private static byte[] CloneBytes(byte[] value)
        {
            return value == null ? null : (byte[])value.Clone();
        }

    }
}
