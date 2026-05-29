// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.Core.Services;

/// <summary>
/// Vollständige Implementierung des Reputations-Service.
///
/// Lookup-Reihenfolge:
/// 1. Private/Loopback-IPs → sofort <see cref="ReputationInfo.Private"/> (kein API-Call)
/// 2. Bekannte Infrastruktur via <see cref="IGeoLocationService"/> (kein API-Call)
/// 3. Lokale Blockliste → sofort <see cref="ReputationCategory.Dangerous"/>
/// 4. In-Memory-Cache (TTL konfigurierbar, Standard 24 h)
/// 5. OfflineMode oder kein API-Key → <see cref="ReputationInfo.Unknown"/>
/// 6. Rate-Limit erschöpft (Free-Tier 1000 req/Tag) → <see cref="ReputationInfo.Unknown"/>
/// 7. AbuseIPDB REST API → Ergebnis cachen und zurückgeben
/// </summary>
public sealed class ReputationService : IReputationService
{
    // ──────────────────────────────────────────────────────────────────────
    // Konstanten
    // ──────────────────────────────────────────────────────────────────────

    private const string ApiBase = "https://api.abuseipdb.com/api/v2/check";
    private const int    MaxCacheEntries = 5_000;

    // ──────────────────────────────────────────────────────────────────────
    // Abhängigkeiten
    // ──────────────────────────────────────────────────────────────────────

    private readonly ISettingsService   _settings;
    private readonly IGeoLocationService _geo;
    private readonly IBlocklistService  _blocklist;
    private readonly ILogger<ReputationService> _logger;

    // ──────────────────────────────────────────────────────────────────────
    // HTTP
    // ──────────────────────────────────────────────────────────────────────

    // Shared-Default-Client für Produktions-DI; Tests injizieren ihren eigenen.
    private static readonly HttpClient _defaultHttp = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(15)
    })
    { Timeout = TimeSpan.FromSeconds(8) };

    private readonly HttpClient _http;

    // ──────────────────────────────────────────────────────────────────────
    // Cache
    // ──────────────────────────────────────────────────────────────────────

    private sealed record CacheEntry(ReputationInfo Info, DateTimeOffset CachedAt);
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    // ──────────────────────────────────────────────────────────────────────
    // Rate-Limiting
    // ──────────────────────────────────────────────────────────────────────

    private readonly object _rateLock = new();
    private int    _dailyRequestCount;
    private DateOnly _lastResetDate = DateOnly.FromDateTime(DateTime.UtcNow);

    // ──────────────────────────────────────────────────────────────────────
    // Konstruktor
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Erstellt eine neue Instanz des <see cref="ReputationService"/>.
    /// </summary>
    public ReputationService(
        ISettingsService          settings,
        IGeoLocationService       geo,
        IBlocklistService         blocklist,
        ILogger<ReputationService> logger)
        : this(settings, geo, blocklist, logger, _defaultHttp) { }

    /// <summary>
    /// Testbarer Konstruktor mit injiziertem <see cref="HttpClient"/> (FakeHandler).
    /// </summary>
    internal ReputationService(
        ISettingsService          settings,
        IGeoLocationService       geo,
        IBlocklistService         blocklist,
        ILogger<ReputationService> logger,
        HttpClient                http)
    {
        _settings  = settings;
        _geo       = geo;
        _blocklist = blocklist;
        _logger    = logger;
        _http      = http;
    }

    // ──────────────────────────────────────────────────────────────────────
    // IReputationService
    // ──────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<ReputationInfo> GetReputationAsync(
        IPAddress ip, CancellationToken ct = default)
    {
        // 1. Private / Loopback → keine externe Abfrage
        if (IPAddress.IsLoopback(ip) || IsPrivate(ip))
            return ReputationInfo.Private;

        // 2. Bekannte Infrastruktur via GeoService → Ergebnis direkt aus Typ ableiten
        var geoInfo = _geo.Lookup(ip);
        if (geoInfo.IsPrivate || geoInfo.IsLoopback)
            return ReputationInfo.Private;

        var infraResult = MapInfraToReputation(geoInfo.InfrastructureCategory);
        if (infraResult is not null)
            return infraResult;

        // 3. Lokale Blockliste
        if (_blocklist.IsBlocked(ip))
        {
            return new ReputationInfo
            {
                Score       = 100,
                Category    = ReputationCategory.Dangerous,
                Source      = "Firehol-L1",
                LastChecked = DateTimeOffset.UtcNow
            };
        }

        var key = ip.ToString();

        // 4. In-Memory-Cache
        var settings = _settings.Load();
        var ttl      = TimeSpan.FromHours(settings.ReputationCacheTtlHours);

        if (_cache.TryGetValue(key, out var entry) &&
            DateTimeOffset.UtcNow - entry.CachedAt < ttl)
        {
            return entry.Info;
        }

        // 5. Offline-Modus oder kein API-Key
        var apiKey = _settings.GetAbuseIpDbApiKey();
        if (settings.OfflineMode || string.IsNullOrWhiteSpace(apiKey))
            return ReputationInfo.Unknown;

        // 6. Rate-Limit prüfen
        if (!TryConsumeQuota(settings))
        {
            _logger.LogDebug("AbuseIPDB Rate-Limit erschöpft ({Max}/Tag)", settings.MaxDailyAbuseIpDbRequests);
            return ReputationInfo.Unknown;
        }

        // 7. AbuseIPDB-API aufrufen
        var result = await QueryAbuseIpDbAsync(ip, apiKey, ct);
        AddToCache(key, result);
        return result;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Private Helpers
    // ──────────────────────────────────────────────────────────────────────

    private async Task<ReputationInfo> QueryAbuseIpDbAsync(
        IPAddress ip, string apiKey, CancellationToken ct)
    {
        try
        {
            var url = $"{ApiBase}?ipAddress={Uri.EscapeDataString(ip.ToString())}&maxAgeInDays=90";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Key",    apiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var resp = await _http.SendAsync(req, ct);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("AbuseIPDB API-Fehler: {Status}", resp.StatusCode);
                return ReputationInfo.Unknown;
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            return ParseAbuseIpDbResponse(json);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Fehler bei AbuseIPDB-Anfrage für {Ip}", ip);
            return ReputationInfo.Unknown;
        }
    }

    private static ReputationInfo ParseAbuseIpDbResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("data", out var data))
            return ReputationInfo.Unknown;

        int  score      = data.TryGetProperty("abuseConfidenceScore", out var sc) ? sc.GetInt32() : -1;
        int  reports    = data.TryGetProperty("totalReports",          out var tr) ? tr.GetInt32() : 0;
        DateTimeOffset? lastReport = null;

        if (data.TryGetProperty("lastReportedAt", out var lr) &&
            lr.ValueKind != JsonValueKind.Null &&
            DateTimeOffset.TryParse(lr.GetString(), out var dt))
            lastReport = dt;

        var category = score switch
        {
            < 0  => ReputationCategory.Unknown,
            0    => ReputationCategory.Safe,
            < 25 => ReputationCategory.Suspicious,
            _    => ReputationCategory.Dangerous
        };

        return new ReputationInfo
        {
            Score       = score,
            Category    = category,
            Source      = "AbuseIPDB",
            LastChecked = DateTimeOffset.UtcNow
        };
    }

    private static ReputationInfo? MapInfraToReputation(IpInfrastructureCategory cat)
    {
        return cat switch
        {
            IpInfrastructureCategory.MicrosoftTelemetry =>
                new() { Score = 0, Category = ReputationCategory.MicrosoftTelemetry,
                        Source = "Built-in", LastChecked = DateTimeOffset.UtcNow },

            IpInfrastructureCategory.MicrosoftCloud or
            IpInfrastructureCategory.GoogleCloud    or
            IpInfrastructureCategory.AwsCloud       or
            IpInfrastructureCategory.Cloudflare     or
            IpInfrastructureCategory.AkamaiCdn      =>
                new() { Score = 0, Category = ReputationCategory.Safe,
                        Source = "Built-in whitelist", LastChecked = DateTimeOffset.UtcNow },

            IpInfrastructureCategory.TorExitNode =>
                new() { Score = 80, Category = ReputationCategory.Dangerous,
                        Source = "Built-in", LastChecked = DateTimeOffset.UtcNow },

            _ => null // kein direktes Ergebnis → weitere Prüfung nötig
        };
    }

    private static bool IsPrivate(IPAddress ip)
    {
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var bytes = ip.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 169 && bytes[1] == 254); // link-local
    }

    private bool TryConsumeQuota(AppSettings settings)
    {
        lock (_rateLock)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (today != _lastResetDate)
            {
                _dailyRequestCount = 0;
                _lastResetDate     = today;
            }

            if (_dailyRequestCount >= settings.MaxDailyAbuseIpDbRequests)
                return false;

            _dailyRequestCount++;
            return true;
        }
    }

    private void AddToCache(string key, ReputationInfo info)
    {
        // Wenn Cache voll: alle abgelaufenen Einträge entfernen
        if (_cache.Count >= MaxCacheEntries)
        {
            var cutoff  = DateTimeOffset.UtcNow - TimeSpan.FromHours(
                          _settings.Load().ReputationCacheTtlHours);
            foreach (var k in _cache.Keys.ToList())
            {
                if (_cache.TryGetValue(k, out var e) && e.CachedAt < cutoff)
                    _cache.TryRemove(k, out _);
            }
        }

        _cache[key] = new CacheEntry(info, DateTimeOffset.UtcNow);
    }
}
