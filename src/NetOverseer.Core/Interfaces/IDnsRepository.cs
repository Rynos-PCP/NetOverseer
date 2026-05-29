// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using NetOverseer.Core.Models;

namespace NetOverseer.Core.Interfaces;

/// <summary>
/// Repository für persistierte DNS-Abfragen.
/// </summary>
public interface IDnsRepository
{
    /// <summary>Fügt mehrere DNS-Einträge in einem Batch ein.</summary>
    Task InsertBatchAsync(IReadOnlyList<DnsRecord> records, CancellationToken ct = default);

    /// <summary>Gibt alle DNS-Abfragen im angegebenen Zeitfenster zurück.</summary>
    Task<IReadOnlyList<DnsRecord>> GetByTimeRangeAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>Gibt DNS-Abfragen eines Prozesses zurück (max. <paramref name="limit"/> Einträge).</summary>
    Task<IReadOnlyList<DnsRecord>> GetByProcessAsync(
        string processName, int limit = 500, CancellationToken ct = default);

    /// <summary>Gibt DNS-Abfragen für eine bestimmte Domain zurück.</summary>
    Task<IReadOnlyList<DnsRecord>> GetByDomainAsync(
        string domain, int limit = 200, CancellationToken ct = default);

    /// <summary>Löscht alle DNS-Einträge, die älter als <paramref name="before"/> sind.</summary>
    Task<int> DeleteOlderThanAsync(DateTimeOffset before, CancellationToken ct = default);
}
