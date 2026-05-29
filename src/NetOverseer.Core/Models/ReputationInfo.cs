// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
namespace NetOverseer.Core.Models;

/// <summary>
/// Klassifizierung der Vertrauenswürdigkeit einer IP-Adresse.
/// </summary>
public enum ReputationCategory
{
    /// <summary>Unbekannt – noch nicht abgefragt.</summary>
    Unknown,
    /// <summary>Vertrauenswürdig, keine bekannten Einträge.</summary>
    Safe,
    /// <summary>Verdächtig (AbuseIPDB-Score 20–50).</summary>
    Suspicious,
    /// <summary>Gefährlich (AbuseIPDB-Score &gt; 50).</summary>
    Dangerous,
    /// <summary>Bekannte Microsoft-Telemetrie-IP.</summary>
    MicrosoftTelemetry,
    /// <summary>Private/Loopback-Adresse.</summary>
    Private
}

/// <summary>
/// Reputationsinformationen zu einer Remote-IP-Adresse.
/// Score = AbuseIPDB-Confidence-of-Abuse (0–100); -1 = unbekannt.
/// </summary>
public sealed class ReputationInfo
{
    /// <summary>Keine Informationen verfügbar (Standardwert).</summary>
    public static readonly ReputationInfo Unknown = new();

    /// <summary>Private/Loopback-Adresse – immer sicher.</summary>
    public static readonly ReputationInfo Private = new()
    {
        Score    = 0,
        Category = ReputationCategory.Private
    };

    /// <summary>AbuseIPDB Confidence-of-Abuse (0 = sauber, 100 = maximale Meldungen). -1 = nicht abgefragt.</summary>
    public int Score { get; init; } = -1;

    /// <summary>Klassifizierung.</summary>
    public ReputationCategory Category { get; init; } = ReputationCategory.Unknown;

    /// <summary>Quelle der Information (z.B. "AbuseIPDB", "Firehol-L1", "Local").</summary>
    public string? Source { get; init; }

    /// <summary>Zeitpunkt der letzten Abfrage (für Cache-TTL).</summary>
    public DateTimeOffset? LastChecked { get; init; }

    /// <summary>True wenn die IP aktiv blockiert ist.</summary>
    public bool IsBlocked { get; init; }
}
