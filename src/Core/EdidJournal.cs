using System;

namespace MacBookEco.Core
{
    /// <summary>
    /// Durable EDID target identity.  It contains no registry path and is
    /// independently re-resolved by the Windows adapter to a live target.
    /// </summary>
    public sealed class EdidTargetIdentity : IEquatable<EdidTargetIdentity>
    {
        public EdidTargetIdentity(
            string profileId,
            string monitorInstanceId,
            string panelHardwareId,
            string manufacturerCode,
            Sha256Digest normalizedEdidHash)
        {
            ProfileId = RequireProfileId(profileId);
            Monitor = CreateMonitorIdentity(
                monitorInstanceId,
                panelHardwareId,
                manufacturerCode,
                normalizedEdidHash);
        }

        /// <summary>
        /// Builds a target from the explicitly durable monitor identity.
        /// </summary>
        public EdidTargetIdentity(string profileId, MonitorIdentity monitor)
        {
            ProfileId = RequireProfileId(profileId);
            if (monitor == null)
            {
                throw new ArgumentNullException(nameof(monitor));
            }

            Monitor = monitor;
        }

        public string ProfileId { get; private set; }

        public MonitorIdentity Monitor { get; private set; }

        public string MonitorInstanceId => Monitor.MonitorInstanceId;

        public string PanelHardwareId => Monitor.PanelHardwareId;

        public string ManufacturerCode => Monitor.ManufacturerCode;

        /// <summary>
        /// Name for EDID field 5 in the durable journal.
        /// That wire field is a base-block SHA-256 fingerprint, not an
        /// invitation to reinterpret old records as a different normalized
        /// signature scheme.
        /// </summary>
        public Sha256Digest NormalizedEdidHash => Monitor.EdidFingerprint;

        public bool Equals(EdidTargetIdentity other)
        {
            return !ReferenceEquals(other, null) &&
                string.Equals(ProfileId, other.ProfileId, StringComparison.Ordinal) &&
                Monitor.Equals(other.Monitor);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as EdidTargetIdentity);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = ProfileId.GetHashCode();
                hash = (hash * 31) + Monitor.GetHashCode();
                return hash;
            }
        }

        // The parameter keeps the public constructor's name so an argument
        // exception names the value the caller actually supplied. The journal
        // wire field is a base-block fingerprint; the two names refer to the
        // same bytes.
        private static MonitorIdentity CreateMonitorIdentity(
            string monitorInstanceId,
            string panelHardwareId,
            string manufacturerCode,
            Sha256Digest normalizedEdidHash)
        {
            // Keep the original primitive validation here.  In particular the
            // strict codec must still reject non-canonical lower-case fields
            // rather than accepting them and silently canonicalizing on a
            // later write.
            string canonicalInstanceId = RequireMonitorInstanceId(monitorInstanceId);
            string canonicalPanelHardwareId = RequirePanelHardwareId(panelHardwareId);
            string canonicalManufacturerCode = RequireManufacturerCode(manufacturerCode);
            if (!canonicalPanelHardwareId.StartsWith(
                    canonicalManufacturerCode,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The panel hardware ID must begin with its manufacturer code.",
                    nameof(panelHardwareId));
            }

            if (normalizedEdidHash == null)
            {
                throw new ArgumentNullException(nameof(normalizedEdidHash));
            }

            return new MonitorIdentity(
                canonicalInstanceId,
                canonicalPanelHardwareId,
                canonicalManufacturerCode,
                normalizedEdidHash);
        }

        private static string RequireProfileId(string value)
        {
            if (value == null || value.Length == 0 || value.Length > 96 ||
                !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException("A canonical profile ID is required.", nameof(value));
            }

            for (var index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool allowed =
                    (character >= 'a' && character <= 'z') ||
                    (character >= '0' && character <= '9') ||
                    character == '-' ||
                    character == '.';
                if (!allowed)
                {
                    throw new ArgumentException(
                        "A profile ID contains a non-canonical character.",
                        nameof(value));
                }
            }

            if (!IsLowerLetterOrDigit(value[0]) ||
                !IsLowerLetterOrDigit(value[value.Length - 1]) ||
                value.IndexOf("..", StringComparison.Ordinal) >= 0)
            {
                throw new ArgumentException("A profile ID is not canonical.", nameof(value));
            }

            return value;
        }

        private static string RequireMonitorInstanceId(string monitorInstanceId)
        {
            if (monitorInstanceId == null || monitorInstanceId.Length == 0 || monitorInstanceId.Length > 256 ||
                !string.Equals(monitorInstanceId, monitorInstanceId.Trim(), StringComparison.Ordinal) ||
                monitorInstanceId[0] == '\\' ||
                monitorInstanceId[monitorInstanceId.Length - 1] == '\\' ||
                monitorInstanceId.IndexOf('\\') <= 0 ||
                monitorInstanceId.IndexOf("\\\\", StringComparison.Ordinal) >= 0 ||
                monitorInstanceId.IndexOf("..", StringComparison.Ordinal) >= 0 ||
                monitorInstanceId.IndexOf('/') >= 0 ||
                monitorInstanceId.IndexOf(':') >= 0)
            {
                throw new ArgumentException(
                    "A canonical monitor instance ID is required.",
                    nameof(monitorInstanceId));
            }

            if (monitorInstanceId.StartsWith("HKLM\\", StringComparison.Ordinal) ||
                monitorInstanceId.StartsWith("HKCU\\", StringComparison.Ordinal) ||
                monitorInstanceId.StartsWith("HKEY_", StringComparison.Ordinal) ||
                monitorInstanceId.StartsWith("REGISTRY\\", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A monitor identity cannot be a registry path.",
                    nameof(monitorInstanceId));
            }

            for (var index = 0; index < monitorInstanceId.Length; index++)
            {
                char character = monitorInstanceId[index];
                bool allowed =
                    (character >= 'A' && character <= 'Z') ||
                    (character >= '0' && character <= '9') ||
                    character == '\\' ||
                    character == '&' ||
                    character == '#' ||
                    character == '_' ||
                    character == '-' ||
                    character == '.' ||
                    character == '{' ||
                    character == '}';
                if (!allowed)
                {
                    throw new ArgumentException(
                        "A monitor instance ID contains a non-canonical character.",
                        nameof(monitorInstanceId));
                }
            }

            return monitorInstanceId;
        }

        private static string RequirePanelHardwareId(string panelHardwareId)
        {
            if (panelHardwareId == null || panelHardwareId.Length < 3 || panelHardwareId.Length > 64 ||
                !string.Equals(panelHardwareId, panelHardwareId.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException("A canonical panel hardware ID is required.", nameof(panelHardwareId));
            }

            for (var index = 0; index < panelHardwareId.Length; index++)
            {
                char character = panelHardwareId[index];
                if (!((character >= 'A' && character <= 'Z') ||
                    (character >= '0' && character <= '9')))
                {
                    throw new ArgumentException(
                        "A panel hardware ID contains a non-canonical character.",
                        nameof(panelHardwareId));
                }
            }

            return panelHardwareId;
        }

        private static string RequireManufacturerCode(string manufacturerCode)
        {
            if (manufacturerCode == null || manufacturerCode.Length != 3)
            {
                throw new ArgumentException(
                    "A manufacturer code must contain exactly three upper-case letters.",
                    nameof(manufacturerCode));
            }

            for (var index = 0; index < manufacturerCode.Length; index++)
            {
                if (manufacturerCode[index] < 'A' || manufacturerCode[index] > 'Z')
                {
                    throw new ArgumentException(
                        "A manufacturer code must contain exactly three upper-case letters.",
                        nameof(manufacturerCode));
                }
            }

            return manufacturerCode;
        }

        private static bool IsLowerLetterOrDigit(char value)
        {
            return (value >= 'a' && value <= 'z') ||
                (value >= '0' && value <= '9');
        }
    }

    /// <summary>
    /// The only EDID ownership facts trusted from a durable journal. There is no
    /// original override byte array or OriginalOverridePresent=true branch.
    /// </summary>
    public sealed class EdidJournalPayload
    {
        public EdidJournalPayload(
            EdidTargetIdentity target,
            Sha256Digest ownedOverrideHash)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (ownedOverrideHash == null)
            {
                throw new ArgumentNullException(nameof(ownedOverrideHash));
            }

            Target = target;
            OwnedOverrideHash = ownedOverrideHash;
        }

        public EdidTargetIdentity Target { get; private set; }

        public Sha256Digest OwnedOverrideHash { get; private set; }
    }

    public sealed class EdidJournal : JournalEnvelope
    {
        public EdidJournal(
            JournalOperationId operationId,
            JournalGeneration generation,
            DateTime createdUtc,
            DateTime updatedUtc,
            EdidJournalState state,
            EdidJournalPayload payload)
            : base(operationId, generation, createdUtc, updatedUtc)
        {
            if (!IsKnownState(state))
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }

            if (state == EdidJournalState.NotInstalled)
            {
                if (payload != null)
                {
                    throw new ArgumentException(
                        "NotInstalled cannot carry an EDID ownership payload.",
                        nameof(payload));
                }
            }
            else if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            State = state;
            Payload = payload;
        }

        public override JournalTransactionKind TransactionKind => JournalTransactionKind.Edid;

        public EdidJournalState State { get; private set; }

        public EdidJournalPayload Payload { get; private set; }

        internal override byte StateCode => (byte)State;

        public EdidJournal TransitionTo(
            EdidJournalState nextState,
            JournalGeneration nextGeneration,
            DateTime updatedUtc)
        {
            return TransitionTo(
                nextState,
                Payload,
                nextGeneration,
                updatedUtc);
        }

        public EdidJournal TransitionTo(
            EdidJournalState nextState,
            EdidJournalPayload nextPayload,
            JournalGeneration nextGeneration,
            DateTime updatedUtc)
        {
            if (!CanTransition(State, nextState))
            {
                throw new InvalidOperationException(
                    string.Format(
                        "EDID journal cannot move from {0} to {1}.",
                        State,
                        nextState));
            }

            RequireImmediateNextGeneration(Generation, nextGeneration);
            if (nextState == EdidJournalState.NotInstalled)
            {
                if (nextPayload != null)
                {
                    throw new ArgumentException(
                        "NotInstalled cannot carry an EDID ownership payload.",
                        nameof(nextPayload));
                }
            }
            else if (nextPayload == null)
            {
                throw new ArgumentNullException(nameof(nextPayload));
            }
            else if (Payload != null &&
                (!Payload.Target.Equals(nextPayload.Target) ||
                !Payload.OwnedOverrideHash.Equals(nextPayload.OwnedOverrideHash)))
            {
                throw new ArgumentException(
                    "A journal transition cannot change the EDID target identity or ownership hash.",
                    nameof(nextPayload));
            }

            return new EdidJournal(
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
            EdidJournalState current,
            EdidJournalState next)
        {
            switch (current)
            {
                case EdidJournalState.NotInstalled:
                    return next == EdidJournalState.InstallPending;
                case EdidJournalState.InstallPending:
                    return next == EdidJournalState.InstallPending ||
                        next == EdidJournalState.Installed ||
                        // An explicit restore may safely abandon an
                        // interrupted install only after the stored target is
                        // revalidated and live bytes are absent or exact
                        // owned.  RestorePending records that intent before
                        // any possible delete.
                        next == EdidJournalState.RestorePending ||
                        next == EdidJournalState.Conflict;
                case EdidJournalState.Installed:
                    return next == EdidJournalState.Installed ||
                        next == EdidJournalState.RestorePending ||
                        next == EdidJournalState.Conflict;
                case EdidJournalState.RestorePending:
                    return next == EdidJournalState.RestorePending ||
                        next == EdidJournalState.Restored ||
                        next == EdidJournalState.Conflict;
                case EdidJournalState.Restored:
                    return next == EdidJournalState.Restored;
                case EdidJournalState.Conflict:
                    // An explicit repair may clear a stale conflict only
                    // after the coordinator has independently revalidated
                    // the durable target and proved that the live override
                    // is byte-for-byte the journal-owned value.
                    return next == EdidJournalState.Conflict ||
                        next == EdidJournalState.Installed;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Whether a record carrying a new OperationId may replace one in
        /// <paramref name="current"/>.  A fresh install is allowed only from a
        /// confirmed inactive terminal state: in particular a Conflict is never
        /// discarded automatically, and an interrupted install is resumed under
        /// its own operation rather than restarted under a new one.
        ///
        /// Restored to InstallPending lives here and not in CanTransition on
        /// purpose.  Within one operation a Restored record is final; it is the
        /// arrival of a new OperationId that makes a further install legitimate.
        /// EdidRecoveryPolicy.ForInstall depends on this edge.
        /// </summary>
        public static bool CanStartNewOperation(
            EdidJournalState current,
            EdidJournalState next)
        {
            return (current == EdidJournalState.NotInstalled ||
                    current == EdidJournalState.Restored) &&
                next == EdidJournalState.InstallPending;
        }

        public static bool IsKnownState(EdidJournalState state)
        {
            return state == EdidJournalState.NotInstalled ||
                state == EdidJournalState.InstallPending ||
                state == EdidJournalState.Installed ||
                state == EdidJournalState.RestorePending ||
                state == EdidJournalState.Restored ||
                state == EdidJournalState.Conflict;
        }
    }
}
