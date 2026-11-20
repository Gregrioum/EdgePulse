EdgePulse 1.2.0 (Loupe HUD Edition)
Compact glassmorphic performance widget for Windows 11

QUICK START

Portable: extract the complete portable ZIP to a writable folder, then run
EdgePulse.exe. Keep portable.flag beside the executable. Preferences are kept
in the Data subfolder. Do not run directly inside a ZIP preview.

Installer: run the Setup executable. It installs only for the current user,
adds a Start menu shortcut, and includes an uninstaller in Windows Settings.
Administrator rights and a separate runtime installation are not required.

Use win-x64 on Intel/AMD PCs and win-arm64 on Windows on Arm PCs.

CONTROLS

- Drag the left live-status indicator dot to move. Alt+drag anywhere also works.
- The widget snaps near monitor work-area edges and avoids the taskbar.
- Click the gear or press F2 while focused to open Settings.
- Right-click the widget or system tray icon for quick layout, position lock,
  click-through mode, fullscreen auto-hide, hide, and quit actions.
- Click CPU, RAM, GPU, VRAM, network, or a drive to open live glassmorphic
  detail cards with a 1-click Task Manager launcher.
- +N opens the list when more drives are connected than will fit on screen.

VISUAL STATES (NORMAL & HIGH LOAD HUD)

- Normal Load (<80% utilization and <78°C): Indigo/Sky double-ring status dot,
  sky-blue Consolas temperature pill badges, and Indigo-to-Sky gradient meters.
- Warning Load (80–89% utilization or 78–87°C): Amber status dot, amber pill
  badges, and amber meters/borders.
- High Load / Thermal Alert (>=90% utilization or >=88°C): Rose-coral status
  dot, rose-coral alert pill badges, and rose-coral meters/borders.

LAYOUTS AND SETTINGS

Micro Strip: 34 logical pixels tall at 100% text size.
Compact Rail: 50 logical pixels tall; the default layout.
Dual Row: 64 logical pixels tall; capacity and disk activity stay visible.

Settings includes layout, background opacity, 1/2/5-second refresh, text size,
always-on-top, edge snapping, position lock, click-through overlay mode (always
defaults to off at Windows sign-in and app startup),
automatic hiding when a fullscreen app/game is focused, launch at Windows
sign-in, graphics-adapter selection, network-adapter selection, network units,
individual metric visibility (CPU, GPU, RAM, VRAM, Network, Drives,
Temperatures), Celsius/Fahrenheit toggle, temperature sensor setup, and reset
position. Changes save automatically and stay synchronized across menus.

Startup is optional and initially off. For portable startup, keep the app in
the same folder; turn startup off and back on after moving it. Windows Startup
Apps or organization policy can override a sign-in entry.

READINGS

Windows performance counters, DXGI, WDDM, and IP Helper provide the data. Task
Manager does not need to be running. Monitoring stays local and requires no
account. The optional CPU sensor setup button downloads the official signed
PawnIO installer only when clicked; the download is verified against a pinned
SHA-256.

- CPU uses Windows Processor Utility, with Processor Time as a fallback.
- GPU usage is the busiest engine after aggregating per-process engine values.
- GPU details include WDDM temperature, fan speed (RPM), and memory clock (MHz)
  when exposed by the graphics driver.
- VRAM is dedicated graphics memory. Integrated GPUs with less than 512 MB of
  dedicated memory show shared memory in the compact display; details show both.
- Memory details show both Physical RAM and Commit Charge (RAM + Pagefile).
- CPU/GPU temperatures are displayed in Consolas pill badges beside utilization
  in every layout, in details, and under Settings > Temperatures (°C or °F).
- CPU temperature uses LibreHardwareMonitor 0.9.6 and the signed PawnIO sensor
  driver on supported x64 Intel/AMD systems. Settings > Temperatures > Set up
  CPU sensor driver downloads the verified official installer and requests
  Windows administrator approval. It is never installed automatically.
- Missing temperatures show a dash (—°C / —°F), never a fabricated reading.
- RAM and capacity use binary units (1 GB represents 1 GiB).
- Automatic network mode sums active physical interfaces and excludes filter
  drivers, virtual interfaces, and endpoint devices to avoid double-counting.
  A VPN or virtual adapter can be selected explicitly.
- Drive percentages mean used storage capacity. "busy" means disk activity.
  Read/write rates are available by clicking a drive.

FILES AND REMOVAL

Portable preferences: Data\settings.json next to EdgePulse.exe.
Installed preferences: %LOCALAPPDATA%\EdgePulse\settings.json.
If a portable folder cannot be written, preferences fall back to the installed
preferences location. Errors, if any, are kept in error.log in that folder.

Portable removal: disable sign-in startup, quit from the tray, then delete the
portable folder. Installed removal: Windows Settings > Apps > Installed apps >
EdgePulse > Uninstall. User preferences are retained after uninstall.

BUILDING FROM SOURCE

Source is in src\EdgePulse. The project uses .NET 10 WPF and built-in Windows
APIs, plus LibreHardwareMonitorLib 0.9.6 for CPU temperature. Build scripts are
in scripts, and the Inno Setup definition is in packaging. Build.ps1 accepts
their full paths.

--verify <folder> runs local diagnostics in an isolated preference folder and
exits. It collects real metrics, checks aggregation and layout switching, and
renders the application's own visuals to PNG without capturing the desktop.
