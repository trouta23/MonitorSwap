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
    private const int OFF_PELS_WIDTH = 108;
    private const int OFF_PELS_HEIGHT = 112;
    private const int OFF_BITS_PER_PEL = 104;
    private const int OFF_DISPLAY_FREQ = 120;

    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "MonitorSwap.log");

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
        var log = new List<string> { $"=== SetPrimary({displayNumber}) at {DateTime.Now:O} ===" };

        var monitors = GetMonitors();
        foreach (var m in monitors)
            log.Add($"  Detected: #{m.Number} {m.DeviceName} primary={m.IsPrimary} pos=({m.X},{m.Y})");

        var target = monitors.FirstOrDefault(m => m.Number == displayNumber);
        if (target == null)
        {
            WriteLog(log, $"Monitor {displayNumber} not found");
            return (false, $"Monitor {displayNumber} not found");
        }
        if (target.IsPrimary)
            return (true, $"Monitor {displayNumber}");

        var current = monitors.FirstOrDefault(m => m.IsPrimary);
        if (current != null)
            _previousPrimary = current.Number;

        int offsetX = target.X;
        int offsetY = target.Y;
        log.Add($"  Target: #{target.Number} {target.DeviceName}, offset=({offsetX},{offsetY})");

        var ordered = monitors.OrderByDescending(m => m.DeviceName == target.DeviceName);
        foreach (var monitor in ordered)
        {
            IntPtr pDevMode = Marshal.AllocHGlobal(DEVMODE_BUFFER);
            try
            {
                ZeroMemory(pDevMode, DEVMODE_BUFFER);
                Marshal.WriteInt16(pDevMode, OFF_DMSIZE, (short)DEVMODE_BUFFER);

                if (!EnumDisplaySettingsEx(monitor.DeviceName, ENUM_CURRENT_SETTINGS, pDevMode, 0))
                {
                    WriteLog(log, $"EnumDisplaySettingsEx failed for {monitor.DeviceName}");
                    return (false, $"Failed to read settings for {monitor.DeviceName}");
                }

                short dmSize = Marshal.ReadInt16(pDevMode, OFF_DMSIZE);
                int dmFields = Marshal.ReadInt32(pDevMode, OFF_DMFIELDS);
                int origX = Marshal.ReadInt32(pDevMode, OFF_POSITION_X);
                int origY = Marshal.ReadInt32(pDevMode, OFF_POSITION_Y);
                int width = Marshal.ReadInt32(pDevMode, OFF_PELS_WIDTH);
                int height = Marshal.ReadInt32(pDevMode, OFF_PELS_HEIGHT);
                int bpp = Marshal.ReadInt32(pDevMode, OFF_BITS_PER_PEL);
                int freq = Marshal.ReadInt32(pDevMode, OFF_DISPLAY_FREQ);

                log.Add($"  {monitor.DeviceName} BEFORE: dmSize={dmSize} dmFields=0x{dmFields:X} pos=({origX},{origY}) res={width}x{height} bpp={bpp} freq={freq}");

                int newX = origX - offsetX;
                int newY = origY - offsetY;
                Marshal.WriteInt32(pDevMode, OFF_POSITION_X, newX);
                Marshal.WriteInt32(pDevMode, OFF_POSITION_Y, newY);
                Marshal.WriteInt32(pDevMode, OFF_DMFIELDS, dmFields | DM_POSITION);

                uint flags = CDS_UPDATEREGISTRY | CDS_NORESET;
                if (monitor.DeviceName == target.DeviceName)
                    flags |= CDS_SET_PRIMARY;

                log.Add($"  {monitor.DeviceName} AFTER:  pos=({newX},{newY}) flags=0x{flags:X} isPrimary={monitor.DeviceName == target.DeviceName}");

                int result = ChangeDisplaySettingsEx(
                    monitor.DeviceName, pDevMode, IntPtr.Zero, flags, IntPtr.Zero);

                log.Add($"  {monitor.DeviceName} RESULT: {result}");

                if (result != 0)
                {
                    WriteLog(log, $"ChangeDisplaySettingsEx returned {result}");
                    return (false, $"Failed to update {monitor.DeviceName} (error {result})\nSee MonitorSwap.log for details");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pDevMode);
            }
        }

        ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);

        log.Add("  Apply call sent. Swap complete.");
        WriteLog(log, null);
        return (true, $"Monitor {displayNumber}");
    }

    public static int GetPrimaryIndex()
    {
        var monitors = GetMonitors();
        var primary = monitors.FirstOrDefault(m => m.IsPrimary);
        return primary?.Number ?? 1;
    }

    private static void WriteLog(List<string> entries, string? error)
    {
        if (error != null)
            entries.Add($"  ERROR: {error}");
        entries.Add("");
        try { File.AppendAllLines(LogPath, entries); } catch { }
    }

    private static void ZeroMemory(IntPtr ptr, int size)
    {
        for (int i = 0; i < size; i++)
            Marshal.WriteByte(ptr, i, 0);
    }
}
