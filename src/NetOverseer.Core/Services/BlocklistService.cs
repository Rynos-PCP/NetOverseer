// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Net;
using Microsoft.Extensions.Logging;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.Core.Services;

/// <summary>
/// Verwaltet die Firehol Level-1-Blockliste (und optional Level 2).
/// Die Liste wird lokal unter %AppData%\NetOverseer\blocklists\ gecacht
/// und einmal pro Woche im Hintergrund aktualisiert.
/// </summary>
public sealed class BlocklistService : IBlocklistService
{
    // ──────────────────────────────────────────────────────────────────────
    // Quellen (Firehol GitHub Mirror mit stabilen URLs)
    // ──────────────────────────────────────────────────────────────────────

    private const string FireholLevel1Url =
        "https://raw.githubusercontent.com/firehol/blocklist-ipsets/master/firehol_level1.netset";

    private const string SpamhausDropUrl =
        "https://www.spamhaus.org/drop/drop.txt";

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromDays(7);

    // ──────────────────────────────────────────────────────────────────────
    // State
    // ──────────────────────────────────────────────────────────────────────

    private readonly ISettingsService  _settings;
    private readonly ILogger<BlocklistService> _logger;
    private readonly string            _cacheDir;
    private readonly SemaphoreSlim     _updateLock = new(1, 1);

    private volatile IpRangeSet _blockSet = IpRangeSet.Empty;

    private static readonly HttpClient _http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(15)
    })
    { Timeout = TimeSpan.FromSeconds(30) };

    // ──────────────────────────────────────────────────────────────────────
    // IBlocklistService
    // ──────────────────────────────────────────────────────────────────────

    public DateTimeOffset? LastUpdated { get; private set; }

    public BlocklistService(ISettingsService settings, ILogger<BlocklistService> logger)
    {
        _settings = settings;
        _logger   = logger;

        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NetOverseer", "blocklists");
        Directory.CreateDirectory(_cacheDir);

        // Vorhandene gecachte Liste sofort laden (kein Netzwerkzugriff)
        _ = Task.Run(LoadFromDiskAsync);
    }

    /// <inheritdoc/>
    public bool IsBlocked(IPAddress ip) => _blockSet.Contains(ip);

    /// <inheritdoc/>
    public async Task UpdateAsync(CancellationToken ct = default)
    {
        // Offline-Modus: keine externen HTTP-Calls erlaubt.
        var prefs = _settings.Load();
        if (prefs.OfflineMode)
        {
            _logger.LogDebug("Blocklisten-Update übersprungen (OfflineMode aktiv).");
            return;
        }

        if (!await _updateLock.WaitAsync(0, ct)) return; // läuft bereits
        try
        {
            _logger.LogInformation("Starte Blocklisten-Aktualisierung …");

            var lines = new List<string>();
            lines.AddRange(await DownloadLinesAsync(FireholLevel1Url, ct));

            // Spamhaus DROP nur laden wenn der Request funktioniert
            try { lines.AddRange(await DownloadLinesAsync(SpamhausDropUrl, ct)); }
            catch { /* Optional, Fehler ignorieren */ }

            if (lines.Count == 0) return;

            // Gecachte Datei aktualisieren
            var filePath = Path.Combine(_cacheDir, "combined.netset");
            await File.WriteAllLinesAsync(filePath, lines, ct);

            _blockSet   = IpRangeSet.FromCidrs(lines);
            LastUpdated = DateTimeOffset.UtcNow;

            // Zeitstempel in Settings persistieren
            var settings = _settings.Load();
            settings.BlocklistLastUpdated = LastUpdated;
            _settings.Save(settings);

            _logger.LogInformation(
                "Blockliste aktualisiert: {Count} Einträge", _blockSet.Count);
        }
        catch (OperationCanceledException) { /* abgebrochen */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Aktualisieren der Blockliste");
        }
        finally
        {
            _updateLock.Release();
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Private Helpers
    // ──────────────────────────────────────────────────────────────────────

    private async Task LoadFromDiskAsync()
    {
        try
        {
            var filePath = Path.Combine(_cacheDir, "combined.netset");
            if (!File.Exists(filePath)) return;

            var lines   = await File.ReadAllLinesAsync(filePath);
            _blockSet   = IpRangeSet.FromCidrs(lines);
            LastUpdated = File.GetLastWriteTimeUtc(filePath);

            _logger.LogDebug(
                "Lokale Blockliste geladen: {Count} Bereiche", _blockSet.Count);

            // Prüfen ob eine Aktualisierung fällig ist
            var settings = _settings.Load();
            if (settings.AutoUpdateBlocklists &&
                LastUpdated < DateTimeOffset.UtcNow - UpdateInterval)
            {
                _ = Task.Run(() => UpdateAsync());
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Blockliste konnte nicht vom Datenträger geladen werden");
        }
    }

    private static async Task<IEnumerable<string>> DownloadLinesAsync(
        string url, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var text  = await resp.Content.ReadAsStringAsync(ct);
        return text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'));
    }
}
