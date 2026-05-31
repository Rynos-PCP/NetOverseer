// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.Core.Services;

/// <summary>
/// Liest und schreibt Einstellungen aus/nach %AppData%\NetOverseer\settings.json.
/// API-Keys werden im Klartext gespeichert; %AppData% ist durch Windows-ACL
/// nur für den aktuellen Benutzer zugänglich.
/// Thread-sicher durch Lock bei Schreibzugriffen.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly string   _path;
    private readonly ILogger<SettingsService> _logger;
    private readonly object   _lock = new();
    private AppSettings?      _cache;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NetOverseer");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
    }

    /// <summary>
    /// Testbarer Konstruktor mit explizitem Dateipfad (nicht %AppData%).
    /// </summary>
    internal SettingsService(string settingsFilePath, ILogger<SettingsService> logger)
    {
        _logger = logger;
        _path   = settingsFilePath;
        var dir = Path.GetDirectoryName(settingsFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    /// <inheritdoc/>
    public AppSettings Load()
    {
        if (_cache is not null) return _cache;

        lock (_lock)
        {
            if (_cache is not null) return _cache;

            if (!File.Exists(_path))
            {
                _cache = new AppSettings();
                return _cache;
            }

            try
            {
                var json = File.ReadAllText(_path);
                _cache   = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions)
                           ?? new AppSettings();
                // Sensible Felder DPAPI-entschlüsseln (akzeptiert auch Legacy-Klartext)
                _cache.AbuseIpDbApiKey   = DataProtection.Unprotect(_cache.AbuseIpDbApiKey);
                _cache.MaxMindLicenseKey = DataProtection.Unprotect(_cache.MaxMindLicenseKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Einstellungen konnten nicht geladen werden – Standardwerte werden verwendet");
                _cache = new AppSettings();
            }

            return _cache;
        }
    }

    /// <inheritdoc/>
    public void Save(AppSettings settings)
    {
        AppSettings? savedSnapshot = null;
        lock (_lock)
        {
            try
            {
                // Klartext-Werte zuerst im Cache behalten, nur die persistierte Variante verschlüsseln
                var onDisk = CloneForPersistence(settings);
                onDisk.AbuseIpDbApiKey   = DataProtection.Protect(settings.AbuseIpDbApiKey);
                onDisk.MaxMindLicenseKey = DataProtection.Protect(settings.MaxMindLicenseKey);

                var json = JsonSerializer.Serialize(onDisk, _jsonOptions);
                File.WriteAllText(_path, json);
                _cache = settings;
                savedSnapshot = settings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Einstellungen konnten nicht gespeichert werden");
            }
        }
        // Event außerhalb des Locks feuern, damit Handler nicht blockieren
        if (savedSnapshot is not null)
        {
            try { SettingsSaved?.Invoke(this, savedSnapshot); }
            catch (Exception ex) { _logger.LogWarning(ex, "Fehler in SettingsSaved-Handler"); }
        }
    }

    /// <inheritdoc/>
    public event EventHandler<AppSettings>? SettingsSaved;

    private static AppSettings CloneForPersistence(AppSettings src) => new()
    {
        Language                       = src.Language,
        Theme                          = src.Theme,
        DataRetentionDays              = src.DataRetentionDays,
        DatabaseWarningSizeMb          = src.DatabaseWarningSizeMb,
        CaptureMethod                  = src.CaptureMethod,
        PollingIntervalMs              = src.PollingIntervalMs,
        IgnoredProcesses               = [.. src.IgnoredProcesses],
        ShowPrivateConnections         = src.ShowPrivateConnections,
        HideGeoDbMissingWarning        = src.HideGeoDbMissingWarning,
        AbuseIpDbApiKey                = src.AbuseIpDbApiKey,
        MaxMindLicenseKey              = src.MaxMindLicenseKey,
        OfflineMode                    = src.OfflineMode,
        AutoUpdateBlocklists           = src.AutoUpdateBlocklists,
        BlocklistUpdateIntervalDays    = src.BlocklistUpdateIntervalDays,
        MaxDailyAbuseIpDbRequests      = src.MaxDailyAbuseIpDbRequests,
        ReputationCacheTtlHours        = src.ReputationCacheTtlHours,
        BlocklistLastUpdated           = src.BlocklistLastUpdated,
        NotifyOnNewUnknownConnection   = src.NotifyOnNewUnknownConnection,
        NotifyOnLowReputation          = src.NotifyOnLowReputation,
        MinReputationScoreForAlert     = src.MinReputationScoreForAlert,
        NotifyOnSuspiciousDns          = src.NotifyOnSuspiciousDns,
        QuietHoursEnabled              = src.QuietHoursEnabled,
        QuietHoursStartHour            = src.QuietHoursStartHour,
        QuietHoursEndHour              = src.QuietHoursEndHour
    };

    /// <inheritdoc/>
    public void Invalidate()
    {
        lock (_lock) { _cache = null; }
    }

    // ── API-Key Zugriff ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string? GetAbuseIpDbApiKey() => Load().AbuseIpDbApiKey;

    /// <inheritdoc/>
    public void SetAbuseIpDbApiKey(string? key)
    {
        lock (_lock)
        {
            var s = Load();
            s.AbuseIpDbApiKey = string.IsNullOrWhiteSpace(key) ? null : key;
            Save(s);
        }
    }

    /// <inheritdoc/>
    public string? GetMaxMindLicenseKey() => Load().MaxMindLicenseKey;

    /// <inheritdoc/>
    public void SetMaxMindLicenseKey(string? key)
    {
        lock (_lock)
        {
            var s = Load();
            s.MaxMindLicenseKey = string.IsNullOrWhiteSpace(key) ? null : key;
            Save(s);
        }
    }
}
