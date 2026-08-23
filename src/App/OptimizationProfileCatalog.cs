using System.Collections.Generic;
using MacBookEco.AppPolicy;
using MacBookEco.Core;

namespace MacBookEco.App
{
    public sealed class OptimizationProfileDefinition
    {
        internal OptimizationProfileDefinition(
            string displayName,
            string description,
            int displayRefreshRate,
            PowerPreset cpuPreset)
        {
            DisplayName = displayName;
            Description = description;
            DisplayRefreshRate = displayRefreshRate;
            CpuPreset = cpuPreset;
        }

        public string DisplayName { get; private set; }
        public string Description { get; private set; }
        public int DisplayRefreshRate { get; private set; }
        public PowerPreset CpuPreset { get; private set; }
    }

    public static class OptimizationProfileCatalog
    {
        private static readonly IList<OptimizationProfileDefinition> ProfilesValue =
            new List<OptimizationProfileDefinition>
            {
                new OptimizationProfileDefinition(
                    "Everyday",
                    "60 Hz display with a responsive but battery-aware CPU plan.",
                    ProfileCatalog.NativeMode.WindowsRefreshRate,
                    PowerPreset.Normal),
                new OptimizationProfileDefinition(
                    "Cool & quiet",
                    "48 Hz and no CPU turbo for lower heat without a severe CPU cap.",
                    ProfileCatalog.CompatibilityMode.WindowsRefreshRate,
                    PowerPreset.Cool),
                new OptimizationProfileDefinition(
                    "Battery saver",
                    "48 Hz with the strongest CPU and passive-cooling limits.",
                    ProfileCatalog.CompatibilityMode.WindowsRefreshRate,
                    PowerPreset.MaximumBattery)
            }.AsReadOnly();

        public static IList<OptimizationProfileDefinition> Profiles => ProfilesValue;
    }
}
