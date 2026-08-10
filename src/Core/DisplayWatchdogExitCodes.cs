namespace MacBookEco.Core
{
    /// <summary>
    /// The rollback watchdog's process exit codes.
    ///
    /// This is a contract between two executables: the watchdog returns these
    /// and the tray process decides what happened from them. It was previously
    /// private constants in the watchdog and bare integers at the three places
    /// that read them, which made the contract unauditable: the meaning of
    /// "20" existed only in a reader's head.
    /// </summary>
    public static class DisplayWatchdogExitCodes
    {
        /// <summary>The session ended without a rollback being needed.</summary>
        public const int Completed = 0;

        /// <summary>Arguments or durable session state were not usable.</summary>
        public const int UsageOrInvalidState = 2;

        /// <summary>The original display mode was put back.</summary>
        public const int RollbackPerformed = 20;

        /// <summary>A rollback was required and did not succeed.</summary>
        public const int RollbackFailed = 21;

        /// <summary>
        /// The journaled panel could not be re-resolved, so no rollback was
        /// attempted: a mode meant for one display is never applied to another.
        /// </summary>
        public const int RollbackTargetUnresolved = 22;
    }
}
