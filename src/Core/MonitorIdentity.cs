using System;

namespace MacBookEco.Core
{
    /// <summary>
    /// Durable identity of a physical monitor.  It deliberately contains no
    /// registry path, adapter LUID, CCD source/target ID, or DISPLAYn name:
    /// those are ephemeral endpoint facts and must be resolved for each OS
    /// action.
    /// </summary>
    public sealed class MonitorIdentity : IEquatable<MonitorIdentity>
    {
        public MonitorIdentity(
            string monitorInstanceId,
            string panelHardwareId,
            string manufacturerCode,
            Sha256Digest edidFingerprint)
        {
            MonitorInstanceId = RequireMonitorInstanceId(monitorInstanceId);
            PanelHardwareId = RequirePanelHardwareId(panelHardwareId);
            ManufacturerCode = RequireManufacturerCode(manufacturerCode);
            if (!PanelHardwareId.StartsWith(
                    ManufacturerCode,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The panel hardware ID must begin with its manufacturer code.",
                    nameof(panelHardwareId));
            }

            if (edidFingerprint == null)
            {
                throw new ArgumentNullException(nameof(edidFingerprint));
            }

            EdidFingerprint = edidFingerprint;
        }

        /// <summary>
        /// The canonical SetupAPI device-instance identifier.  This is an
        /// identity value, not a registry subkey.
        /// </summary>
        public string MonitorInstanceId { get; private set; }

        /// <summary>
        /// Canonical monitor hardware ID, for example APPA044.
        /// </summary>
        public string PanelHardwareId { get; private set; }

        /// <summary>
        /// Three-letter EDID manufacturer code, for example APP.
        /// </summary>
        public string ManufacturerCode { get; private set; }

        /// <summary>
        /// A versioned SHA-256 fingerprint of the reviewed base EDID/profile.
        /// The durable EDID journal uses an exact base-block hash here.
        /// Callers must never silently reinterpret persisted bytes.
        /// </summary>
        public Sha256Digest EdidFingerprint { get; private set; }

        /// <summary>
        /// Builds the identity fingerprint from EdidBaseBlock.NormalizedSignature.
        /// A different field convention must never be silently reinterpreted.
        /// </summary>
        public static MonitorIdentity FromNormalizedEdid(
            string monitorInstanceId,
            string panelHardwareId,
            EdidBaseBlock edid)
        {
            if (edid == null)
            {
                throw new ArgumentNullException(nameof(edid));
            }

            return new MonitorIdentity(
                monitorInstanceId,
                panelHardwareId,
                edid.ManufacturerCode,
                edid.NormalizedSignature);
        }

        /// <summary>
        /// Builds the identity fingerprint from the exact base-block bytes,
        /// which is the convention the durable EDID journal stores.  This is
        /// deliberately not FromNormalizedEdid: the two hash different inputs
        /// and their results must never be compared with each other.
        ///
        /// Both halves of compare-before-restore resolve through here, so a
        /// panel that one half accepts is a panel the other half accepts.
        /// </summary>
        public static MonitorIdentity FromExactBaseEdid(
            string monitorInstanceId,
            string panelHardwareId,
            EdidBaseBlock edid)
        {
            if (edid == null)
            {
                throw new ArgumentNullException(nameof(edid));
            }

            return new MonitorIdentity(
                monitorInstanceId,
                panelHardwareId,
                edid.ManufacturerCode,
                Sha256Digest.Compute(edid.ToByteArray()));
        }

        public bool Equals(MonitorIdentity other)
        {
            return !ReferenceEquals(other, null) &&
                string.Equals(
                    MonitorInstanceId,
                    other.MonitorInstanceId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    PanelHardwareId,
                    other.PanelHardwareId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    ManufacturerCode,
                    other.ManufacturerCode,
                    StringComparison.Ordinal) &&
                EdidFingerprint.Equals(other.EdidFingerprint);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as MonitorIdentity);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = MonitorInstanceId.GetHashCode();
                hash = (hash * 31) + PanelHardwareId.GetHashCode();
                hash = (hash * 31) + ManufacturerCode.GetHashCode();
                hash = (hash * 31) + EdidFingerprint.GetHashCode();
                return hash;
            }
        }

        private static string RequireMonitorInstanceId(string monitorInstanceId)
        {
            if (monitorInstanceId == null)
            {
                throw new ArgumentNullException(nameof(monitorInstanceId));
            }

            if (!string.Equals(monitorInstanceId, monitorInstanceId.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A monitor instance ID cannot have leading or trailing whitespace.",
                    nameof(monitorInstanceId));
            }

            var canonical = monitorInstanceId.ToUpperInvariant();
            if (canonical.Length == 0 || canonical.Length > 256 ||
                canonical[0] == '\\' ||
                canonical[canonical.Length - 1] == '\\' ||
                canonical.IndexOf('\\') <= 0 ||
                canonical.IndexOf("\\\\", StringComparison.Ordinal) >= 0 ||
                canonical.IndexOf("..", StringComparison.Ordinal) >= 0 ||
                canonical.IndexOf('/') >= 0 ||
                canonical.IndexOf(':') >= 0 ||
                canonical.StartsWith("HKLM\\", StringComparison.Ordinal) ||
                canonical.StartsWith("HKCU\\", StringComparison.Ordinal) ||
                canonical.StartsWith("HKEY_", StringComparison.Ordinal) ||
                canonical.StartsWith("REGISTRY\\", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A canonical monitor instance ID is required.",
                    nameof(monitorInstanceId));
            }

            for (var index = 0; index < canonical.Length; index++)
            {
                char character = canonical[index];
                bool allowed =
                    (character >= 'A' && character <= 'Z') ||
                    (character >= '0' && character <= '9') ||
                    character == '\\' ||
                    character == '&' ||
                    character == '#' ||
                    character == '_' ||
                    character == '-' ||
                    character == '.' ||
                    character == '{' ||
                    character == '}';
                if (!allowed)
                {
                    throw new ArgumentException(
                        "A monitor instance ID contains a non-canonical character.",
                        nameof(monitorInstanceId));
                }
            }

            return canonical;
        }

        private static string RequirePanelHardwareId(string panelHardwareId)
        {
            if (panelHardwareId == null)
            {
                throw new ArgumentNullException(nameof(panelHardwareId));
            }

            if (!string.Equals(panelHardwareId, panelHardwareId.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A panel hardware ID cannot have leading or trailing whitespace.",
                    nameof(panelHardwareId));
            }

            var canonical = HardwareSnapshot.NormalizePanelHardwareId(panelHardwareId);
            if (canonical.Length < 3 || canonical.Length > 64)
            {
                throw new ArgumentException(
                    "A canonical panel hardware ID is required.",
                    nameof(panelHardwareId));
            }

            for (var index = 0; index < canonical.Length; index++)
            {
                char character = canonical[index];
                if (!((character >= 'A' && character <= 'Z') ||
                    (character >= '0' && character <= '9')))
                {
                    throw new ArgumentException(
                        "A panel hardware ID contains a non-canonical character.",
                        nameof(panelHardwareId));
                }
            }

            return canonical;
        }

        private static string RequireManufacturerCode(string manufacturerCode)
        {
            if (manufacturerCode == null)
            {
                throw new ArgumentNullException(nameof(manufacturerCode));
            }

            if (!string.Equals(manufacturerCode, manufacturerCode.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A manufacturer code cannot have leading or trailing whitespace.",
                    nameof(manufacturerCode));
            }

            var canonical = manufacturerCode.ToUpperInvariant();
            if (canonical.Length != 3)
            {
                throw new ArgumentException(
                    "A manufacturer code must contain exactly three upper-case letters.",
                    nameof(manufacturerCode));
            }

            for (var index = 0; index < canonical.Length; index++)
            {
                if (canonical[index] < 'A' || canonical[index] > 'Z')
                {
                    throw new ArgumentException(
                        "A manufacturer code must contain exactly three upper-case letters.",
                        nameof(manufacturerCode));
                }
            }

            return canonical;
        }
    }
}
