using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace EdgePulse.Native;
public sealed class PdhQuery : IDisposable
{
    private IntPtr query;
    private readonly Dictionary<string, IntPtr> counters = new();
    private const uint MoreData = 0x800007D2;
    private const uint Format = 0x00000200 | 0x00008000;
    public PdhQuery() { if (PdhOpenQueryW(null, UIntPtr.Zero, out query) != 0) query = IntPtr.Zero; }
    public bool Add(string key, string path)
    {
        if (query == IntPtr.Zero) return false; if (counters.ContainsKey(key)) return true;
        if (PdhAddEnglishCounterW(query, path, UIntPtr.Zero, out var handle) != 0) return false;
        counters[key] = handle; return true;
    }
    public bool Collect() => query != IntPtr.Zero && PdhCollectQueryData(query) == 0;
    public double? Value(string key)
    {
        if (!counters.TryGetValue(key, out var handle)) return null;
        return PdhGetFormattedCounterValue(handle, Format, out _, out var value) == 0 && value.Status <= 1 && double.IsFinite(value.Value) ? value.Value : null;
    }
    public Dictionary<string, double> Values(string key)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (!counters.TryGetValue(key, out var handle)) return result;
        uint bytes = 0, count = 0;
        var status = PdhGetFormattedCounterArrayW(handle, Format, ref bytes, ref count, IntPtr.Zero);
        if (status != MoreData || bytes == 0 || bytes > 64 * 1024 * 1024) return result;
        for (int retry = 0; retry < 3; retry++)
        {
            var buffer = Marshal.AllocHGlobal((int)bytes);
            try
            {
                status = PdhGetFormattedCounterArrayW(handle, Format, ref bytes, ref count, buffer);
                if (status == MoreData) continue; if (status != 0) return result;
                int itemSize = Marshal.SizeOf<CounterItem>();
                for (int i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<CounterItem>(IntPtr.Add(buffer, i * itemSize)); var name = Marshal.PtrToStringUni(item.Name);
                    if (name != null && item.Value.Status <= 1 && double.IsFinite(item.Value.Value)) result[name] = item.Value.Value;
                }
                return result;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return result;
    }
    public void Dispose() { if (query != IntPtr.Zero) { PdhCloseQuery(query); query = IntPtr.Zero; counters.Clear(); } }
    [StructLayout(LayoutKind.Explicit, Size = 16)] private struct CounterValue { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Value; }
    [StructLayout(LayoutKind.Sequential)] private struct CounterItem { public IntPtr Name; public CounterValue Value; }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, UIntPtr userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, UIntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bytes, ref uint count, IntPtr buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
}
