using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace EdgePulse.Native;
public sealed class WindowPlacement
{
    private readonly Window window;
    private readonly AppSettings settings;
    private IntPtr handle;
    private bool restoring;
    public WindowPlacement(Window window, AppSettings settings) { this.window = window; this.settings = settings; }
    public void Attach()
    {
        handle = new WindowInteropHelper(window).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(Hook);
        ApplyExtendedStyles();
    }
    public void ApplyExtendedStyles()
    {
        if (handle == IntPtr.Zero) return;
        // Tool window (0x80): keep out of Alt-Tab; Transparent (0x20): optional click-through overlay.
        long style = GetWindowLongPtrW(handle, -20).ToInt64() | 0x80L;
        if (settings.ClickThrough) style |= 0x20L;
        else style &= ~0x20L;
        SetWindowLongPtrW(handle, -20, new IntPtr(style));
    }
    public bool IsForegroundFullscreen()
    {
        if (handle == IntPtr.Zero) return false;
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == handle || fg == GetShellWindow() || fg == GetDesktopWindow()) return false;
        GetWindowThreadProcessId(fg, out uint fgPid);
        if (fgPid == (uint)Environment.ProcessId) return false;
        if (!GetWindowRect(fg, out var fgRect)) return false;
        var screen = CurrentScreen();
        var bounds = screen.Bounds;
        return fgRect.Left <= bounds.Left && fgRect.Top <= bounds.Top && fgRect.Right >= bounds.Right && fgRect.Bottom >= bounds.Bottom;
    }
    public double Scale => handle == IntPtr.Zero ? 1 : GetDpiForWindow(handle) / 96.0;
    public double AvailableWidth => Math.Max(320, (handle == IntPtr.Zero ? CurrentScreen() : Forms.Screen.FromHandle(handle)).WorkingArea.Width / Math.Max(.5, Scale) - 16);
    private Forms.Screen CurrentScreen() => Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == settings.MonitorName) ?? (handle != IntPtr.Zero ? Forms.Screen.FromHandle(handle) : Forms.Screen.PrimaryScreen!);
    public void Restore()
    {
        if (handle == IntPtr.Zero || restoring) return;
        restoring = true;
        try
        {
            ApplyExtendedStyles();
            var screen = CurrentScreen(); var work = screen.WorkingArea;
            GetWindowRect(handle, out var rect);
            double scale = Math.Max(.5, Scale);
            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            int dx = (int)Math.Round(settings.OffsetX * scale), dy = (int)Math.Round(settings.OffsetY * scale);
            int x = settings.HorizontalAnchor == "right" ? work.Right - width - dx : work.Left + dx;
            int y = settings.VerticalAnchor == "bottom" ? work.Bottom - height - dy : work.Top + dy;
            x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
            y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));
            SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010);
        }
        finally { restoring = false; }
    }
    public void Remember()
    {
        if (handle == IntPtr.Zero) return;
        GetWindowRect(handle, out var rect); var screen = Forms.Screen.FromHandle(handle); var work = screen.WorkingArea;
        double scale = Math.Max(.5, Scale);
        settings.MonitorName = screen.DeviceName;
        settings.HorizontalAnchor = Math.Abs(work.Right - rect.Right) <= Math.Abs(rect.Left - work.Left) ? "right" : "left";
        settings.VerticalAnchor = Math.Abs(work.Bottom - rect.Bottom) <= Math.Abs(rect.Top - work.Top) ? "bottom" : "top";
        settings.OffsetX = Math.Max(0, settings.HorizontalAnchor == "right" ? work.Right - rect.Right : rect.Left - work.Left) / scale;
        settings.OffsetY = Math.Max(0, settings.VerticalAnchor == "bottom" ? work.Bottom - rect.Bottom : rect.Top - work.Top) / scale;
    }
    public void Reset() { settings.MonitorName = Forms.Screen.PrimaryScreen?.DeviceName; settings.HorizontalAnchor = "right"; settings.VerticalAnchor = "top"; settings.OffsetX = settings.OffsetY = 12; Restore(); }
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0216 && settings.SnapToEdges) // WM_MOVING, physical pixels under PerMonitorV2
        {
            var r = Marshal.PtrToStructure<RectNative>(lParam);
            var monitor = MonitorFromRect(ref r, 2); var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfoW(monitor, ref info))
            {
                int width = r.Right - r.Left, height = r.Bottom - r.Top;
                int threshold = (int)(18 * Scale), gap = (int)(8 * Scale);
                if (Math.Abs(r.Left - info.Work.Left) < threshold) { r.Left = info.Work.Left + gap; r.Right = r.Left + width; }
                else if (Math.Abs(r.Right - info.Work.Right) < threshold) { r.Right = info.Work.Right - gap; r.Left = r.Right - width; }
                if (Math.Abs(r.Top - info.Work.Top) < threshold) { r.Top = info.Work.Top + gap; r.Bottom = r.Top + height; }
                else if (Math.Abs(r.Bottom - info.Work.Bottom) < threshold) { r.Bottom = info.Work.Bottom - gap; r.Top = r.Bottom - height; }
                Marshal.StructureToPtr(r, lParam, false); handled = true; return new IntPtr(1);
            }
        }
        return IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)] private struct RectNative { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public RectNative Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RectNative rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref RectNative rect, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}
