using System;
using System.Collections.Generic;
using System.Globalization;

namespace EdgePulse;
public enum WidgetLayout { MicroStrip, CompactRail, DualRow }
public enum LoadState { Normal, Warning, High }
public sealed record GpuInfo(string Id, string Name, ulong DedicatedBytes, ulong SharedLimitBytes);
public sealed record GpuReading(GpuInfo Info, double? Utilization, double? DedicatedUsed, double? SharedUsed, double? Temperature = null,
    uint? FanRpm = null, double? MemoryClockMhz = null);
public sealed record NetworkReading(string Id, string Name, double ReceiveBytesPerSecond, double SendBytesPerSecond, bool Physical = true);
public sealed record DriveReading(string Id, string Label, bool Removable, long? TotalBytes, long? UsedBytes,
    double? Activity, double? ReadBytesPerSecond, double? WriteBytesPerSecond, string? Status = null)
{
    public double? UsedPercent => TotalBytes > 0 && UsedBytes.HasValue ? UsedBytes.Value * 100.0 / TotalBytes.Value : null;
}
public sealed record Snapshot(DateTimeOffset Timestamp, double? Cpu, ulong TotalMemory, ulong UsedMemory,
    IReadOnlyList<GpuReading> Gpus, IReadOnlyList<NetworkReading> Networks, IReadOnlyList<DriveReading> Drives, string? Error = null,
    double? CpuTemperature = null, string CpuTemperatureStatus = "Waiting for temperature sensors…",
    ulong TotalCommit = 0, ulong UsedCommit = 0)
{
    public double? MemoryPercent => TotalMemory > 0 ? UsedMemory * 100.0 / TotalMemory : null;
    public double? CommitPercent => TotalCommit > 0 ? UsedCommit * 100.0 / TotalCommit : null;
    public static Snapshot Empty => new(DateTimeOffset.Now, null, 0, 0, Array.Empty<GpuReading>(), Array.Empty<NetworkReading>(), Array.Empty<DriveReading>());
}
public static class Display
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    public static string Temperature(double? value, bool fahrenheit = false)
    {
        if (!value.HasValue || !double.IsFinite(value.Value)) return fahrenheit ? "—°F" : "—°C";
        double v = fahrenheit ? value.Value * 9.0 / 5.0 + 32.0 : value.Value;
        return v.ToString("0", Culture) + (fahrenheit ? "°F" : "°C");
    }
    public static string Percent(double? value) => value.HasValue && double.IsFinite(value.Value) ? Math.Clamp(value.Value, 0, 100).ToString("0", Culture) + "%" : "N/A";
    public static string Size(double? bytes)
    {
        if (!bytes.HasValue || !double.IsFinite(bytes.Value) || bytes.Value < 0) return "N/A";
        string[] units = { "B", "KB", "MB", "GB", "TB" }; double v = bytes.Value; int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return v.ToString(i == 0 ? "0" : "0.#", Culture) + " " + units[i];
    }
    public static string Rate(double? bytes, bool bits = false) => !bytes.HasValue ? "N/A" : bits ? BitRate(bytes.Value * 8) : Size(bytes) + "/s";
    private static string BitRate(double bits)
    {
        string[] units = { "b/s", "Kb/s", "Mb/s", "Gb/s" }; int i = 0;
        while (bits >= 1000 && i < units.Length - 1) { bits /= 1000; i++; }
        return bits.ToString("0.#", Culture) + " " + units[i];
    }
    public static string MemoryPair(double? used, double total) => used.HasValue && total > 0 ? (used.Value / 1073741824).ToString("0.#", Culture) + "/" + (total / 1073741824).ToString("0.#", Culture) + " GB" : "N/A";
    public static LoadState EvaluateLoad(double? percent, double? celsius = null)
    {
        if (percent >= 90 || celsius >= 88) return LoadState.High;
        if (percent >= 80 || celsius >= 78) return LoadState.Warning;
        return LoadState.Normal;
    }
}
