using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace EdgePulse.Native;
public static class GpuAdapters
{
    public static IReadOnlyList<GpuInfo> Enumerate()
    {
        var result = new List<GpuInfo>(); var iid = new Guid("770AAE78-F26F-4DBA-A829-253C83D1B387");
        if (CreateDXGIFactory1(ref iid, out var factory) < 0) return result;
        try
        {
            var enumerate = GetMethod<EnumAdapters>(factory, 12);
            for (uint i = 0; i < 32; i++)
            {
                if (enumerate(factory, i, out var adapter) < 0) break;
                try
                {
                    if (GetMethod<GetDesc>(adapter, 10)(adapter, out var desc) < 0 || (desc.Flags & 2) != 0) continue;
                    var id = $"luid_0x{desc.High:x8}_0x{desc.Low:x8}";
                    result.Add(new(id, desc.Description.TrimEnd('\0'), (ulong)desc.DedicatedVideoMemory + (ulong)desc.DedicatedSystemMemory, (ulong)desc.SharedSystemMemory));
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Marshal.Release(factory); }
        return result;
    }
    private static T GetMethod<T>(IntPtr obj, int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size));
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Desc
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision; public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint Low; public int High; public uint Flags;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapters(IntPtr self, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDesc(IntPtr self, out Desc desc);
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
}
