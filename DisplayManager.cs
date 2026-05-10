using System.Diagnostics;

namespace MonitorSwap;

public static class DisplayManager
{
    private static int _previousPrimary = -1;

    public record MonitorInfo(int Number, bool IsPrimary);

    public static List<MonitorInfo> GetMonitors()
    {
        return Screen.AllScreens
            .Select(s => new MonitorInfo(ExtractNumber(s.DeviceName), s.Primary))
            .OrderBy(m => m.Number)
            .ToList();
    }

    private static int ExtractNumber(string deviceName)
    {
        var digits = new string(deviceName.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out int n) ? n : 0;
    }

    public static (bool Success, string Message) TogglePrimary()
    {
        var monitors = GetMonitors();
        if (monitors.Count < 2)
            return (false, "Only one monitor detected");

        int targetNumber;
        if (_previousPrimary > 0 && monitors.Any(m => m.Number == _previousPrimary && !m.IsPrimary))
            targetNumber = _previousPrimary;
        else
            targetNumber = monitors.First(m => !m.IsPrimary).Number;

        return SetPrimary(targetNumber);
    }

    public static (bool Success, string Message) SetPrimary(int displayNumber)
    {
        var nircmdPath = Path.Combine(AppContext.BaseDirectory, "nircmd.exe");
        if (!File.Exists(nircmdPath))
            return (false, "nircmd.exe not found next to MonitorSwap.exe");

        var currentPrimary = Screen.AllScreens.FirstOrDefault(s => s.Primary);
        if (currentPrimary != null)
            _previousPrimary = ExtractNumber(currentPrimary.DeviceName);

        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = nircmdPath,
                Arguments = $"setprimarydisplay {displayNumber}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            });

            if (proc == null)
                return (false, "Failed to start nircmd");

            proc.WaitForExit(5000);

            if (proc.ExitCode != 0)
                return (false, $"nircmd failed (exit {proc.ExitCode}): {proc.StandardError.ReadToEnd()}");

            return (true, $"Monitor {displayNumber}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static int GetPrimaryIndex()
    {
        var primary = Screen.AllScreens.FirstOrDefault(s => s.Primary);
        return primary != null ? ExtractNumber(primary.DeviceName) : 1;
    }
}
