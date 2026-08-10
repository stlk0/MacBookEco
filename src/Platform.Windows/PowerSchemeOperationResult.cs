using System;
using System.Collections.Generic;

namespace MacBookEco.Platform.Windows
{
    public enum PowerSchemeOperationOutcome
    {
        Succeeded,
        Failed,
        Indeterminate
    }

    public enum PowerSettingOperationOutcome
    {
        Applied,
        Unsupported,
        Failed
    }

    public sealed class PowerSettingOperationResult
    {
        private PowerSettingOperationResult(
            string name,
            PowerSettingOperationOutcome outcome,
            string message)
        {
            Name = name ?? string.Empty;
            Outcome = outcome;
            Message = message ?? string.Empty;
        }

        public string Name { get; private set; }

        public PowerSettingOperationOutcome Outcome { get; private set; }

        public string Message { get; private set; }

        internal static PowerSettingOperationResult Applied(string name)
        {
            return new PowerSettingOperationResult(
                name,
                PowerSettingOperationOutcome.Applied,
                "Applied and read back.");
        }

        internal static PowerSettingOperationResult Unsupported(string name)
        {
            return new PowerSettingOperationResult(
                name,
                PowerSettingOperationOutcome.Unsupported,
                "Not exposed by this Windows power scheme.");
        }

        internal static PowerSettingOperationResult Failed(
            string name,
            string message)
        {
            return new PowerSettingOperationResult(
                name,
                PowerSettingOperationOutcome.Failed,
                message);
        }
    }

    public sealed class PowerSchemeOperationResult
    {
        public bool Succeeded { get; private set; }
        public PowerSchemeOperationOutcome Outcome { get; private set; }
        public Guid OriginalScheme { get; private set; }
        public Guid OwnedScheme { get; private set; }
        public IList<string> SkippedSettings { get; private set; }
        public IList<PowerSettingOperationResult> SettingResults { get; private set; }
        public bool OwnedSchemeRetained { get; private set; }
        public string Message { get; private set; }

        internal static PowerSchemeOperationResult Success(
            string message,
            Guid original,
            Guid owned,
            IList<PowerSettingOperationResult> settings,
            bool retained)
        {
            return Create(
                PowerSchemeOperationOutcome.Succeeded,
                message,
                original,
                owned,
                settings,
                retained);
        }

        internal static PowerSchemeOperationResult Failed(
            string message,
            Guid original,
            Guid owned,
            IList<PowerSettingOperationResult> settings,
            bool retained)
        {
            return Create(
                PowerSchemeOperationOutcome.Failed,
                message,
                original,
                owned,
                settings,
                retained);
        }

        internal static PowerSchemeOperationResult Indeterminate(
            string message,
            Guid original,
            Guid owned,
            IList<PowerSettingOperationResult> settings,
            bool retained)
        {
            return Create(
                PowerSchemeOperationOutcome.Indeterminate,
                message,
                original,
                owned,
                settings,
                retained);
        }

        private static PowerSchemeOperationResult Create(
            PowerSchemeOperationOutcome outcome,
            string message,
            Guid original,
            Guid owned,
            IList<PowerSettingOperationResult> settings,
            bool retained)
        {
            List<PowerSettingOperationResult> copied =
                settings == null
                    ? new List<PowerSettingOperationResult>()
                    : new List<PowerSettingOperationResult>(settings);
            List<string> skipped = new List<string>();
            int index;
            for (index = 0; index < copied.Count; index++)
            {
                if (copied[index] != null &&
                    copied[index].Outcome ==
                        PowerSettingOperationOutcome.Unsupported)
                {
                    skipped.Add(copied[index].Name);
                }
            }

            PowerSchemeOperationResult result =
                new PowerSchemeOperationResult();
            result.Succeeded = outcome == PowerSchemeOperationOutcome.Succeeded;
            result.Outcome = outcome;
            result.Message = message ?? string.Empty;
            result.OriginalScheme = original;
            result.OwnedScheme = owned;
            result.SettingResults = copied.AsReadOnly();
            result.SkippedSettings = skipped.AsReadOnly();
            result.OwnedSchemeRetained = retained;
            return result;
        }
    }
}
