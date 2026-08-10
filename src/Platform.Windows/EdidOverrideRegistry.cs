using System;
using System.ComponentModel;
using System.IO;
using MacBookEco.Core;
using Microsoft.Win32.SafeHandles;

namespace MacBookEco.Platform.Windows
{
    /// <summary>
    /// The only code that writes or deletes an EDID override value.
    ///
    /// Every operation compares the live value against the exact expected bytes
    /// immediately before it acts, through the same handle it is about to use.
    /// There is deliberately no blind write and no blind delete: a value that
    /// is not byte-for-byte the app-owned override belongs to something else
    /// and is left alone.
    /// </summary>
    internal static class EdidOverrideRegistry
    {
        private const string OverrideSubKey = "EDID_OVERRIDE";
        private const string OverrideValueName = "0";

        internal static byte[] Read(SafeRegistryHandle deviceKey)
        {
            return MonitorDevnodeReader.ReadBinaryValue(
                deviceKey,
                OverrideSubKey,
                OverrideValueName);
        }

        internal static void Write(SafeRegistryHandle deviceKey, byte[] value)
        {
            if (value == null || value.Length != EdidBaseBlock.Length)
            {
                throw new ArgumentException(
                    "An EDID override base block must contain exactly 128 bytes.",
                    nameof(value));
            }

            SafeRegistryHandle overrideKey;
            uint disposition;
            int error = NativeMethods.RegCreateKeyEx(
                deviceKey,
                OverrideSubKey,
                0,
                null,
                NativeMethods.REG_OPTION_NON_VOLATILE,
                DisplayTopologyNativeMethods.KEY_QUERY_VALUE |
                NativeMethods.KEY_SET_VALUE |
                NativeMethods.KEY_CREATE_SUB_KEY,
                IntPtr.Zero,
                out overrideKey,
                out disposition);
            if (error != DisplayTopologyNativeMethods.ERROR_SUCCESS)
            {
                overrideKey.Dispose();
                throw new Win32Exception(error);
            }

            using (overrideKey)
            {
                // The resolver already compared this value through a separately
                // proven devnode handle. Re-read it on the exact subkey handle
                // as well: an app-owned lock serializes MacBook Eco writers, and
                // this second comparison is the closest the registry allows us
                // to get to detecting an intervening OS or driver write.
                byte[] current = MonitorDevnodeReader.ReadBinaryValue(
                    overrideKey,
                    null,
                    OverrideValueName);
                if (current != null)
                {
                    throw new SecureStateConflictException(
                        "An EDID override appeared immediately before the app-owned value could be written.");
                }

                error = NativeMethods.RegSetValueEx(
                    overrideKey,
                    OverrideValueName,
                    0,
                    DisplayTopologyNativeMethods.REG_BINARY,
                    value,
                    value.Length);
                if (error != DisplayTopologyNativeMethods.ERROR_SUCCESS)
                {
                    throw new Win32Exception(error);
                }

                error = NativeMethods.RegFlushKey(overrideKey);
                if (error != DisplayTopologyNativeMethods.ERROR_SUCCESS)
                {
                    throw new Win32Exception(error);
                }
            }
        }

        /// <summary>
        /// Deletes only the exact app-owned bytes and proves their absence
        /// after the write-through flush.
        /// </summary>
        internal static void DeleteExact(
            SafeRegistryHandle deviceKey,
            byte[] expectedValue)
        {
            if (expectedValue == null ||
                expectedValue.Length != EdidBaseBlock.Length)
            {
                throw new ArgumentException(
                    "An expected EDID override base block is required.",
                    nameof(expectedValue));
            }

            RequireExactOwnedValue(Read(deviceKey), expectedValue);

            SafeRegistryHandle overrideKey;
            int error = DisplayTopologyNativeMethods.RegOpenKeyEx(
                deviceKey,
                OverrideSubKey,
                0,
                DisplayTopologyNativeMethods.KEY_QUERY_VALUE |
                    NativeMethods.KEY_SET_VALUE,
                out overrideKey);
            if (error != DisplayTopologyNativeMethods.ERROR_SUCCESS)
            {
                overrideKey.Dispose();
                if (error == DisplayTopologyNativeMethods.ERROR_FILE_NOT_FOUND)
                {
                    throw new SecureStateConflictException(
                        "The app-owned EDID override disappeared before removal.");
                }

                throw new Win32Exception(error);
            }

            using (overrideKey)
            {
                // The subkey was reopened after the first comparison. Re-read
                // through this exact handle so a foreign replacement is not
                // deleted merely because it appeared in that window.
                RequireExactOwnedValue(
                    MonitorDevnodeReader.ReadBinaryValue(
                        overrideKey,
                        null,
                        OverrideValueName),
                    expectedValue);

                error = NativeMethods.RegDeleteValue(overrideKey, OverrideValueName);
                if (error == DisplayTopologyNativeMethods.ERROR_FILE_NOT_FOUND)
                {
                    throw new SecureStateConflictException(
                        "The app-owned EDID override disappeared before removal.");
                }

                if (error != DisplayTopologyNativeMethods.ERROR_SUCCESS)
                {
                    throw new Win32Exception(error);
                }

                error = NativeMethods.RegFlushKey(overrideKey);
                if (error != DisplayTopologyNativeMethods.ERROR_SUCCESS)
                {
                    throw new Win32Exception(error);
                }
            }

            if (Read(deviceKey) != null)
            {
                throw new IOException(
                    "The exact EDID override still exists after removal.");
            }
        }

        private static void RequireExactOwnedValue(
            byte[] current,
            byte[] expectedValue)
        {
            if (current == null)
            {
                throw new SecureStateConflictException(
                    "The app-owned EDID override disappeared before removal.");
            }

            if (!FixedTimeComparer.AreEqual(current, expectedValue))
            {
                throw new SecureStateConflictException(
                    "The current EDID override is not the exact app-owned value.");
            }
        }
    }
}
