// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.Data;

/// <summary>
/// Hosted Service der täglich Datenbankwartung durchführt
/// (VACUUM + Löschen alter Einträge gemäß Aufbewahrungsfrist).
/// Startet 60 Sekunden nach App-Start und wiederholt sich alle 24 Stunden.
/// </summary>
public sealed class DatabaseMaintenanceService : BackgroundService
{
    private static readonly TimeSpan InitialDelay  = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromHours(24);

    private readonly IDatabaseService        _db;
    private readonly ISettingsService        _settings;
    private readonly ILogger<DatabaseMaintenanceService> _logger;

    public DatabaseMaintenanceService(
        IDatabaseService   db,
        ISettingsService   settings,
        ILogger<DatabaseMaintenanceService> logger)
    {
        _db       = db;
        _settings = settings;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("DatabaseMaintenanceService: Warte {Delay}s vor erstem Wartungslauf.",
            InitialDelay.TotalSeconds);

        await Task.Delay(InitialDelay, stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunMaintenanceAsync(stoppingToken).ConfigureAwait(false);
            await Task.Delay(RepeatInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunMaintenanceAsync(CancellationToken ct)
    {
        try
        {
            var retentionDays = _settings.Load().DataRetentionDays;

            _logger.LogInformation(
                "DatabaseMaintenanceService: Wartung gestartet (Aufbewahrung {Days} Tage).",
                retentionDays);

            await _db.RunMaintenanceAsync(retentionDays, ct).ConfigureAwait(false);

            // Größenwarnung prüfen
            long sizeBytes = _db.GetDatabaseSizeBytes();
            long warnBytes = (long)_settings.Load().DatabaseWarningSizeMb * 1024 * 1024;
            if (warnBytes > 0 && sizeBytes > warnBytes)
            {
                _logger.LogWarning(
                    "Datenbankgröße ({SizeMb:F1} MB) überschreitet Warnschwelle ({WarnMb} MB).",
                    sizeBytes / 1_048_576.0, _settings.Load().DatabaseWarningSizeMb);
            }
        }
        catch (OperationCanceledException) { /* erwartet beim Beenden */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DatabaseMaintenanceService: Wartung fehlgeschlagen.");
        }
    }
}
