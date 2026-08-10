using System;

namespace MacBookEco.Core
{
    public sealed class HardwareSnapshot
    {
        public HardwareSnapshot(
            string systemManufacturer,
            string systemModel,
            bool isInternalDisplay,
            string panelHardwareId,
            EdidBaseBlock edid,
            string gpuName,
            string gpuDeviceId,
            string driverVersion)
        {
            if (string.IsNullOrWhiteSpace(systemModel))
            {
                throw new ArgumentException("A system model is required.", nameof(systemModel));
            }

            if (string.IsNullOrWhiteSpace(panelHardwareId))
            {
                throw new ArgumentException(
                    "A panel hardware identifier is required.",
                    nameof(panelHardwareId));
            }

            if (edid == null)
            {
                throw new ArgumentNullException(nameof(edid));
            }

            SystemManufacturer = NormalizeText(systemManufacturer);
            SystemModel = NormalizeText(systemModel);
            IsInternalDisplay = isInternalDisplay;
            PanelHardwareId = NormalizePanelHardwareId(panelHardwareId);
            Edid = edid;
            GpuName = NormalizeText(gpuName);
            GpuDeviceId = NormalizeText(gpuDeviceId);
            DriverVersion = NormalizeText(driverVersion);
        }

        public string SystemManufacturer { get; private set; }

        public string SystemModel { get; private set; }

        public bool IsInternalDisplay { get; private set; }

        public string PanelHardwareId { get; private set; }

        public EdidBaseBlock Edid { get; private set; }

        public Sha256Digest NormalizedEdidSignature => Edid.NormalizedSignature;

        public DetailedTiming NativeTiming => Edid.PreferredTiming;

        public string GpuName { get; private set; }

        public string GpuDeviceId { get; private set; }

        public string DriverVersion { get; private set; }

        public static string NormalizePanelHardwareId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Trim().Replace('/', '\\').ToUpperInvariant();
            var parts = normalized.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (
                parts.Length >= 2 &&
                (parts[0] == "MONITOR" || parts[0] == "DISPLAY"))
            {
                return parts[1];
            }

            return normalized;
        }

        private static string NormalizeText(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }
}
