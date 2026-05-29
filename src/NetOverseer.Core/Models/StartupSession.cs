// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
namespace NetOverseer.Core.Models;

/// <summary>Metadaten einer Startup-Aufzeichnungssitzung.</summary>
public sealed class StartupSession
{
    public long Id { get; init; }

    /// <summary>Hochgerechneter Systemstart-Zeitpunkt (UTC).</summary>
    public DateTime BootTime { get; init; }

    /// <summary>Beginn der Aufzeichnung (UTC).</summary>
    public DateTime RecordingStart { get; init; }

    /// <summary>Ende der Aufzeichnung (UTC).</summary>
    public DateTime RecordingEnd { get; init; }

    /// <summary>Anzahl erfasster eindeutiger Verbindungen.</summary>
    public int ConnectionCount { get; init; }

    public TimeSpan RecordingDuration => RecordingEnd - RecordingStart;
}
