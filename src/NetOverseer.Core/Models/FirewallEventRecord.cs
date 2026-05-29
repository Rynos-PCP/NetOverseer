// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
namespace NetOverseer.Core.Models;

/// <summary>
/// Persistiertes Firewall-Ereignis aus der SQLite-Datenbank.
/// Entspricht einer Zeile in der FirewallEvents-Tabelle.
/// </summary>
public sealed class FirewallEventRecord
{
    public long Id { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string RuleName { get; init; } = string.Empty;
    /// <summary>"Block" oder "Allow"</summary>
    public string Action { get; init; } = string.Empty;
    /// <summary>Kompakter String-Key der auslösenden Verbindung (optional).</summary>
    public string TriggeringConnection { get; init; } = string.Empty;
}
