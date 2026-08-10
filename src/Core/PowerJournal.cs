using System;

namespace MacBookEco.Core
{
    /// <summary>
    /// Identity for an app-owned power resource.  It accepts GUIDs and a
    /// compiled-policy fingerprint only; it never accepts a caller-provided
    /// power scheme path or arbitrary setting list.
    /// </summary>
    public sealed class PowerTargetIdentity : IEquatable<PowerTargetIdentity>
    {
        public PowerTargetIdentity(
            Guid originalSchemeId,
            Guid ownedSchemeId,
            PowerPresetId preset,
            Sha256Digest managedSettingsHash)
        {
            if (originalSchemeId == Guid.Empty)
            {
                throw new ArgumentException(
                    "An original power scheme ID is required.",
                    nameof(originalSchemeId));
            }

            if (ownedSchemeId == Guid.Empty)
            {
                throw new ArgumentException(
                    "An owned power scheme ID is required.",
                    nameof(ownedSchemeId));
            }

            if (originalSchemeId == ownedSchemeId)
            {
                throw new ArgumentException(
                    "Original and owned power scheme IDs must differ.",
                    nameof(ownedSchemeId));
            }

            if (!IsKnownPreset(preset))
            {
                throw new ArgumentOutOfRangeException(nameof(preset));
            }

            if (managedSettingsHash == null)
            {
                throw new ArgumentNullException(nameof(managedSettingsHash));
            }

            OriginalSchemeId = originalSchemeId;
            OwnedSchemeId = ownedSchemeId;
            Preset = preset;
            ManagedSettingsHash = managedSettingsHash;
        }

        public Guid OriginalSchemeId { get; private set; }

        public Guid OwnedSchemeId { get; private set; }

        public PowerPresetId Preset { get; private set; }

        public Sha256Digest ManagedSettingsHash { get; private set; }

        public PowerTargetIdentity WithOriginalScheme(Guid originalSchemeId)
        {
            return new PowerTargetIdentity(
                originalSchemeId,
                OwnedSchemeId,
                Preset,
                ManagedSettingsHash);
        }

        public bool HasSameOwnedResource(PowerTargetIdentity other)
        {
            return !ReferenceEquals(other, null) &&
                OwnedSchemeId == other.OwnedSchemeId &&
                Preset == other.Preset &&
                ManagedSettingsHash.Equals(other.ManagedSettingsHash);
        }

        public bool Equals(PowerTargetIdentity other)
        {
            return HasSameOwnedResource(other) &&
                OriginalSchemeId == other.OriginalSchemeId;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as PowerTargetIdentity);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = OriginalSchemeId.GetHashCode();
                hash = (hash * 31) + OwnedSchemeId.GetHashCode();
                hash = (hash * 31) + (int)Preset;
                hash = (hash * 31) + ManagedSettingsHash.GetHashCode();
                return hash;
            }
        }

        public static bool IsKnownPreset(PowerPresetId preset)
        {
            return preset == PowerPresetId.Normal ||
                preset == PowerPresetId.Cool ||
                preset == PowerPresetId.MaximumBattery;
        }
    }

    public sealed class PowerJournalPayload
    {
        public PowerJournalPayload(
            PowerTargetIdentity target,
            PowerInactiveReason inactiveReason)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (!IsKnownInactiveReason(inactiveReason))
            {
                throw new ArgumentOutOfRangeException(nameof(inactiveReason));
            }

            Target = target;
            InactiveReason = inactiveReason;
        }

        public PowerTargetIdentity Target { get; private set; }

        public PowerInactiveReason InactiveReason { get; private set; }

        public static bool IsKnownInactiveReason(PowerInactiveReason reason)
        {
            return reason == PowerInactiveReason.None ||
                reason == PowerInactiveReason.OriginalAlreadyActive ||
                reason == PowerInactiveReason.ExternalSelection;
        }
    }

    public sealed class PowerJournal : JournalEnvelope
    {
        public PowerJournal(
            JournalOperationId operationId,
            JournalGeneration generation,
            DateTime createdUtc,
            DateTime updatedUtc,
            PowerJournalState state,
            PowerJournalPayload payload)
            : base(operationId, generation, createdUtc, updatedUtc)
        {
            if (!IsKnownState(state))
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }

            if (state == PowerJournalState.NotManaged)
            {
                if (payload != null)
                {
                    throw new ArgumentException(
                        "NotManaged cannot carry a power ownership payload.",
                        nameof(payload));
                }
            }
            else if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }
            else if (state == PowerJournalState.InactiveRetained)
            {
                if (payload.InactiveReason == PowerInactiveReason.None)
                {
                    throw new ArgumentException(
                        "InactiveRetained requires an explicit inactive reason.",
                        nameof(payload));
                }
            }
            else if (payload.InactiveReason != PowerInactiveReason.None)
            {
                throw new ArgumentException(
                    "Only InactiveRetained may carry an inactive reason.",
                    nameof(payload));
            }

            State = state;
            Payload = payload;
        }

        public override JournalTransactionKind TransactionKind => JournalTransactionKind.Power;

        public PowerJournalState State { get; private set; }

        public PowerJournalPayload Payload { get; private set; }

        internal override byte StateCode => (byte)State;

        public PowerJournal TransitionTo(
            PowerJournalState nextState,
            PowerJournalPayload nextPayload,
            JournalGeneration nextGeneration,
            DateTime updatedUtc)
        {
            if (!CanTransition(State, nextState))
            {
                throw new InvalidOperationException(
                    string.Format(
                        "Power journal cannot move from {0} to {1}.",
                        State,
                        nextState));
            }

            RequireImmediateNextGeneration(Generation, nextGeneration);
            ValidateTransitionPayload(nextState, nextPayload);
            return new PowerJournal(
                OperationId,
                nextGeneration,
                CreatedUtc,
                updatedUtc,
                nextState,
                nextPayload);
        }

        /// <summary>
        /// Transitions available to a record that keeps the same OperationId.
        /// This is deliberately not the whole state graph: a record that starts
        /// a new operation moves under CanStartNewOperation instead, and the
        /// two answer different questions about the same pair of states.
        /// </summary>
        public static bool CanTransition(
            PowerJournalState current,
            PowerJournalState next)
        {
            switch (current)
            {
                case PowerJournalState.NotManaged:
                    return next == PowerJournalState.Creating;
                case PowerJournalState.Creating:
                    return next == PowerJournalState.Creating ||
                        next == PowerJournalState.Applied ||
                        next == PowerJournalState.Conflict;
                case PowerJournalState.Applied:
                    return next == PowerJournalState.Creating ||
                        next == PowerJournalState.Applied ||
                        next == PowerJournalState.RestorePending ||
                        next == PowerJournalState.InactiveRetained ||
                        next == PowerJournalState.Conflict;
                case PowerJournalState.RestorePending:
                    return next == PowerJournalState.RestorePending ||
                        next == PowerJournalState.InactiveRetained ||
                        next == PowerJournalState.Conflict;
                case PowerJournalState.InactiveRetained:
                    return next == PowerJournalState.Creating ||
                        next == PowerJournalState.InactiveRetained ||
                        next == PowerJournalState.Applied ||
                        next == PowerJournalState.RestorePending ||
                        next == PowerJournalState.Conflict;
                case PowerJournalState.Conflict:
                    return next == PowerJournalState.Conflict;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Whether a record carrying a new OperationId may replace one in
        /// <paramref name="current"/>.  Only an explicit NotManaged terminal
        /// record qualifies: a retained or conflicted app-owned scheme is never
        /// automatically discarded, because the user still owns the scheme it
        /// names.
        ///
        /// This is stricter than the EDID rule, which also admits Restored.
        /// The asymmetry is real: abandoning a restored EDID override leaves
        /// nothing behind, while abandoning a retained power journal would
        /// orphan a duplicated scheme that nothing would later delete.
        /// </summary>
        public static bool CanStartNewOperation(
            PowerJournalState current,
            PowerJournalState next)
        {
            return current == PowerJournalState.NotManaged &&
                next == PowerJournalState.Creating;
        }

        public static bool IsKnownState(PowerJournalState state)
        {
            return state == PowerJournalState.NotManaged ||
                state == PowerJournalState.Creating ||
                state == PowerJournalState.Applied ||
                state == PowerJournalState.RestorePending ||
                state == PowerJournalState.InactiveRetained ||
                state == PowerJournalState.Conflict;
        }

        private void ValidateTransitionPayload(
            PowerJournalState nextState,
            PowerJournalPayload nextPayload)
        {
            if (nextState == PowerJournalState.NotManaged)
            {
                if (nextPayload != null)
                {
                    throw new ArgumentException(
                        "NotManaged cannot carry a power ownership payload.",
                        nameof(nextPayload));
                }

                return;
            }

            if (nextPayload == null)
            {
                throw new ArgumentNullException(nameof(nextPayload));
            }

            if (Payload == null)
            {
                return;
            }

            bool allowsReconfiguredOwnedScheme =
                nextState == PowerJournalState.Creating &&
                (State == PowerJournalState.Applied ||
                 State == PowerJournalState.InactiveRetained);
            if (allowsReconfiguredOwnedScheme)
            {
                if (Payload.Target.OwnedSchemeId !=
                    nextPayload.Target.OwnedSchemeId)
                {
                    throw new ArgumentException(
                        "Reconfiguration may retain only the same owned power-scheme GUID.",
                        nameof(nextPayload));
                }
            }
            else if (State == PowerJournalState.InactiveRetained &&
                nextState == PowerJournalState.Applied)
            {
                if (!Payload.Target.HasSameOwnedResource(nextPayload.Target))
                {
                    throw new ArgumentException(
                        "Re-activation may retain only the same owned power resource.",
                        nameof(nextPayload));
                }
            }
            else if (!Payload.Target.Equals(nextPayload.Target))
            {
                throw new ArgumentException(
                    "A journal transition cannot change the power target identity.",
                    nameof(nextPayload));
            }
        }
    }
}
