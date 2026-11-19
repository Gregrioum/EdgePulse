using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace EdgePulse;
public static class Verification
{
    public static async Task Run(MainWindow window, string directory)
    {
        var checks = new List<object>(); var failures = new List<string>();
        void Check(string name, bool pass, string? detail = null) { checks.Add(new { name, pass, detail }); if (!pass) failures.Add(name); }
        Directory.CreateDirectory(directory);
        try
        {
            await Task.Delay(2500);
            var live = window.Latest;
            Check("Live CPU sample", live.Cpu is >= 0 and <= 100, Display.Percent(live.Cpu));
            Check("Live physical memory", live.TotalMemory > 0 && live.UsedMemory <= live.TotalMemory, Display.MemoryPair(live.UsedMemory, live.TotalMemory));
            Check("Live commit charge", live.TotalCommit >= live.TotalMemory && live.UsedCommit > 0, Display.MemoryPair(live.UsedCommit, live.TotalCommit));
            Check("Local volume discovery", live.Drives.Any(d => d.TotalBytes > 0), string.Join(", ", live.Drives.Select(d => d.Id)));
            Check("Monotonic network rates", live.Networks.All(n => n.ReceiveBytesPerSecond >= 0 && n.SendBytesPerSecond >= 0));
            const string adapter = "luid_0x00000000_0x00000001";
            var engines = new Dictionary<string, double> {
                ["pid_1_" + adapter + "_phys_0_eng_0_engtype_3D"] = 40,
                ["pid_2_" + adapter + "_phys_0_eng_0_engtype_3D"] = 35,
                ["pid_1_" + adapter + "_phys_0_eng_1_engtype_Copy"] = 30,
                ["pid_3_luid_0x00000000_0x00000002_phys_0_eng_0_engtype_3D"] = 100
            };
            Check("GPU aggregates processes per engine, isolates adapter", TelemetryService.AggregateGpuUsage(engines, adapter) == 75);
            Check("Missing GPU counter remains unavailable", TelemetryService.AggregateGpuUsage(new Dictionary<string, double>(), adapter) == null);
            Check("GPU temperature deci-Celsius conversion and missing sentinels", Native.GpuTemperature.FromDeciCelsius(447) == 44.7 && Native.GpuTemperature.FromDeciCelsius(0) == null && Native.GpuTemperature.FromDeciCelsius(uint.MaxValue) == null);
            Check("CPU uses package over individual cores", CpuTemperatureSensor.Select(new (string, double?)[] { ("CPU Package", 61), ("CPU Core #1", 68), ("Core Max", 68) }) == 61);
            Check("CPU uses Tdie before control temperature", CpuTemperatureSensor.Select(new (string, double?)[] { ("Core (Tctl)", 85), ("Core (Tdie)", 65) }) == 65);
            Check("CPU rejects invalid and distance-to-limit sensors", CpuTemperatureSensor.Select(new (string, double?)[] { ("CPU Package", double.NaN), ("CPU Core #1", 0), ("CPU Core #1 Distance to TjMax", 60) }) == null);
            Check("CPU core fallback and missing temperature formatting", CpuTemperatureSensor.Select(new (string, double?)[] { ("CPU Core #1", 58), ("CPU Core #2", 62) }) == 62 && Display.Temperature(null) == "—°C" && Display.Temperature(100, true) == "212°F");
            Check("Load state thresholds evaluate Normal, Warning, and High", Display.EvaluateLoad(50, 55) == LoadState.Normal && Display.EvaluateLoad(82, 60) == LoadState.Warning && Display.EvaluateLoad(65, 90) == LoadState.High);
            var store = new SettingsStore(Path.Combine(directory, "roundtrip"));
            store.Save(new AppSettings { Layout = WidgetLayout.DualRow, Opacity = .65, LockPosition = true, ClickThrough = true, HideInFullscreen = true, Fahrenheit = true, GpuId = "test", HorizontalAnchor = "left", OffsetX = 40 });
            var restored = store.Load();
            Check("Settings survive restart", restored.Layout == WidgetLayout.DualRow && restored.Opacity == .65 && restored.LockPosition && restored.ClickThrough && restored.HideInFullscreen && restored.Fahrenheit && restored.GpuId == "test" && restored.OffsetX == 40);
            File.WriteAllText(store.FilePath, "{invalid-json"); Check("Corrupt settings recover safely", store.Load().Layout == WidgetLayout.CompactRail);

            // Normal load renders
            var normalFixture = live with { Cpu = 28, CpuTemperature = 54, Gpus = live.Gpus.Select(g => g with { Utilization = 36, Temperature = 51 }).ToArray() };
            foreach (var layout in Enum.GetValues<WidgetLayout>())
            {
                window.Settings.Layout = layout; window.ApplySettings(); window.SetSnapshot(normalFixture); window.UpdateLayout();
                Capture(window.Surface, Path.Combine(directory, MainWindow.LayoutName(layout).Replace(' ', '-') + ".png"));
                Check(layout + " compact height", window.Surface.Height == (layout == WidgetLayout.MicroStrip ? 34 : layout == WidgetLayout.CompactRail ? 50 : 64), window.Surface.Width + " × " + window.Surface.Height);
            }

            // High-load / thermal alert fixture across layouts and scales
            var fixture = live with { Cpu = 100, CpuTemperature = 100, Gpus = live.Gpus.Select(g => g with { Utilization = 100, Temperature = 100 }).ToArray(), Networks = new[] { new NetworkReading("fixture", "Fixture", 1023 * 1024, 99.9 * 1024) } };
            foreach (var layout in Enum.GetValues<WidgetLayout>())
            {
                window.Settings.Layout = layout;
                foreach (double scale in new[] { 1.0, 1.25, 1.5 })
                {
                    window.Settings.TextScale = scale; window.SetSnapshot(fixture); window.UpdateLayout();
                    Check(layout + " temperatures fit at " + scale, window.Surface.Width <= 1100 && window.Surface.Height <= 64 * scale);
                    Capture(window.Surface, Path.Combine(directory, layout + "-temperature-fixture-" + scale + ".png"));
                    Check(layout + " temperature text has no clipping or overlap at " + scale, window.Surface.TextFits);
                }
                window.Settings.TextScale = 1;
            }
            var connected = live.Drives.Concat(Enumerable.Range(0, 8).Select(i => new DriveReading(((char)('M' + i)) + ":", "External test " + i, true, 1000000000000, 126000000000, 21, 72000000, 8400000))).ToArray();
            foreach (var layout in Enum.GetValues<WidgetLayout>())
            {
                window.Settings.Layout = layout; window.SetSnapshot(live with { Drives = connected }); window.UpdateLayout();
                Check(layout + " external-drive overflow keeps height", window.Surface.Width <= 1100 && window.Surface.Height <= 64);
                Capture(window.Surface, Path.Combine(directory, MainWindow.LayoutName(layout).Replace(' ', '-') + "-many-drives.png"));
                Check(layout + " overflow text fits", window.Surface.TextFits);
                window.SetSnapshot(live); window.UpdateLayout(); Check(layout + " drive removal", window.Latest.Drives.Count == live.Drives.Count);
            }
            window.Settings.Layout = WidgetLayout.CompactRail; window.Settings.Opacity = .82; window.ApplySettings(); window.SetSnapshot(live);
            var settingsWindow = new SettingsWindow(window, true) { ShowActivated = false };
            settingsWindow.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); settingsWindow.UpdateLayout();
            var layoutSelector = Find<ComboBox>(settingsWindow, c => c.Name == "LayoutSelector");
            Check("Settings layout control exists", layoutSelector != null);
            if (layoutSelector != null) { layoutSelector.SelectedIndex = 0; Check("Settings switches live layout", window.Settings.Layout == WidgetLayout.MicroStrip && window.Surface.Height == 34); layoutSelector.SelectedIndex = 1; }
            Capture(settingsWindow, Path.Combine(directory, "Settings.png"));
            var tabs = Find<TabControl>(settingsWindow, _ => true);
            if (tabs != null) { tabs.SelectedIndex = 1; settingsWindow.UpdateLayout(); Capture(settingsWindow, Path.Combine(directory, "Settings-Devices.png")); }
            if (tabs != null) { tabs.SelectedIndex = 2; settingsWindow.UpdateLayout(); Capture(settingsWindow, Path.Combine(directory, "Settings-Temperatures.png")); }
            settingsWindow.Close();
            foreach (var (name, menu) in new[] { ("Widget-menu", window.CreateWidgetMenu()), ("Drives-menu", window.CreateDriveMenu()) })
            {
                menu.Measure(new Size(700, 1000)); menu.Arrange(new Rect(menu.DesiredSize)); menu.UpdateLayout();
                var rendered = Capture(menu, Path.Combine(directory, name + ".png"));
                byte[] pixels = new byte[rendered.PixelWidth * rendered.PixelHeight * 4]; rendered.CopyPixels(pixels, rendered.PixelWidth * 4, 0);
                int dark = 0, opaque = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                    if (pixels[i + 3] > 240) { opaque++; if (pixels[i] < 100 && pixels[i + 1] < 100 && pixels[i + 2] < 100) dark++; }
                Check(name + " renders dark background", opaque > 0 && dark > opaque * .8, $"{dark}/{opaque} opaque pixels are dark");
                Check(name + " renders readable menu labels", menu.Items.OfType<MenuItem>().All(item => Find<ContentPresenter>(item, p => p.Content == item.Header && p.ActualWidth > 0) != null));
                if (name == "Widget-menu")
                {
                    var selected = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, MainWindow.LayoutName(window.Settings.Layout)));
                    var mark = selected.Template.FindName("Check", selected) as FrameworkElement;
                    Check("Menu selected layout checkmark is visible", selected.IsChecked && mark?.Visibility == Visibility.Visible);
                    var other = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Dual Row"));
                    other.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Check("Menu layout command updates widget", window.Settings.Layout == WidgetLayout.DualRow);
                }
            }
            File.WriteAllText(Path.Combine(directory, "verification.json"), JsonSerializer.Serialize(new { checkedAt = DateTimeOffset.Now, architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), checks, failures, snapshot = live }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { failures.Add(e.ToString()); File.WriteAllText(Path.Combine(directory, "failure.txt"), e.ToString()); }
        finally { Application.Current.Shutdown(failures.Count == 0 ? 0 : 1); }
    }
    private static T? Find<T>(DependencyObject root, Func<T, bool> matches) where T : DependencyObject
    {
        if (root is T value && matches(value)) return value;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var found = Find(VisualTreeHelper.GetChild(root, i), matches); if (found != null) return found; }
        return null;
    }
    private static RenderTargetBitmap Capture(FrameworkElement element, string file)
    {
        element.UpdateLayout();
        int width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth)), height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(file); png.Save(stream);
        return bitmap;
    }
}
