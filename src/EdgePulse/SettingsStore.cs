using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace EdgePulse;
public sealed class AppSettings
{
    public WidgetLayout Layout { get; set; } = WidgetLayout.CompactRail;
    public double Opacity { get; set; } = .82;
    public int RefreshSeconds { get; set; } = 1;
    public double TextScale { get; set; } = 1;
    public bool AlwaysOnTop { get; set; } = true;
    public bool LockPosition { get; set; }
    [JsonIgnore]
    public bool ClickThrough { get; set; }
    public bool HideInFullscreen { get; set; }
    public bool SnapToEdges { get; set; } = true;
    public bool ShowCpu { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowMemory { get; set; } = true;
    public bool ShowVram { get; set; } = true;
    public bool ShowNetwork { get; set; } = true;
    public bool ShowDrives { get; set; } = true;
    public bool ShowTemperatures { get; set; } = true;
    public bool Fahrenheit { get; set; }
    public bool NetworkInBits { get; set; }
    public string GpuId { get; set; } = "auto";
    public string NetworkId { get; set; } = "auto";
    public string? MonitorName { get; set; }
    public double OffsetX { get; set; } = 12;
    public double OffsetY { get; set; } = 12;
    public string HorizontalAnchor { get; set; } = "right";
    public string VerticalAnchor { get; set; } = "top";
    public void Normalize()
    {
        if (!Enum.IsDefined(Layout)) Layout = WidgetLayout.CompactRail;
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, .45, 1) : .82;
        TextScale = double.IsFinite(TextScale) ? Math.Clamp(TextScale, .9, 1.5) : 1;
        RefreshSeconds = RefreshSeconds is 1 or 2 or 5 ? RefreshSeconds : 1;
        OffsetX = double.IsFinite(OffsetX) ? Math.Max(0, OffsetX) : 12;
        OffsetY = double.IsFinite(OffsetY) ? Math.Max(0, OffsetY) : 12;
        if (HorizontalAnchor is not ("left" or "right")) HorizontalAnchor = "left";
        if (VerticalAnchor is not ("top" or "bottom")) VerticalAnchor = "top";
        if (!ShowCpu && !ShowGpu && !ShowMemory && !ShowVram && !ShowNetwork) ShowCpu = true;
        GpuId ??= "auto"; NetworkId ??= "auto";
    }
}
public sealed class SettingsStore
{
    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public bool Portable { get; }
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public SettingsStore(string? overrideDirectory = null)
    {
        Portable = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag"));
        DirectoryPath = overrideDirectory ?? (Portable ? Path.Combine(AppContext.BaseDirectory, "Data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EdgePulse"));
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            string probe = Path.Combine(DirectoryPath, ".write-test-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        }
        catch { DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EdgePulse"); Directory.CreateDirectory(DirectoryPath); }
    }
    public AppSettings Load()
    {
        try { var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new(); s.ClickThrough = false; s.Normalize(); return s; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return new(); }
    }
    public void Save(AppSettings settings)
    {
        settings.Normalize(); var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Json)); File.Move(tmp, FilePath, true);
    }
    public void Log(Exception e)
    {
        try { var file = Path.Combine(DirectoryPath, "error.log"); if (File.Exists(file) && new FileInfo(file).Length > 1024 * 1024) File.Move(file, file + ".previous", true); File.AppendAllText(file, DateTimeOffset.Now + " " + e + Environment.NewLine); }
        catch { }
    }
}
public static class StartupSetting
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "EdgePulse";
    public static bool Enabled { get { using var key = Registry.CurrentUser.OpenSubKey(RunPath); return key?.GetValue(Name) is string value && value.Contains("EdgePulse.exe", StringComparison.OrdinalIgnoreCase); } }
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunPath);
        if (enabled) key.SetValue(Name, "\"" + Environment.ProcessPath + "\" --startup", RegistryValueKind.String);
        else key.DeleteValue(Name, false);
    }
}
