// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
namespace NetOverseer.Core.Models;

/// <summary>DNS-Abfragetypen (IANA Query Types).</summary>
public enum DnsQueryType
{
    A     = 1,
    Ns    = 2,
    CName = 5,
    Ptr   = 12,
    Mx    = 15,
    Aaaa  = 28,
    Srv   = 33,
    Txt   = 16,
    Other = 0,
}

/// <summary>Klassifizierung einer DNS-Anfrage nach Zweck des Ziels.</summary>
public enum DnsCategory
{
    Normal,
    Tracker,
    Telemetry,
    Cdn,
    Suspicious,
    Unknown,
}

/// <summary>
/// Repräsentiert eine aufgezeichnete DNS-Anfrage inkl. Antwort.
/// Unveränderlich nach der Erzeugung (init-Properties).
/// </summary>
public sealed class DnsQueryEvent
{
    /// <summary>Zeitpunkt der Anfrage (UTC).</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Prozess-ID des anfragenden Prozesses.</summary>
    public int ProcessId { get; init; }

    /// <summary>Prozessname (z.B. "chrome.exe").</summary>
    public string ProcessName { get; init; } = string.Empty;

    /// <summary>Abgefragter Hostname / Domain (z.B. "example.com").</summary>
    public string QueryName { get; init; } = string.Empty;

    /// <summary>DNS-Abfragetyp (A, AAAA, CNAME, …).</summary>
    public DnsQueryType QueryType { get; init; }

    /// <summary>Aufgelöste IP-Adressen (leer bei Fehlschlag).</summary>
    public string[] ResolvedAddresses { get; init; } = [];

    /// <summary>Klassifizierung der Domain (Tracker, Telemetry, …).</summary>
    public DnsCategory Category { get; init; }

    /// <summary>Ob die Anfrage erfolgreich beantwortet wurde (ResponseCode = 0).</summary>
    public bool WasSuccessful { get; init; } = true;
}
