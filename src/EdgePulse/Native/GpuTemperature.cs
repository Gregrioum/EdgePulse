using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace EdgePulse.Native;
// Query the same WDDM adapter identified by DXGI; no vendor-specific GPU matching.
public static class GpuTemperature
{
    public static double? Read(string adapterId) => ReadPerf(adapterId).Temperature;
    public static unsafe (double? Temperature, uint? FanRpm, double? MemoryClockMhz) ReadPerf(string adapterId)
    {
        var parts = adapterId.Split('_');
        if (parts.Length != 3 || !uint.TryParse(parts[1].AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint high)
            || !uint.TryParse(parts[2].AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint low)) return (null, null, null);
        var adapter = new OpenAdapter { Low = low, High = unchecked((int)high) };
        if (D3DKMTOpenAdapterFromLuid(ref adapter) != 0) return (null, null, null);
        try
        {
            var perf = new PerformanceData();
            var query = new Query { Adapter = adapter.Handle, Type = 62, Data = (IntPtr)(&perf), Size = (uint)sizeof(PerformanceData) };
            if (D3DKMTQueryAdapterInfo(ref query) != 0) return (null, null, null);
            double? temp = FromDeciCelsius(perf.Temperature);
            uint? fan = perf.FanRPM is > 0 and <= 15000 ? perf.FanRPM : null;
            double? memMhz = perf.MemoryFrequency is > 1_000_000 and <= 50_000_000_000UL ? perf.MemoryFrequency / 1_000_000.0 : null;
            return (temp, fan, memMhz);
        }
        finally { uint handle = adapter.Handle; D3DKMTCloseAdapter(ref handle); }
    }
    // Zero is the common unsupported sentinel. Do not turn it into a 0°C reading.
    public static double? FromDeciCelsius(uint value) => value is > 0 and <= 1500 ? value / 10.0 : null;
    [StructLayout(LayoutKind.Sequential)] private struct OpenAdapter { public uint Low; public int High; public uint Handle; }
    [StructLayout(LayoutKind.Sequential)] private struct Query { public uint Adapter, Type; public IntPtr Data; public uint Size; }
    [StructLayout(LayoutKind.Sequential, Pack = 8)] private struct PerformanceData
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency, MaxMemoryFrequency, MaxMemoryFrequencyOC, MemoryBandwidth, PCIEBandwidth;
        public uint FanRPM, Power, Temperature;
        public byte PowerStateOverride;
    }
    [DllImport("gdi32.dll")] private static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapter adapter);
    [DllImport("gdi32.dll")] private static extern int D3DKMTQueryAdapterInfo(ref Query query);
    [DllImport("gdi32.dll")] private static extern int D3DKMTCloseAdapter(ref uint handle);
}
