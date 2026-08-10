using System;

namespace MacBookEco.Core
{
    /// <summary>
    /// A deliberately small, side-effect-free description of the power-plan
    /// reconciliation matrix.  The Windows adapter supplies only facts read
    /// while it owns the power journal lock; this policy decides whether a
    /// retry may mutate, must retain the current selection, or must stop at a
    /// durable conflict.
    /// </summary>
    public static class PowerRecoveryPolicy
    {
        public static PowerReconciliationAction ForCreating(
            PowerOwnedSchemeState destination,
            bool originalExists)
        {
            if (!originalExists ||
                destination == PowerOwnedSchemeState.ForeignOrDiverged)
            {
                return PowerReconciliationAction.Conflict;
            }

            switch (destination)
            {
                case PowerOwnedSchemeState.Missing:
                    return PowerReconciliationAction.DuplicateWithRecordedGuid;
                case PowerOwnedSchemeState.ExactOwned:
                case PowerOwnedSchemeState.UnmarkedDuplicate:
                    return PowerReconciliationAction.ConfigureAndActivate;
                default:
                    return PowerReconciliationAction.Conflict;
            }
        }

        public static PowerReconciliationAction ForApplied(
            PowerOwnedSchemeState owned,
            bool originalExists,
            PowerActiveSchemeRelation active)
        {
            if (owned != PowerOwnedSchemeState.ExactOwned || !originalExists)
                return PowerReconciliationAction.Conflict;

            switch (active)
            {
                case PowerActiveSchemeRelation.Owned:
                    return PowerReconciliationAction.ConfirmApplied;
                case PowerActiveSchemeRelation.Original:
                    return PowerReconciliationAction.RetainOriginalSelection;
                case PowerActiveSchemeRelation.External:
                    return PowerReconciliationAction.RetainExternalSelection;
                default:
                    return PowerReconciliationAction.Conflict;
            }
        }

        public static PowerReconciliationAction ForRestorePending(
            PowerOwnedSchemeState owned,
            bool originalExists,
            PowerActiveSchemeRelation active)
        {
            if (owned != PowerOwnedSchemeState.ExactOwned || !originalExists)
                return PowerReconciliationAction.Conflict;

            switch (active)
            {
                case PowerActiveSchemeRelation.Owned:
                    return PowerReconciliationAction.ActivateOriginal;
                case PowerActiveSchemeRelation.Original:
                    return PowerReconciliationAction.CompleteRetainedOriginal;
                case PowerActiveSchemeRelation.External:
                    return PowerReconciliationAction.RetainExternalSelection;
                default:
                    return PowerReconciliationAction.Conflict;
            }
        }

        public static PowerReconciliationAction ForInactiveRetained(
            PowerOwnedSchemeState owned,
            bool activeIsOwned)
        {
            if (owned != PowerOwnedSchemeState.ExactOwned)
                return PowerReconciliationAction.Conflict;

            return activeIsOwned
                ? PowerReconciliationAction.ConfigureAndActivate
                : PowerReconciliationAction.ReactivateRetained;
        }
    }

    public enum PowerOwnedSchemeState
    {
        Missing,
        ExactOwned,
        UnmarkedDuplicate,
        ForeignOrDiverged
    }

    public enum PowerActiveSchemeRelation
    {
        Owned,
        Original,
        External
    }

    public enum PowerReconciliationAction
    {
        DuplicateWithRecordedGuid,
        ConfigureAndActivate,
        ConfirmApplied,
        RetainOriginalSelection,
        RetainExternalSelection,
        ActivateOriginal,
        CompleteRetainedOriginal,
        ReactivateRetained,
        Conflict
    }
}
