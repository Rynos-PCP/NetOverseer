// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using NetOverseer.Core.Models;

namespace NetOverseer.Core.Interfaces;

/// <summary>
/// Datenbankdienst: initialisiert und pflegt die SQLite-Hauptdatenbank.
/// </summary>
public interface IDatabaseService
{
    /// <summary>
    /// Verbindungs-String für die SQLite-Datenbank (für Repositories).
    /// </summary>
    string ConnectionString { get; }

    /// <summary>
    /// Initialisiert Schema und führt ausstehende Migrationen durch.
    /// Muss einmal beim App-Start aufgerufen werden.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Führt Wartungsaufgaben durch: VACUUM, Löschen alter Einträge.
    /// Sollte täglich im Hintergrund aufgerufen werden.
    /// </summary>
    Task RunMaintenanceAsync(int retentionDays, CancellationToken ct = default);

    /// <summary>
    /// Gibt die aktuelle Datenbankgröße in Bytes zurück.
    /// </summary>
    long GetDatabaseSizeBytes();
}
