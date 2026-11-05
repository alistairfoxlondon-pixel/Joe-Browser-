using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace JoeBrowser
{
    /// <summary>
    /// Answers the one machine-level question that changes how a profile has to be launched:
    /// is there a GPU Chrome can render WebGL with, or will it hand back a null context?
    /// </summary>
    internal static class SystemProbe
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private const int Attached = 0x00000001;
        private const int MirroringDriver = 0x00000008;
        private const int SmRemoteSession = 0x1000;     // SM_REMOTESESSION

        private static readonly List<string> SoftwareOnly = new List<string>
        {
            "basic display adapter", "basic render driver", "remote display adapter",
            "rdp miniport", "microsoft windows remote desktop", "indirect display driver"
        };

        private static int _hardwareGl = -1;

        /// <summary>
        /// True when at least one attached adapter looks like real graphics hardware. Servers,
        /// cloud desktops and RDP sessions only expose the Microsoft basic adapter, and Chrome
        /// refuses to create a WebGL context there unless it is pointed at SwiftShader.
        /// </summary>
        public static bool HasHardwareGl()
        {
            int cached = _hardwareGl;
            if (cached >= 0) return cached == 1;

            bool hardware = false;
            try
            {
                if (!IsHeadlessSession())
                {
                    uint adapters = 0;
                    while (adapters < 16)
                    {
                        var dd = new DISPLAY_DEVICE();
                        dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                        if (!EnumDisplayDevices(null, adapters, ref dd, 0)) break;
                        adapters++;
                        if ((dd.StateFlags & Attached) == 0) continue;
                        if ((dd.StateFlags & MirroringDriver) != 0) continue;
                        string name = (dd.DeviceString ?? "").ToLowerInvariant();
                        bool software = false;
                        foreach (string s in SoftwareOnly)
                            if (name.IndexOf(s, StringComparison.Ordinal) >= 0) { software = true; break; }
                        if (!software && name.Length > 0) { hardware = true; break; }
                    }
                }
            }
            catch (Exception ex) { Log.Error("Display adapter probe failed", ex); }

            _hardwareGl = hardware ? 1 : 0;
            return _hardwareGl == 1;
        }

        /// <summary>
        /// Terminal Server sessions can report the client's GPU through the adapter list, which is
        /// not a device this session is able to render with.
        /// </summary>
        private static bool IsHeadlessSession()
        {
            try { return GetSystemMetrics(SmRemoteSession) != 0; }
            catch { return false; }
        }
    }
}
