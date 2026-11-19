using System;
using System.Linq;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

namespace EdgePulse;
public sealed class CpuTemperatureSensor : IDisposable
{
    private Computer? computer;
    private DateTime nextOpen;
    public string Status { get; private set; } = "Waiting for CPU sensor…";
    public static bool Supported => RuntimeInformation.ProcessArchitecture == Architecture.X64;
    public static bool DriverInstalled
    {
        get
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hive.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
            return key != null;
        }
    }
    public double? Read()
    {
        if (!Supported) { Status = "CPU temperature is unavailable on this ARM64 device."; return null; }
        try
        {
            if (computer == null)
            {
                if (!DriverInstalled) { Status = "CPU temperature needs the PawnIO sensor driver. Open Settings → Temperatures to set it up."; return null; }
                if (DateTime.UtcNow < nextOpen) return null;
                nextOpen = DateTime.UtcNow.AddSeconds(30);
                computer = new Computer { IsCpuEnabled = true };
                computer.Open();
            }
            var readings = computer.Hardware.Where(h => h.HardwareType == HardwareType.Cpu).Select(h =>
            {
                h.Update();
                return Select(h.Sensors.Where(s => s.SensorType == SensorType.Temperature)
                    .Select(s => (s.Name, (double?)s.Value)).ToArray());
            }).Where(v => v.HasValue).ToArray();
            if (readings.Length == 0) { Status = "CPU sensor unavailable. Check PawnIO, then try running EdgePulse as administrator if access is restricted."; return null; }
            Status = "CPU package / Tdie temperature (hottest CPU if more than one). Core maximum is used when package temperature is unavailable.";
            return readings.Max();
        }
        catch (Exception)
        {
            Dispose(); Status = "CPU sensor could not be read. Check PawnIO and try Rescan sensors."; return null;
        }
    }
    public static double? Select((string Name, double? Value)[] sensors)
    {
        var valid = sensors.Where(s => s.Value.HasValue && double.IsFinite(s.Value.Value) && s.Value is > 0 and <= 150
            && !s.Name.Contains("Distance", StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (string name in new[] { "CPU Package", "Core (Tdie)", "Core (Tctl/Tdie)", "CPU (Tctl/Tdie)", "Core Max" })
        {
            var matches = valid.Where(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length > 0) return matches.Max(s => s.Value);
        }
        var cores = valid.Where(s => s.Name.StartsWith("CPU Core", StringComparison.OrdinalIgnoreCase)).ToArray();
        return cores.Length > 0 ? cores.Max(s => s.Value) : null;
    }
    public void Reset() { Dispose(); nextOpen = DateTime.MinValue; }
    public void Dispose() { try { computer?.Close(); } catch { } finally { computer = null; } }
}
