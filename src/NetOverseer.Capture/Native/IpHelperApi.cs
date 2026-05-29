// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Runtime.InteropServices;
using System.Net;

namespace NetOverseer.Capture.Native;

/// <summary>
/// P/Invoke-Signaturen und Strukturen für die Windows IP Helper API.
/// GetExtendedTcpTable / GetExtendedUdpTable liefern den aktuellen Verbindungsstatus
/// inklusive Prozess-ID (PID) ohne Kernel-Treiber.
/// </summary>
internal static class IpHelperApi
{
    private const string IpHlpApi = "iphlpapi.dll";

    // TCP-Zustände gemäß MIB_TCP_STATE
    internal enum MibTcpState : uint
    {
        Closed = 1,
        Listen = 2,
        SynSent = 3,
        SynRcvd = 4,
        Established = 5,
        FinWait1 = 6,
        FinWait2 = 7,
        CloseWait = 8,
        Closing = 9,
        LastAck = 10,
        TimeWait = 11,
        DeleteTcb = 12
    }

    internal enum TcpTableClass : int
    {
        TcpTableOwnerPidAll = 5,
    }

    internal enum UdpTableClass : int
    {
        UdpTableOwnerPid = 1,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MibTcpRowOwnerPid
    {
        public MibTcpState State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MibUdpRowOwnerPid
    {
        public uint LocalAddr;
        public uint LocalPort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MibTcp6RowOwnerPid
    {
        // Inline-Bytes für IPv6-Adressen (16 Byte) + ScopeId (4 Byte) gemäß MIB_TCP6ROW_OWNER_PID.
        public byte LocalAddr0; public byte LocalAddr1; public byte LocalAddr2; public byte LocalAddr3;
        public byte LocalAddr4; public byte LocalAddr5; public byte LocalAddr6; public byte LocalAddr7;
        public byte LocalAddr8; public byte LocalAddr9; public byte LocalAddrA; public byte LocalAddrB;
        public byte LocalAddrC; public byte LocalAddrD; public byte LocalAddrE; public byte LocalAddrF;
        public uint LocalScopeId;
        public uint LocalPort;
        public byte RemoteAddr0; public byte RemoteAddr1; public byte RemoteAddr2; public byte RemoteAddr3;
        public byte RemoteAddr4; public byte RemoteAddr5; public byte RemoteAddr6; public byte RemoteAddr7;
        public byte RemoteAddr8; public byte RemoteAddr9; public byte RemoteAddrA; public byte RemoteAddrB;
        public byte RemoteAddrC; public byte RemoteAddrD; public byte RemoteAddrE; public byte RemoteAddrF;
        public uint RemoteScopeId;
        public uint RemotePort;
        public MibTcpState State;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MibUdp6RowOwnerPid
    {
        public byte LocalAddr0; public byte LocalAddr1; public byte LocalAddr2; public byte LocalAddr3;
        public byte LocalAddr4; public byte LocalAddr5; public byte LocalAddr6; public byte LocalAddr7;
        public byte LocalAddr8; public byte LocalAddr9; public byte LocalAddrA; public byte LocalAddrB;
        public byte LocalAddrC; public byte LocalAddrD; public byte LocalAddrE; public byte LocalAddrF;
        public uint LocalScopeId;
        public uint LocalPort;
        public uint OwningPid;
    }

    [DllImport(IpHlpApi, SetLastError = true)]
    internal static extern uint GetExtendedTcpTable(
        nint pTcpTable,
        ref uint dwSize,
        [MarshalAs(UnmanagedType.Bool)] bool bOrder,
        uint ulAf,
        TcpTableClass tableClass,
        uint reserved);

    [DllImport(IpHlpApi, SetLastError = true)]
    internal static extern uint GetExtendedUdpTable(
        nint pUdpTable,
        ref uint dwSize,
        [MarshalAs(UnmanagedType.Bool)] bool bOrder,
        uint ulAf,
        UdpTableClass tableClass,
        uint reserved);

    internal const uint AfInet = 2;    // IPv4 (AF_INET)
    internal const uint AfInet6 = 23;  // IPv6 (AF_INET6)
    internal const uint ErrorInsufficientBuffer = 122;
    internal const uint NoError = 0;

    /// <summary>
    /// Konvertiert einen 16-bit-Netzwerk-Port (Big-Endian) in einen Host-Port.
    /// </summary>
    internal static int NetworkToHostPort(uint networkPort) =>
        (int)(((networkPort & 0xFF) << 8) | ((networkPort >> 8) & 0xFF));

    /// <summary>
    /// Konvertiert eine 32-bit IP-Adresse (Little-Endian) in ein IPAddress-Objekt.
    /// </summary>
    internal static IPAddress ToIpAddress(uint addr)
    {
        Span<byte> bytes = stackalloc byte[4];
        bytes[0] = (byte)(addr & 0xFF);
        bytes[1] = (byte)((addr >> 8) & 0xFF);
        bytes[2] = (byte)((addr >> 16) & 0xFF);
        bytes[3] = (byte)((addr >> 24) & 0xFF);
        return new IPAddress(bytes);
    }

    /// <summary>
    /// Liest 16 zusammenhängende Bytes als IPv6-Adresse (Network-Byte-Order).
    /// </summary>
    internal static unsafe IPAddress ToIpV6Address(ref byte firstByte, uint scopeId)
    {
        ReadOnlySpan<byte> span = new(System.Runtime.CompilerServices.Unsafe.AsPointer(ref firstByte), 16);
        return new IPAddress(span, scopeId);
    }
}
