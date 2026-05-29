// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
namespace NetOverseer.Core.Models;

/// <summary>
/// Persistierte Verbindung aus der SQLite-Datenbank.
/// Entspricht einer Zeile in der Connections-Tabelle.
/// </summary>
public sealed class ConnectionRecord
{
    public long Id { get; init; }
    public long SessionId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public string LocalIp { get; init; } = string.Empty;
    public int LocalPort { get; init; }
    public string RemoteIp { get; init; } = string.Empty;
    public int RemotePort { get; init; }
    public string Protocol { get; init; } = string.Empty;
    public long BytesSent { get; init; }
    public long BytesReceived { get; init; }
    public double Duration { get; init; }
    public string GeoCountry { get; init; } = string.Empty;
    public string GeoCity { get; init; } = string.Empty;
    public int ReputationScore { get; init; } = -1;
    public bool IsBlocked { get; init; }
}
