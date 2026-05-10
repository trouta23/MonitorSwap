using System.Runtime.InteropServices;

namespace MonitorSwap;

public static class DisplayManager
{
    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplayDevices(
        string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplaySettingsEx(
        string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern int ChangeDisplaySettingsEx(
        string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwFlags, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern int ChangeDisplaySettingsEx(
        string? lpszDeviceName, IntPtr lpDevMode, IntPtr hwnd, uint dwFlags, IntPtr lParam);

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const uint CDS_UPDATEREGISTRY = 0x01;
    private const uint CDS_NORESET = 0x10000000;
    private const uint CDS_SET_PRIMARY = 0x10;
    private const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
    private const uint DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4;
    private const int DM_POSITION = 0x20;
    private const int DM_PELSWIDTH = 0x80000;
    private const int DM_PELSHEIGHT = 0x100000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Ansi)]
    public struct DEVMODE
    {
        [FieldOffset(0), MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        [FieldOffset(32)] public short dmSpecVersion;
        [FieldOffset(34)] public short dmDriverVersion;
        [FieldOffset(36)] public short dmSize;
        [FieldOffset(38)] public short dmDriverExtra;
        [FieldOffset(40)] public int dmFields;
        [FieldOffset(44)] public int dmPositionX;
        [FieldOffset(48)] public int dmPositionY;
        [FieldOffset(52)] public int dmDisplayOrientation;
        [FieldOffset(56)] public int dmDisplayFixedOutput;
        [FieldOffset(60)] public short dmColor;
        [FieldOffset(62)] public short dmDuplex;
        [FieldOffset(64)] public short dmYResolution;
        [FieldOffset(66)] public short dmTTOption;
        [FieldOffset(68)] public short dmCollate;
        [FieldOffset(70), MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        [FieldOffset(102)] public short dmLogPixels;
        [FieldOffset(104)] public int dmBitsPerPel;
        [FieldOffset(108)] public int dmPelsWidth;
        [FieldOffset(112)] public int dmPelsHeight;
        [FieldOffset(116)] public int dmDisplayFlags;
        [FieldOffset(120)] public int dmDisplayFrequency;
    }

    public record MonitorInfo(
        string DeviceName, string DisplayName, bool IsPrimary,
        int X, int Y, int Width, int Height);

    public static List<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };

        for (uint i = 0; EnumDisplayDevices(null, i, ref device, 0); i++)
        {
            if ((device.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0)
            {
                device.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
                continue;
            }

            var displayName = device.DeviceString;
            var child = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (EnumDisplayDevices(device.DeviceName, 0, ref child, 0)
                && !string.IsNullOrEmpty(child.DeviceString))
            {
                displayName = child.DeviceString;
            }

            var devMode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };

            if (EnumDisplaySettingsEx(device.DeviceName, ENUM_CURRENT_SETTINGS, ref devMode, 0))
            {
                monitors.Add(new MonitorInfo(
                    device.DeviceName,
                    displayName,
                    (device.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0,
                    devMode.dmPositionX, devMode.dmPositionY,
                    devMode.dmPelsWidth, devMode.dmPelsHeight));
            }

            device.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
        }

        return monitors;
    }

    private const int SwappableCount = 2;

    public static (bool Success, string Message) CyclePrimary()
    {
        var monitors = GetMonitors();
        if (monitors.Count < 2)
            return (false, "Only one monitor detected");

        var target = monitors.Take(SwappableCount).FirstOrDefault(m => !m.IsPrimary)
            ?? monitors[0];

        return SetPrimary(monitors, target);
    }

    public static (bool Success, string Message) SetPrimary(string deviceName)
    {
        var monitors = GetMonitors();
        var target = monitors.FirstOrDefault(m => m.DeviceName == deviceName);
        if (target == null)
            return (false, $"Monitor {deviceName} not found");
        if (target.IsPrimary)
            return (true, target.DisplayName);

        return SetPrimary(monitors, target);
    }

    private static (bool Success, string Message) SetPrimary(
        List<MonitorInfo> monitors, MonitorInfo target)
    {
        int offsetX = target.X;
        int offsetY = target.Y;

        foreach (var monitor in monitors)
        {
            var devMode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            EnumDisplaySettingsEx(monitor.DeviceName, ENUM_CURRENT_SETTINGS, ref devMode, 0);

            devMode.dmPositionX -= offsetX;
            devMode.dmPositionY -= offsetY;
            devMode.dmFields = DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT;

            uint flags = CDS_UPDATEREGISTRY | CDS_NORESET;
            if (monitor.DeviceName == target.DeviceName)
                flags |= CDS_SET_PRIMARY;

            int result = ChangeDisplaySettingsEx(
                monitor.DeviceName, ref devMode, IntPtr.Zero, flags, IntPtr.Zero);
            if (result != 0)
                return (false, $"Failed to update {monitor.DeviceName} (error {result})");
        }

        ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);

        return (true, target.DisplayName);
    }

    public static int GetPrimaryIndex()
    {
        var monitors = GetMonitors();
        int idx = monitors.FindIndex(m => m.IsPrimary);
        return idx >= 0 ? idx + 1 : 1;
    }
}
