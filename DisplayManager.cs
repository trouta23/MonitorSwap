using System.Runtime.InteropServices;

namespace MonitorSwap;

public static class DisplayManager
{
    private static int _previousPrimary = -1;

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(
        uint flags, out uint numPaths, out uint numModes);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags, ref uint numPaths, IntPtr pathArray,
        ref uint numModes, IntPtr modeArray, IntPtr topologyId);

    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(
        uint numPaths, IntPtr pathArray,
        uint numModes, IntPtr modeArray, uint flags);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(IntPtr requestPacket);

    private const uint QDC_ONLY_ACTIVE_PATHS = 0x2;
    private const uint SDC_APPLY = 0x80;
    private const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x20;
    private const uint SDC_SAVE_TO_DATABASE = 0x200;
    private const uint SDC_ALLOW_CHANGES = 0x400;
    private const uint MODE_TYPE_SOURCE = 1;
    private const uint DEVICE_INFO_GET_SOURCE_NAME = 1;

    private const int PATH_SIZE = 72;
    private const int MODE_SIZE = 64;
    private const int SRC_NAME_SIZE = 84;
    private const uint MODE_IDX_INVALID = 0xFFFFFFFF;

    private const int P_SRC_ADAPTER_LO = 0;
    private const int P_SRC_ADAPTER_HI = 4;
    private const int P_SRC_ID = 8;
    private const int P_SRC_MODE_IDX = 12;
    private const int P_FLAGS = 68;

    private const int M_TYPE = 0;
    private const int M_SRC_POS_X = 28;
    private const int M_SRC_POS_Y = 32;

    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "MonitorSwap.log");

    public record MonitorInfo(int Number, string DeviceName, bool IsPrimary, int X, int Y);

    public static List<MonitorInfo> GetMonitors()
    {
        if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint numPaths, out uint numModes) != 0)
            return new();

        IntPtr paths = Marshal.AllocHGlobal((int)(numPaths * PATH_SIZE));
        IntPtr modes = Marshal.AllocHGlobal((int)(numModes * MODE_SIZE));
        try
        {
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref numPaths, paths, ref numModes, modes, IntPtr.Zero) != 0)
                return new();

            var monitors = new List<MonitorInfo>();
            int number = 1;

            for (uint i = 0; i < numPaths; i++)
            {
                IntPtr p = paths + (int)i * PATH_SIZE;
                if (((uint)Marshal.ReadInt32(p, P_FLAGS) & 1) == 0) continue;

                string gdi = GetSourceName(p);
                if (string.IsNullOrEmpty(gdi)) continue;

                uint modeIdx = (uint)Marshal.ReadInt32(p, P_SRC_MODE_IDX);
                int x = 0, y = 0;
                if (modeIdx != MODE_IDX_INVALID && modeIdx < numModes)
                {
                    IntPtr m = modes + (int)modeIdx * MODE_SIZE;
                    if ((uint)Marshal.ReadInt32(m, M_TYPE) == MODE_TYPE_SOURCE)
                    {
                        x = Marshal.ReadInt32(m, M_SRC_POS_X);
                        y = Marshal.ReadInt32(m, M_SRC_POS_Y);
                    }
                }

                monitors.Add(new MonitorInfo(number++, gdi, x == 0 && y == 0, x, y));
            }

            return monitors;
        }
        finally
        {
            Marshal.FreeHGlobal(paths);
            Marshal.FreeHGlobal(modes);
        }
    }

    public static (bool Success, string Message) TogglePrimary()
    {
        var monitors = GetMonitors();
        if (monitors.Count < 2)
            return (false, "Only one monitor detected");

        MonitorInfo? target = _previousPrimary > 0
            ? monitors.FirstOrDefault(m => m.Number == _previousPrimary && !m.IsPrimary)
            : null;
        target ??= monitors.First(m => !m.IsPrimary);

        return SetPrimary(target.Number);
    }

    public static (bool Success, string Message) SetPrimary(int displayNumber)
    {
        var log = new List<string> { $"=== SetPrimary({displayNumber}) at {DateTime.Now:O} ===" };

        int err = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint numPaths, out uint numModes);
        if (err != 0)
        {
            WriteLog(log, $"GetDisplayConfigBufferSizes failed: {err}");
            return (false, $"Failed to query display config (error {err})");
        }

        IntPtr paths = Marshal.AllocHGlobal((int)(numPaths * PATH_SIZE));
        IntPtr modes = Marshal.AllocHGlobal((int)(numModes * MODE_SIZE));
        try
        {
            err = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref numPaths, paths, ref numModes, modes, IntPtr.Zero);
            if (err != 0)
            {
                WriteLog(log, $"QueryDisplayConfig failed: {err}");
                return (false, $"Failed to query display config (error {err})");
            }

            int targetModeIdx = -1;
            int number = 1;
            int offsetX = 0, offsetY = 0;

            for (uint i = 0; i < numPaths; i++)
            {
                IntPtr p = paths + (int)i * PATH_SIZE;
                if (((uint)Marshal.ReadInt32(p, P_FLAGS) & 1) == 0) continue;

                string gdi = GetSourceName(p);
                if (string.IsNullOrEmpty(gdi)) continue;

                uint modeIdx = (uint)Marshal.ReadInt32(p, P_SRC_MODE_IDX);
                int x = 0, y = 0;
                if (modeIdx != MODE_IDX_INVALID && modeIdx < numModes)
                {
                    IntPtr m = modes + (int)modeIdx * MODE_SIZE;
                    if ((uint)Marshal.ReadInt32(m, M_TYPE) == MODE_TYPE_SOURCE)
                    {
                        x = Marshal.ReadInt32(m, M_SRC_POS_X);
                        y = Marshal.ReadInt32(m, M_SRC_POS_Y);
                    }
                }

                bool isPrimary = x == 0 && y == 0;
                log.Add($"  Path {i}: {gdi} #{number} pos=({x},{y}) primary={isPrimary} modeIdx={modeIdx}");

                if (isPrimary)
                    _previousPrimary = number;

                if (number == displayNumber)
                {
                    targetModeIdx = (int)modeIdx;
                    offsetX = x;
                    offsetY = y;
                }

                number++;
            }

            if (targetModeIdx < 0)
            {
                WriteLog(log, $"Monitor {displayNumber} not found");
                return (false, $"Monitor {displayNumber} not found");
            }

            if (offsetX == 0 && offsetY == 0)
            {
                log.Add("  Already primary.");
                WriteLog(log, null);
                return (true, $"Monitor {displayNumber}");
            }

            log.Add($"  Shifting all source modes by ({-offsetX},{-offsetY})");

            for (uint i = 0; i < numModes; i++)
            {
                IntPtr m = modes + (int)i * MODE_SIZE;
                if ((uint)Marshal.ReadInt32(m, M_TYPE) != MODE_TYPE_SOURCE) continue;

                int oldX = Marshal.ReadInt32(m, M_SRC_POS_X);
                int oldY = Marshal.ReadInt32(m, M_SRC_POS_Y);
                int newX = oldX - offsetX;
                int newY = oldY - offsetY;

                Marshal.WriteInt32(m, M_SRC_POS_X, newX);
                Marshal.WriteInt32(m, M_SRC_POS_Y, newY);

                log.Add($"  Mode {i}: ({oldX},{oldY}) -> ({newX},{newY})");
            }

            uint sdcFlags = SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_APPLY
                | SDC_SAVE_TO_DATABASE | SDC_ALLOW_CHANGES;

            err = SetDisplayConfig(numPaths, paths, numModes, modes, sdcFlags);
            log.Add($"  SetDisplayConfig result: {err}");

            if (err != 0)
            {
                WriteLog(log, $"SetDisplayConfig failed: {err}");
                return (false, $"Failed to apply display config (error {err})\nSee MonitorSwap.log");
            }

            log.Add("  Swap complete.");
            WriteLog(log, null);
            return (true, $"Monitor {displayNumber}");
        }
        finally
        {
            Marshal.FreeHGlobal(paths);
            Marshal.FreeHGlobal(modes);
        }
    }

    public static int GetPrimaryIndex()
    {
        var monitors = GetMonitors();
        var primary = monitors.FirstOrDefault(m => m.IsPrimary);
        return primary?.Number ?? 1;
    }

    private static string GetSourceName(IntPtr pathPtr)
    {
        IntPtr packet = Marshal.AllocHGlobal(SRC_NAME_SIZE);
        try
        {
            ZeroMemory(packet, SRC_NAME_SIZE);
            Marshal.WriteInt32(packet, 0, (int)DEVICE_INFO_GET_SOURCE_NAME);
            Marshal.WriteInt32(packet, 4, SRC_NAME_SIZE);
            Marshal.WriteInt32(packet, 8, Marshal.ReadInt32(pathPtr, P_SRC_ADAPTER_LO));
            Marshal.WriteInt32(packet, 12, Marshal.ReadInt32(pathPtr, P_SRC_ADAPTER_HI));
            Marshal.WriteInt32(packet, 16, Marshal.ReadInt32(pathPtr, P_SRC_ID));

            if (DisplayConfigGetDeviceInfo(packet) != 0) return "";

            return Marshal.PtrToStringUni(packet + 20, 32)?.TrimEnd('\0') ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(packet);
        }
    }

    private static void WriteLog(List<string> entries, string? error)
    {
        if (error != null) entries.Add($"  ERROR: {error}");
        entries.Add("");
        try { File.AppendAllLines(LogPath, entries); } catch { }
    }

    private static void ZeroMemory(IntPtr ptr, int size)
    {
        for (int i = 0; i < size; i++)
            Marshal.WriteByte(ptr, i, 0);
    }
}
