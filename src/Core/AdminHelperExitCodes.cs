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
    }
}
