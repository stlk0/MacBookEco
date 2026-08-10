using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MacBookEco.Platform.Windows
{
    /// <summary>
    /// A caller can create a stream only from the already checked native file
    /// handle.  No path-based reopen is exposed after validation.
    /// </summary>
    internal sealed class SecureStateFileHandle : IDisposable
    {
        private readonly SecureStateStore store;
        private readonly SecureStateFile file;
        private readonly SafeFileHandle handle;
        private readonly FileAccess access;
        private SecureStateStore.SecureStateObjectIdentity? identity;
        private bool disposed;

        internal SecureStateFileHandle(
            SecureStateStore store,
            SecureStateFile file,
            SafeFileHandle handle,
            FileAccess access)
        {
            this.store = store;
            this.file = file;
            this.handle = handle;
            this.access = access;
        }

        /// <summary>
        /// The returned stream borrows this handle: it wraps the same native
        /// handle without owning it, so it must be disposed before, or with,
        /// the SecureStateFileHandle it came from.  Every caller does that
        /// within a single using scope.  Taking a DangerousAddRef here would
        /// not extend that lifetime -- the matching release has to run before
        /// the stream is ever touched -- so this does not pretend to.
        /// </summary>
        internal FileStream OpenStream()
        {
            ThrowIfDisposed();
            Revalidate();

            SafeFileHandle nonOwningHandle = new SafeFileHandle(
                handle.DangerousGetHandle(),
                false);
            return new FileStream(nonOwningHandle, access, 4096, false);
        }

        internal void ValidateForUse()
        {
            ThrowIfDisposed();
            Revalidate();
        }

        /// <summary>
        /// Records the object identity seen on the first validation and requires
        /// every later one to observe the same object, so a component replaced
        /// at the same path between two checks is refused.
        /// </summary>
        private void Revalidate()
        {
            identity = store.ValidateFileForUse(file, handle, identity);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            handle.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException("SecureStateFileHandle");
        }
    }

    /// <summary>
    /// LockFileEx locks are kernel-owned and therefore released automatically
    /// if the helper crashes. The backing file is itself a checked object.
    /// </summary>
    internal sealed class SecureStateLockHandle : IDisposable
    {
        private readonly SecureStateStore store;
        private readonly SecureStateLockKind kind;
        private SafeFileHandle handle;
        private NativeOverlapped overlapped;
        private bool locked;

        internal SecureStateLockHandle(
            SecureStateStore store,
            SecureStateLockKind kind,
            SafeFileHandle handle,
            NativeOverlapped overlapped)
        {
            this.store = store;
            this.kind = kind;
            this.handle = handle;
            this.overlapped = overlapped;
            locked = true;
        }

        internal SecureStateLockKind GetHeldKind(SecureStateStore expectedStore)
        {
            if (!Object.ReferenceEquals(store, expectedStore))
            {
                throw new InvalidOperationException(
                    "The transaction lock belongs to a different trusted state store.");
            }

            if (handle == null || !locked)
                throw new ObjectDisposedException("SecureStateLockHandle");

            return kind;
        }

        public void Dispose()
        {
            if (handle == null)
                return;

            try
            {
                if (locked)
                {
                    SecureStateNative.UnlockFileEx(
                        handle,
                        0,
                        UInt32.MaxValue,
                        UInt32.MaxValue,
                        ref overlapped);
                    locked = false;
                }
            }
            finally
            {
                handle.Dispose();
                handle = null;
            }
        }
    }
}