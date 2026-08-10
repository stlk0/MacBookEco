using System;

namespace MacBookEco.Core
{
    public enum JournalTransactionKind
    {
        Edid = 1,
        Power = 2
    }

    public enum EdidJournalState
    {
        NotInstalled = 1,
        InstallPending = 2,
        Installed = 3,
        RestorePending = 4,
        Restored = 5,
        Conflict = 6
    }

    public enum PowerJournalState
    {
        NotManaged = 1,
        Creating = 2,
        Applied = 3,
        RestorePending = 4,
        InactiveRetained = 5,
        Conflict = 6
    }

    /// <summary>
    /// Stable policy identifiers.  The Windows adapter must map these values to
    /// the compiled preset catalog; a journal never supplies arbitrary settings.
    /// </summary>
    public enum PowerPresetId
    {
        Normal = 1,
        Cool = 2,
        MaximumBattery = 3
    }

    public enum PowerInactiveReason
    {
        None = 0,
        OriginalAlreadyActive = 1,
        ExternalSelection = 2
    }

    /// <summary>
    /// Immutable, non-zero journal generation.  The store is responsible for
    /// durable replacement; this value object prevents an unversioned record.
    /// </summary>
    public sealed class JournalGeneration : IEquatable<JournalGeneration>
    {
        public JournalGeneration(ulong value)
        {
            if (value == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "A journal generation must be greater than zero.");
            }

            Value = value;
        }

        public ulong Value { get; private set; }

        public JournalGeneration Next()
        {
            if (Value == ulong.MaxValue)
            {
                throw new InvalidOperationException(
                    "The journal generation cannot advance beyond UInt64.MaxValue.");
            }

            return new JournalGeneration(Value + 1);
        }

        public bool IsImmediateSuccessorOf(JournalGeneration previous)
        {
            return previous != null &&
                previous.Value != ulong.MaxValue &&
                Value == previous.Value + 1;
        }

        public bool Equals(JournalGeneration other)
        {
            return !ReferenceEquals(other, null) && Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as JournalGeneration);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// A random, non-empty operation identifier.  It has one canonical journal
    /// representation: a lower-case GUID in D format.
    /// </summary>
    public sealed class JournalOperationId : IEquatable<JournalOperationId>
    {
        public JournalOperationId(Guid value)
        {
            if (value == Guid.Empty)
            {
                throw new ArgumentException(
                    "A journal operation ID cannot be empty.",
                    nameof(value));
            }

            Value = value;
        }

        public Guid Value { get; private set; }

        public static JournalOperationId NewId()
        {
            return new JournalOperationId(Guid.NewGuid());
        }

        public static JournalOperationId ParseCanonical(string value)
        {
            JournalOperationId result;
            if (!TryParseCanonical(value, out result))
            {
                throw new FormatException(
                    "A journal operation ID must be a non-empty lower-case D GUID.");
            }

            return result;
        }

        public static bool TryParseCanonical(
            string value,
            out JournalOperationId result)
        {
            result = null;
            Guid parsed;
            if (value == null ||
                value.Length != 36 ||
                !Guid.TryParseExact(value, "D", out parsed) ||
                parsed == Guid.Empty ||
                !string.Equals(
                    parsed.ToString("D"),
                    value,
                    StringComparison.Ordinal))
            {
                return false;
            }

            result = new JournalOperationId(parsed);
            return true;
        }

        public bool Equals(JournalOperationId other)
        {
            return !ReferenceEquals(other, null) && Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as JournalOperationId);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value.ToString("D");
        }
    }

    /// <summary>
    /// Base envelope shared by the two journal domains.  Timestamps are
    /// diagnostic only and are deliberately not used for target authorization.
    /// </summary>
    public abstract class JournalEnvelope
    {
        public const int FormatMarkerValue = 1;

        protected JournalEnvelope(
            JournalOperationId operationId,
            JournalGeneration generation,
            DateTime createdUtc,
            DateTime updatedUtc)
        {
            if (operationId == null)
            {
                throw new ArgumentNullException(nameof(operationId));
            }

            if (generation == null)
            {
                throw new ArgumentNullException(nameof(generation));
            }

            RequireUtc("createdUtc", createdUtc);
            RequireUtc("updatedUtc", updatedUtc);
            if (updatedUtc < createdUtc)
            {
                throw new ArgumentException(
                    "The journal update timestamp cannot precede creation.",
                    nameof(updatedUtc));
            }

            OperationId = operationId;
            Generation = generation;
            CreatedUtc = createdUtc;
            UpdatedUtc = updatedUtc;
        }

        public abstract JournalTransactionKind TransactionKind { get; }

        public JournalOperationId OperationId { get; private set; }

        public JournalGeneration Generation { get; private set; }

        public DateTime CreatedUtc { get; private set; }

        public DateTime UpdatedUtc { get; private set; }

        internal abstract byte StateCode { get; }

        protected static void RequireImmediateNextGeneration(
            JournalGeneration current,
            JournalGeneration next)
        {
            if (next == null || !next.IsImmediateSuccessorOf(current))
            {
                throw new ArgumentException(
                    "A transition must use the immediate next journal generation.",
                    nameof(next));
            }
        }

        protected static void RequireUtc(string name, DateTime value)
        {
            if (value.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException(
                    "Journal timestamps must be explicitly UTC.",
                    name);
            }
        }
    }

    /// <summary>
    /// A parse failure is distinct from an absent journal.  Callers must treat
    /// it as conflict/recovery-required rather than silently starting fresh.
    /// </summary>
    public sealed class JournalFormatException : FormatException
    {
        public JournalFormatException(string message)
            : base(message)
        {
        }

        public JournalFormatException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
