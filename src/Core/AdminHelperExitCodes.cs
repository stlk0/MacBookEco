namespace MacBookEco.Core
{
    /// <summary>
    /// The elevated helper's process exit codes.
    ///
    /// This is a contract between two executables: the helper returns these
    /// and the tray process turns them into a typed outcome. It was previously
    /// private constants in the helper and bare integers in the reader, so the
    /// two halves could drift without anything noticing.
    /// </summary>
    public static class AdminHelperExitCodes
    {
        /// <summary>The requested transaction completed and was verified.</summary>
        public const int Success = 0;

        /// <summary>The fixed command or its arguments were rejected.</summary>
        public const int Usage = 2;

        /// <summary>
        /// This MacBook, panel, driver or profile is not supported. Distinct
        /// from failure: nothing was attempted.
        /// </summary>
        public const int Unsupported = 3;

        /// <summary>
        /// The transaction did not complete. No unverified change is reported
        /// as success.
        /// </summary>
        public const int Failed = 10;

        /// <summary>
        /// The transaction reached an indeterminate boundary. Recovery must
        /// reconcile the durable journal before another privileged change.
        /// </summary>
        public const int Indeterminate = 11;

        // These bounded diagnostic codes carry no exception text or machine
        // identity. They temporarily distinguish common display failures in
        // hardware-test builds while preserving the typed outcome above.
        public const int RequiresNative60 = 20;
        public const int ExternalDisplaysAttached = 21;
        public const int DescriptorSlotsUnavailable = 22;
        public const int ExistingOverride = 23;
        public const int HistoricalJournalState = 24;
        public const int MonitorIdentityMismatch = 25;
        public const int JournalConflict = 26;
        public const int NativeFailure = 27;
        public const int InstallReconciliation = 31;
        public const int RestoreReconciliation = 32;
        public const int JournalPersistence = 33;

        public static bool IsIndeterminate(int exitCode)
        {
            return exitCode == Indeterminate ||
                exitCode == InstallReconciliation ||
                exitCode == RestoreReconciliation ||
                exitCode == JournalPersistence;
        }

        public static string DiagnosticReason(int exitCode)
        {
            switch (exitCode)
            {
                case Unsupported:
                    return "UnsupportedHardware";
                case RequiresNative60:
                    return "RequiresNative60";
                case ExternalDisplaysAttached:
                    return "ExternalDisplaysAttached";
                case DescriptorSlotsUnavailable:
                    return "DescriptorSlotsUnavailable";
                case ExistingOverride:
                    return "ExistingOverride";
                case HistoricalJournalState:
                    return "HistoricalJournalState";
                case MonitorIdentityMismatch:
                    return "MonitorIdentityMismatch";
                case JournalConflict:
                    return "JournalConflict";
                case NativeFailure:
                    return "NativeFailure";
                case InstallReconciliation:
                    return "InstallReconciliation";
                case RestoreReconciliation:
                    return "RestoreReconciliation";
                case JournalPersistence:
                    return "JournalPersistence";
                default:
                    return null;
            }
        }
    }
}
