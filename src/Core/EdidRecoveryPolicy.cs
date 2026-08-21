using System;

namespace MacBookEco.Core
{
    /// <summary>
    /// The only live-state classifications used by the EDID reconciliation
    /// coordinator. "Owned" means that the caller has independently proved
    /// the stable monitor identity and either byte-for-byte equality with a
    /// compiled override or equality with the SHA-256 stored in MacBook Eco's
    /// protected journal for a historical profile.
    /// </summary>
    public enum EdidLiveOverrideState
    {
        Absent = 1,
        ExactOwned = 2,
        ForeignOrInvalid = 3
    }

    /// <summary>
    /// Typed, domain-specific reconciliation decisions.  They deliberately do
    /// not perform I/O: the Windows adapter must re-resolve identity, compare
    /// immediately before a mutation, and read back afterwards.
    /// </summary>
    public enum EdidReconciliationAction
    {
        StartNewInstall = 1,
        WriteOwnedOverride = 2,
        MarkInstalled = 3,
        ConfirmInstalled = 4,
        ReconcileRestoreFirst = 5,
        StartRestore = 6,
        DeleteOwnedOverride = 7,
        MarkRestored = 8,
        ConfirmRestored = 9,
        Conflict = 10,
        Blocked = 11
    }

    /// <summary>
    /// Pure EDID state-machine policy.  Keeping this table out of the native
    /// registry adapter makes crash/retry behavior testable without HKLM and
    /// prevents a caller from treating a missing value as an implicit restore
    /// while the durable state still says Installed.
    /// </summary>
    public static class EdidRecoveryPolicy
    {
        public static bool RequiresOriginalForNewInstall(
            EdidJournalState previousState)
        {
            RequireKnownState(previousState);
            return previousState == EdidJournalState.Restored;
        }

        public static EdidLiveOverrideState ClassifyProtectedJournalOverride(
            byte[] currentOverride,
            Sha256Digest ownedOverrideHash)
        {
            if (ownedOverrideHash == null)
            {
                throw new ArgumentNullException(nameof(ownedOverrideHash));
            }

            if (currentOverride == null)
            {
                return EdidLiveOverrideState.Absent;
            }

            return currentOverride.Length == EdidBaseBlock.Length &&
                Sha256Digest.Compute(currentOverride).Equals(ownedOverrideHash)
                    ? EdidLiveOverrideState.ExactOwned
                    : EdidLiveOverrideState.ForeignOrInvalid;
        }

        public static EdidReconciliationAction ForInstall(
            EdidJournalState state,
            EdidLiveOverrideState liveState)
        {
            RequireKnownState(state);
            RequireKnownLiveState(liveState);

            switch (state)
            {
                case EdidJournalState.NotInstalled:
                case EdidJournalState.Restored:
                    return EdidReconciliationAction.StartNewInstall;

                case EdidJournalState.InstallPending:
                    return liveState == EdidLiveOverrideState.Absent
                        ? EdidReconciliationAction.WriteOwnedOverride
                        : liveState == EdidLiveOverrideState.ExactOwned
                            ? EdidReconciliationAction.MarkInstalled
                            : EdidReconciliationAction.Conflict;

                case EdidJournalState.Installed:
                    return liveState == EdidLiveOverrideState.ExactOwned
                        ? EdidReconciliationAction.ConfirmInstalled
                        : EdidReconciliationAction.Conflict;

                case EdidJournalState.RestorePending:
                    return EdidReconciliationAction.ReconcileRestoreFirst;

                case EdidJournalState.Conflict:
                    // Conflict repair is deliberately asymmetric: exact
                    // owned bytes can restore the durable Installed marker,
                    // but absent or foreign bytes remain fail-closed. The
                    // The Windows coordinator proves target identity plus
                    // either the compiled profile bytes or the exact protected
                    // historical ownership digest before calling this policy.
                    return liveState == EdidLiveOverrideState.ExactOwned
                        ? EdidReconciliationAction.MarkInstalled
                        : EdidReconciliationAction.Blocked;

                default:
                    throw new ArgumentOutOfRangeException(nameof(state));
            }
        }

        public static EdidReconciliationAction ForRestore(
            EdidJournalState state,
            EdidLiveOverrideState liveState)
        {
            RequireKnownState(state);
            RequireKnownLiveState(liveState);

            switch (state)
            {
                case EdidJournalState.NotInstalled:
                    return EdidReconciliationAction.Blocked;

                case EdidJournalState.InstallPending:
                    // A user explicitly asking for restore need not finish a
                    // previously interrupted write.  After offline target
                    // revalidation, absent bytes mean no owned value needs
                    // deletion; exact bytes can be removed under the durable
                    // RestorePending intent.  Foreign/invalid state remains
                    // fail-closed.
                    return liveState == EdidLiveOverrideState.ForeignOrInvalid
                        ? EdidReconciliationAction.Conflict
                        : EdidReconciliationAction.StartRestore;

                case EdidJournalState.Installed:
                    return liveState == EdidLiveOverrideState.ExactOwned
                        ? EdidReconciliationAction.StartRestore
                        : EdidReconciliationAction.Conflict;

                case EdidJournalState.RestorePending:
                    return liveState == EdidLiveOverrideState.Absent
                        ? EdidReconciliationAction.MarkRestored
                        : liveState == EdidLiveOverrideState.ExactOwned
                            ? EdidReconciliationAction.DeleteOwnedOverride
                            : EdidReconciliationAction.Conflict;

                case EdidJournalState.Restored:
                    return liveState == EdidLiveOverrideState.Absent
                        ? EdidReconciliationAction.ConfirmRestored
                        : EdidReconciliationAction.Conflict;

                case EdidJournalState.Conflict:
                    return EdidReconciliationAction.Blocked;

                default:
                    throw new ArgumentOutOfRangeException(nameof(state));
            }
        }

        private static void RequireKnownState(EdidJournalState state)
        {
            if (!EdidJournal.IsKnownState(state))
                throw new ArgumentOutOfRangeException(nameof(state));
        }

        private static void RequireKnownLiveState(EdidLiveOverrideState liveState)
        {
            if (liveState != EdidLiveOverrideState.Absent &&
                liveState != EdidLiveOverrideState.ExactOwned &&
                liveState != EdidLiveOverrideState.ForeignOrInvalid)
            {
                throw new ArgumentOutOfRangeException(nameof(liveState));
            }
        }
    }
}
