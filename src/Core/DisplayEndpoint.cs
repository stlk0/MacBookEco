using System;

namespace MacBookEco.Core
{
    /// <summary>
    /// An ephemeral display-topology endpoint.  It is intentionally distinct
    /// from MonitorIdentity and must be re-resolved before every Windows
    /// action; do not serialize it into durable journals.
    /// </summary>
    public sealed class DisplayEndpoint : IEquatable<DisplayEndpoint>
    {
        public DisplayEndpoint(
            ulong adapterLuid,
            uint sourceId,
            uint targetId,
            string gdiDeviceName)
        {
            AdapterLuid = adapterLuid;
            SourceId = sourceId;
            TargetId = targetId;
            GdiDeviceName = RequireGdiDeviceName(gdiDeviceName);
        }

        /// <summary>
        /// Opaque unsigned representation of the Windows adapter LUID.
        /// </summary>
        public ulong AdapterLuid { get; private set; }

        public uint SourceId { get; private set; }

        public uint TargetId { get; private set; }

        /// <summary>
        /// Canonical Win32 GDI device name, for example \\.\DISPLAY1.
        /// </summary>
        public string GdiDeviceName { get; private set; }

        public bool Equals(DisplayEndpoint other)
        {
            return !ReferenceEquals(other, null) &&
                AdapterLuid == other.AdapterLuid &&
                SourceId == other.SourceId &&
                TargetId == other.TargetId &&
                string.Equals(
                    GdiDeviceName,
                    other.GdiDeviceName,
                    StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as DisplayEndpoint);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = AdapterLuid.GetHashCode();
                hash = (hash * 31) + SourceId.GetHashCode();
                hash = (hash * 31) + TargetId.GetHashCode();
                hash = (hash * 31) + GdiDeviceName.GetHashCode();
                return hash;
            }
        }

        private static string RequireGdiDeviceName(string gdiDeviceName)
        {
            const string Prefix = "\\\\.\\DISPLAY";
            if (gdiDeviceName == null)
            {
                throw new ArgumentNullException(nameof(gdiDeviceName));
            }

            if (!string.Equals(gdiDeviceName, gdiDeviceName.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A GDI device name cannot have leading or trailing whitespace.",
                    nameof(gdiDeviceName));
            }

            var canonical = gdiDeviceName.ToUpperInvariant();
            if (canonical.Length <= Prefix.Length ||
                !canonical.StartsWith(Prefix, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A canonical \\\\.\\DISPLAYn device name is required.",
                    nameof(gdiDeviceName));
            }

            for (var index = Prefix.Length; index < canonical.Length; index++)
            {
                if (canonical[index] < '0' || canonical[index] > '9')
                {
                    throw new ArgumentException(
                        "A canonical \\\\.\\DISPLAYn device name is required.",
                        nameof(gdiDeviceName));
                }
            }

            return canonical;
        }
    }
}
