using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;

namespace EdgePulse;
public sealed class WidgetSurface : FrameworkElement
{
    // Cached frozen brushes, pens, and typefaces (Variation C — Colour Picker Loupe HUD & Pill Badges)
    private static readonly Brush Ink = FrozenBrush("#F5F8FF");
    private static readonly Brush InkWarn = FrozenBrush("#FDE68A");
    private static readonly Brush InkHigh = FrozenBrush("#FECDD3");
    private static readonly Brush Muted = FrozenBrush("#94A3B8");
    private static readonly Brush SubMuted = FrozenBrush("#CBD5E1");
    private static readonly Brush Track = FrozenBrush(Color.FromArgb(34, 255, 255, 255));
    private static readonly Brush HoverFill = FrozenBrush(Color.FromArgb(24, 56, 189, 248));
    private static readonly Pen HoverPen = FrozenPen(Color.FromArgb(90, 125, 211, 252), 1);
    private static readonly Pen DividerPen = FrozenPen(Color.FromArgb(34, 255, 255, 255), 1);

    // Status dot & border palette (Normal = Indigo/Sky, Warning = Amber, High = Rose)
    private static readonly Brush DotNormalOuter = FrozenBrush(Color.FromArgb(72, 99, 102, 241));
    private static readonly Brush DotNormalInner = FrozenBrush("#6366F1");
    private static readonly Brush DotWarnOuter = FrozenBrush(Color.FromArgb(82, 245, 158, 11));
    private static readonly Brush DotWarnInner = FrozenBrush("#F59E0B");
    private static readonly Brush DotHighOuter = FrozenBrush(Color.FromArgb(96, 244, 63, 94));
    private static readonly Brush DotHighInner = FrozenBrush("#F43F5E");
    private static readonly Pen DotRingPen = FrozenPen(Color.FromArgb(210, 255, 255, 255), 1);

    private static readonly Pen BorderNormalPen = FrozenPen(Color.FromArgb(135, 125, 211, 252), 1.1);
    private static readonly Pen BorderWarnPen = FrozenPen(Color.FromArgb(185, 245, 158, 11), 1.2);
    private static readonly Pen BorderHighPen = FrozenPen(Color.FromArgb(215, 244, 63, 94), 1.3);

    // Pill badge styles (Normal Sky, Warning Amber, High Rose)
    private static readonly Brush PillNormalBg = FrozenBrush(Color.FromArgb(38, 56, 189, 248));
    private static readonly Pen PillNormalPen = FrozenPen(Color.FromArgb(115, 125, 211, 252), 1);
    private static readonly Brush PillNormalText = FrozenBrush("#BAE6FD");

    private static readonly Brush PillWarnBg = FrozenBrush(Color.FromArgb(46, 245, 158, 11));
    private static readonly Pen PillWarnPen = FrozenPen(Color.FromArgb(165, 251, 191, 36), 1);
    private static readonly Brush PillWarnText = FrozenBrush("#FDE68A");

    private static readonly Brush PillHighBg = FrozenBrush(Color.FromArgb(56, 244, 63, 94));
    private static readonly Pen PillHighPen = FrozenPen(Color.FromArgb(195, 251, 113, 133), 1);
    private static readonly Brush PillHighText = FrozenBrush("#FECDD3");

    // Meter fills
    private static readonly Brush MeterNormal = CreateGradient(Color.FromRgb(99, 102, 241), Color.FromRgb(56, 189, 248), new Point(0, 0), new Point(1, 0));
    private static readonly Brush MeterWarn = FrozenBrush("#F59E0B");
    private static readonly Brush MeterHigh = FrozenBrush("#F43F5E");

    // Ambient glow & glass sheen
    private static readonly Brush SheenBrush = CreateGradient(Color.FromArgb(24, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(0, 1));
    private static readonly Brush GlowNormal = CreateRadialGlow(Color.FromArgb(48, 99, 102, 241));
    private static readonly Brush GlowWarn = CreateRadialGlow(Color.FromArgb(56, 245, 158, 11));
    private static readonly Brush GlowHigh = CreateRadialGlow(Color.FromArgb(68, 244, 63, 94));

    // Cached typefaces
    private static readonly Typeface UiRegular = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface UiMedium = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface MonoBold = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface FluentIcons = new(new FontFamily("Segoe Fluent Icons"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private readonly AppSettings settings;
    private Snapshot data = Snapshot.Empty;
    private double effectiveScale = 1;
    private double cachedOpacity = -1;
    private Brush? cachedSurfaceBg;

    private const double GroupGap = 14, InnerGap = 6, SectionGap = 22, StartX = 24, EndSpace = 32, TempPillPad = 8;
    private Cell[] cells = Array.Empty<Cell>();
    private double performanceWidth, driveWidth;
    private int visibleDriveCount;
    private LoadState overallLoad = LoadState.Normal;
    private readonly List<HitRegion> targets = new();
    private readonly List<Rect> textBounds = new();
    public bool TextFits => textBounds.All(r => r.Left >= 1 && r.Right <= Width / effectiveScale - 1 && r.Top >= 1 && r.Bottom <= Height / effectiveScale - 1)
        && !textBounds.Where((r, i) => textBounds.Skip(i + 1).Any(other => Rect.Intersect(r, other) is var overlap && !overlap.IsEmpty && overlap.Width > .5 && overlap.Height > .5)).Any();
    private string? hoverKey;
    public event Action? DragRequested;
    public event Action<string>? ItemClicked;

    public WidgetSurface(AppSettings settings)
    {
        this.settings = settings; Focusable = true;
        MouseMove += (_, e) =>
        {
            var hit = Hit(e.GetPosition(this));
            Cursor = hit?.ActionTarget == "move" ? (settings.LockPosition ? Cursors.Arrow : Cursors.SizeAll) : Cursors.Hand;
            if (hoverKey != hit?.Key)
            {
                hoverKey = hit?.Key;
                ToolTip = hit?.Tip;
                InvalidateVisual();
            }
        };
        MouseLeave += (_, _) =>
        {
            if (hoverKey != null) { hoverKey = null; ToolTip = null; InvalidateVisual(); }
        };
        MouseLeftButtonDown += (_, e) =>
        {
            var hit = Hit(e.GetPosition(this)); if (hit == null) return;
            if (hit.Value.ActionTarget == "move" || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) { if (!settings.LockPosition) DragRequested?.Invoke(); }
            else ItemClicked?.Invoke(hit.Value.ActionTarget);
            e.Handled = true;
        };
        KeyDown += (_, e) => { if (e.Key is Key.F2 or Key.Enter) { ItemClicked?.Invoke("settings"); e.Handled = true; } };
    }

    public void Update(Snapshot snapshot, double availableWidth)
    {
        data = snapshot;
        bool rail = settings.Layout == WidgetLayout.CompactRail;
        bool dual = settings.Layout == WidgetLayout.DualRow;
        cells = BuildCells();
        overallLoad = ComputeOverallLoad();
        performanceWidth = cells.Length > 0 ? cells.Sum(c => c.Width) + GroupGap * (cells.Length - 1) : 60;
        driveWidth = rail ? Math.Ceiling(MeasureText("100%", 12, true, true)) : Math.Ceiling(MeasureText("W:", 11) + InnerGap + MeasureText("100%", 12, true, true));
        int drives = settings.ShowDrives ? snapshot.Drives.Count : 0;
        double core = StartX + performanceWidth + EndSpace;
        // Leave room for an overflow control even when no drive cell fits.
        double minimum = core + (!dual && drives > 0 ? SectionGap + 28 : 0);
        effectiveScale = Math.Min(settings.TextScale, availableWidth / minimum);
        double budget = availableWidth / effectiveScale;
        double area = dual ? performanceWidth : budget - core - (drives > 0 ? SectionGap : 0);
        double itemWidth = dual ? DualDriveWidth : driveWidth;
        visibleDriveCount = Math.Min(drives, Math.Max(0, (int)((area + GroupGap) / (itemWidth + GroupGap))));
        if (visibleDriveCount < drives)
            while (visibleDriveCount > 0 && visibleDriveCount * (itemWidth + GroupGap) + 28 > area) visibleDriveCount--;
        double driveArea = visibleDriveCount * itemWidth + Math.Max(0, visibleDriveCount - 1) * GroupGap;
        if (visibleDriveCount < drives) driveArea += (visibleDriveCount > 0 ? GroupGap : 0) + 28;
        Width = Math.Min(availableWidth, (core + (!dual && drives > 0 ? SectionGap + driveArea : 0)) * effectiveScale);
        Height = (settings.Layout == WidgetLayout.MicroStrip ? 34 : rail ? 50 : 64) * effectiveScale;
        InvalidateVisual();
    }

    private LoadState ComputeOverallLoad()
    {
        var gpu = SelectedGpu;
        bool shared = gpu != null && gpu.Info.DedicatedBytes < 512 * 1024 * 1024;
        double? used = shared ? gpu?.SharedUsed : gpu?.DedicatedUsed;
        double total = shared ? gpu?.Info.SharedLimitBytes ?? 0 : gpu?.Info.DedicatedBytes ?? 0;
        double? vramPct = total > 0 && used.HasValue ? used / total * 100 : null;

        LoadState max = LoadState.Normal;
        void Consider(LoadState s) { if (s > max) max = s; }
        if (settings.ShowCpu) Consider(Display.EvaluateLoad(data.Cpu, settings.ShowTemperatures ? data.CpuTemperature : null));
        if (settings.ShowGpu) Consider(Display.EvaluateLoad(gpu?.Utilization, settings.ShowTemperatures ? gpu?.Temperature : null));
        if (settings.ShowMemory) Consider(Display.EvaluateLoad(data.MemoryPercent));
        if (settings.ShowVram) Consider(Display.EvaluateLoad(vramPct));
        return max;
    }

    private const double DualDriveWidth = 176;
    private Cell[] BuildCells()
    {
        var gpu = SelectedGpu;
        bool shared = gpu != null && gpu.Info.DedicatedBytes < 512 * 1024 * 1024;
        double? used = shared ? gpu?.SharedUsed : gpu?.DedicatedUsed;
        double total = shared ? gpu?.Info.SharedLimitBytes ?? 0 : gpu?.Info.DedicatedBytes ?? 0;
        double? memoryPercent = total > 0 && used.HasValue ? used / total * 100 : null;
        bool micro = settings.Layout == WidgetLayout.MicroStrip;
        bool showTemp = settings.ShowTemperatures;
        bool fahr = settings.Fahrenheit;

        var list = new List<Cell>(5);
        if (settings.ShowCpu)
            list.Add(new Cell("cpu", "CPU", Display.Percent(data.Cpu), data.Cpu, "cpu",
                "CPU utilization · " + Display.Temperature(data.CpuTemperature, fahr) + "\n" + data.CpuTemperatureStatus,
                showTemp ? Display.Temperature(data.CpuTemperature, fahr) : null, data.CpuTemperature));
        if (settings.ShowGpu)
            list.Add(new Cell("gpu", "GPU", Display.Percent(gpu?.Utilization), gpu?.Utilization, "gpu",
                (gpu?.Info.Name ?? "No GPU data available") + "\nTemperature: " + Display.Temperature(gpu?.Temperature, fahr)
                + (gpu?.FanRpm.HasValue == true ? $" · Fan: {gpu.FanRpm} RPM" : "")
                + (gpu?.Temperature == null ? " · not exposed by the graphics driver" : ""),
                showTemp ? Display.Temperature(gpu?.Temperature, fahr) : null, gpu?.Temperature));
        if (settings.ShowMemory)
            list.Add(new Cell("memory", "RAM", micro ? Display.Percent(data.MemoryPercent) : Display.MemoryPair(data.UsedMemory, data.TotalMemory),
                data.MemoryPercent, "memory",
                "Physical memory: " + Display.MemoryPair(data.UsedMemory, data.TotalMemory)
                + (data.TotalCommit > 0 ? "\nCommit charge: " + Display.MemoryPair(data.UsedCommit, data.TotalCommit) : "")));
        if (settings.ShowVram)
            list.Add(new Cell("vram", shared ? "SHARED" : "VRAM", micro ? Display.Percent(memoryPercent) : Display.MemoryPair(used, total),
                memoryPercent, "gpu",
                (shared ? "Shared GPU memory: " : "Dedicated GPU memory: ") + Display.MemoryPair(used, total)
                + (gpu?.MemoryClockMhz.HasValue == true ? $"\nMemory clock: {gpu.MemoryClockMhz:0} MHz" : "")));
        if (settings.ShowNetwork)
            list.Add(new Cell("network", "NET", NetworkText(SelectedNetworks, settings.NetworkInBits), null, "network",
                "Receive ↓ / send ↑\nClick for network adapters"));

        if (list.Count == 0)
            list.Add(new Cell("cpu", "CPU", Display.Percent(data.Cpu), data.Cpu, "cpu", "CPU utilization", showTemp ? Display.Temperature(data.CpuTemperature, fahr) : null, data.CpuTemperature));

        string maxTempSample = fahr ? "212°F" : "100°C";
        foreach (var cell in list)
        {
            double valueWidth = cell.ActionTarget == "network" ? NetworkWidth : MeasureText(cell.Value, 12, true, true);
            if (cell.Temperature != null || micro && cell.Key is "memory" or "vram") valueWidth = MeasureText("100%", 12, true, true);
            if (cell.Temperature != null) valueWidth += InnerGap + MeasureText(maxTempSample, 10.5, false, true) + TempPillPad;
            if (!micro && cell.Key is "memory" or "vram" && cell.Temperature == null)
            {
                double capacity = cell.Key == "memory" ? data.TotalMemory : total;
                valueWidth = Math.Max(valueWidth, MeasureText(Display.MemoryPair(capacity, capacity), 12, true, true));
            }
            cell.ValueWidth = Math.Ceiling(valueWidth);
            cell.Width = Math.Ceiling(settings.Layout == WidgetLayout.CompactRail
                ? Math.Max(MeasureText(cell.Label, 10.5, true), cell.ValueWidth)
                : MeasureText(cell.Label, 10.5, true) + InnerGap + cell.ValueWidth);
        }
        return list.ToArray();
    }

    private double NetworkNumberWidth => Math.Ceiling(Math.Max(MeasureText("99.9", 12, true, true), MeasureText("1023", 12, true, true)));
    private double NetworkWidth => 2 * (MeasureText("↓", 12, true) + 2 + NetworkNumberWidth) + InnerGap + 4 + MeasureText("MB/s", 12, true, true);
    public GpuReading? SelectedGpu => settings.GpuId == "auto" ? data.Gpus.OrderByDescending(g => g.Info.DedicatedBytes).FirstOrDefault() : data.Gpus.FirstOrDefault(g => g.Info.Id == settings.GpuId);
    public IReadOnlyList<NetworkReading> SelectedNetworks => settings.NetworkId == "auto" ? data.Networks.Where(n => n.Physical).ToArray() : data.Networks.Where(n => n.Id == settings.NetworkId).ToArray();

    private Brush GetSurfaceBackground()
    {
        if (cachedSurfaceBg != null && Math.Abs(cachedOpacity - settings.Opacity) < 0.001) return cachedSurfaceBg;
        cachedOpacity = settings.Opacity;
        byte alpha = (byte)Math.Clamp(Math.Round(settings.Opacity * 255), 100, 255);
        var bg = new LinearGradientBrush(Color.FromArgb(alpha, 22, 24, 34), Color.FromArgb(alpha, 13, 16, 24), new Point(0, 0), new Point(1, 1));
        bg.Freeze();
        cachedSurfaceBg = bg;
        return bg;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); if (Width <= 0 || Height <= 0) return;
        targets.Clear(); textBounds.Clear();
        dc.PushTransform(new ScaleTransform(effectiveScale, effectiveScale));
        double width = Width / effectiveScale, height = Height / effectiveScale;
        var bodyRect = new Rect(.5, .5, width - 1, height - 1);
        var borderPen = overallLoad switch { LoadState.High => BorderHighPen, LoadState.Warning => BorderWarnPen, _ => BorderNormalPen };

        // 1. Base Glassmorphic Indigo-Slate Surface
        dc.DrawRoundedRectangle(GetSurfaceBackground(), borderPen, bodyRect, 8, 8);

        // 2. Ambient Left Load Glow + Top Glass Sheen clipped inside widget bounds
        dc.PushClip(new RectangleGeometry(bodyRect, 8, 8));
        var glowBrush = overallLoad switch { LoadState.High => GlowHigh, LoadState.Warning => GlowWarn, _ => GlowNormal };
        dc.DrawEllipse(glowBrush, null, new Point(28, height * 0.45), 95, height * 1.1);
        dc.DrawRectangle(SheenBrush, null, new Rect(1, 1, width - 2, Math.Min(26, height * 0.48)));
        dc.Pop();

        // 3. Variation C Double-Ring Live Status Indicator Dot + Drag Handle (left 21px)
        bool rail = settings.Layout == WidgetLayout.CompactRail;
        bool dual = settings.Layout == WidgetLayout.DualRow;
        double dotY = dual ? 17 : height / 2;
        var (dotOuter, dotInner) = overallLoad switch
        {
            LoadState.High => (DotHighOuter, DotHighInner),
            LoadState.Warning => (DotWarnOuter, DotWarnInner),
            _ => (DotNormalOuter, DotNormalInner)
        };
        dc.DrawEllipse(dotOuter, null, new Point(11.5, dotY), 6.2, 6.2);
        dc.DrawEllipse(dotInner, DotRingPen, new Point(11.5, dotY), 3.6, 3.6);
        if (dual)
        {
            for (int gx = 9; gx <= 14; gx += 5)
                for (int gy = 43; gy <= 51; gy += 4)
                    dc.DrawEllipse(Muted, null, new Point(gx, gy), .75, .75);
        }
        string loadName = overallLoad switch { LoadState.High => "High load / thermal alert", LoadState.Warning => "Elevated load", _ => "Normal load" };
        AddTarget("move", new Rect(0, 0, 21, height), "move",
            (settings.LockPosition ? $"Status: {loadName}\nPosition locked · Right-click for Settings" : $"Status: {loadName}\nDrag to move · snaps to screen edges\nAlt+drag anywhere · F2 opens Settings"));

        // 4. Performance Metric Cells
        double at = StartX;
        foreach (var cell in cells)
        {
            DrawCell(dc, cell, at, rail); at += cell.Width + GroupGap;
        }
        if (cells.Length > 0) at -= GroupGap;

        // 5. Connected Drives
        int drives = settings.ShowDrives ? data.Drives.Count : 0;
        if (dual)
        {
            dc.DrawLine(DividerPen, new Point(StartX, 32), new Point(width - EndSpace, 32));
            if (drives == 0) Text(dc, settings.ShowDrives ? "Waiting for connected drives…" : "Drive row hidden · change layout in Settings", StartX, 41, 11, Muted);
            for (int i = 0; i < visibleDriveCount; i++)
            {
                var d = data.Drives[i]; double x = StartX + i * (DualDriveWidth + GroupGap);
                string dKey = "drive:" + d.Id;
                var dRect = new Rect(x - 3, 33, DualDriveWidth + 6, 30);
                if (hoverKey == dKey) dc.DrawRoundedRectangle(HoverFill, HoverPen, dRect, 5, 5);
                LoadState dLoad = Display.EvaluateLoad(Math.Max(d.UsedPercent ?? 0, d.Activity ?? 0));
                Text(dc, d.Id, x, 41, 10.5, Muted, true);
                Text(dc, Display.Percent(d.UsedPercent), x + 20, 41, 12, ValueBrush(Display.EvaluateLoad(d.UsedPercent)), true, true);
                Meter(dc, x + 58, 48, 40, d.UsedPercent, dLoad);
                Text(dc, "busy " + Display.Percent(d.Activity), x + 108, 42, 11, d.Activity >= 85 ? InkWarn : Muted);
                AddTarget(dKey, dRect, dKey, DriveTip(d));
            }
            if (drives > visibleDriveCount) DrawOverflow(dc, StartX + visibleDriveCount * (DualDriveWidth + GroupGap), 41, drives - visibleDriveCount);
        }
        else
        {
            if (drives > 0)
            {
                dc.DrawLine(DividerPen, new Point(at + SectionGap / 2, 8), new Point(at + SectionGap / 2, height - 8));
                at += SectionGap;
            }
            for (int i = 0; i < visibleDriveCount; i++)
            {
                var d = data.Drives[i];
                string dKey = "drive:" + d.Id;
                var cell = new Cell(dKey, d.Id, Display.Percent(d.UsedPercent), d.UsedPercent, dKey, DriveTip(d)) { Width = driveWidth };
                DrawCell(dc, cell, at, rail); at += driveWidth + GroupGap;
            }
            if (drives > visibleDriveCount) DrawOverflow(dc, at, height / 2 - 7, drives - visibleDriveCount);
        }

        // 6. Right Settings Icon Button
        double settingsHeight = rail ? 50 : 32;
        var settingsRect = new Rect(width - 27, 0, 27, settingsHeight);
        if (hoverKey == "settings")
            dc.DrawRoundedRectangle(HoverFill, HoverPen, new Rect(width - 25, (settingsHeight - 22) / 2, 22, 22), 5, 5);
        Text(dc, "\uE713", width - 21, settingsHeight / 2 - 7, 12, hoverKey == "settings" ? Ink : SubMuted, false, false, FluentIcons);
        AddTarget("settings", settingsRect, "settings", "Settings · F2\nRight-click for layouts and tray controls");

        if (data.Error != null) { dc.DrawEllipse(DotWarnInner, null, new Point(width - 5, 5), 2.2, 2.2); }
        dc.Pop();

        // Keep tooltip live while cursor remains over a target
        if (hoverKey != null)
        {
            var current = targets.FirstOrDefault(t => t.Key == hoverKey);
            if (current.Key != null && !Equals(ToolTip, current.Tip)) ToolTip = current.Tip;
        }
    }

    private void DrawCell(DrawingContext dc, Cell c, double x, bool rail)
    {
        double cellH = rail ? 50 : 32;
        var hitRect = new Rect(x - 3, 0, c.Width + 6, cellH);
        if (hoverKey == c.Key)
            dc.DrawRoundedRectangle(HoverFill, HoverPen, new Rect(x - 3, 2, c.Width + 6, cellH - 4), 5, 5);

        double valueX = x, y = rail ? 21 : 9;
        if (rail) Text(dc, c.Label, x, 5, 10.5, Muted, true);
        else valueX += Text(dc, c.Label, x, 10, 10.5, Muted, true) + InnerGap;

        LoadState valLoad = Display.EvaluateLoad(c.Percent);
        if (c.ActionTarget == "network" && SelectedNetworks.Count > 0) DrawNetwork(dc, valueX, y);
        else Text(dc, c.Value, valueX, y, 12, ValueBrush(valLoad), true, true);

        if (c.Temperature != null)
        {
            double pillX = valueX + MeasureText("100%", 12, true, true) + InnerGap;
            DrawTempPill(dc, c.Temperature, c.RawCelsius, pillX, y);
        }

        if (rail && c.Percent.HasValue)
        {
            LoadState meterLoad = Display.EvaluateLoad(c.Percent, c.RawCelsius);
            Meter(dc, x, 41, c.Width, c.Percent, meterLoad);
        }
        AddTarget(c.Key, hitRect, c.ActionTarget, c.Tip);
    }

    private void DrawTempPill(DrawingContext dc, string tempText, double? celsius, double x, double y)
    {
        LoadState tempLoad = Display.EvaluateLoad(null, celsius);
        var (bg, pen, fg) = tempLoad switch
        {
            LoadState.High => (PillHighBg, PillHighPen, PillHighText),
            LoadState.Warning => (PillWarnBg, PillWarnPen, PillWarnText),
            _ => (PillNormalBg, PillNormalPen, PillNormalText)
        };
        double textW = MeasureText(tempText, 10.5, false, true);
        double pillW = textW + TempPillPad;
        var pillRect = new Rect(x, y - 0.5, pillW, 15.5);
        dc.DrawRoundedRectangle(bg, pen, pillRect, 4, 4);
        Text(dc, tempText, x + TempPillPad / 2, y + 1, 10.5, fg, false, true);
    }

    private void DrawNetwork(DrawingContext dc, double x, double y)
    {
        var (rx, tx, unit) = NetworkParts(SelectedNetworks, settings.NetworkInBits);
        double arrow = MeasureText("↓", 12, true);
        foreach (var pair in new[] { ("↓", rx), ("↑", tx) })
        {
            Text(dc, pair.Item1, x, y, 12, PillNormalText, true);
            string value = RateNumber(pair.Item2);
            Text(dc, value, x + arrow + 2 + NetworkNumberWidth - MeasureText(value, 12, true, true), y, 12, Ink, true, true);
            x += arrow + 2 + NetworkNumberWidth + InnerGap;
        }
        Text(dc, unit, x - InnerGap + 4, y, 12, SubMuted, true, true);
    }

    private void DrawOverflow(DrawingContext dc, double x, double y, int count)
    {
        var rect = new Rect(x, y - 3, 28, 20);
        dc.DrawRoundedRectangle(PillNormalBg, PillNormalPen, rect, 4, 4);
        Text(dc, "+" + count, x + 4, y, 10.5, PillNormalText, true, true);
        AddTarget("drives", new Rect(x, y - 7, 28, 28), "drives", "Show all connected drives");
    }

    private static Brush ValueBrush(LoadState state) => state switch { LoadState.High => InkHigh, LoadState.Warning => InkWarn, _ => Ink };
    private void AddTarget(string key, Rect rect, string actionTarget, string tip) => targets.Add(new(key, new Rect(rect.X * effectiveScale, rect.Y * effectiveScale, rect.Width * effectiveScale, rect.Height * effectiveScale), actionTarget, tip));
    private HitRegion? Hit(Point p) { for (int i = targets.Count - 1; i >= 0; i--) if (targets[i].Rect.Contains(p)) return targets[i]; return null; }
    private static string DriveTip(DriveReading d) => $"{d.Id} {d.Label}\n{Display.Size(d.UsedBytes)} / {Display.Size(d.TotalBytes)} used\nActivity {Display.Percent(d.Activity)} · Read {Display.Rate(d.ReadBytesPerSecond)} · Write {Display.Rate(d.WriteBytesPerSecond)}\nClick for details";
    public static string NetworkText(IReadOnlyList<NetworkReading> networks, bool bits)
    {
        if (networks.Count == 0) return "N/A";
        var (rx, tx, unit) = NetworkParts(networks, bits);
        return "↓" + RateNumber(rx) + " ↑" + RateNumber(tx) + " " + unit;
    }
    private static string RateNumber(double value) => value.ToString(value >= 99.95 ? "0" : "0.#", CultureInfo.InvariantCulture);
    private static (double Rx, double Tx, string Unit) NetworkParts(IReadOnlyList<NetworkReading> networks, bool bits)
    {
        double rx = networks.Sum(n => n.ReceiveBytesPerSecond), tx = networks.Sum(n => n.SendBytesPerSecond);
        if (bits) { rx *= 8; tx *= 8; }
        double basis = bits ? 1000 : 1024;
        string[] units = bits ? new[] { "b/s", "Kb/s", "Mb/s", "Gb/s", "Tb/s" } : new[] { "B/s", "KB/s", "MB/s", "GB/s", "TB/s" };
        int unit = 0;
        while (Math.Round(Math.Max(rx, tx)) >= basis && unit < units.Length - 1) { rx /= basis; tx /= basis; unit++; }
        return (rx, tx, units[unit]);
    }

    private double Text(DrawingContext dc, string value, double x, double y, double size, Brush brush, bool medium = false, bool mono = false, Typeface? customTypeface = null)
    {
        Typeface tf = customTypeface ?? (mono ? MonoBold : medium ? UiMedium : UiRegular);
        var text = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        textBounds.Add(new Rect(x, y, text.WidthIncludingTrailingWhitespace, text.Height));
        dc.DrawText(text, new Point(x, y));
        return text.WidthIncludingTrailingWhitespace;
    }

    private double MeasureText(string value, double size, bool medium = false, bool mono = false)
    {
        Typeface tf = mono ? MonoBold : medium ? UiMedium : UiRegular;
        return new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, size, Ink, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;
    }

    private static void Meter(DrawingContext dc, double x, double y, double width, double? percent, LoadState state = LoadState.Normal)
    {
        dc.DrawRoundedRectangle(Track, null, new Rect(x, y, width, 2.5), 1.25, 1.25);
        if (percent.HasValue && percent.Value > 0)
        {
            Brush fill = state switch { LoadState.High => MeterHigh, LoadState.Warning => MeterWarn, _ => MeterNormal };
            dc.DrawRoundedRectangle(fill, null, new Rect(x, y, width * Math.Clamp(percent.Value, 0, 100) / 100, 2.5), 1.25, 1.25);
        }
    }

    private static Brush FrozenBrush(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    private static Brush FrozenBrush(Color color) { var b = new SolidColorBrush(color); b.Freeze(); return b; }
    private static Pen FrozenPen(Color color, double thickness) { var p = new Pen(FrozenBrush(color), thickness); p.Freeze(); return p; }
    private static Brush CreateGradient(Color start, Color end, Point p1, Point p2) { var g = new LinearGradientBrush(start, end, p1, p2); g.Freeze(); return g; }
    private static Brush CreateRadialGlow(Color center)
    {
        var g = new RadialGradientBrush(center, Color.FromArgb(0, center.R, center.G, center.B));
        g.Freeze();
        return g;
    }

    private readonly record struct HitRegion(string Key, Rect Rect, string ActionTarget, string Tip);
    private sealed record Cell(string Key, string Label, string Value, double? Percent, string ActionTarget, string Tip, string? Temperature = null, double? RawCelsius = null)
    {
        public double Width { get; set; }
        public double ValueWidth { get; set; }
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new SurfacePeer(this);
    private sealed class SurfacePeer(WidgetSurface owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => "EdgePulseWidget";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
        protected override string GetNameCore() => "EdgePulse " + owner.settings.Layout + ", CPU " + Display.Percent(owner.data.Cpu) + ", temperature " + Display.Temperature(owner.data.CpuTemperature, owner.settings.Fahrenheit) + ", GPU temperature " + Display.Temperature(owner.SelectedGpu?.Temperature, owner.settings.Fahrenheit) + ", memory " + Display.Percent(owner.data.MemoryPercent) + ". Press F2 for Settings.";
    }
}
