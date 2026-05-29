// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using NetOverseer.Core.Models;

namespace NetOverseer.Core.Interfaces;

/// <summary>
/// Repository für persistierte Netzwerkverbindungen.
/// </summary>
public interface IConnectionRepository
{
    /// <summary>Fügt mehrere Verbindungen in einem Batch ein.</summary>
    Task InsertBatchAsync(IReadOnlyList<ConnectionRecord> records, CancellationToken ct = default);

    /// <summary>Gibt alle Verbindungen zurück, die im angegebenen Zeitfenster liegen.</summary>
    Task<IReadOnlyList<ConnectionRecord>> GetByTimeRangeAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>Gibt alle Verbindungen eines Prozessnamens zurück (max. <paramref name="limit"/> Einträge).</summary>
    Task<IReadOnlyList<ConnectionRecord>> GetByProcessAsync(
        string processName, int limit = 500, CancellationToken ct = default);

    /// <summary>Gibt alle Verbindungen zu einer Remote-IP zurück.</summary>
    Task<IReadOnlyList<ConnectionRecord>> GetByRemoteIpAsync(
        string remoteIp, int limit = 200, CancellationToken ct = default);

    /// <summary>Löscht alle Verbindungen, die älter als <paramref name="before"/> sind.</summary>
    Task<int> DeleteOlderThanAsync(DateTimeOffset before, CancellationToken ct = default);

    /// <summary>Zählt alle gespeicherten Verbindungen.</summary>
    Task<long> CountAsync(CancellationToken ct = default);

    /// <summary>
    /// Gibt pro App eine Zusammenfassung (Verbindungsanzahl, Bytes, schlechtester Score)
    /// für den angegebenen Zeitraum zurück.
    /// </summary>
    Task<IReadOnlyList<AppActivitySummary>> GetAppActivityAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>
    /// Gibt Verbindungsanzahlen pro (App, Zeitbucket) zurück – für Sparklines.
    /// Die Zeitspanne [<paramref name="from"/>, <paramref name="to"/>] wird in
    /// <paramref name="bucketCount"/> gleichgroße Buckets aufgeteilt.
    /// </summary>
    Task<IReadOnlyList<AppBucketCount>> GetBucketedActivityAsync(
        DateTimeOffset from, DateTimeOffset to, int bucketCount,
        CancellationToken ct = default);

    /// <summary>
    /// Gibt die verdächtigsten Verbindungen (niedrigster Reputations-Score > -1)
    /// im angegebenen Zeitraum zurück.
    /// </summary>
    Task<IReadOnlyList<ConnectionRecord>> GetSuspiciousConnectionsAsync(
        DateTimeOffset from, DateTimeOffset to, int limit = 20,
        CancellationToken ct = default);
}
