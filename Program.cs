namespace MonitorSwap;

static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    static void Main()
    {
        try
        {
            _mutex = new Mutex(true, "MonitorSwap_SingleInstance", out bool isNew);
            if (!isNew)
            {
                MessageBox.Show(
                    "MonitorSwap is already running in the system tray.",
                    "MonitorSwap",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new TrayApp());

            GC.KeepAlive(_mutex);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}",
                "MonitorSwap - Startup Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
