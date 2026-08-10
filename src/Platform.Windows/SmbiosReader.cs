using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace MacBookEco.Platform.Windows
{
    internal sealed class SmbiosIdentity
    {
        internal string Manufacturer;
        internal string ProductName;
    }

    /// <summary>
    /// Reads the SMBIOS type 1 structure to identify the machine. This is the
    /// value the reviewed display profiles match against, so it is read from
    /// firmware first and only falls back to the registry copy that older or
    /// unusually virtualized Windows installations expose.
    /// </summary>
    internal static class SmbiosReader
    {
        // 'RSMB', the raw SMBIOS firmware table provider signature.
        private static readonly uint RsmbProvider =
            ((uint)'R') |
            ((uint)'S' << 8) |
            ((uint)'M' << 16) |
            ((uint)'B' << 24);

        private const int RawSmbiosHeaderBytes = 8;
        private const byte SystemInformationType = 1;
        private const byte EndOfTableType = 127;

        internal static SmbiosIdentity ReadIdentity()
        {
            try
            {
                SmbiosIdentity identity = ParseTypeOne(ReadRawTable());
                if (identity != null)
                {
                    return identity;
                }
            }
            catch
            {
                // Fall through to the read-only registry copy below.
            }

            SmbiosIdentity fallback = new SmbiosIdentity();
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\BIOS",
                    false))
                {
                    if (key != null)
                    {
                        fallback.Manufacturer = key.GetValue("SystemManufacturer") as string;
                        fallback.ProductName = key.GetValue("SystemProductName") as string;
                    }
                }
            }
            catch
            {
                // An unidentifiable machine is simply unsupported, which the
                // profile match reports far more usefully than an exception
                // thrown out of discovery.
            }

            return fallback;
        }

        private static byte[] ReadRawTable()
        {
            uint size = NativeMethods.GetSystemFirmwareTable(
                RsmbProvider,
                0,
                IntPtr.Zero,
                0);
            if (size < RawSmbiosHeaderBytes)
            {
                throw new InvalidOperationException(
                    "GetSystemFirmwareTable returned no SMBIOS data.");
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                uint written = NativeMethods.GetSystemFirmwareTable(
                    RsmbProvider,
                    0,
                    buffer,
                    size);
                if (written != size)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                byte[] result = new byte[size];
                Marshal.Copy(buffer, result, 0, result.Length);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static SmbiosIdentity ParseTypeOne(byte[] raw)
        {
            if (raw == null || raw.Length < 12)
            {
                return null;
            }

            int declaredLength = BitConverter.ToInt32(raw, 4);
            int end = Math.Min(raw.Length, RawSmbiosHeaderBytes + declaredLength);
            int offset = RawSmbiosHeaderBytes;

            while (offset + 4 <= end)
            {
                byte type = raw[offset];
                int structureLength = raw[offset + 1];
                if (structureLength < 4 || offset + structureLength > end)
                {
                    break;
                }

                int stringsStart = offset + structureLength;
                int next = FindStructureEnd(raw, stringsStart, end);
                if (next < 0)
                {
                    break;
                }

                if (type == SystemInformationType && structureLength >= 8)
                {
                    SmbiosIdentity identity = new SmbiosIdentity();
                    identity.Manufacturer = ReadSmbiosString(
                        raw,
                        stringsStart,
                        next,
                        raw[offset + 4]);
                    identity.ProductName = ReadSmbiosString(
                        raw,
                        stringsStart,
                        next,
                        raw[offset + 5]);
                    return identity;
                }

                if (type == EndOfTableType)
                {
                    break;
                }

                offset = next;
            }

            return null;
        }

        /// <summary>
        /// A structure's string table ends at a double NUL.
        /// </summary>
        private static int FindStructureEnd(byte[] bytes, int start, int limit)
        {
            int index = start;
            while (index + 1 < limit)
            {
                if (bytes[index] == 0 && bytes[index + 1] == 0)
                {
                    return index + 2;
                }

                index++;
            }

            return -1;
        }

        private static string ReadSmbiosString(
            byte[] bytes,
            int start,
            int structureEnd,
            byte stringIndex)
        {
            if (stringIndex == 0)
            {
                return null;
            }

            int current = 1;
            int valueStart = start;
            int index = start;
            int stringLimit = Math.Max(start, structureEnd - 2);

            while (index <= stringLimit)
            {
                if (index == stringLimit || bytes[index] == 0)
                {
                    if (current == stringIndex)
                    {
                        return Encoding.ASCII.GetString(
                            bytes,
                            valueStart,
                            index - valueStart).Trim();
                    }

                    current++;
                    valueStart = index + 1;
                }

                index++;
            }

            return null;
        }
    }
}
