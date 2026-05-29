// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
namespace NetOverseer.Core.Interfaces;

/// <summary>
/// Background-Worker der Verbindungs- und DNS-Ereignisse gepuffert in SQLite schreibt.
/// Muss durch die App gestartet und beim Herunterfahren gestoppt werden.
/// </summary>
public interface IPersistenceWorker
{
    /// <summary>Startet den Hintergrund-Flush-Loop.</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Flusht verbleibende Puffer-Einhalte und stoppt den Worker.</summary>
    Task StopAsync();

    /// <summary>Gibt an ob der Worker aktiv ist.</summary>
    bool IsRunning { get; }
}
