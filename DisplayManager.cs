using System.Runtime.InteropServices;

namespace MonitorSwap;

public static class DisplayManager
{
    private static int _previousPrimary = -1;

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplayDevices(
        string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplaySettingsEx(
        string lpszDeviceName, int iModeNum, IntPtr lpDevMode, uint dwFlags);

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

    private const int DEVMODE_BUFFER = 256;
    private const int OFF_DMSIZE = 36;
    private const int OFF_DMFIELDS = 40;
    private const int OFF_POSITION_X = 44;
    private const int OFF_POSITION_Y = 48;

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

    public record MonitorInfo(int Number, string DeviceName, bool IsPrimary, int X, int Y);

    public static List<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        int number = 1;

        for (uint i = 0; EnumDisplayDevices(null, i, ref device, 0); i++)
        {
            if ((device.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0)
            {
                device.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
                continue;
            }

            IntPtr pDevMode = Marshal.AllocHGlobal(DEVMODE_BUFFER);
            try
            {
                ZeroMemory(pDevMode, DEVMODE_BUFFER);
                Marshal.WriteInt16(pDevMode, OFF_DMSIZE, (short)DEVMODE_BUFFER);

                if (EnumDisplaySettingsEx(device.DeviceName, ENUM_CURRENT_SETTINGS, pDevMode, 0))
                {
                    int posX = Marshal.ReadInt32(pDevMode, OFF_POSITION_X);
                    int posY = Marshal.ReadInt32(pDevMode, OFF_POSITION_Y);

                    monitors.Add(new MonitorInfo(
                        number++,
                        device.DeviceName,
                        (device.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0,
                        posX, posY));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pDevMode);
            }

            device.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
        }

        return monitors;
    }

    public static (bool Success, string Message) TogglePrimary()
    {
        var monitors = GetMonitors();
        if (monitors.Count < 2)
            return (false, "Only one monitor detected");

        MonitorInfo? target;
        if (_previousPrimary > 0)
            target = monitors.FirstOrDefault(m => m.Number == _previousPrimary && !m.IsPrimary);
        else
            target = null;

        target ??= monitors.First(m => !m.IsPrimary);

        return SetPrimary(target.Number);
    }

    public static (bool Success, string Message) SetPrimary(int displayNumber)
    {
        var monitors = GetMonitors();
        var target = monitors.FirstOrDefault(m => m.Number == displayNumber);
        if (target == null)
            return (false, $"Monitor {displayNumber} not found");
        if (target.IsPrimary)
            return (true, $"Monitor {displayNumber}");

        var current = monitors.FirstOrDefault(m => m.IsPrimary);
        if (current != null)
            _previousPrimary = current.Number;

        int offsetX = target.X;
        int offsetY = target.Y;

        foreach (var monitor in monitors)
        {
            IntPtr pDevMode = Marshal.AllocHGlobal(DEVMODE_BUFFER);
            try
            {
                ZeroMemory(pDevMode, DEVMODE_BUFFER);
                Marshal.WriteInt16(pDevMode, OFF_DMSIZE, (short)DEVMODE_BUFFER);

                if (!EnumDisplaySettingsEx(monitor.DeviceName, ENUM_CURRENT_SETTINGS, pDevMode, 0))
                    return (false, $"Failed to read settings for {monitor.DeviceName}");

                int posX = Marshal.ReadInt32(pDevMode, OFF_POSITION_X) - offsetX;
                int posY = Marshal.ReadInt32(pDevMode, OFF_POSITION_Y) - offsetY;
                Marshal.WriteInt32(pDevMode, OFF_POSITION_X, posX);
                Marshal.WriteInt32(pDevMode, OFF_POSITION_Y, posY);

                int fields = Marshal.ReadInt32(pDevMode, OFF_DMFIELDS);
                Marshal.WriteInt32(pDevMode, OFF_DMFIELDS, fields | DM_POSITION);

                uint flags = CDS_UPDATEREGISTRY | CDS_NORESET;
                if (monitor.DeviceName == target.DeviceName)
                    flags |= CDS_SET_PRIMARY;

                int result = ChangeDisplaySettingsEx(
                    monitor.DeviceName, pDevMode, IntPtr.Zero, flags, IntPtr.Zero);
                if (result != 0)
                    return (false, $"Failed to update {monitor.DeviceName} (error {result})");
            }
            finally
            {
                Marshal.FreeHGlobal(pDevMode);
            }
        }

        ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);

        return (true, $"Monitor {displayNumber}");
    }

    public static int GetPrimaryIndex()
    {
        var monitors = GetMonitors();
        var primary = monitors.FirstOrDefault(m => m.IsPrimary);
        return primary?.Number ?? 1;
    }

    private static void ZeroMemory(IntPtr ptr, int size)
    {
        for (int i = 0; i < size; i++)
            Marshal.WriteByte(ptr, i, 0);
    }
}
