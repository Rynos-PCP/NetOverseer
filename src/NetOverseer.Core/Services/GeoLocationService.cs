// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Net;
using System.Net.Sockets;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;
using Microsoft.Extensions.Logging;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.Core.Services;

/// <summary>
/// Lokale IP-Geolocation via MaxMind GeoLite2-City Datenbank.
///
/// Datenbankpfad: %AppData%\NetOverseer\geoip\GeoLite2-City.mmdb
/// Fallback: Paketiertes GeoLite2-Country.mmdb im App-Verzeichnis.
///
/// Performance: MaxMind GeoIP2 Reader ist thread-safe und arbeitet
/// mit Memory-Mapped Files → typisch unter 0.1ms pro Lookup.
/// </summary>
public sealed class GeoLocationService : IGeoLocationService, IDisposable
{
    private readonly ILogger<GeoLocationService> _logger;
    private DatabaseReader? _reader;

    // Bekannte IP-Ranges (CIDR) für Infrastruktur-Kategorisierung
    // Vereinfacht – für Produktion aus Microsoft IP-Listen laden
    private static readonly (string Cidr, IpInfrastructureCategory Category)[] KnownRanges =
    [
        // Microsoft Telemetry / Azure
        ("13.64.0.0/11", IpInfrastructureCategory.MicrosoftCloud),
        ("13.96.0.0/13", IpInfrastructureCategory.MicrosoftCloud),
        ("20.0.0.0/8", IpInfrastructureCategory.MicrosoftCloud),
        ("40.64.0.0/10", IpInfrastructureCategory.MicrosoftCloud),
        ("52.0.0.0/8", IpInfrastructureCategory.MicrosoftCloud),
        ("104.40.0.0/13", IpInfrastructureCategory.MicrosoftCloud),
        // Google
        ("8.8.8.0/24", IpInfrastructureCategory.GoogleCloud),
        ("8.8.4.0/24", IpInfrastructureCategory.GoogleCloud),
        ("34.0.0.0/8", IpInfrastructureCategory.GoogleCloud),
        ("35.0.0.0/8", IpInfrastructureCategory.GoogleCloud),
        // Cloudflare
        ("1.1.1.0/24", IpInfrastructureCategory.Cloudflare),
        ("1.0.0.0/24", IpInfrastructureCategory.Cloudflare),
        ("104.16.0.0/12", IpInfrastructureCategory.Cloudflare),
    ];

    // Vorberechnete Netzwerkmasken für schnelle Lookups
    private static readonly (uint Network, uint Mask, IpInfrastructureCategory Category)[] _parsedRanges;

    static GeoLocationService()
    {
        _parsedRanges = KnownRanges
            .Where(r => TryParseCidr(r.Cidr, out _, out _))
            .Select(r => { TryParseCidr(r.Cidr, out uint net, out uint mask); return (net, mask, r.Category); })
            .ToArray();
    }

    /// <inheritdoc/>
    public bool IsDatabaseLoaded => _reader is not null;

    /// <inheritdoc/>
    public string? DatabasePath { get; private set; }

    public GeoLocationService(ILogger<GeoLocationService> logger)
    {
        _logger = logger;
        TryLoadDatabase();
    }

    private void TryLoadDatabase()
    {
        string[] searchPaths =
        [
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NetOverseer", "geoip", "GeoLite2-City.mmdb"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NetOverseer", "geoip", "GeoLite2-Country.mmdb"),
            Path.Combine(AppContext.BaseDirectory, "GeoLite2-City.mmdb"),
            Path.Combine(AppContext.BaseDirectory, "GeoLite2-Country.mmdb"),
        ];

        foreach (var path in searchPaths)
        {
            if (!File.Exists(path)) continue;
            try
            {
                _reader = new DatabaseReader(path);
                DatabasePath = path;
                _logger.LogInformation("GeoLite2-Datenbank geladen: {Path}", path);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Konnte GeoLite2-Datenbank nicht laden: {Path}", path);
            }
        }

        _logger.LogWarning(
            "Keine GeoLite2-Datenbank gefunden. Geolocation deaktiviert. " +
            "Datenbank herunterladen und nach %AppData%\\NetOverseer\\geoip\\ legen.");
    }

    /// <inheritdoc/>
    public GeoInfo Lookup(IPAddress ip)
    {
        // Loopback prüfen
        if (IPAddress.IsLoopback(ip))
            return GeoInfo.Loopback;

        // Private Adressen prüfen (RFC1918)
        if (IsPrivateAddress(ip))
            return GeoInfo.PrivateNetwork(ip);

        // Bekannte Infrastruktur prüfen (vor DB-Lookup für Performanz)
        var infraCategory = CheckKnownRanges(ip);

        if (_reader is null)
            return infraCategory != IpInfrastructureCategory.Unknown
                ? new GeoInfo { InfrastructureCategory = infraCategory }
                : GeoInfo.Unknown;

        try
        {
            // City-Datenbank versuchen
            if (_reader.TryCity(ip, out var cityResponse) && cityResponse is not null)
            {
                return new GeoInfo
                {
                    CountryCode = cityResponse.Country.IsoCode ?? string.Empty,
                    CountryName = cityResponse.Country.Name ?? string.Empty,
                    City = cityResponse.City.Name ?? string.Empty,
                    Latitude = cityResponse.Location.Latitude,
                    Longitude = cityResponse.Location.Longitude,
                    Organization = string.Empty,  // Nur in ISP/ASN DB
                    IsPrivate = false,
                    InfrastructureCategory = infraCategory
                };
            }

            // Country-Datenbank als Fallback
            if (_reader.TryCountry(ip, out var countryResponse) && countryResponse is not null)
            {
                return new GeoInfo
                {
                    CountryCode = countryResponse.Country.IsoCode ?? string.Empty,
                    CountryName = countryResponse.Country.Name ?? string.Empty,
                    InfrastructureCategory = infraCategory
                };
            }
        }
        catch (AddressNotFoundException)
        {
            // IP nicht in Datenbank – normal für private Ranges die nicht gefiltert wurden
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GeoLite2-Lookup fehlgeschlagen für {IP}.", ip);
        }

        return infraCategory != IpInfrastructureCategory.Unknown
            ? new GeoInfo { InfrastructureCategory = infraCategory }
            : GeoInfo.Unknown;
    }

    private static bool IsPrivateAddress(IPAddress ip)
    {
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal ||
                   ip.IsIPv6UniqueLocal || ip.Equals(IPAddress.IPv6Loopback);
        }

        byte[] bytes = ip.GetAddressBytes();
        return bytes[0] switch
        {
            10 => true,                                               // 10.0.0.0/8
            172 => bytes[1] is >= 16 and <= 31,                     // 172.16.0.0/12
            192 => bytes[1] == 168,                                   // 192.168.0.0/16
            169 => bytes[1] == 254,                                   // 169.254.0.0/16 (APIPA)
            _ => false
        };
    }

    private static IpInfrastructureCategory CheckKnownRanges(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork)
            return IpInfrastructureCategory.Unknown;

        byte[] bytes = ip.GetAddressBytes();
        uint addr = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) |
                    ((uint)bytes[2] << 8) | bytes[3];

        foreach (var (network, mask, category) in _parsedRanges)
        {
            if ((addr & mask) == network)
                return category;
        }

        return IpInfrastructureCategory.Unknown;
    }

    private static bool TryParseCidr(string cidr, out uint network, out uint mask)
    {
        network = 0; mask = 0;
        var parts = cidr.Split('/');
        if (parts.Length != 2) return false;
        if (!IPAddress.TryParse(parts[0], out var ip)) return false;
        if (!int.TryParse(parts[1], out int prefix) || prefix < 0 || prefix > 32) return false;

        byte[] bytes = ip.GetAddressBytes();
        network = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) |
                  ((uint)bytes[2] << 8) | bytes[3];
        mask = prefix == 0 ? 0u : 0xFFFFFFFF << (32 - prefix);
        network &= mask;
        return true;
    }

    /// <inheritdoc/>
    public void Dispose() => _reader?.Dispose();
}
