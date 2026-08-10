using System;
using System.IO;
using MacBookEco.Core;

namespace MacBookEco.Platform.Windows
{
    /// <summary>
    /// Domain-scoped facade over the trusted store. It deliberately exposes
    /// only typed EDID/power records; a caller never supplies a journal file
    /// name, path, or a parser fallback.
    /// </summary>
    internal sealed class JournalStore : IDisposable
    {
        private static readonly TimeSpan TransactionLockTimeout =
            TimeSpan.FromSeconds(15);

        private readonly SecureStateStore store;
        private readonly SecureStateLockHandle transactionLock;
        private readonly SecureStateLockKind kind;
        private bool disposed;

        private JournalStore(
            SecureStateStore store,
            SecureStateLockHandle transactionLock,
            SecureStateLockKind kind)
        {
            this.store = store;
            this.transactionLock = transactionLock;
            this.kind = kind;
        }

        internal static JournalStore OpenEdidMutation()
        {
            return OpenMutation(SecureStateLockKind.Edid);
        }

        internal static JournalStore OpenPowerMutation()
        {
            return OpenMutation(SecureStateLockKind.Power);
        }

        internal EdidJournal ReadEdid()
        {
            EnsureKind(SecureStateLockKind.Edid);
            JournalEnvelope envelope = ReadEnvelope();
            if (envelope == null)
                return null;

            EdidJournal journal = envelope as EdidJournal;
            if (journal == null)
            {
                throw new SecureStateConflictException(
                    "The trusted EDID journal contains a power record.");
            }

            return journal;
        }

        internal PowerJournal ReadPower()
        {
            EnsureKind(SecureStateLockKind.Power);
            JournalEnvelope envelope = ReadEnvelope();
            if (envelope == null)
                return null;

            PowerJournal journal = envelope as PowerJournal;
            if (journal == null)
            {
                throw new SecureStateConflictException(
                    "The trusted power journal contains an EDID record.");
            }

            return journal;
        }

        internal EdidJournal SaveEdid(EdidJournal journal)
        {
            EnsureKind(SecureStateLockKind.Edid);
            if (journal == null)
                throw new ArgumentNullException(nameof(journal));

            // The mutation lock is already held, so the current verified
            // generation is the sole authority for an allowed replacement.
            // Do this immediately before serialization to prevent a caller
            // from constructing a typed-but-unrelated record that skips a
            // recovery boundary.
            ValidateEdidReplacement(ReadEdid(), journal);

            JournalEnvelope verified = ReplaceAndStrictRead(journal);
            EdidJournal result = verified as EdidJournal;
            if (result == null)
            {
                throw new SecureStateConflictException(
                    "The durable EDID journal changed kind during replacement.");
            }

            return result;
        }

        private static void ValidateEdidReplacement(
            EdidJournal current,
            EdidJournal next)
        {
            if (current == null)
            {
                if (next.State != EdidJournalState.InstallPending ||
                    next.Generation.Value != 1)
                {
                    throw new SecureStateConflictException(
                        "The first EDID journal record must be generation 1 InstallPending.");
                }

                return;
            }

            if (!next.Generation.IsImmediateSuccessorOf(current.Generation))
            {
                throw new SecureStateConflictException(
                    "The next EDID journal generation is not the immediate durable successor.");
            }

            if (next.OperationId.Equals(current.OperationId))
            {
                if (!EdidJournal.CanTransition(current.State, next.State))
                {
                    throw new SecureStateConflictException(
                        "The next EDID journal state is not an allowed transition.");
                }

                // Both sides guarded, as in ValidatePowerReplacement: a
                // fail-closed validator must not itself fault on the shape it
                // is meant to reject.
                if (current.Payload != null && next.Payload != null &&
                    (!current.Payload.Target.Equals(next.Payload.Target) ||
                     !current.Payload.OwnedOverrideHash.Equals(
                        next.Payload.OwnedOverrideHash) ||
                     !OptionalDigestEquals(
                        current.Payload.SourceEdidSignature,
                        next.Payload.SourceEdidSignature)))
                {
                    throw new SecureStateConflictException(
                        "An EDID journal transition attempted to change durable ownership facts.");
                }

                return;
            }

            if (!EdidJournal.CanStartNewOperation(current.State, next.State))
            {
                throw new SecureStateConflictException(
                    "A new EDID operation cannot replace a non-terminal or conflicted journal.");
            }
        }

        private static bool OptionalDigestEquals(
            Sha256Digest first,
            Sha256Digest second)
        {
            if (first == null || second == null)
            {
                return first == null && second == null;
            }

            return first.Equals(second);
        }

        internal PowerJournal SavePower(PowerJournal journal)
        {
            EnsureKind(SecureStateLockKind.Power);
            if (journal == null)
                throw new ArgumentNullException(nameof(journal));

            // Match the EDID replacement discipline: this lock-protected
            // read is the only authority for a durable power transition.  In
            // particular a retained GUID cannot be silently abandoned for a
            // new plan, and a conflicted operation cannot restart itself.
            ValidatePowerReplacement(ReadPower(), journal);

            JournalEnvelope verified = ReplaceAndStrictRead(journal);
            PowerJournal result = verified as PowerJournal;
            if (result == null)
            {
                throw new SecureStateConflictException(
                    "The durable power journal changed kind during replacement.");
            }

            return result;
        }

        private static void ValidatePowerReplacement(
            PowerJournal current,
            PowerJournal next)
        {
            if (current == null)
            {
                if (next.State != PowerJournalState.Creating ||
                    next.Generation.Value != 1)
                {
                    throw new SecureStateConflictException(
                        "The first power journal record must be generation 1 Creating.");
                }

                return;
            }

            if (!next.Generation.IsImmediateSuccessorOf(current.Generation))
            {
                throw new SecureStateConflictException(
                    "The next power journal generation is not the immediate durable successor.");
            }

            if (next.OperationId.Equals(current.OperationId))
            {
                if (!PowerJournal.CanTransition(current.State, next.State))
                {
                    throw new SecureStateConflictException(
                        "The next power journal state is not an allowed transition.");
                }

                if (current.Payload != null && next.Payload != null)
                {
                    bool mayReconfigureOwnedScheme =
                        next.State == PowerJournalState.Creating &&
                        (current.State == PowerJournalState.Applied ||
                         current.State == PowerJournalState.InactiveRetained);
                    if (mayReconfigureOwnedScheme)
                    {
                        if (current.Payload.Target.OwnedSchemeId !=
                            next.Payload.Target.OwnedSchemeId)
                        {
                            throw new SecureStateConflictException(
                                "A power preset switch attempted to replace its owned scheme.");
                        }
                    }
                    else if (
                        current.State == PowerJournalState.InactiveRetained &&
                        next.State == PowerJournalState.Applied)
                    {
                        if (!current.Payload.Target.HasSameOwnedResource(
                                next.Payload.Target))
                        {
                            throw new SecureStateConflictException(
                                "A retained power transaction attempted to replace its owned resource.");
                        }
                    }
                    else if (!current.Payload.Target.Equals(next.Payload.Target))
                    {
                        throw new SecureStateConflictException(
                            "A power journal transition attempted to change durable ownership facts.");
                    }
                }

                return;
            }

            if (!PowerJournal.CanStartNewOperation(current.State, next.State))
            {
                throw new SecureStateConflictException(
                    "A new power operation cannot replace a retained, active, or conflicted journal.");
            }
        }

        internal static EdidJournal ReadEdidStatus()
        {
            JournalEnvelope envelope = ReadStatusEnvelope(
                SecureStateFile.EdidJournal);
            if (envelope == null)
                return null;

            EdidJournal journal = envelope as EdidJournal;
            if (journal == null)
            {
                throw new SecureStateConflictException(
                    "The trusted EDID journal contains a power record.");
            }

            return journal;
        }

        internal static PowerJournal ReadPowerStatus()
        {
            JournalEnvelope envelope = ReadStatusEnvelope(
                SecureStateFile.PowerJournal);
            if (envelope == null)
                return null;

            PowerJournal journal = envelope as PowerJournal;
            if (journal == null)
            {
                throw new SecureStateConflictException(
                    "The trusted power journal contains an EDID record.");
            }

            return journal;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            try
            {
                if (transactionLock != null)
                    transactionLock.Dispose();
            }
            finally
            {
                if (store != null)
                    store.Dispose();
            }
        }

        private static JournalStore OpenMutation(SecureStateLockKind kind)
        {
            SecureStateStore secureStore = SecureStateStore.OpenOrCreateElevated();
            SecureStateLockHandle lockHandle = null;
            try
            {
                lockHandle = kind == SecureStateLockKind.Edid
                    ? secureStore.AcquireEdidLock(TransactionLockTimeout)
                    : secureStore.AcquirePowerLock(TransactionLockTimeout);
                return new JournalStore(secureStore, lockHandle, kind);
            }
            catch
            {
                if (lockHandle != null)
                    lockHandle.Dispose();
                secureStore.Dispose();
                throw;
            }
        }

        private JournalEnvelope ReadEnvelope()
        {
            ThrowIfDisposed();
            byte[] bytes = store.ReadCurrentJournal(transactionLock);
            if (bytes == null)
                return null;

            return ParseStrict(bytes);
        }

        private JournalEnvelope ReplaceAndStrictRead(JournalEnvelope journal)
        {
            ThrowIfDisposed();
            byte[] serialized;
            try
            {
                serialized = JournalCodec.Serialize(journal);
            }
            catch (ArgumentException exception)
            {
                throw new SecureStateConflictException(
                    "The next trusted journal violates its typed schema.",
                    exception);
            }

            byte[] verified = store.ReplaceJournal(transactionLock, serialized);
            JournalEnvelope parsed = ParseStrict(verified);
            byte[] canonical = JournalCodec.Serialize(parsed);
            if (!FixedTimeComparer.AreEqual(serialized, canonical) ||
                !FixedTimeComparer.AreEqual(serialized, verified))
            {
                throw new SecureStateConflictException(
                    "The durable trusted journal was not a canonical read-back.");
            }

            return parsed;
        }

        private static JournalEnvelope ReadStatusEnvelope(SecureStateFile file)
        {
            SecureStateStore secureStore;
            if (!SecureStateStore.TryOpenExistingReadOnly(out secureStore))
                return null;

            using (secureStore)
            {
                SecureStateFileHandle handle;
                if (!secureStore.TryOpenExisting(file, FileAccess.Read, out handle))
                    return null;

                using (handle)
                {
                    return ParseStrict(ReadBounded(handle));
                }
            }
        }

        private static byte[] ReadBounded(SecureStateFileHandle handle)
        {
            using (FileStream stream = handle.OpenStream())
            {
                long length = stream.Length;
                if (length <= 0 ||
                    length > JournalCodec.MaximumJournalBytes)
                {
                    throw new SecureStateConflictException(
                        "The trusted journal has an invalid bounded length.");
                }

                byte[] bytes = new byte[(int)length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read == 0)
                    {
                        throw new SecureStateConflictException(
                            "The trusted journal was truncated while it was read.");
                    }

                    offset += read;
                }

                if (stream.ReadByte() != -1)
                {
                    throw new SecureStateConflictException(
                        "The trusted journal grew while it was read.");
                }

                handle.ValidateForUse();
                return bytes;
            }
        }

        private static JournalEnvelope ParseStrict(byte[] bytes)
        {
            try
            {
                return JournalCodec.Parse(bytes);
            }
            catch (JournalFormatException exception)
            {
                throw new SecureStateConflictException(
                    "The trusted journal is malformed.",
                    exception);
            }
        }

        private void EnsureKind(SecureStateLockKind expected)
        {
            ThrowIfDisposed();
            if (kind != expected)
                throw new InvalidOperationException(
                    "The trusted journal operation used the wrong domain lock.");
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException("JournalStore");
        }

    }

}
