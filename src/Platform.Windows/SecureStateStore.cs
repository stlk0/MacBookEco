using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using MacBookEco.Core;
using Microsoft.Win32.SafeHandles;

namespace MacBookEco.Platform.Windows
{
    // Only these compiled names may exist below the trusted root. Keeping
    // callers away from arbitrary path strings is part of the trust boundary.
    internal enum SecureStateFile
    {
        EdidJournal,
        EdidJournalPrevious,
        EdidJournalStaging,
        PowerJournal,
        PowerJournalPrevious,
        PowerJournalStaging,
        EdidLock,
        PowerLock
    }

    internal enum SecureStateLockKind
    {
        Edid,
        Power
    }

    internal enum JournalFileRole
    {
        Current,
        Previous,
        Staging
    }

    /// <summary>
    /// Opens the privileged state root only through handles that have passed
    /// the security, object-type, reparse, link-count, and final-path checks.
    /// This class intentionally contains no environment-variable override and
    /// no path-taking public API.
    /// </summary>
    internal sealed class SecureStateStore : IDisposable
    {
        private const string RootDirectoryName = "MacBookEco.State";
        private const int MaximumLockTimeoutMilliseconds = 120000;
        private const int LockRetryMilliseconds = 50;
        private const int MaximumJournalBytes = 64 * 1024;

        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint ReadControl = 0x00020000;
        private const uint FileReadAttributes = 0x00000080;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint FileShareDelete = 0x00000004;

        /// <summary>
        /// Journal components tolerate delete sharing because ReplaceJournal
        /// renames them and an unelevated status reader may hold the current
        /// generation open across that commit.
        /// </summary>
        private const uint JournalShareMode =
            FileShareRead | FileShareWrite | FileShareDelete;

        /// <summary>
        /// A lock file must not.  A byte-range lock follows the file object,
        /// not the name, so with delete sharing a second process could rename
        /// the locked file away, create a fresh one at the same path, and take
        /// its own "exclusive" lock on a different object.  Both holders would
        /// then believe they own the transaction and neither would see an
        /// error.  Nothing in this class ever renames a lock file, so refusing
        /// delete sharing costs nothing.
        /// </summary>
        private const uint LockShareMode = FileShareRead | FileShareWrite;
        private const uint CreateNew = 1;
        private const uint OpenExisting = 3;
        private const uint FileAttributeNormal = 0x00000080;
        private const uint FileFlagWriteThrough = 0x80000000;
        private const uint FileFlagOpenReparsePoint = 0x00200000;
        private const uint FileFlagBackupSemantics = 0x02000000;
        private const uint FileTypeDisk = 0x0001;
        private const uint LockfileExclusiveLock = 0x00000002;
        private const uint LockfileFailImmediately = 0x00000001;
        private const uint MovefileWriteThrough = 0x00000008;
        private const int ErrorFileNotFound = 2;
        private const int ErrorFileExists = 80;
        private const int ErrorAlreadyExists = 183;
        private const int ErrorLockViolation = 33;
        private const int ErrorIoPending = 997;

        private const int FileAllAccess = 0x1F01FF;
        private const int FileReadAndExecute = 0x1200A9;
        private const int SeDaclPresent = 0x0004;
        private const int SeDaclDefaulted = 0x0008;
        private const int SeDaclAutoInheritRequired = 0x0100;
        private const int SeDaclProtected = 0x1000;

        private static readonly SecurityIdentifier SystemSid =
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        private static readonly SecurityIdentifier AdministratorsSid =
            new SecurityIdentifier(
                WellKnownSidType.BuiltinAdministratorsSid,
                null);
        private static readonly SecurityIdentifier UsersSid =
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        private readonly SafeFileHandle rootHandle;
        private readonly string rootPath;
        private readonly bool mayMutate;
        private readonly SecureStateObjectIdentity rootIdentity;
        private bool disposed;

        private SecureStateStore(
            SafeFileHandle rootHandle,
            string rootPath,
            bool mayMutate,
            SecureStateObjectIdentity rootIdentity)
        {
            this.rootHandle = rootHandle;
            this.rootPath = rootPath;
            this.mayMutate = mayMutate;
            this.rootIdentity = rootIdentity;
        }

        internal static string FixedRootPath => GetFixedRootPath();

        /// <summary>
        /// Creates a missing root only from an elevated token.  An existing root
        /// is never repaired: it must independently pass the same checks.
        /// </summary>
        internal static SecureStateStore OpenOrCreateElevated()
        {
            RequireElevatedToken();

            string expectedRootPath = GetFixedRootPath();
            CreateRootIfMissing(expectedRootPath);

            SafeFileHandle handle = OpenExistingDirectory(expectedRootPath);
            try
            {
                SecureStateObjectIdentity identity =
                    ValidateRootHandle(handle, expectedRootPath);
                return new SecureStateStore(
                    handle, expectedRootPath, true, identity);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Opens an already-existing state root for unprivileged diagnostics. It
        /// never creates or repairs anything; false is reserved for a genuinely
        /// absent root, while an inaccessible or malformed root is a conflict.
        /// </summary>
        internal static bool TryOpenExistingReadOnly(out SecureStateStore store)
        {
            string expectedRootPath = GetFixedRootPath();
            SafeFileHandle handle = TryOpenExistingDirectory(expectedRootPath);
            if (handle == null)
            {
                store = null;
                return false;
            }

            try
            {
                SecureStateObjectIdentity identity =
                    ValidateRootHandle(handle, expectedRootPath);
                store = new SecureStateStore(
                    handle, expectedRootPath, false, identity);
                return true;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Opens only a pre-defined component.  False means the object was
        /// genuinely absent; all other open failures remain a typed conflict.
        /// </summary>
        internal bool TryOpenExisting(
            SecureStateFile file,
            FileAccess access,
            out SecureStateFileHandle result)
        {
            ThrowIfDisposed();
            RequireReadOnlyAccess(access);
            ValidateRoot();

            string expectedPath = GetObjectPath(file);
            SafeFileHandle handle = TryOpenExistingFile(
                expectedPath, access, JournalShareMode);
            if (handle == null)
            {
                result = null;
                return false;
            }

            try
            {
                ValidateFileHandle(handle, expectedPath);
                result = new SecureStateFileHandle(this, file, handle, access);
                return true;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Opens a fixed state component, creating it with the exact descriptor
        /// only if it did not exist.  A concurrent/pre-created object is opened
        /// and validated rather than silently trusted or repaired.
        /// </summary>
        internal SecureStateFileHandle OpenOrCreate(
            SecureStateFile file,
            FileAccess access)
        {
            ThrowIfDisposed();
            RequireMutatingAccess();
            SafeFileHandle handle = OpenOrCreateFileHandle(
                file, access, JournalShareMode);
            return new SecureStateFileHandle(this, file, handle, access);
        }

        /// <summary>
        /// Reads a bounded journal only while the matching domain lock is held.
        /// The returned bytes came from a freshly opened and revalidated handle.
        /// </summary>
        internal byte[] ReadCurrentJournal(SecureStateLockHandle transactionLock)
        {
            SecureStateLockKind kind = RequireHeldLock(transactionLock);
            SecureStateFile current = GetJournalFile(kind, JournalFileRole.Current);
            SecureStateFileHandle handle;
            if (!TryOpenExisting(current, FileAccess.Read, out handle))
                return null;

            using (handle)
            {
                return ReadJournalBytes(handle);
            }
        }

        /// <summary>
        /// Durably writes a checked staging file, atomically makes it current,
        /// retains the prior current generation, and reopens the result by a
        /// checked handle.  The matching EDID/power lock is mandatory.
        /// </summary>
        internal byte[] ReplaceJournal(
            SecureStateLockHandle transactionLock,
            byte[] journalBytes)
        {
            RequireMutatingAccess();
            SecureStateLockKind kind = RequireHeldLock(transactionLock);
            byte[] expected = CopyAndValidateJournalBytes(journalBytes);
            SecureStateFile current = GetJournalFile(kind, JournalFileRole.Current);
            SecureStateFile previous = GetJournalFile(kind, JournalFileRole.Previous);
            SecureStateFile staging = GetJournalFile(kind, JournalFileRole.Staging);

            WriteStagingJournal(staging, expected);
            ValidateExistingJournalComponent(staging);
            bool hadCurrent = ValidateExistingJournalComponent(current);
            ValidateExistingJournalComponent(previous);

            // All three paths are compiled direct children of a root that is
            // still held without delete sharing.  They were just validated via
            // handles; the native replacement receives no caller-controlled path.
            ValidateRoot();
            string currentPath = GetObjectPath(current);
            string stagingPath = GetObjectPath(staging);
            string previousPath = GetObjectPath(previous);
            bool committed;
            if (hadCurrent)
            {
                committed = SecureStateNative.ReplaceFileW(
                    currentPath,
                    stagingPath,
                    previousPath,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero);
            }
            else
            {
                committed = SecureStateNative.MoveFileExW(
                    stagingPath,
                    currentPath,
                    MovefileWriteThrough);
            }

            if (!committed)
            {
                int error = Marshal.GetLastWin32Error();
                throw CreateOpenConflict(
                    "The trusted journal could not be atomically committed.",
                    error);
            }

            // Do not infer success from the native return alone.  Reopen every
            // relevant component and compare the durable current generation.
            ValidateRoot();
            byte[] verified = ReadCurrentJournal(transactionLock);
            if (!FixedTimeComparer.AreEqual(expected, verified))
            {
                throw new SecureStateConflictException(
                    "The committed trusted journal did not match its durable read-back.");
            }

            bool hasPreviousAfterCommit =
                ValidateExistingJournalComponent(previous);
            if (hadCurrent && !hasPreviousAfterCommit)
            {
                throw new SecureStateConflictException(
                    "The prior trusted journal generation was not retained.");
            }

            if (ValidateExistingJournalComponent(staging))
            {
                throw new SecureStateConflictException(
                    "The trusted journal staging file remained after replacement.");
            }

            return verified;
        }

        internal SecureStateLockHandle AcquireEdidLock(TimeSpan timeout)
        {
            return AcquireLock(SecureStateLockKind.Edid, timeout);
        }

        internal SecureStateLockHandle AcquirePowerLock(TimeSpan timeout)
        {
            return AcquireLock(SecureStateLockKind.Power, timeout);
        }

        internal SecureStateLockHandle AcquireLock(
            SecureStateLockKind kind,
            TimeSpan timeout)
        {
            ThrowIfDisposed();
            RequireMutatingAccess();
            int timeoutMilliseconds = ValidateTimeout(timeout);
            SecureStateFile lockFile = kind == SecureStateLockKind.Edid
                ? SecureStateFile.EdidLock
                : SecureStateFile.PowerLock;
            SafeFileHandle handle = OpenOrCreateFileHandle(
                lockFile,
                FileAccess.ReadWrite,
                LockShareMode);
            NativeOverlapped overlapped = new NativeOverlapped();
            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                while (true)
                {
                    if (SecureStateNative.LockFileEx(
                            handle,
                            LockfileExclusiveLock | LockfileFailImmediately,
                            0,
                            UInt32.MaxValue,
                            UInt32.MaxValue,
                            ref overlapped))
                    {
                        // Validate again while the exact locked handle is held.
                        ValidateRoot();
                        ValidateFileHandle(handle, GetObjectPath(lockFile));
                        return new SecureStateLockHandle(this, kind, handle, overlapped);
                    }

                    int error = Marshal.GetLastWin32Error();
                    if (error != ErrorLockViolation && error != ErrorIoPending)
                        throw CreateOpenConflict("The protected lock could not be acquired.", error);

                    if (timeoutMilliseconds == 0 ||
                        stopwatch.ElapsedMilliseconds >= timeoutMilliseconds)
                    {
                        throw new SecureStateBusyException(kind);
                    }

                    long remaining = timeoutMilliseconds - stopwatch.ElapsedMilliseconds;
                    int delay = remaining < LockRetryMilliseconds
                        ? (int)remaining
                        : LockRetryMilliseconds;
                    if (delay > 0)
                        Thread.Sleep(delay);
                }
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Revalidates a component handle its caller already holds, and returns
        /// the object identity it observed.  A caller that has validated before
        /// passes back what it saw then, so a component swapped between two
        /// validations is refused rather than accepted on its path alone.
        /// </summary>
        internal SecureStateObjectIdentity ValidateFileForUse(
            SecureStateFile file,
            SafeFileHandle handle,
            SecureStateObjectIdentity? expectedIdentity)
        {
            ThrowIfDisposed();
            if (handle == null || handle.IsInvalid || handle.IsClosed)
                throw new ObjectDisposedException("SecureStateFileHandle");

            // Root stays open without FILE_SHARE_DELETE, so a verified child
            // cannot be swapped by renaming the root between these checks.
            ValidateRoot();
            return ValidateObjectHandle(
                handle, GetObjectPath(file), false, true, expectedIdentity);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            rootHandle.Dispose();
        }

        /// <summary>
        /// The same check the EDID and power services already make through
        /// PrivilegeGuard. This used to be a second, independent implementation
        /// built on OpenProcessToken and GetTokenInformation, which meant two
        /// definitions of "elevated" could drift apart in exactly the component
        /// whose whole job is to refuse unprivileged mutation.
        /// </summary>
        private static void RequireElevatedToken()
        {
            try
            {
                PrivilegeGuard.RequireAdministrator();
            }
            catch (UnauthorizedAccessException)
            {
                throw new UnauthorizedAccessException(
                    "Trusted state mutation can only run in the elevated helper.");
            }
        }

        private static string GetFixedRootPath()
        {
            // SpecialFolder resolves the OS ProgramData known location; no
            // caller input or environment-variable override participates.
            string programData = Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData);
            if (string.IsNullOrEmpty(programData))
                throw new InvalidOperationException(
                    "The ProgramData known folder is unavailable.");

            string canonicalProgramData = Path.GetFullPath(programData);
            string result = Path.GetFullPath(
                Path.Combine(canonicalProgramData, RootDirectoryName));
            string requiredPrefix = EnsureTrailingDirectorySeparator(canonicalProgramData);
            if (!result.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "The compiled trusted state root resolved outside ProgramData.");

            return TrimTrailingDirectorySeparators(result);
        }

        private static void CreateRootIfMissing(string expectedRootPath)
        {
            using (SecurityDescriptorBuffer descriptor =
                new SecurityDescriptorBuffer())
            {
                if (SecureStateNative.CreateDirectoryW(
                        expectedRootPath,
                        descriptor.SecurityAttributes))
                {
                    return;
                }

                int error = Marshal.GetLastWin32Error();
                if (error == ErrorAlreadyExists)
                    return;

                throw CreateOpenConflict(
                    "The trusted state root could not be created.",
                    error);
            }
        }

        private static SafeFileHandle OpenExistingDirectory(string expectedRootPath)
        {
            SafeFileHandle handle = TryOpenExistingDirectory(expectedRootPath);
            if (handle == null)
            {
                throw new SecureStateConflictException(
                    "The trusted state root disappeared before it could be opened.");
            }

            return handle;
        }

        private static SafeFileHandle TryOpenExistingDirectory(string expectedRootPath)
        {
            // Deliberately without FILE_SHARE_DELETE: ValidateFileForUse
            // revalidates a child against this handle and relies on the root
            // being unrenameable for as long as it is held. READ_CONTROL and
            // FILE_READ_ATTRIBUTES are not sharing-checked, so a concurrent
            // reader still opens the root; only delete and rename are refused.
            SafeFileHandle handle = SecureStateNative.CreateFileW(
                expectedRootPath,
                ReadControl | FileReadAttributes,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                FileFlagBackupSemantics | FileFlagOpenReparsePoint,
                IntPtr.Zero);
            if (handle == null || handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                if (handle != null)
                    handle.Dispose();
                if (error == ErrorFileNotFound)
                    return null;
                throw CreateOpenConflict(
                    "The trusted state root could not be opened.",
                    error);
            }

            return handle;
        }

        private SafeFileHandle OpenOrCreateFileHandle(
            SecureStateFile file,
            FileAccess access,
            uint shareMode)
        {
            RequireMutatingAccess();
            ValidateRoot();
            string expectedPath = GetObjectPath(file);
            SafeFileHandle handle = TryOpenExistingFile(
                expectedPath, access, shareMode);
            if (handle == null)
            {
                handle = TryCreateNewFile(expectedPath, access, shareMode);
                if (handle == null)
                    handle = TryOpenExistingFile(
                        expectedPath, access, shareMode);
                if (handle == null)
                    throw new SecureStateConflictException(
                        "The trusted state file changed while it was being opened.");
            }

            try
            {
                ValidateFileHandle(handle, expectedPath);
                return handle;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        private static SafeFileHandle TryOpenExistingFile(
            string expectedPath,
            FileAccess access,
            uint shareMode)
        {
            SafeFileHandle handle = SecureStateNative.CreateFileW(
                expectedPath,
                GetDesiredAccess(access),
                shareMode,
                IntPtr.Zero,
                OpenExisting,
                GetFileFlags(access),
                IntPtr.Zero);
            if (handle != null && !handle.IsInvalid)
                return handle;

            int error = Marshal.GetLastWin32Error();
            if (handle != null)
                handle.Dispose();
            if (error == ErrorFileNotFound)
                return null;

            throw CreateOpenConflict(
                "A trusted state file could not be safely opened.",
                error);
        }

        private static SafeFileHandle TryCreateNewFile(
            string expectedPath,
            FileAccess access,
            uint shareMode)
        {
            using (SecurityDescriptorBuffer descriptor =
                new SecurityDescriptorBuffer())
            {
                SafeFileHandle handle = SecureStateNative.CreateFileW(
                    expectedPath,
                    GetDesiredAccess(access),
                    shareMode,
                    descriptor.SecurityAttributes,
                    CreateNew,
                    GetFileFlags(access),
                    IntPtr.Zero);
                if (handle != null && !handle.IsInvalid)
                    return handle;

                int error = Marshal.GetLastWin32Error();
                if (handle != null)
                    handle.Dispose();
                if (error == ErrorFileExists || error == ErrorAlreadyExists)
                    return null;

                throw CreateOpenConflict(
                    "A trusted state file could not be created.",
                    error);
            }
        }

        private SecureStateLockKind RequireHeldLock(
            SecureStateLockHandle transactionLock)
        {
            ThrowIfDisposed();
            if (transactionLock == null)
                throw new ArgumentNullException(nameof(transactionLock));

            return transactionLock.GetHeldKind(this);
        }

        private static SecureStateFile GetJournalFile(
            SecureStateLockKind kind,
            JournalFileRole role)
        {
            if (kind == SecureStateLockKind.Edid)
            {
                switch (role)
                {
                    case JournalFileRole.Current:
                        return SecureStateFile.EdidJournal;
                    case JournalFileRole.Previous:
                        return SecureStateFile.EdidJournalPrevious;
                    case JournalFileRole.Staging:
                        return SecureStateFile.EdidJournalStaging;
                }
            }
            else if (kind == SecureStateLockKind.Power)
            {
                switch (role)
                {
                    case JournalFileRole.Current:
                        return SecureStateFile.PowerJournal;
                    case JournalFileRole.Previous:
                        return SecureStateFile.PowerJournalPrevious;
                    case JournalFileRole.Staging:
                        return SecureStateFile.PowerJournalStaging;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        private bool ValidateExistingJournalComponent(SecureStateFile file)
        {
            SecureStateFileHandle handle;
            if (!TryOpenExisting(file, FileAccess.Read, out handle))
                return false;

            using (handle)
            {
                handle.ValidateForUse();
                return true;
            }
        }

        private void WriteStagingJournal(
            SecureStateFile staging,
            byte[] expected)
        {
            using (SecureStateFileHandle handle =
                OpenOrCreate(staging, FileAccess.ReadWrite))
            {
                using (FileStream stream = handle.OpenStream())
                {
                    stream.Seek(0, SeekOrigin.Begin);
                    stream.SetLength(0);
                    stream.Write(expected, 0, expected.Length);
                    stream.Flush(true);
                }

                handle.ValidateForUse();
                byte[] readBack = ReadJournalBytes(handle);
                if (!FixedTimeComparer.AreEqual(expected, readBack))
                {
                    throw new SecureStateConflictException(
                        "The trusted journal staging write did not survive read-back.");
                }
            }
        }

        private static byte[] CopyAndValidateJournalBytes(byte[] journalBytes)
        {
            if (journalBytes == null)
                throw new ArgumentNullException(nameof(journalBytes));
            if (journalBytes.Length > MaximumJournalBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(journalBytes),
                    "A journal cannot exceed 64 KiB.");
            }

            byte[] result = new byte[journalBytes.Length];
            Buffer.BlockCopy(journalBytes, 0, result, 0, result.Length);
            return result;
        }

        private static byte[] ReadJournalBytes(SecureStateFileHandle handle)
        {
            byte[] result;
            using (FileStream stream = handle.OpenStream())
            {
                // The same checked native handle is reused after the durable
                // staging write. Its kernel file pointer is therefore at EOF
                // even though this is a new FileStream wrapper.
                RewindJournalStream(stream);
                long length = stream.Length;
                if (length < 0 || length > MaximumJournalBytes)
                {
                    throw new SecureStateConflictException(
                        "The trusted journal exceeds its 64 KiB bound.");
                }

                result = new byte[(int)length];
                int offset = 0;
                while (offset < result.Length)
                {
                    int read = stream.Read(result, offset, result.Length - offset);
                    if (read == 0)
                    {
                        throw new SecureStateConflictException(
                            "The trusted journal was truncated while it was being read.");
                    }

                    offset += read;
                }

                if (stream.ReadByte() != -1)
                {
                    throw new SecureStateConflictException(
                        "The trusted journal grew while it was being read.");
                }
            }

            handle.ValidateForUse();
            return result;
        }

        private string GetObjectPath(SecureStateFile file)
        {
            return Path.Combine(rootPath, GetObjectName(file));
        }

        private static string GetObjectName(SecureStateFile file)
        {
            switch (file)
            {
                case SecureStateFile.EdidJournal:
                    return "edid.journal";
                case SecureStateFile.EdidJournalPrevious:
                    return "edid.journal.previous";
                case SecureStateFile.EdidJournalStaging:
                    return "edid.journal.staging";
                case SecureStateFile.PowerJournal:
                    return "power.journal";
                case SecureStateFile.PowerJournalPrevious:
                    return "power.journal.previous";
                case SecureStateFile.PowerJournalStaging:
                    return "power.journal.staging";
                case SecureStateFile.EdidLock:
                    return "edid.transaction.lock";
                case SecureStateFile.PowerLock:
                    return "power.transaction.lock";
                default:
                    throw new ArgumentOutOfRangeException(nameof(file));
            }
        }

        private static uint GetDesiredAccess(FileAccess access)
        {
            if (access != FileAccess.Read &&
                access != FileAccess.Write &&
                access != FileAccess.ReadWrite)
            {
                throw new ArgumentOutOfRangeException(nameof(access));
            }

            uint result = ReadControl | FileReadAttributes;
            if ((access & FileAccess.Read) == FileAccess.Read)
                result |= GenericRead;
            if ((access & FileAccess.Write) == FileAccess.Write)
                result |= GenericWrite;
            return result;
        }

        private static uint GetFileFlags(FileAccess access)
        {
            uint result = FileAttributeNormal | FileFlagOpenReparsePoint;
            if ((access & FileAccess.Write) == FileAccess.Write)
                result |= FileFlagWriteThrough;
            return result;
        }

        private static int ValidateTimeout(TimeSpan timeout)
        {
            if (timeout < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout));
            if (timeout.TotalMilliseconds > MaximumLockTimeoutMilliseconds)
                throw new ArgumentOutOfRangeException(
                    nameof(timeout),
                    "A transaction lock timeout must be bounded to two minutes or less.");

            return (int)Math.Ceiling(timeout.TotalMilliseconds);
        }

        private static SecureStateObjectIdentity ValidateRootHandle(
            SafeFileHandle handle,
            string expectedPath)
        {
            return ValidateObjectHandle(handle, expectedPath, true, false, null);
        }

        /// <summary>
        /// Revalidates the root this store was opened against.  The root is held
        /// for the lifetime of the store without delete sharing, so it cannot be
        /// renamed away; requiring the same NTFS identity as well means a root
        /// that somehow resolved elsewhere is refused rather than trusted.
        /// </summary>
        private void ValidateRoot()
        {
            ValidateObjectHandle(
                rootHandle, rootPath, true, false, rootIdentity);
        }

        private static SecureStateObjectIdentity ValidateFileHandle(
            SafeFileHandle handle,
            string expectedPath)
        {
            return ValidateObjectHandle(handle, expectedPath, false, true, null);
        }

        /// <summary>
        /// Returns the NTFS identity of the validated object.  A caller holding
        /// a long-lived handle passes the identity it saw first back in as
        /// <paramref name="expectedIdentity"/>: the final-path comparison below
        /// proves only that a name still resolves here, while the volume serial
        /// and file index prove it is still the same object behind that name.
        /// </summary>
        private static SecureStateObjectIdentity ValidateObjectHandle(
            SafeFileHandle handle,
            string expectedPath,
            bool requireDirectory,
            bool requireSingleLink,
            SecureStateObjectIdentity? expectedIdentity)
        {
            if (handle == null || handle.IsInvalid || handle.IsClosed)
                throw new SecureStateConflictException(
                    "The trusted state handle is invalid.");

            ByHandleFileInformation information;
            if (!SecureStateNative.GetFileInformationByHandle(handle, out information))
            {
                int error = Marshal.GetLastWin32Error();
                throw CreateOpenConflict(
                    "The trusted state object metadata could not be read.",
                    error);
            }

            bool isDirectory =
                (information.FileAttributes & (uint)FileAttributes.Directory) != 0;
            bool isReparsePoint =
                (information.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0;
            if (SecureStateNative.GetFileType(handle) != FileTypeDisk ||
                isDirectory != requireDirectory ||
                isReparsePoint)
            {
                throw new SecureStateConflictException(
                    "The trusted state object has an unexpected type or reparse attribute.");
            }

            if (requireSingleLink && information.NumberOfLinks != 1)
            {
                throw new SecureStateConflictException(
                    "The trusted state file is not a single-link file.");
            }

            string finalPath = GetFinalPath(handle);
            if (!string.Equals(
                    CanonicalizePath(finalPath),
                    CanonicalizePath(expectedPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new SecureStateConflictException(
                    "The trusted state object resolved to an unexpected final path.");
            }

            SecureStateObjectIdentity identity =
                SecureStateObjectIdentity.FromInformation(information);
            if (expectedIdentity.HasValue &&
                !identity.Equals(expectedIdentity.Value))
            {
                throw new SecureStateConflictException(
                    "The trusted state object was replaced by a different object "
                        + "at the same path.");
            }

            ValidateExactSecurityDescriptor(handle);
            return identity;
        }

        private static string GetFinalPath(SafeFileHandle handle)
        {
            const int MaximumFinalPathCharacters = 32768;
            StringBuilder path = new StringBuilder(MaximumFinalPathCharacters);
            uint length = SecureStateNative.GetFinalPathNameByHandleW(
                handle,
                path,
                (uint)path.Capacity,
                0);
            if (length == 0 || length >= path.Capacity)
            {
                int error = Marshal.GetLastWin32Error();
                throw CreateOpenConflict(
                    "The trusted state final path could not be read.",
                    error);
            }

            return path.ToString();
        }

        private static string CanonicalizePath(string path)
        {
            const string ExtendedPathPrefix = "\\\\?\\";
            const string ExtendedUncPathPrefix = "\\\\?\\UNC\\";
            string result = path;
            if (result.StartsWith(
                    ExtendedUncPathPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                result = "\\\\" + result.Substring(ExtendedUncPathPrefix.Length);
            }
            else if (result.StartsWith(
                         ExtendedPathPrefix,
                         StringComparison.OrdinalIgnoreCase))
            {
                result = result.Substring(ExtendedPathPrefix.Length);
            }

            return TrimTrailingDirectorySeparators(Path.GetFullPath(result));
        }

        private static string EnsureTrailingDirectorySeparator(string path)
        {
            if (path.EndsWith(
                    Path.DirectorySeparatorChar.ToString(),
                    StringComparison.Ordinal) ||
                path.EndsWith(
                    Path.AltDirectorySeparatorChar.ToString(),
                    StringComparison.Ordinal))
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }

        private static string TrimTrailingDirectorySeparators(string path)
        {
            return path.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }

        private static void ValidateExactSecurityDescriptor(SafeFileHandle handle)
        {
            IntPtr owner;
            IntPtr group;
            IntPtr dacl;
            IntPtr sacl;
            IntPtr descriptor;
            int status = SecureStateNative.GetSecurityInfo(
                handle,
                SecureStateNative.SeFileObject,
                SecureStateNative.OwnerSecurityInformation |
                SecureStateNative.DaclSecurityInformation,
                out owner,
                out group,
                out dacl,
                out sacl,
                out descriptor);
            if (status != 0 || descriptor == IntPtr.Zero)
            {
                throw new SecureStateConflictException(
                    "The trusted state object security descriptor could not be read.",
                    new Win32Exception(status));
            }

            try
            {
                uint descriptorLength =
                    SecureStateNative.GetSecurityDescriptorLength(descriptor);
                if (descriptorLength == 0 || descriptorLength > Int32.MaxValue)
                {
                    throw new SecureStateConflictException(
                        "The trusted state object has an invalid security descriptor.");
                }

                byte[] bytes = new byte[(int)descriptorLength];
                Marshal.Copy(descriptor, bytes, 0, bytes.Length);
                RawSecurityDescriptor security =
                    new RawSecurityDescriptor(bytes, 0);
                if (!IsTrustedOwner(security.Owner))
                {
                    throw new SecureStateConflictException(
                        "The trusted state object has an unexpected owner.");
                }

                int control = (int)security.ControlFlags;
                // ReplaceFile may mark the resulting descriptor
                // SE_DACL_AUTO_INHERITED even when the DACL remains protected
                // and every ACE is still explicit and byte-for-byte equal to
                // our allowlist. The flag alone does not grant inheritance.
                if (!HasAcceptedDaclControlFlags(control) ||
                    security.DiscretionaryAcl == null ||
                    security.DiscretionaryAcl.Count != 3)
                {
                    throw new SecureStateConflictException(
                        "The trusted state object does not have the required protected DACL.");
                }

                ValidateExpectedAce(
                    security.DiscretionaryAcl[0],
                    SystemSid,
                    FileAllAccess);
                ValidateExpectedAce(
                    security.DiscretionaryAcl[1],
                    AdministratorsSid,
                    FileAllAccess);
                ValidateExpectedAce(
                    security.DiscretionaryAcl[2],
                    UsersSid,
                    FileReadAndExecute);
            }
            catch (ArgumentException ex)
            {
                throw new SecureStateConflictException(
                    "The trusted state object has a malformed security descriptor.",
                    ex);
            }
            finally
            {
                SecureStateNative.LocalFree(descriptor);
            }
        }

        internal static void RewindJournalStream(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (!stream.CanSeek)
                throw new ArgumentException(
                    "A journal stream must support bounded seek.",
                    nameof(stream));

            stream.Seek(0, SeekOrigin.Begin);
        }

        internal static bool HasAcceptedDaclControlFlags(int control)
        {
            return (control & SeDaclPresent) != 0 &&
                (control & SeDaclProtected) != 0 &&
                (control & SeDaclDefaulted) == 0 &&
                (control & SeDaclAutoInheritRequired) == 0;
        }

        private static void ValidateExpectedAce(
            GenericAce ace,
            SecurityIdentifier expectedSid,
            int expectedAccessMask)
        {
            CommonAce commonAce = ace as CommonAce;
            if (commonAce == null ||
                commonAce.AceType != AceType.AccessAllowed ||
                commonAce.AceFlags != AceFlags.None ||
                commonAce.AccessMask != expectedAccessMask ||
                !commonAce.SecurityIdentifier.Equals(expectedSid))
            {
                throw new SecureStateConflictException(
                    "The trusted state object DACL is not the exact expected DACL.");
            }
        }

        private static bool IsTrustedOwner(SecurityIdentifier owner)
        {
            return owner != null &&
                (owner.Equals(SystemSid) || owner.Equals(AdministratorsSid));
        }

        private static SecureStateConflictException CreateOpenConflict(
            string message,
            int error)
        {
            return new SecureStateConflictException(
                message + " " + new Win32Exception(error).Message,
                new Win32Exception(error));
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException("SecureStateStore");
        }

        private void RequireReadOnlyAccess(FileAccess access)
        {
            if (!mayMutate && access != FileAccess.Read)
            {
                throw new UnauthorizedAccessException(
                    "A read-only trusted state store cannot request write access.");
            }
        }

        private void RequireMutatingAccess()
        {
            if (!mayMutate)
            {
                throw new UnauthorizedAccessException(
                    "This trusted state store was opened for read-only diagnostics.");
            }
        }

        /// <summary>
        /// The NTFS identity of an open object: volume serial plus file index.
        /// Unlike a path, it does not change when the object is renamed and
        /// cannot be reused by a different object created at the same name.
        /// </summary>
        internal struct SecureStateObjectIdentity
        {
            private uint volumeSerialNumber;
            private uint fileIndexHigh;
            private uint fileIndexLow;

            internal static SecureStateObjectIdentity FromInformation(
                ByHandleFileInformation information)
            {
                SecureStateObjectIdentity identity;
                identity.volumeSerialNumber = information.VolumeSerialNumber;
                identity.fileIndexHigh = information.FileIndexHigh;
                identity.fileIndexLow = information.FileIndexLow;
                return identity;
            }

            internal bool Equals(SecureStateObjectIdentity other)
            {
                return volumeSerialNumber == other.volumeSerialNumber &&
                    fileIndexHigh == other.fileIndexHigh &&
                    fileIndexLow == other.fileIndexLow;
            }
        }

        private sealed class SecurityDescriptorBuffer : IDisposable
        {
            private IntPtr descriptor;
            private IntPtr attributes;

            internal SecurityDescriptorBuffer()
            {
                RawAcl dacl = new RawAcl(2, 3);
                dacl.InsertAce(
                    0,
                    new CommonAce(
                        AceFlags.None,
                        AceQualifier.AccessAllowed,
                        FileAllAccess,
                        SystemSid,
                        false,
                        null));
                dacl.InsertAce(
                    1,
                    new CommonAce(
                        AceFlags.None,
                        AceQualifier.AccessAllowed,
                        FileAllAccess,
                        AdministratorsSid,
                        false,
                        null));
                dacl.InsertAce(
                    2,
                    new CommonAce(
                        AceFlags.None,
                        AceQualifier.AccessAllowed,
                        FileReadAndExecute,
                        UsersSid,
                        false,
                        null));

                RawSecurityDescriptor security = new RawSecurityDescriptor(
                    ControlFlags.DiscretionaryAclPresent |
                    ControlFlags.DiscretionaryAclProtected,
                    AdministratorsSid,
                    AdministratorsSid,
                    null,
                    dacl);
                byte[] binary = new byte[security.BinaryLength];
                security.GetBinaryForm(binary, 0);
                descriptor = Marshal.AllocHGlobal(binary.Length);
                Marshal.Copy(binary, 0, descriptor, binary.Length);

                NativeSecurityAttributes values = new NativeSecurityAttributes();
                values.Length = Marshal.SizeOf(typeof(NativeSecurityAttributes));
                values.SecurityDescriptor = descriptor;
                values.InheritHandle = 0;
                attributes = Marshal.AllocHGlobal(values.Length);
                Marshal.StructureToPtr(values, attributes, false);
            }

            internal IntPtr SecurityAttributes => attributes;

            public void Dispose()
            {
                if (attributes != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(attributes);
                    attributes = IntPtr.Zero;
                }

                if (descriptor != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(descriptor);
                    descriptor = IntPtr.Zero;
                }
            }
        }
    }
}
