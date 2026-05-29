// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Runtime.InteropServices;

namespace NetOverseer.Capture.Native;

/// <summary>
/// P/Invoke-Signaturen für die Windows Filtering Platform (WFP) User-Mode API (fwpuclnt.dll).
/// Diese Signaturen ermöglichen das Öffnen der WFP-Engine, Hinzufügen von Filtern
/// und das Blockieren von Verbindungen ohne Kernel-Treiber.
///
/// HINWEIS: Echtes Packet-Capture über WFP erfordert einen Kernel-Mode-Callout-Treiber.
/// Die User-Mode API (FWPM) wird hier für Firewall-Regelmanagement genutzt.
/// Für Verbindungsüberwachung wird IpHelperCapture als primäre Quelle verwendet.
/// </summary>
internal static class WfpApi
{
    private const string FwpUClnt = "fwpuclnt.dll";

    internal static readonly Guid FWPM_LAYER_ALE_FLOW_ESTABLISHED_V4 =
        new("af80470a-5596-4c13-9992-539e6fe57967");

    internal static readonly Guid FWPM_SUBLAYER_UNIVERSAL =
        new("eebecc03-ced4-4380-819a-2734397b2b74");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct FwpmDisplayData0
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Name;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Description;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct FwpmSession0
    {
        public Guid SessionKey;
        public FwpmDisplayData0 DisplayData;
        public uint Flags;
        public uint TxnWaitTimeoutInMsec;
        public uint ProcessId;
        public nint Sid;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Username;
        [MarshalAs(UnmanagedType.Bool)]
        public bool KernelMode;
    }

    [DllImport(FwpUClnt, EntryPoint = "FwpmEngineOpen0", SetLastError = false)]
    internal static extern uint FwpmEngineOpen0(
        [MarshalAs(UnmanagedType.LPWStr)] string? serverName,
        uint authnService,          // RPC_C_AUTHN_DEFAULT = 0xFFFFFFFF
        nint authIdentity,          // null = current user
        in FwpmSession0 session,
        out nint engineHandle);

    [DllImport(FwpUClnt, EntryPoint = "FwpmEngineClose0", SetLastError = false)]
    internal static extern uint FwpmEngineClose0(nint engineHandle);

    [DllImport(FwpUClnt, EntryPoint = "FwpmTransactionBegin0", SetLastError = false)]
    internal static extern uint FwpmTransactionBegin0(nint engineHandle, uint flags);

    [DllImport(FwpUClnt, EntryPoint = "FwpmTransactionCommit0", SetLastError = false)]
    internal static extern uint FwpmTransactionCommit0(nint engineHandle);

    [DllImport(FwpUClnt, EntryPoint = "FwpmTransactionAbort0", SetLastError = false)]
    internal static extern uint FwpmTransactionAbort0(nint engineHandle);

    internal const uint RpcCAuthnDefault = 0xFFFFFFFF;
    internal const uint FwpmSessionFlagDynamic = 0x00000001;
    internal const uint ErrorSuccess = 0;

    /// <summary>
    /// Prüft ob ein FWPM-Return-Code einen Fehler anzeigt.
    /// FWPM-Fehlercodes beginnen mit 0x8032xxxx.
    /// </summary>
    internal static bool IsSuccess(uint result) => result == ErrorSuccess;

    /// <summary>
    /// Konvertiert einen FWPM-Fehlercode in eine lesbare Fehlermeldung.
    /// </summary>
    internal static string GetErrorMessage(uint errorCode) => errorCode switch
    {
        0x80320001 => "FWP_E_CALLOUT_NOT_FOUND",
        0x80320002 => "FWP_E_CONDITION_NOT_FOUND",
        0x80320003 => "FWP_E_FILTER_NOT_FOUND",
        0x80320004 => "FWP_E_LAYER_NOT_FOUND",
        0x80320005 => "FWP_E_PROVIDER_NOT_FOUND",
        0x80320006 => "FWP_E_PROVIDER_CONTEXT_NOT_FOUND",
        0x80320007 => "FWP_E_SUBLAYER_NOT_FOUND",
        0x80320008 => "FWP_E_NOT_FOUND",
        0x80320009 => "FWP_E_ALREADY_EXISTS",
        0x8032000A => "FWP_E_IN_USE",
        0x80070005 => "ERROR_ACCESS_DENIED (Administrator-Rechte erforderlich)",
        _ => $"WFP-Fehler 0x{errorCode:X8}"
    };
}
