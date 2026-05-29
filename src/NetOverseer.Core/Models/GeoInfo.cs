// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Net;

namespace NetOverseer.Core.Models;

/// <summary>
/// Kategorie einer IP-Adresse nach bekannter Infrastruktur.
/// </summary>
public enum IpInfrastructureCategory
{
    Unknown,
    Private,
    Loopback,
    MicrosoftCloud,
    MicrosoftTelemetry,
    GoogleCloud,
    AwsCloud,
    Cloudflare,
    AkamaiCdn,
    TorExitNode,
    Public
}

/// <summary>
/// Geolocation-Informationen zu einer IP-Adresse.
/// </summary>
public sealed class GeoInfo
{
    /// <summary>ISO 3166-1 alpha-2 Ländercode (z.B. "DE", "US").</summary>
    public string CountryCode { get; init; } = string.Empty;

    /// <summary>Vollständiger Ländername auf Englisch.</summary>
    public string CountryName { get; init; } = string.Empty;

    /// <summary>Stadtname (falls verfügbar, sonst leer).</summary>
    public string City { get; init; } = string.Empty;

    /// <summary>Geografischer Breitengrad (kann null sein).</summary>
    public double? Latitude { get; init; }

    /// <summary>Geografischer Längengrad (kann null sein).</summary>
    public double? Longitude { get; init; }

    /// <summary>ISP oder Organisation (aus MaxMind ASN-Daten).</summary>
    public string Organization { get; init; } = string.Empty;

    /// <summary>Gibt an ob die IP eine private/RFC1918 Adresse ist.</summary>
    public bool IsPrivate { get; init; }

    /// <summary>Gibt an ob es sich um Loopback (127.x.x.x / ::1) handelt.</summary>
    public bool IsLoopback { get; init; }

    /// <summary>Bekannte Infrastruktur-Kategorie der IP.</summary>
    public IpInfrastructureCategory InfrastructureCategory { get; init; }

    /// <summary>Singleton für private IP-Adressen.</summary>
    public static GeoInfo PrivateNetwork(IPAddress ip) => new()
    {
        CountryCode = "LAN",
        CountryName = "Private Network",
        IsPrivate = true,
        InfrastructureCategory = IpInfrastructureCategory.Private
    };

    /// <summary>Singleton für Loopback-Adressen.</summary>
    public static readonly GeoInfo Loopback = new()
    {
        CountryCode = "LO",
        CountryName = "Loopback",
        IsLoopback = true,
        IsPrivate = true,
        InfrastructureCategory = IpInfrastructureCategory.Loopback
    };

    /// <summary>Singleton für unbekannte Adressen (z.B. DB nicht vorhanden).</summary>
    public static readonly GeoInfo Unknown = new()
    {
        CountryCode = "??",
        CountryName = "Unknown"
    };
}
