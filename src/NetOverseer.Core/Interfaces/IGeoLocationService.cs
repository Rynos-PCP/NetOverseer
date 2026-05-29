// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Net;
using NetOverseer.Core.Models;

namespace NetOverseer.Core.Interfaces;

/// <summary>
/// Ermittelt Geolocation-Informationen zu IP-Adressen aus einer lokalen MaxMind-Datenbank.
/// </summary>
public interface IGeoLocationService
{
    /// <summary>
    /// Synchrone Geolocation-Abfrage (muss unter 1ms bleiben, da im heißen Pfad genutzt).
    /// Nutzt lokale MaxMind GeoLite2 Datenbank – kein Netzwerkzugriff.
    /// </summary>
    /// <param name="ip">Die aufzulösende IP-Adresse.</param>
    /// <returns>
    /// Geolocation-Informationen oder <see cref="GeoInfo.Unknown"/> wenn die IP
    /// nicht in der Datenbank gefunden wird oder die DB nicht geladen ist.
    /// </returns>
    GeoInfo Lookup(IPAddress ip);

    /// <summary>Gibt an ob die GeoLite2-Datenbank erfolgreich geladen ist.</summary>
    bool IsDatabaseLoaded { get; }

    /// <summary>Pfad zur aktuell geladenen Datenbankdatei.</summary>
    string? DatabasePath { get; }
}
