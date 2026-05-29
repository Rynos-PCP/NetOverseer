// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Net;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.Core.Interfaces;

/// <summary>
/// Verwaltet lokale IP-Blocklisten und erlaubt schnelle Membership-Abfragen.
/// </summary>
public interface IBlocklistService
{
    /// <summary>Gibt zurück, ob die IP-Adresse in einer der geladenen Blocklisten vorkommt.</summary>
    bool IsBlocked(IPAddress ip);

    /// <summary>Lädt/aktualisiert die Blocklisten aus dem Internet (Firehol Level 1).</summary>
    Task UpdateAsync(CancellationToken ct = default);

    /// <summary>Zeitpunkt der letzten Aktualisierung; null wenn keine Liste vorhanden.</summary>
    DateTimeOffset? LastUpdated { get; }
}
