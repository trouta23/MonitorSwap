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

        // Phase 1: Set new primary without position changes.
        // Let Windows handle the coordinate shift.
        {
            IntPtr pDevMode = Marshal.AllocHGlobal(DEVMODE_BUFFER);
            try
            {
                ZeroMemory(pDevMode, DEVMODE_BUFFER);
                Marshal.WriteInt16(pDevMode, OFF_DMSIZE, (short)DEVMODE_BUFFER);

                if (!EnumDisplaySettingsEx(target.DeviceName, ENUM_CURRENT_SETTINGS, pDevMode, 0))
                {
                    WriteLog(log, $"EnumDisplaySettingsEx failed for {target.DeviceName}");
                    return (false, $"Failed to read settings for {target.DeviceName}");
                }

                log.Add($"  Phase 1: Setting {target.DeviceName} as primary (no position change)");

                int result = ChangeDisplaySettingsEx(
                    target.DeviceName, pDevMode, IntPtr.Zero,
                    CDS_SET_PRIMARY | CDS_UPDATEREGISTRY, IntPtr.Zero);

                log.Add($"  Phase 1 RESULT: {result}");

                if (result != 0)
                {
                    WriteLog(log, $"Phase 1 failed: {result}");
                    return (false, $"Failed to set {target.DeviceName} as primary (error {result})\nSee MonitorSwap.log for details");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pDevMode);
            }
        }

        // Phase 2: Read new positions after Windows adjusted coordinates,
        // then fix positions if needed.
        var newMonitors = GetMonitors();
        foreach (var m in newMonitors)
            log.Add($"  After phase 1: #{m.Number} {m.DeviceName} primary={m.IsPrimary} pos=({m.X},{m.Y})");

        var newTarget = newMonitors.FirstOrDefault(m => m.DeviceName == target.DeviceName);
        if (newTarget != null && newTarget.X == 0 && newTarget.Y == 0)
        {
            // Check if other monitors need position fixes
            bool positionsCorrect = true;
            foreach (var monitor in newMonitors)
            {
                var orig = monitors.FirstOrDefault(m => m.DeviceName == monitor.DeviceName);
                if (orig == null) continue;
                int expectedX = orig.X - offsetX;
                int expectedY = orig.Y - offsetY;
                if (monitor.X != expectedX || monitor.Y != expectedY)
                {
                    positionsCorrect = false;
                    log.Add($"  Position drift: {monitor.DeviceName} is at ({monitor.X},{monitor.Y}), expected ({expectedX},{expectedY})");
                }
            }

            if (!positionsCorrect)
            {
                log.Add("  Phase 2: Fixing positions...");
                foreach (var monitor in newMonitors)
                {
                    var orig = monitors.FirstOrDefault(m => m.DeviceName == monitor.DeviceName);
                    if (orig == null) continue;
                    int expectedX = orig.X - offsetX;
                    int expectedY = orig.Y - offsetY;
                    if (monitor.X == expectedX && monitor.Y == expectedY) continue;

                    IntPtr pDevMode = Marshal.AllocHGlobal(DEVMODE_BUFFER);
                    try
                    {
                        ZeroMemory(pDevMode, DEVMODE_BUFFER);
                        Marshal.WriteInt16(pDevMode, OFF_DMSIZE, (short)DEVMODE_BUFFER);
                        EnumDisplaySettingsEx(monitor.DeviceName, ENUM_CURRENT_SETTINGS, pDevMode, 0);

                        Marshal.WriteInt32(pDevMode, OFF_POSITION_X, expectedX);
                        Marshal.WriteInt32(pDevMode, OFF_POSITION_Y, expectedY);
                        int fields = Marshal.ReadInt32(pDevMode, OFF_DMFIELDS);
                        Marshal.WriteInt32(pDevMode, OFF_DMFIELDS, fields | DM_POSITION);

                        int result = ChangeDisplaySettingsEx(
                            monitor.DeviceName, pDevMode, IntPtr.Zero,
                            CDS_UPDATEREGISTRY | CDS_NORESET, IntPtr.Zero);

                        log.Add($"  Phase 2: {monitor.DeviceName} pos=({expectedX},{expectedY}) RESULT: {result}");
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pDevMode);
                    }
                }
                ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
            }
        }

        log.Add("  Swap complete.");
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
