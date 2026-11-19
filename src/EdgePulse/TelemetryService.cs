using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EdgePulse.Native;

namespace EdgePulse;
public sealed class TelemetryService : IDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly SettingsStore store;
    private Task? worker;
    private int interval = 1;
    private volatile bool refreshDevices;
    public event Action<Snapshot>? Updated;
    public Snapshot Latest { get; private set; } = Snapshot.Empty;
    public int IntervalSeconds { get => interval; set => interval = Math.Clamp(value, 1, 5); }
    public TelemetryService(SettingsStore store) { this.store = store; }
    public void Start() => worker ??= Task.Run(Run);
    public void RefreshDevices() => refreshDevices = true;
    private async Task Run()
    {
        using var pdh = new PdhQuery();
        using var cpuTemperature = new CpuTemperatureSensor();
        void AddCounters()
        {
            pdh.Add("cpu", @"\Processor Information(_Total)\% Processor Utility");
            pdh.Add("cpu-time", @"\Processor(_Total)\% Processor Time");
            pdh.Add("engines", @"\GPU Engine(*)\Utilization Percentage");
            pdh.Add("vram", @"\GPU Adapter Memory(*)\Dedicated Usage");
            pdh.Add("shared", @"\GPU Adapter Memory(*)\Shared Usage");
            pdh.Add("disk-idle", @"\LogicalDisk(*)\% Idle Time");
            pdh.Add("disk-read", @"\LogicalDisk(*)\Disk Read Bytes/sec");
            pdh.Add("disk-write", @"\LogicalDisk(*)\Disk Write Bytes/sec");
        }
        var clock = Stopwatch.StartNew();
        var previousNetwork = new Dictionary<string, (ulong Rx, ulong Tx, double Time)>();
        IReadOnlyList<GpuInfo> adapters = Array.Empty<GpuInfo>();
        IReadOnlyList<DriveReading> drives = Array.Empty<DriveReading>();
        Task<IReadOnlyList<DriveReading>>? driveScan = null;
        double lastDriveScan = -100, lastAdapterScan = -100;

        try
        {
            AddCounters();
            pdh.Collect();
            double t0 = clock.Elapsed.TotalSeconds;
            foreach (var nic in NetworkCounters.Read())
                previousNetwork[nic.Id] = (nic.Received, nic.Sent, t0);
            try { adapters = GpuAdapters.Enumerate(); lastAdapterScan = t0; } catch (Exception e) { store.Log(e); }
            try { drives = ScanDrives(); lastDriveScan = t0; } catch (Exception e) { store.Log(e); }
            await Task.Delay(250, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e) { store.Log(e); }

        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                double now = clock.Elapsed.TotalSeconds;
                bool changed = refreshDevices; refreshDevices = false;
                if (changed) cpuTemperature.Reset();
                if (now - lastAdapterScan >= 30 || changed)
                {
                    try { adapters = GpuAdapters.Enumerate(); } catch (Exception e) { store.Log(e); }
                    lastAdapterScan = now; AddCounters();
                }
                if (driveScan?.IsCompleted == true)
                {
                    try { drives = await driveScan.ConfigureAwait(false); } catch (Exception e) { store.Log(e); }
                    driveScan = null;
                }
                if (driveScan == null && (now - lastDriveScan >= 3 || changed))
                {
                    lastDriveScan = now; driveScan = Task.Run(ScanDrives);
                }
                bool collected = pdh.Collect();
                double? cpu = collected ? pdh.Value("cpu") ?? pdh.Value("cpu-time") : null;
                if (cpu.HasValue) cpu = Math.Clamp(cpu.Value, 0, 100);
                var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
                bool hasMemory = GlobalMemoryStatusEx(ref memory);
                var engines = collected ? pdh.Values("engines") : new();
                var dedicated = collected ? pdh.Values("vram") : new();
                var shared = collected ? pdh.Values("shared") : new();
                var gpuReadings = adapters.Select(a =>
                {
                    var perf = GpuTemperature.ReadPerf(a.Id);
                    return new GpuReading(a, AggregateGpuUsage(engines, a.Id), SumMemory(dedicated, a.Id), SumMemory(shared, a.Id), perf.Temperature, perf.FanRpm, perf.MemoryClockMhz);
                }).ToArray();
                var idle = collected ? pdh.Values("disk-idle") : new();
                var reads = collected ? pdh.Values("disk-read") : new();
                var writes = collected ? pdh.Values("disk-write") : new();
                var driveReadings = drives.Select(d => d with
                {
                    Activity = d.TotalBytes.HasValue && idle.TryGetValue(d.Id, out double idleValue) ? Math.Clamp(100 - idleValue, 0, 100) : null,
                    ReadBytesPerSecond = d.TotalBytes.HasValue && reads.TryGetValue(d.Id, out double readValue) ? Math.Max(0, readValue) : null,
                    WriteBytesPerSecond = d.TotalBytes.HasValue && writes.TryGetValue(d.Id, out double writeValue) ? Math.Max(0, writeValue) : null
                }).ToArray();
                var networks = new List<NetworkReading>();
                var seen = new HashSet<string>();
                foreach (var nic in NetworkCounters.Read())
                {
                    seen.Add(nic.Id);
                    if (previousNetwork.TryGetValue(nic.Id, out var prev) && now > prev.Time)
                    {
                        double seconds = now - prev.Time;
                        networks.Add(new(nic.Id, nic.Name, nic.Received >= prev.Rx ? (nic.Received - prev.Rx) / seconds : 0, nic.Sent >= prev.Tx ? (nic.Sent - prev.Tx) / seconds : 0, nic.Physical));
                    }
                    previousNetwork[nic.Id] = (nic.Received, nic.Sent, now);
                }
                foreach (var key in previousNetwork.Keys.Where(k => !seen.Contains(k)).ToArray()) previousNetwork.Remove(key);
                double? cpuCelsius = cpuTemperature.Read();
                ulong totalMem = hasMemory ? memory.TotalPhysical : 0;
                ulong usedMem = hasMemory && memory.TotalPhysical >= memory.AvailablePhysical ? memory.TotalPhysical - memory.AvailablePhysical : 0;
                ulong totalCommit = hasMemory ? memory.TotalPageFile : 0;
                ulong usedCommit = hasMemory && memory.TotalPageFile >= memory.AvailablePageFile ? memory.TotalPageFile - memory.AvailablePageFile : 0;
                Latest = new(DateTimeOffset.Now, cpu, totalMem, usedMem, gpuReadings, networks, driveReadings,
                    CpuTemperature: cpuCelsius, CpuTemperatureStatus: cpuTemperature.Status,
                    TotalCommit: totalCommit, UsedCommit: usedCommit);
                Updated?.Invoke(Latest);
            }
            catch (Exception e)
            {
                store.Log(e); Latest = Snapshot.Empty with { Error = "Performance data unavailable. Retrying…" }; Updated?.Invoke(Latest);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(interval), cancellation.Token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
        }
    }
    public static double? AggregateGpuUsage(IReadOnlyDictionary<string, double> values, string adapterId)
    {
        // Sum processes per physical engine, then use the busiest engine, as Task Manager does.
        var engines = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in values)
        {
            int adapter = name.IndexOf(adapterId + "_", StringComparison.OrdinalIgnoreCase);
            if (adapter < 0) continue;
            string engine = name[(adapter + adapterId.Length)..];
            engines[engine] = engines.GetValueOrDefault(engine) + Math.Max(0, value);
        }
        return engines.Count > 0 ? Math.Clamp(engines.Values.Max(), 0, 100) : null;
    }
    private static double? SumMemory(Dictionary<string, double> values, string adapterId)
    {
        var matches = values.Where(p => p.Key.StartsWith(adapterId + "_", StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 0 ? null : matches.Sum(p => Math.Max(0, p.Value));
    }
    private static IReadOnlyList<DriveReading> ScanDrives()
    {
        var result = new List<DriveReading>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.CDRom)) continue;
                string id = drive.Name.TrimEnd('\\');
                if (!drive.IsReady) { if (drive.DriveType != DriveType.CDRom) result.Add(new(id, "Not ready", drive.DriveType == DriveType.Removable, null, null, null, null, null, "Not ready or locked")); continue; }
                long total = drive.TotalSize, used = Math.Max(0, total - drive.TotalFreeSpace);
                result.Add(new(id, string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local disk" : drive.VolumeLabel, drive.DriveType == DriveType.Removable, total, used, null, null, null));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return result.OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public void Dispose() { cancellation.Cancel(); /* Worker owns and releases native counters after the pending sample. */ }
    public Task Completion => worker ?? Task.CompletedTask;
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
