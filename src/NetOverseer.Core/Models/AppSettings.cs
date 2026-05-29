// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
namespace NetOverseer.Core.Models;

/// <summary>
/// Anwendungseinstellungen, die in %AppData%\NetOverseer\settings.json gespeichert werden.
/// API-Keys werden unverschlüsselt gespeichert; %AppData% ist durch Windows-ACL benutzerspezifisch geschützt.
/// </summary>
public sealed class AppSettings
{
    // ── Allgemein ────────────────────────────────────────────────────────────

    /// <summary>Anzeigesprache: "de" (Deutsch) oder "en" (Englisch).</summary>
    public string Language { get; set; } = "de";

    /// <summary>Farbschema: "System" / "Light" / "Dark".</summary>
    public string Theme { get; set; } = "System";

    /// <summary>Verbindungshistorie nach X Tagen löschen (0 = niemals).</summary>
    public int DataRetentionDays { get; set; } = 30;

    /// <summary>Warnung ausgeben wenn SQLite-Datenbank größer als X MB wird.</summary>
    public int DatabaseWarningSizeMb { get; set; } = 500;

    // ── Überwachung ──────────────────────────────────────────────────────────

    /// <summary>Capture-Methode: "IpHelper" (Polling) oder "Wfp" (Windows Filtering Platform).</summary>
    public string CaptureMethod { get; set; } = "IpHelper";

    /// <summary>Polling-Intervall in Millisekunden (nur IpHelper, 250–2000).</summary>
    public int PollingIntervalMs { get; set; } = 500;

    /// <summary>Prozess-Namen die ignoriert werden (kein Tracking).</summary>
    public List<string> IgnoredProcesses { get; set; } = [];

    /// <summary>Private/Loopback-Verbindungen in der Live-Ansicht anzeigen.</summary>
    public bool ShowPrivateConnections { get; set; } = false;

    // ── Reputation & APIs ────────────────────────────────────────────────────

    /// <summary>AbuseIPDB API-Key (Klartext; %AppData% ist per ACL nur für den aktuellen Benutzer zugänglich).</summary>
    public string? AbuseIpDbApiKey { get; set; }

    /// <summary>MaxMind GeoLite2 Lizenz-Key (Klartext).</summary>
    public string? MaxMindLicenseKey { get; set; }

    /// <summary>Wenn true, werden keine externen APIs aufgerufen (nur lokale Listen).</summary>
    public bool OfflineMode { get; set; }

    /// <summary>Automatisches Aktualisieren der Blocklisten im Hintergrund.</summary>
    public bool AutoUpdateBlocklists { get; set; } = true;

    /// <summary>Aktualisierungsintervall der Blocklisten in Tagen.</summary>
    public int BlocklistUpdateIntervalDays { get; set; } = 7;

    /// <summary>Maximale AbuseIPDB-Anfragen pro Tag (Free Tier = 1000).</summary>
    public int MaxDailyAbuseIpDbRequests { get; set; } = 1000;

    /// <summary>Cache-TTL für Reputationsergebnisse in Stunden.</summary>
    public int ReputationCacheTtlHours { get; set; } = 24;

    /// <summary>Zeitpunkt der letzten Blocklisten-Aktualisierung.</summary>
    public DateTimeOffset? BlocklistLastUpdated { get; set; }

    // ── Benachrichtigungen ───────────────────────────────────────────────────

    /// <summary>Toast-Benachrichtigung bei einer neuen, unbekannten Verbindung.</summary>
    public bool NotifyOnNewUnknownConnection { get; set; } = false;

    /// <summary>Toast-Benachrichtigung wenn ein Reputations-Score unter den Schwellwert fällt.</summary>
    public bool NotifyOnLowReputation { get; set; } = true;

    /// <summary>Mindest-Score für Benachrichtigung (0–100, Alarm wenn Score darunter).</summary>
    public int MinReputationScoreForAlert { get; set; } = 20;

    /// <summary>Toast-Benachrichtigung bei verdächtiger DNS-Anfrage.</summary>
    public bool NotifyOnSuspiciousDns { get; set; } = false;

    /// <summary>Stille Stunden aktiv (keine Benachrichtigungen in diesem Zeitraum).</summary>
    public bool QuietHoursEnabled { get; set; } = false;

    /// <summary>Beginn der Stillen Stunden (Stunde, 0–23).</summary>
    public int QuietHoursStartHour { get; set; } = 22;

    /// <summary>Ende der Stillen Stunden (Stunde, 0–23).</summary>
    public int QuietHoursEndHour { get; set; } = 8;
}
