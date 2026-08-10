using System;
using System.Runtime.InteropServices;

namespace MacBookEco.Platform.Windows
{
    // This is deliberately separate from the broader hardware, registry and
    // power interop surface. The watchdog compiles only this display-mode API.
    internal static class DisplayModeNativeMethods
    {
        internal const int ENUM_CURRENT_SETTINGS = -1;
        internal const uint CDS_UPDATEREGISTRY = 0x00000001;
        internal const uint CDS_TEST = 0x00000002;
        internal const int DISP_CHANGE_SUCCESSFUL = 0;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumDisplaySettingsEx(
            string deviceName,
            int modeNumber,
            ref DEVMODE deviceMode,
            uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int ChangeDisplaySettingsEx(
            string deviceName,
            ref DEVMODE deviceMode,
            IntPtr windowHandle,
            uint flags,
            IntPtr parameter);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string DeviceName;

        internal short SpecVersion;
        internal short DriverVersion;
        internal short Size;
        internal short DriverExtra;
        internal int Fields;
        internal int PositionX;
        internal int PositionY;
        internal int DisplayOrientation;
        internal int DisplayFixedOutput;
        internal short Color;
        internal short Duplex;
        internal short YResolution;
        internal short TTOption;
        internal short Collate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string FormName;

        internal short LogPixels;
        internal int BitsPerPel;
        internal int PelsWidth;
        internal int PelsHeight;
        internal int DisplayFlags;
        internal int DisplayFrequency;
        internal int ICMMethod;
        internal int ICMIntent;
        internal int MediaType;
        internal int DitherType;
        internal int Reserved1;
        internal int Reserved2;
        internal int PanningWidth;
        internal int PanningHeight;

        internal static DEVMODE Create()
        {
            DEVMODE mode = new DEVMODE();
            mode.Size = (short)Marshal.SizeOf(typeof(DEVMODE));
            return mode;
        }
    }
}
