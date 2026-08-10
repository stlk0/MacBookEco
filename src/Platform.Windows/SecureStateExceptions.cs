using System;
using System.IO;

namespace MacBookEco.Platform.Windows
{
    internal enum SecureStateFailureKind
    {
        Conflict,
        Busy
    }

    internal class SecureStateException : IOException
    {
        internal SecureStateException(
            SecureStateFailureKind kind,
            string message)
            : base(message)
        {
            Kind = kind;
        }

        internal SecureStateException(
            SecureStateFailureKind kind,
            string message,
            Exception innerException)
            : base(message, innerException)
        {
            Kind = kind;
        }

        internal SecureStateFailureKind Kind { get; private set; }
    }

    // A privileged caller must surface this as Conflict/RecoveryRequired and
    // must not attempt to repair, move, or reuse the object.
    internal sealed class SecureStateConflictException : SecureStateException
    {
        internal SecureStateConflictException(string message)
            : base(SecureStateFailureKind.Conflict, message)
        {
        }

        internal SecureStateConflictException(
            string message,
            Exception innerException)
            : base(SecureStateFailureKind.Conflict, message, innerException)
        {
        }
    }

    internal sealed class SecureStateBusyException : SecureStateException
    {
        internal SecureStateBusyException(SecureStateLockKind kind)
            : base(
                SecureStateFailureKind.Busy,
                "The " + kind.ToString() + " transaction is already in progress.")
        {
            LockKind = kind;
        }

        internal SecureStateLockKind LockKind { get; private set; }
    }
}