// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Net;
using NetOverseer.Core.Models;

namespace NetOverseer.Core.Interfaces;

/// <summary>
/// In-Memory-Cache für DNS-Abfragen.
/// Ermöglicht die Korrelation von IP-Adressen mit Hostnamen in der Live-Ansicht.
/// Alle Methoden sind thread-safe.
/// </summary>
public interface IDnsCache
{
    /// <summary>
    /// Liefert den zuletzt aufgelösten Hostnamen für eine IP-Adresse,
    /// oder <c>null</c> wenn unbekannt.
    /// </summary>
    string? GetHostname(IPAddress ip);

    /// <summary>
    /// Liefert den zuletzt aufgelösten Hostnamen für eine IP-Adresse als String,
    /// oder <c>null</c> wenn unbekannt.
    /// </summary>
    string? GetHostname(string ip);

    /// <summary>Speichert ein DNS-Ereignis und aktualisiert den IP→Hostname-Index.</summary>
    void Record(DnsQueryEvent evt);

    /// <summary>Gibt die letzten <paramref name="maxCount"/> aufgezeichneten Ereignisse zurück (neueste zuerst).</summary>
    IReadOnlyList<DnsQueryEvent> GetRecentQueries(int maxCount = 500);
}
