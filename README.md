# MonitorSwap

Windows system tray utility that swaps your primary monitor with a hotkey.

## Usage

1. Download `MonitorSwap.exe` from [Releases](https://github.com/trouta23/MonitorSwap/releases)
2. Run it (if Windows SmartScreen appears, click **More info** then **Run anyway**)
3. Find the numbered icon in your system tray (bottom-right, may be behind the **^** arrow)
4. **Ctrl+Shift+M** to swap your primary monitor
5. Right-click the tray icon for more options

## Features

- **Hotkey toggle** - Ctrl+Shift+M swaps between your last two primary monitors
- **Tray menu** - Right-click to pick any connected monitor as primary
- **Tray icon** - Shows which monitor is currently primary
- **Run on Startup** - Optional, toggle from the right-click menu
- **Multi-monitor** - Works with 2, 3, or more monitors
- **No install** - Single exe, no dependencies, no setup

## Uninstall

1. Right-click tray icon, turn off **Run on Startup** (if enabled)
2. Right-click tray icon, click **Exit**
3. Delete the exe

## Building from source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
dotnet publish -r win-x64 -c Release
```

Output is in `bin/Release/net8.0-windows/win-x64/publish/`.
