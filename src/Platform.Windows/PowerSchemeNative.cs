using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MacBookEco.AppPolicy;

namespace MacBookEco.Platform.Windows
{
    /// <summary>
    /// The friendly name of the app-owned power scheme, which doubles as the
    /// ownership marker.
    ///
    /// This lived as a hand-built string in two places: the elevated writer
    /// that stamps it and the unelevated reader that recognises it. Changing
    /// one without the other would not fail to compile or throw. It would
    /// quietly stop recognising MacBook Eco's own scheme, reporting a healthy
    /// installation as Conflict and refusing to restore the user's original
    /// plan. One definition removes that failure mode entirely.
    /// </summary>
    internal static class PowerSchemeNaming
    {
        /// <summary>
        /// Includes a prefix of the owned GUID so two installations, or a
        /// leftover scheme from an earlier one, remain distinguishable by name
        /// in the Windows power UI.
        /// </summary>
        internal static string OwnedFriendlyName(PowerPreset preset, Guid owned)
        {
            return "MacBook Eco (" +
                PowerPresetCatalog.Get(preset).DisplayName +
                ", " +
                owned.ToString("N").Substring(0, 8) +
                ")";
        }
    }

    /// <summary>
    /// Thin, shared wrapper over the powrprof scheme APIs.
    ///
    /// The elevated writer and the unelevated status reader both need to read
    /// the active scheme and a scheme's friendly name, and both had their own
    /// copy including the two-call buffer-sizing protocol and the not-found
    /// classification. Only the mutating members require elevation; the reads
    /// are safe from the tray.
    /// </summary>
    internal static class PowerSchemeNative
    {
        private const uint MaximumFriendlyNameBytes = 65536;

        internal static Guid ReadActiveScheme()
        {
            IntPtr pointer;
            uint error = NativeMethods.PowerGetActiveScheme(
                IntPtr.Zero,
                out pointer);
            if (error != NativeMethods.ERROR_SUCCESS)
            {
                throw new Win32Exception((int)error, "PowerGetActiveScheme failed.");
            }

            if (pointer == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "PowerGetActiveScheme returned a null GUID.");
            }

            try
            {
                return (Guid)Marshal.PtrToStructure(pointer, typeof(Guid));
            }
            finally
            {
                NativeMethods.LocalFree(pointer);
            }
        }

        internal static void SetActiveScheme(Guid scheme)
        {
            uint error = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref scheme);
            if (error != NativeMethods.ERROR_SUCCESS)
            {
                throw new Win32Exception((int)error, "PowerSetActiveScheme failed.");
            }
        }

        /// <summary>
        /// Duplicates a scheme into a caller-chosen destination GUID. The GUID
        /// is recorded in the durable journal before this call, so recovery can
        /// reconcile against an exact identity instead of hunting for a scheme
        /// by name. The pointer round-trip is verified because the API is
        /// documented to allocate its own GUID when handed a null pointer.
        /// </summary>
        internal static void DuplicateScheme(Guid source, Guid destination)
        {
            IntPtr supplied = IntPtr.Zero;
            try
            {
                supplied = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Guid)));
                Marshal.StructureToPtr(destination, supplied, false);
                IntPtr returned = supplied;
                uint error = NativeMethods.PowerDuplicateScheme(
                    IntPtr.Zero,
                    ref source,
                    ref returned);
                // The API may replace the pointer with one it allocated itself,
                // including on failure, so release that before reporting either
                // outcome.
                if (returned != supplied && returned != IntPtr.Zero)
                {
                    NativeMethods.LocalFree(returned);
                }

                if (error != NativeMethods.ERROR_SUCCESS)
                {
                    throw new Win32Exception(
                        (int)error,
                        "PowerDuplicateScheme failed.");
                }

                if (returned != supplied)
                {
                    throw new InvalidOperationException(
                        "PowerDuplicateScheme did not preserve the pre-recorded destination GUID pointer.");
                }

                Guid readBack = (Guid)Marshal.PtrToStructure(supplied, typeof(Guid));
                if (readBack != destination)
                {
                    throw new InvalidOperationException(
                        "PowerDuplicateScheme changed the pre-recorded destination GUID.");
                }
            }
            finally
            {
                if (supplied != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(supplied);
                }
            }
        }

        internal static void WriteFriendlyName(Guid scheme, string name)
        {
            byte[] value = Encoding.Unicode.GetBytes(name + "\0");
            uint error = NativeMethods.PowerWriteFriendlyName(
                IntPtr.Zero,
                ref scheme,
                IntPtr.Zero,
                IntPtr.Zero,
                value,
                (uint)value.Length);
            if (error != NativeMethods.ERROR_SUCCESS)
            {
                throw new Win32Exception((int)error, "PowerWriteFriendlyName failed.");
            }
        }

        /// <summary>
        /// Absence is reported only for an explicit native not-found result.
        /// Any other failure throws, so a transient error can never be mistaken
        /// for "this scheme does not exist" and used to justify recreating it.
        /// </summary>
        internal static bool TryReadFriendlyName(Guid scheme, out string name)
        {
            name = null;
            uint size = 0;
            uint error = NativeMethods.PowerReadFriendlyName(
                IntPtr.Zero,
                ref scheme,
                IntPtr.Zero,
                IntPtr.Zero,
                null,
                ref size);
            if (IsDocumentedNotFound(error))
            {
                return false;
            }

            if (error != NativeMethods.ERROR_SUCCESS &&
                error != NativeMethods.ERROR_MORE_DATA)
            {
                throw new Win32Exception((int)error, "PowerReadFriendlyName failed.");
            }

            if (size == 0 || size > MaximumFriendlyNameBytes)
            {
                throw new InvalidDataException(
                    "PowerReadFriendlyName returned an invalid length.");
            }

            byte[] value = new byte[size];
            error = NativeMethods.PowerReadFriendlyName(
                IntPtr.Zero,
                ref scheme,
                IntPtr.Zero,
                IntPtr.Zero,
                value,
                ref size);
            if (IsDocumentedNotFound(error))
            {
                return false;
            }

            if (error != NativeMethods.ERROR_SUCCESS)
            {
                throw new Win32Exception((int)error, "PowerReadFriendlyName failed.");
            }

            name = Encoding.Unicode.GetString(value, 0, (int)size).TrimEnd('\0');
            return true;
        }

        internal static bool SchemeExists(Guid scheme)
        {
            string ignored;
            return TryReadFriendlyName(scheme, out ignored);
        }

        internal static bool IsDocumentedNotFound(uint error)
        {
            return error == NativeMethods.ERROR_FILE_NOT_FOUND ||
                error == NativeMethods.ERROR_NOT_FOUND;
        }
    }
}
