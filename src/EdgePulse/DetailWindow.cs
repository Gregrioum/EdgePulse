using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EdgePulse;
public sealed class DetailWindow : Window
{
    private readonly MainWindow main;
    private readonly string target;
    private readonly StackPanel cardsPanel = new();
    private readonly Ellipse statusDot = new() { Width = 10, Height = 10, Stroke = Brushes.White, StrokeThickness = 1 };
    private readonly Border statusBadge = new() { CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private readonly TextBlock statusBadgeText = new() { FontFamily = new FontFamily("Consolas"), FontSize = 11, FontWeight = FontWeights.Bold };

    public DetailWindow(MainWindow main, string target)
    {
        this.main = main; this.target = target;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "EdgePulse · Details"; Width = 470; Height = 415; MinWidth = 380; MinHeight = 300;
        Owner = main; WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = main.Settings.AlwaysOnTop;

        var outer = new DockPanel { Margin = new Thickness(22) };

        // Header matching Colour Picker v1.2.1
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var dotWrap = new Grid { Width = 18, Height = 18, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
        dotWrap.Children.Add(new Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(Color.FromArgb(65, 99, 102, 241)) });
        dotWrap.Children.Add(statusDot);
        DockPanel.SetDock(dotWrap, Dock.Left);
        header.Children.Add(dotWrap);

        string titleText = target.StartsWith("drive:") ? "Drive " + target[6..] : target switch { "gpu" => "Graphics & VRAM", "network" => "Network Adapters", "memory" => "Memory & Commit", _ => "Processor & Thermal" };
        var heading = new TextBlock { Text = titleText, FontSize = 19, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(heading, Dock.Left);
        header.Children.Add(heading);

        statusBadge.Child = statusBadgeText;
        DockPanel.SetDock(statusBadge, Dock.Right);
        header.Children.Add(statusBadge);

        DockPanel.SetDock(header, Dock.Top);
        outer.Children.Add(header);

        // Footer with Colour-Picker gradient action button
        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var closeBtn = new Button { Content = "Done", MinWidth = 82, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        closeBtn.Click += (_, _) => Close();
        DockPanel.SetDock(closeBtn, Dock.Right);
        footer.Children.Add(closeBtn);

        var taskMgrBtn = new Button
        {
            Content = "⊕  Open Task Manager",
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new LinearGradientBrush(Color.FromRgb(99, 102, 241), Color.FromRgb(56, 189, 248), new Point(0, 0), new Point(1, 0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255))
        };
        taskMgrBtn.Click += (_, _) => Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
        footer.Children.Add(taskMgrBtn);
        DockPanel.SetDock(footer, Dock.Bottom);
        outer.Children.Add(footer);

        outer.Children.Add(new ScrollViewer { Content = cardsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = outer;

        main.SnapshotChanged += Update;
        main.SettingsChanged += OnSettingsChanged;
        Closed += (_, _) => { main.SnapshotChanged -= Update; main.SettingsChanged -= OnSettingsChanged; };
        SourceInitialized += (_, _) => Ui.DarkTitle(this);
        Update(main.Latest);
    }

    private void OnSettingsChanged() => Update(main.Latest);

    private void Update(Snapshot data)
    {
        bool fahr = main.Settings.Fahrenheit;
        cardsPanel.Children.Clear();
        LoadState worst = LoadState.Normal;
        void TrackLoad(LoadState s) { if (s > worst) worst = s; }

        if (target.StartsWith("drive:"))
        {
            var d = data.Drives.FirstOrDefault(x => x.Id == target[6..]);
            if (d == null)
            {
                cardsPanel.Children.Add(CreateCard("DRIVE STATUS", "This drive is disconnected."));
            }
            else
            {
                TrackLoad(Display.EvaluateLoad(Math.Max(d.UsedPercent ?? 0, d.Activity ?? 0)));
                cardsPanel.Children.Add(CreateCard(d.Id + " · " + d.Label.ToUpperInvariant(),
                    $"Used: {Display.Size(d.UsedBytes)} / {Display.Size(d.TotalBytes)} ({Display.Percent(d.UsedPercent)})\nFree: {Display.Size(d.TotalBytes - d.UsedBytes)}",
                    d.UsedPercent));
                cardsPanel.Children.Add(CreateCard("DISK THROUGHPUT & ACTIVITY",
                    $"Activity:  {Display.Percent(d.Activity)}\nRead:      {Display.Rate(d.ReadBytesPerSecond)}\nWrite:     {Display.Rate(d.WriteBytesPerSecond)}" + (d.Status != null ? "\n" + d.Status : ""),
                    d.Activity));
            }
        }
        else if (target == "gpu")
        {
            foreach (var g in data.Gpus)
            {
                TrackLoad(Display.EvaluateLoad(g.Utilization, g.Temperature));
                string extras = "";
                if (g.FanRpm.HasValue) extras += $"   ·   Fan: {g.FanRpm} RPM";
                if (g.MemoryClockMhz.HasValue) extras += $"   ·   Mem Clock: {g.MemoryClockMhz:0} MHz";
                cardsPanel.Children.Add(CreateCard(g.Info.Name.ToUpperInvariant(),
                    $"Utilization:  {Display.Percent(g.Utilization)}   ·   Temp: {Display.Temperature(g.Temperature, fahr)}{extras}\n"
                    + (!g.Temperature.HasValue ? "Temperature is not exposed by the graphics driver.\n" : "")
                    + $"Dedicated:    {Display.MemoryPair(g.DedicatedUsed, g.Info.DedicatedBytes)}\n"
                    + $"Shared:       {Display.MemoryPair(g.SharedUsed, g.Info.SharedLimitBytes)}",
                    g.Utilization, g.Temperature));
            }
            if (data.Gpus.Count == 0)
                cardsPanel.Children.Add(CreateCard("GRAPHICS", "No graphics adapter data available."));
        }
        else if (target == "network")
        {
            foreach (var n in data.Networks)
            {
                cardsPanel.Children.Add(CreateCard(n.Name.ToUpperInvariant() + (n.Physical ? " · PHYSICAL" : " · VIRTUAL"),
                    $"Receive ↓   {Display.Rate(n.ReceiveBytesPerSecond, main.Settings.NetworkInBits)}\nSend ↑      {Display.Rate(n.SendBytesPerSecond, main.Settings.NetworkInBits)}"));
            }
            if (data.Networks.Count == 0)
                cardsPanel.Children.Add(CreateCard("NETWORK", "No active network adapter, or waiting for the first sample."));
        }
        else if (target == "memory")
        {
            TrackLoad(Display.EvaluateLoad(data.MemoryPercent));
            cardsPanel.Children.Add(CreateCard("PHYSICAL MEMORY (RAM)",
                $"Utilization:  {Display.Percent(data.MemoryPercent)}\nUsed:         {Display.Size(data.UsedMemory)} / {Display.Size(data.TotalMemory)}\nAvailable:    {Display.Size(data.TotalMemory - data.UsedMemory)}",
                data.MemoryPercent));
            if (data.TotalCommit > 0)
            {
                cardsPanel.Children.Add(CreateCard("COMMIT CHARGE (RAM + PAGEFILE)",
                    $"Committed:    {Display.Percent(data.CommitPercent)}\nUsed:         {Display.Size(data.UsedCommit)} / {Display.Size(data.TotalCommit)}",
                    data.CommitPercent));
            }
        }
        else
        {
            TrackLoad(Display.EvaluateLoad(data.Cpu, data.CpuTemperature));
            cardsPanel.Children.Add(CreateCard("PROCESSOR UTILIZATION & THERMAL",
                $"Utilization:         {Display.Percent(data.Cpu)}\nPackage Temperature: {Display.Temperature(data.CpuTemperature, fahr)}\nLogical Processors:  {Environment.ProcessorCount}",
                data.Cpu, data.CpuTemperature));
            cardsPanel.Children.Add(CreateCard("SENSOR & SAMPLING SOURCE",
                $"{data.CpuTemperatureStatus}\nWindows performance counters · {main.Settings.RefreshSeconds} s refresh"));
        }

        ApplyHeaderStatus(worst);
    }

    private void ApplyHeaderStatus(LoadState state)
    {
        var (dotCol, bgCol, brdCol, txtCol, label) = state switch
        {
            LoadState.High => (Color.FromRgb(244, 63, 94), Color.FromArgb(46, 244, 63, 94), Color.FromArgb(180, 251, 113, 133), Color.FromRgb(254, 205, 211), "HIGH LOAD"),
            LoadState.Warning => (Color.FromRgb(245, 158, 11), Color.FromArgb(42, 245, 158, 11), Color.FromArgb(160, 251, 191, 36), Color.FromRgb(253, 230, 138), "ELEVATED"),
            _ => (Color.FromRgb(56, 189, 248), Color.FromArgb(36, 56, 189, 248), Color.FromArgb(115, 125, 211, 252), Color.FromRgb(186, 230, 253), "NORMAL LOAD")
        };
        statusDot.Fill = new SolidColorBrush(dotCol);
        statusBadge.Background = new SolidColorBrush(bgCol);
        statusBadge.BorderBrush = new SolidColorBrush(brdCol);
        statusBadge.BorderThickness = new Thickness(1);
        statusBadgeText.Foreground = new SolidColorBrush(txtCol);
        statusBadgeText.Text = label;
    }

    private static Border CreateCard(string title, string body, double? percent = null, double? celsius = null)
    {
        LoadState state = Display.EvaluateLoad(percent, celsius);
        Color borderCol = state switch
        {
            LoadState.High => Color.FromArgb(165, 244, 63, 94),
            LoadState.Warning => Color.FromArgb(145, 245, 158, 11),
            _ => Color.FromArgb(60, 255, 255, 255)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Margin = new Thickness(0, 0, 0, 5)
        });
        stack.Children.Add(new TextBlock
        {
            Text = body,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12.5,
            LineHeight = 20,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(245, 248, 255))
        });
        if (percent.HasValue)
        {
            var track = new Grid { Height = 4, Margin = new Thickness(0, 9, 0, 0), Background = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)) };
            Brush fill = state switch
            {
                LoadState.High => new SolidColorBrush(Color.FromRgb(244, 63, 94)),
                LoadState.Warning => new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                _ => new LinearGradientBrush(Color.FromRgb(99, 102, 241), Color.FromRgb(56, 189, 248), new Point(0, 0), new Point(1, 0))
            };
            var bar = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Clamp(percent.Value, 0, 100) * 3.8,
                Background = fill,
                CornerRadius = new CornerRadius(2)
            };
            track.Children.Add(bar);
            stack.Children.Add(track);
        }
        return new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(13, 11, 13, 11),
            Margin = new Thickness(0, 0, 0, 10),
            Background = new LinearGradientBrush(Color.FromArgb(26, 255, 255, 255), Color.FromArgb(12, 255, 255, 255), new Point(0, 0), new Point(0, 1)),
            BorderBrush = new SolidColorBrush(borderCol),
            BorderThickness = new Thickness(1),
            Child = stack
        };
    }
}
