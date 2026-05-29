// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
namespace NetOverseer.Core.Models;

/// <summary>Einzelne Netzwerkverbindung aus einer Startup-Aufzeichnung.</summary>
public sealed class StartupConnection
{
    public long Id { get; init; }
    public long SessionId { get; init; }

    /// <summary>Absoluter Zeitstempel der Verbindung (UTC).</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>Sekunden seit Systemstart zum Zeitpunkt der Verbindung.</summary>
    public double OffsetSeconds { get; init; }

    public int    ProcessId   { get; init; }
    public string ProcessName { get; init; } = "";
    public string LocalIp     { get; init; } = "";
    public int    LocalPort   { get; init; }
    public string RemoteIp    { get; init; } = "";
    public int    RemotePort  { get; init; }

    /// <summary>"TCP" oder "UDP".</summary>
    public string Protocol { get; init; } = "TCP";
}
