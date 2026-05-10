using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MonitorSwap;

public class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly HotkeyWindow _hotkeyWindow;
    private ToolStripMenuItem? _startupItem;

    private const int HOTKEY_ID = 9001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public TrayApp()
    {
        _trayIcon = new NotifyIcon
        {
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _trayIcon.DoubleClick += (_, _) => Swap();

        RefreshIcon();

        _hotkeyWindow = new HotkeyWindow(Swap);
        bool registered = RegisterHotKey(
            _hotkeyWindow.Handle, HOTKEY_ID,
            MOD_CONTROL | MOD_ALT | MOD_NOREPEAT,
            (uint)Keys.M);

        if (!registered)
        {
            _trayIcon.BalloonTipTitle = "MonitorSwap";
            _trayIcon.BalloonTipText = "Ctrl+Alt+M is taken by another app. Use the tray icon instead.";
            _trayIcon.BalloonTipIcon = ToolTipIcon.Warning;
            _trayIcon.ShowBalloonTip(3000);
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add("Swap Primary Monitor", null, (_, _) => Swap());
        menu.Items.Add(new ToolStripSeparator());

        var monitors = DisplayManager.GetMonitors();
        foreach (var (monitor, idx) in monitors.Select((m, i) => (m, i)))
        {
            var label = $"Monitor {idx + 1}: {monitor.DisplayName}";
            if (monitor.IsPrimary) label += " [primary]";
            var item = new ToolStripMenuItem(label);
            if (monitor.IsPrimary)
            {
                item.Checked = true;
                item.Enabled = false;
            }
            else
            {
                var deviceName = monitor.DeviceName;
                item.Click += (_, _) => SetSpecificMonitor(deviceName);
            }
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());

        _startupItem = new ToolStripMenuItem("Run on Startup") { Checked = IsStartupEnabled() };
        _startupItem.Click += (_, _) => ToggleStartup();
        menu.Items.Add(_startupItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Hotkey: Ctrl+Alt+M").Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());

        return menu;
    }

    private void SetSpecificMonitor(string deviceName)
    {
        var (success, message) = DisplayManager.SetPrimary(deviceName);
        HandleSwapResult(success, message);
    }

    private void Swap()
    {
        var (success, message) = DisplayManager.CyclePrimary();
        HandleSwapResult(success, message);
    }

    private void HandleSwapResult(bool success, string message)
    {
        if (success)
        {
            RefreshIcon();
            RebuildMenu();
            _trayIcon.BalloonTipTitle = "Monitor Swapped";
            _trayIcon.BalloonTipText = $"Primary: {message}";
            _trayIcon.BalloonTipIcon = ToolTipIcon.Info;
        }
        else
        {
            _trayIcon.BalloonTipTitle = "Swap Failed";
            _trayIcon.BalloonTipText = message;
            _trayIcon.BalloonTipIcon = ToolTipIcon.Error;
        }

        _trayIcon.ShowBalloonTip(2000);
    }

    private void RefreshIcon()
    {
        int primary = DisplayManager.GetPrimaryIndex();
        var oldIcon = _trayIcon.Icon;
        _trayIcon.Icon = CreateIcon(primary);
        _trayIcon.Text = $"MonitorSwap - Primary: Monitor {primary}\nCtrl+Alt+M to swap";
        oldIcon?.Dispose();
    }

    private void RebuildMenu()
    {
        _trayIcon.ContextMenuStrip?.Dispose();
        _trayIcon.ContextMenuStrip = BuildMenu();
    }

    private static Icon CreateIcon(int number)
    {
        using var bitmap = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.Transparent);

        using var bg = new SolidBrush(Color.FromArgb(24, 24, 27));
        using var accent = new Pen(Color.FromArgb(99, 102, 241), 1.5f);
        g.FillEllipse(bg, 1, 1, 13, 13);
        g.DrawEllipse(accent, 1, 1, 13, 13);

        using var font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        using var brush = new SolidBrush(Color.White);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString(number.ToString(), font, brush, new RectangleF(0, 0, 16, 16), sf);

        IntPtr hIcon = bitmap.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    private const string StartupKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "MonitorSwap";

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupKey, false);
        return key?.GetValue(AppName) != null;
    }

    private void ToggleStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupKey, true);
        if (key == null) return;

        if (IsStartupEnabled())
        {
            key.DeleteValue(AppName, false);
            if (_startupItem != null) _startupItem.Checked = false;
        }
        else
        {
            string exePath = Environment.ProcessPath ?? Application.ExecutablePath;
            key.SetValue(AppName, $"\"{exePath}\"");
            if (_startupItem != null) _startupItem.Checked = true;
        }
    }

    private void Exit() => Application.Exit();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UnregisterHotKey(_hotkeyWindow.Handle, HOTKEY_ID);
            _hotkeyWindow.DestroyHandle();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    private class HotkeyWindow : NativeWindow
    {
        private const int WM_HOTKEY = 0x0312;
        private readonly Action _onHotkey;

        public HotkeyWindow(Action onHotkey)
        {
            _onHotkey = onHotkey;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
                _onHotkey();
            base.WndProc(ref m);
        }
    }
}
