// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
namespace NetOverseer.Core.Models;

/// <summary>
/// Überwachungs-Sitzung: entspricht einer App-Laufzeit.
/// Entspricht einer Zeile in der Sessions-Tabelle.
/// </summary>
public sealed class MonitorSession
{
    public long Id { get; init; }
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset? EndTime { get; init; }
    public long TotalConnections { get; init; }
    public long TotalBytesTransferred { get; init; }
}
