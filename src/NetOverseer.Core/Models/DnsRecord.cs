// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
namespace NetOverseer.Core.Models;

/// <summary>
/// Persistiertes DNS-Query-Ergebnis aus der SQLite-Datenbank.
/// Entspricht einer Zeile in der DnsQueries-Tabelle.
/// </summary>
public sealed class DnsRecord
{
    public long Id { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string QueryType { get; init; } = string.Empty;
    public string ResolvedIps { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
}
