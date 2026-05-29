// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Net;
using NetOverseer.Core.Models;

namespace NetOverseer.Core.Interfaces;

/// <summary>
/// Ermittelt den Reputations-Score einer Remote-IP-Adresse.
/// Implementierungen dürfen cachen und asynchrone Netzwerk-Aufrufe durchführen.
/// </summary>
public interface IReputationService
{
    /// <summary>
    /// Liefert Reputationsinformationen zur angegebenen IP-Adresse.
    /// Private/Loopback-Adressen werden sofort ohne Netzwerkzugriff beantwortet.
    /// </summary>
    Task<ReputationInfo> GetReputationAsync(IPAddress ip, CancellationToken ct = default);
}
