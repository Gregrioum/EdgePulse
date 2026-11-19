using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EdgePulse;
public sealed class SettingsWindow : Window
{
    private readonly MainWindow main;
    private readonly AppSettings settings;
    private readonly bool verification;
    private readonly ComboBox gpuSelect = new(), networkSelect = new();
    private readonly TextBlock temperatureStatus = Note("");
    private ComboBox? layoutCombo, refreshCombo, textScaleCombo;
    private Slider? opacitySlider;
    private TextBlock? opacityLabel;
    private CheckBox? topMostCheck, snapCheck, lockCheck, clickThroughCheck, fullscreenCheck, bitsCheck, showDrivesCheck, showCpuCheck, showGpuCheck, showMemCheck, showVramCheck, showNetCheck, showTempCheck, fahrCheck;
    private bool updatingDevices, syncingSettings;

    public SettingsWindow(MainWindow main, bool verification = false)
    {
        this.main = main; settings = main.Settings; this.verification = verification;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "EdgePulse · Settings"; Width = 545; Height = 660; MinWidth = 470; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Owner = main; ShowInTaskbar = true;
        var outer = new DockPanel { Margin = new Thickness(24) };

        // Header styled after Colour Picker v1.2.1
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        var dotGrid = new Grid { Width = 18, Height = 18, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        dotGrid.Children.Add(new Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(Color.FromArgb(75, 99, 102, 241)) });
        dotGrid.Children.Add(new Ellipse { Width = 11, Height = 11, Fill = new SolidColorBrush(Color.FromRgb(99, 102, 241)), Stroke = Brushes.White, StrokeThickness = 1 });
        titleRow.Children.Add(dotGrid);
        titleRow.Children.Add(new TextBlock { Text = "EdgePulse", FontSize = 22, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        var badge = new Border
        {
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(10, 2, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(36, 56, 189, 248)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(105, 125, 211, 252)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = "F2 · HUD", FontFamily = new FontFamily("Consolas"), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(186, 230, 253)) }
        };
        titleRow.Children.Add(badge);
        heading.Children.Add(titleRow);
        heading.Children.Add(new TextBlock { Text = "Glassmorphic Loupe HUD · Normal & High-Load Telemetry", Foreground = Ui.Muted, Margin = new Thickness(0, 5, 0, 0) });
        DockPanel.SetDock(heading, Dock.Top); outer.Children.Add(heading);

        var footer = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var close = new Button
        {
            Content = "Done",
            MinWidth = 90,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = new LinearGradientBrush(Color.FromRgb(99, 102, 241), Color.FromRgb(56, 189, 248), new Point(0, 0), new Point(1, 0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(165, 255, 255, 255))
        };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right); footer.Children.Add(close);
        footer.Children.Add(new TextBlock { Text = "Changes save automatically", Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
        DockPanel.SetDock(footer, Dock.Bottom); outer.Children.Add(footer);

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "General", Content = Scroll(General()) });
        tabs.Items.Add(new TabItem { Header = "Devices", Content = Scroll(Devices()) });
        tabs.Items.Add(new TabItem { Header = "Temperatures", Content = Scroll(Temperatures()) });
        tabs.Items.Add(new TabItem { Header = "About", Content = Scroll(About()) });
        outer.Children.Add(tabs); Content = outer;

        SourceInitialized += (_, _) => Ui.DarkTitle(this);
        main.SnapshotChanged += UpdateDevices;
        main.SettingsChanged += SyncFromSettings;
        Closed += (_, _) => { main.SnapshotChanged -= UpdateDevices; main.SettingsChanged -= SyncFromSettings; };
        UpdateDevices(main.Latest);
    }

    private static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

    private StackPanel General()
    {
        var panel = new StackPanel();
        Label(panel, "Layout");
        layoutCombo = new ComboBox { Name = "LayoutSelector", SelectedIndex = (int)settings.Layout, Margin = new Thickness(0, 0, 0, 14) };
        layoutCombo.Items.Add("Micro Strip · 34 px · one line");
        layoutCombo.Items.Add("Compact Rail · 50 px · thin meters");
        layoutCombo.Items.Add("Dual Row · 64 px · disk activity visible");
        layoutCombo.SelectedIndex = (int)settings.Layout;
        layoutCombo.SelectionChanged += (_, _) => { if (!syncingSettings && layoutCombo.SelectedIndex >= 0) { settings.Layout = (WidgetLayout)layoutCombo.SelectedIndex; Apply(); } };
        panel.Children.Add(layoutCombo);

        opacityLabel = Label(panel, "Background opacity · " + (settings.Opacity * 100).ToString("0") + "%");
        opacitySlider = new Slider { Name = "OpacitySlider", Minimum = 45, Maximum = 100, Value = settings.Opacity * 100, TickFrequency = 5, IsSnapToTickEnabled = true, Margin = new Thickness(0, 0, 0, 14) };
        opacitySlider.ValueChanged += (_, _) => { if (!syncingSettings) { settings.Opacity = opacitySlider.Value / 100; opacityLabel.Text = "Background opacity · " + opacitySlider.Value.ToString("0") + "%"; Apply(); } };
        panel.Children.Add(opacitySlider);

        var row = new Grid { Margin = new Thickness(0, 0, 0, 13) }; row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new());
        var left = new StackPanel { Margin = new Thickness(0, 0, 10, 0) }; Label(left, "Refresh interval");
        refreshCombo = new ComboBox { ItemsSource = new[] { "1 second", "2 seconds", "5 seconds" }, SelectedIndex = settings.RefreshSeconds == 1 ? 0 : settings.RefreshSeconds == 2 ? 1 : 2 };
        refreshCombo.SelectionChanged += (_, _) => { if (!syncingSettings) { settings.RefreshSeconds = new[] { 1, 2, 5 }[Math.Max(0, refreshCombo.SelectedIndex)]; Apply(); } };
        left.Children.Add(refreshCombo); row.Children.Add(left);

        var right = new StackPanel { Margin = new Thickness(10, 0, 0, 0) }; Label(right, "Text size");
        double[] scales = { 1, 1.1, 1.25, 1.5 };
        textScaleCombo = new ComboBox { ItemsSource = new[] { "100% · compact", "110%", "125%", "150%" }, SelectedIndex = Math.Max(0, Array.IndexOf(scales, settings.TextScale)) };
        textScaleCombo.SelectionChanged += (_, _) => { if (!syncingSettings) { settings.TextScale = scales[Math.Max(0, textScaleCombo.SelectedIndex)]; Apply(); } };
        right.Children.Add(textScaleCombo); Grid.SetColumn(right, 1); row.Children.Add(right); panel.Children.Add(row);

        topMostCheck = Check(panel, "Keep widget above other windows", settings.AlwaysOnTop, value => settings.AlwaysOnTop = value);
        snapCheck = Check(panel, "Snap to screen edges", settings.SnapToEdges, value => settings.SnapToEdges = value);
        lockCheck = Check(panel, "Lock widget position", settings.LockPosition, value => settings.LockPosition = value);
        clickThroughCheck = Check(panel, "Click-through overlay mode (use tray icon to toggle back)", settings.ClickThrough, value => settings.ClickThrough = value);
        fullscreenCheck = Check(panel, "Automatically hide when a fullscreen app or game is active", settings.HideInFullscreen, value => settings.HideInFullscreen = value);

        var startup = new CheckBox { Content = "Start EdgePulse when I sign in to Windows", IsChecked = !verification && StartupSetting.Enabled, Name = "StartupCheckbox" };
        startup.Click += (_, _) =>
        {
            if (verification) return;
            try { StartupSetting.Set(startup.IsChecked == true); }
            catch (Exception e) { main.Store.Log(e); startup.IsChecked = StartupSetting.Enabled; MessageBox.Show(this, "Windows could not save the startup setting. " + e.Message, "EdgePulse", MessageBoxButton.OK, MessageBoxImage.Information); }
        }; panel.Children.Add(startup);
        if (main.Store.Portable) panel.Children.Add(new TextBlock { Text = "For startup, keep the portable folder in the same location.", FontSize = 11, Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 7) });
        var reset = new Button { Content = "Reset widget position", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) }; reset.Click += (_, _) => main.ResetPosition(); panel.Children.Add(reset);
        return panel;
    }

    private StackPanel Devices()
    {
        var panel = new StackPanel();
        Label(panel, "Graphics adapter"); gpuSelect.Margin = new Thickness(0, 0, 0, 8); panel.Children.Add(gpuSelect);
        panel.Children.Add(Note("Automatic selects the adapter with the most dedicated memory. Integrated graphics show shared GPU memory when dedicated VRAM is small."));
        gpuSelect.SelectionChanged += (_, _) => { if (!updatingDevices && gpuSelect.SelectedItem is Choice c) { settings.GpuId = c.Id; Apply(); } };
        Label(panel, "Network adapter", true); networkSelect.Margin = new Thickness(0, 0, 0, 8); panel.Children.Add(networkSelect);
        panel.Children.Add(Note("Automatic totals active physical adapters. Choose a VPN or virtual adapter explicitly if you want its traffic."));
        networkSelect.SelectionChanged += (_, _) => { if (!updatingDevices && networkSelect.SelectedItem is Choice c) { settings.NetworkId = c.Id; Apply(); } };
        bitsCheck = Check(panel, "Show network speed in bits per second", settings.NetworkInBits, value => settings.NetworkInBits = value);

        Label(panel, "Visible HUD metrics", true);
        var metricsGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        metricsGrid.ColumnDefinitions.Add(new()); metricsGrid.ColumnDefinitions.Add(new());
        var col1 = new StackPanel(); var col2 = new StackPanel();
        showCpuCheck = Check(col1, "Show CPU utilization", settings.ShowCpu, value => settings.ShowCpu = value);
        showGpuCheck = Check(col1, "Show GPU utilization", settings.ShowGpu, value => settings.ShowGpu = value);
        showMemCheck = Check(col1, "Show RAM usage", settings.ShowMemory, value => settings.ShowMemory = value);
        showVramCheck = Check(col2, "Show VRAM / Shared GPU", settings.ShowVram, value => settings.ShowVram = value);
        showNetCheck = Check(col2, "Show Network throughput", settings.ShowNetwork, value => settings.ShowNetwork = value);
        showDrivesCheck = Check(col2, "Show connected drives", settings.ShowDrives, value => settings.ShowDrives = value);
        metricsGrid.Children.Add(col1); Grid.SetColumn(col2, 1); metricsGrid.Children.Add(col2);
        panel.Children.Add(metricsGrid);

        panel.Children.Add(Note("Local and external volumes appear automatically. Click a drive for capacity, activity, and read/write speeds. Extra drives stay accessible through +N."));
        var refresh = new Button { Content = "Rescan devices", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) }; refresh.Click += (_, _) => main.Telemetry.RefreshDevices(); panel.Children.Add(refresh);
        return panel;
    }

    private StackPanel Temperatures()
    {
        var panel = new StackPanel();
        Label(panel, "CPU and GPU temperature badges");
        panel.Children.Add(Note("Shown in Consolas HUD pill badges beside CPU/GPU usage in all layouts. Sky blue indicates normal operating temperature, Amber indicates ≥78°C, and Rose indicates ≥88°C."));
        showTempCheck = Check(panel, "Show temperature badges on widget", settings.ShowTemperatures, value => settings.ShowTemperatures = value);
        fahrCheck = Check(panel, "Display temperatures in Fahrenheit (°F)", settings.Fahrenheit, value => settings.Fahrenheit = value);

        Label(panel, "Live sensor status", true);
        var card = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(55, 125, 211, 252)),
            BorderThickness = new Thickness(1),
            Child = temperatureStatus
        };
        panel.Children.Add(card);

        Label(panel, "CPU sensor setup", true);
        panel.Children.Add(Note("On supported Intel/AMD PCs, CPU temperatures use LibreHardwareMonitor and the signed PawnIO driver. Installing the driver requires Windows administrator approval. It stays installed separately from the portable widget; remove it through Windows Settings → Apps if no longer needed."));
        var install = new Button { Content = "Set up CPU sensor driver…", IsEnabled = CpuTemperatureSensor.Supported && !verification, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 8) };
        install.Click += async (_, _) =>
        {
            install.IsEnabled = false; install.Content = "Downloading verified sensor driver…";
            try { await SensorSetup.Install(); main.Telemetry.RefreshDevices(); }
            catch (Exception e) { main.Store.Log(e); MessageBox.Show(this, "Sensor setup was not completed. " + e.Message, "EdgePulse", MessageBoxButton.OK, MessageBoxImage.Information); }
            finally { install.IsEnabled = true; install.Content = "Set up CPU sensor driver…"; }
        };
        panel.Children.Add(install);
        panel.Children.Add(Note("Downloads the official PawnIO 2.1 installer only when you click setup. GPU temperature uses Windows directly. CPU temperature is currently unavailable on ARM64."));
        var rescan = new Button { Content = "Rescan sensors", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
        rescan.Click += (_, _) => main.Telemetry.RefreshDevices(); panel.Children.Add(rescan);
        return panel;
    }

    private StackPanel About()
    {
        var panel = new StackPanel(); Label(panel, "EdgePulse 1.1.0 · Loupe HUD Edition");
        panel.Children.Add(Note("A compact, local glassmorphic performance widget for Windows 11. No accounts, telemetry, or manufacturer utilities are required."));
        Label(panel, "Using the widget", true);
        panel.Children.Add(Note("Drag the left status dot to move. Alt+drag anywhere also works. Click the gear or press F2 for Settings. Right-click the widget or tray icon for layout, position lock, click-through, hide, and quit controls."));
        Label(panel, "Performance & thermal readings", true);
        panel.Children.Add(Note("Windows supplies utilization, memory, commit charge, network, drive, and WDDM GPU readings. CPU temperature uses LibreHardwareMonitor 0.9.6 (MPL-2.0). Missing counters show N/A; missing temperatures show a dash."));
        Label(panel, main.Store.Portable ? "Portable mode" : "Installed mode", true);
        panel.Children.Add(Note("Preferences: " + main.Store.FilePath));
        var folder = new Button { Content = "Open preferences folder", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) }; folder.Click += (_, _) => Process.Start(new ProcessStartInfo(main.Store.DirectoryPath) { UseShellExecute = true }); panel.Children.Add(folder);
        return panel;
    }

    private void SyncFromSettings()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(SyncFromSettings); return; }
        syncingSettings = true;
        try
        {
            if (layoutCombo != null) layoutCombo.SelectedIndex = (int)settings.Layout;
            if (opacitySlider != null) opacitySlider.Value = settings.Opacity * 100;
            if (opacityLabel != null) opacityLabel.Text = "Background opacity · " + (settings.Opacity * 100).ToString("0") + "%";
            if (refreshCombo != null) refreshCombo.SelectedIndex = settings.RefreshSeconds == 1 ? 0 : settings.RefreshSeconds == 2 ? 1 : 2;
            double[] scales = { 1, 1.1, 1.25, 1.5 };
            if (textScaleCombo != null) textScaleCombo.SelectedIndex = Math.Max(0, Array.IndexOf(scales, settings.TextScale));
            if (topMostCheck != null) topMostCheck.IsChecked = settings.AlwaysOnTop;
            if (snapCheck != null) snapCheck.IsChecked = settings.SnapToEdges;
            if (lockCheck != null) lockCheck.IsChecked = settings.LockPosition;
            if (clickThroughCheck != null) clickThroughCheck.IsChecked = settings.ClickThrough;
            if (fullscreenCheck != null) fullscreenCheck.IsChecked = settings.HideInFullscreen;
            if (bitsCheck != null) bitsCheck.IsChecked = settings.NetworkInBits;
            if (showCpuCheck != null) showCpuCheck.IsChecked = settings.ShowCpu;
            if (showGpuCheck != null) showGpuCheck.IsChecked = settings.ShowGpu;
            if (showMemCheck != null) showMemCheck.IsChecked = settings.ShowMemory;
            if (showVramCheck != null) showVramCheck.IsChecked = settings.ShowVram;
            if (showNetCheck != null) showNetCheck.IsChecked = settings.ShowNetwork;
            if (showDrivesCheck != null) showDrivesCheck.IsChecked = settings.ShowDrives;
            if (showTempCheck != null) showTempCheck.IsChecked = settings.ShowTemperatures;
            if (fahrCheck != null) fahrCheck.IsChecked = settings.Fahrenheit;
        }
        finally { syncingSettings = false; }
    }

    private void UpdateDevices(Snapshot snapshot)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => UpdateDevices(snapshot)); return; }
        bool fahr = settings.Fahrenheit;
        temperatureStatus.Text = "CPU: " + Display.Temperature(snapshot.CpuTemperature, fahr) + "\n" + snapshot.CpuTemperatureStatus + "\n\n" + string.Join("\n", snapshot.Gpus.Select(g => g.Info.Name + ": " + Display.Temperature(g.Temperature, fahr) + (g.Temperature.HasValue ? "" : " · not exposed by driver")));
        updatingDevices = true;
        try
        {
            var gpus = new[] { new Choice("auto", "Automatic") }.Concat(snapshot.Gpus.Select(g => new Choice(g.Info.Id, g.Info.Name))).ToList();
            if (settings.GpuId != "auto" && gpus.All(g => g.Id != settings.GpuId)) gpus.Add(new(settings.GpuId, "Selected adapter · disconnected"));
            var nets = new[] { new Choice("auto", "Automatic · physical adapters") }.Concat(snapshot.Networks.Select(n => new Choice(n.Id, n.Name))).ToList();
            if (settings.NetworkId != "auto" && nets.All(n => n.Id != settings.NetworkId)) nets.Add(new(settings.NetworkId, "Selected adapter · disconnected"));
            if (!gpuSelect.Items.Cast<Choice>().SequenceEqual(gpus)) { gpuSelect.ItemsSource = gpus; gpuSelect.SelectedItem = gpus.First(g => g.Id == settings.GpuId); }
            if (!networkSelect.Items.Cast<Choice>().SequenceEqual(nets)) { networkSelect.ItemsSource = nets; networkSelect.SelectedItem = nets.First(n => n.Id == settings.NetworkId); }
        }
        finally { updatingDevices = false; }
    }

    private void Apply()
    {
        main.ApplySettings();
        SyncFromSettings();
    }

    private CheckBox Check(Panel panel, string label, bool value, Action<bool> set)
    {
        var c = new CheckBox { Content = label, IsChecked = value };
        c.Click += (_, _) => { if (!syncingSettings) { set(c.IsChecked == true); Apply(); } };
        panel.Children.Add(c);
        return c;
    }

    private static TextBlock Label(Panel panel, string label, bool space = false) { var text = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, space ? 18 : 0, 0, 7) }; panel.Children.Add(text); return text; }
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Muted, FontSize = 12, LineHeight = 19 };
    private sealed record Choice(string Id, string Label) { public override string ToString() => Label; }
}
public static class Ui
{
    public static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(148, 163, 184));
    public static void DarkTitle(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        int yes = 1; DwmSetWindowAttribute(handle, 20, ref yes, 4);
        int round = 2; DwmSetWindowAttribute(handle, 33, ref round, 4);
        int border = 0x00A26B58; DwmSetWindowAttribute(handle, 34, ref border, 4);
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
