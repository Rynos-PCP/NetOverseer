// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.IO.Compression;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using NetOverseer.Core.Interfaces;

namespace NetOverseer.Core.Services;

/// <summary>
/// Lädt die MaxMind GeoLite2-Datenbank herunter, sofern ein gültiger Lizenz-Key
/// in den Einstellungen hinterlegt ist. Die Datenbank wird nach
/// <c>%AppData%\NetOverseer\geoip\GeoLite2-City.mmdb</c> entpackt.
///
/// Ohne Lizenz-Key oder im OfflineMode bleibt der Service inaktiv (kein Netzzugriff).
/// MaxMind verlangt einen kostenfreien Account; Details:
/// https://dev.maxmind.com/geoip/updating-databases?lang=en
/// </summary>
public sealed class GeoDbUpdater
{
    private const string DownloadUrlTemplate =
        "https://download.maxmind.com/app/geoip_download?edition_id=GeoLite2-City&license_key={0}&suffix=tar.gz";

    private readonly ISettingsService _settings;
    private readonly ILogger<GeoDbUpdater> _logger;
    private readonly string _targetDir;

    private static readonly HttpClient _http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(15)
    })
    { Timeout = TimeSpan.FromMinutes(5) };

    public GeoDbUpdater(ISettingsService settings, ILogger<GeoDbUpdater> logger)
    {
        _settings = settings;
        _logger   = logger;
        _targetDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NetOverseer", "geoip");
        Directory.CreateDirectory(_targetDir);
    }

    /// <summary>
    /// Versucht die GeoLite2-City Datenbank zu aktualisieren.
    /// Gibt true zurück, wenn die Datei erfolgreich heruntergeladen und entpackt wurde.
    /// </summary>
    public async Task<bool> TryUpdateAsync(CancellationToken ct = default)
    {
        var prefs = _settings.Load();
        if (prefs.OfflineMode)
        {
            _logger.LogDebug("GeoDb-Update übersprungen (OfflineMode).");
            return false;
        }

        var licenseKey = _settings.GetMaxMindLicenseKey();
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            _logger.LogDebug("GeoDb-Update übersprungen (kein MaxMind-Lizenz-Key konfiguriert).");
            return false;
        }

        var url = string.Format(DownloadUrlTemplate, Uri.EscapeDataString(licenseKey));
        var tempArchive = Path.Combine(Path.GetTempPath(), $"geolite2-{Guid.NewGuid():N}.tar.gz");

        try
        {
            _logger.LogInformation("Lade MaxMind GeoLite2-City Datenbank…");
            using (var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("GeoDb-Download fehlgeschlagen: HTTP {Status}", resp.StatusCode);
                    return false;
                }
                await using var fs = File.Create(tempArchive);
                await resp.Content.CopyToAsync(fs, ct);
            }

            ExtractMmdbFromTarGz(tempArchive, Path.Combine(_targetDir, "GeoLite2-City.mmdb"));
            _logger.LogInformation("GeoLite2-City Datenbank aktualisiert.");
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GeoDb-Update fehlgeschlagen.");
            return false;
        }
        finally
        {
            try { if (File.Exists(tempArchive)) File.Delete(tempArchive); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Extrahiert die einzelne .mmdb-Datei aus einem MaxMind tar.gz Archiv.
    /// MaxMind packt die DB in einen versionierten Unterordner.
    /// </summary>
    private static void ExtractMmdbFromTarGz(string archivePath, string targetMmdb)
    {
        using var fs   = File.OpenRead(archivePath);
        using var gz   = new GZipStream(fs, CompressionMode.Decompress);
        // .NET 8: System.Formats.Tar.TarReader
        using var tar  = new System.Formats.Tar.TarReader(gz, leaveOpen: false);
        System.Formats.Tar.TarEntry? entry;
        while ((entry = tar.GetNextEntry()) is not null)
        {
            if (entry.Name.EndsWith(".mmdb", StringComparison.OrdinalIgnoreCase) &&
                entry.DataStream is not null)
            {
                // Atomar via Temp-File + Move, damit ein laufender Reader nicht halb-gelesene Bytes sieht.
                var tmp = targetMmdb + ".new";
                using (var outFs = File.Create(tmp))
                    entry.DataStream.CopyTo(outFs);
                File.Move(tmp, targetMmdb, overwrite: true);
                return;
            }
        }
        throw new InvalidDataException("Keine .mmdb-Datei im GeoLite2-Archiv gefunden.");
    }
}
