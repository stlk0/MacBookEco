using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MacBookEco.Core
{
    public static class ProfileCatalog
    {
        public const string MacBookPro161Appa044ProfileId =
            "macbookpro16-1-appa044-48hz";

        private static readonly ReadOnlyCollection<DisplayProfile> Profiles =
            Array.AsReadOnly(GeneratedProfileCatalog.Create());

        public static ReadOnlyCollection<DisplayProfile> All => Profiles;

        public static DisplayProfile GetById(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return null;
            }

            for (var index = 0; index < Profiles.Count; index++)
            {
                if (
                    string.Equals(
                        Profiles[index].Id,
                        profileId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Profiles[index];
                }
            }

            return null;
        }

        public static ProfileSelectionResult Select(HardwareSnapshot hardware)
        {
            if (hardware == null)
            {
                throw new ArgumentNullException(nameof(hardware));
            }

            DisplayProfileMatch closest = null;
            for (var index = 0; index < Profiles.Count; index++)
            {
                var match = Profiles[index].Match(hardware);
                if (match.HardwareSupported)
                {
                    return new ProfileSelectionResult(Profiles[index], match);
                }

                if (
                    closest == null ||
                    match.RejectionReasons.Count < closest.RejectionReasons.Count)
                {
                    closest = match;
                }
            }

            return new ProfileSelectionResult(null, closest);
        }
    }
}
