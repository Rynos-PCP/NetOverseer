// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
namespace NetOverseer.Core.Models;

/// <summary>
/// Aggregierte Aktivitätszusammenfassung einer Anwendung für einen Zeitraum.
/// Wird vom HistoryViewModel für die Zeitachsen-Darstellung verwendet.
/// </summary>
public sealed class AppActivitySummary
{
    public string ProcessName { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public long ConnectionCount { get; init; }
    public long TotalBytesSent { get; init; }
    public long TotalBytesReceived { get; init; }
    /// <summary>Schlechtester Reputations-Score im Zeitraum (-1 = unbekannt).</summary>
    public int MinReputationScore { get; init; } = -1;
    public DateTimeOffset LastSeen { get; init; }
}

/// <summary>
/// Zeitpunkt-Aktivitätsbucket für eine Anwendung (für Sparklines in der Zeitachse).
/// </summary>
public sealed class AppBucketCount
{
    public int BucketIndex { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public long Count { get; init; }
}
