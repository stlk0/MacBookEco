using System.Collections.Generic;

namespace MacBookEco.Platform.Windows
{
    internal sealed class PowerSettingsConfigurationResult
    {
        internal PowerSettingsConfigurationResult(
            IList<PowerSettingOperationResult> results,
            bool succeeded)
        {
            Results = results == null
                ? new List<PowerSettingOperationResult>().AsReadOnly()
                : new List<PowerSettingOperationResult>(results).AsReadOnly();
            Succeeded = succeeded;
        }

        internal IList<PowerSettingOperationResult> Results { get; private set; }

        internal bool Succeeded { get; private set; }
    }
}
