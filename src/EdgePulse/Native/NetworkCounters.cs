using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace EdgePulse.Native;
public sealed record NetworkCounter(string Id, string Name, ulong Received, ulong Sent, bool Physical);
public static class NetworkCounters
{
    private static readonly int TableFirstOffset = Marshal.OffsetOf<Table>(nameof(Table.First)).ToInt32();
    private static readonly int RowSize = Marshal.SizeOf<Row>();
    private static readonly int TypeOffset = Marshal.OffsetOf<Row>(nameof(Row.Type)).ToInt32();
    private static readonly int FlagsOffset = Marshal.OffsetOf<Row>(nameof(Row.Flags)).ToInt32();
    private static readonly int OperStatusOffset = Marshal.OffsetOf<Row>(nameof(Row.OperStatus)).ToInt32();

    public static IReadOnlyList<NetworkCounter> Read()
    {
        var result = new List<NetworkCounter>();
        if (GetIfTable2(out var table) != 0) return result;
        try
        {
            int count = Marshal.ReadInt32(table);
            if (count < 0 || count > 100000) return result;
            for (int i = 0; i < count; i++)
            {
                IntPtr rowPtr = IntPtr.Add(table, TableFirstOffset + i * RowSize);
                // Fast pre-filter before marshaling strings and byte arrays.
                int operStatus = Marshal.ReadInt32(rowPtr, OperStatusOffset);
                int type = Marshal.ReadInt32(rowPtr, TypeOffset);
                byte flags = Marshal.ReadByte(rowPtr, FlagsOffset);
                if (operStatus != 1 || type == 24 || (flags & 2) != 0 || (flags & 128) != 0) continue;
                var row = Marshal.PtrToStructure<Row>(rowPtr);
                result.Add(new(row.InterfaceGuid.ToString("B"), row.Alias, row.InOctets, row.OutOctets, (flags & 1) != 0 && (flags & 4) != 0));
            }
        }
        finally { FreeMibTable(table); }
        return result;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Row
    {
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Alias;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Description;
        public uint PhysicalAddressLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PhysicalAddress;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PermanentPhysicalAddress;
        public uint Mtu, Type, TunnelType, MediaType, PhysicalMediumType, AccessType, DirectionType;
        public byte Flags;
        public uint OperStatus, AdminStatus, MediaConnectState;
        public Guid NetworkGuid;
        public uint ConnectionType;
        public ulong TransmitLinkSpeed, ReceiveLinkSpeed, InOctets, InUcastPkts, InNUcastPkts, InDiscards, InErrors, InUnknownProtos, InUcastOctets, InMulticastOctets, InBroadcastOctets;
        public ulong OutOctets, OutUcastPkts, OutNUcastPkts, OutDiscards, OutErrors, OutUcastOctets, OutMulticastOctets, OutBroadcastOctets, OutQLen;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Table { public uint Count; public Row First; }
    [DllImport("iphlpapi.dll")] private static extern uint GetIfTable2(out IntPtr table);
    [DllImport("iphlpapi.dll")] private static extern void FreeMibTable(IntPtr table);
}
