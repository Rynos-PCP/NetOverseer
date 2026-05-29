// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Net;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.App.Services;

/// <summary>
/// Vorläufiger Reputations-Service ohne externe API-Aufrufe.
/// Wird in Phase 1 / Schritt 3 durch die vollständige AbuseIPDB-Implementierung ersetzt.
/// Kennt schon lokale Infrastruktur-Kategorien aus GeoInfo.
/// </summary>
internal sealed class StubReputationService : IReputationService
{
    private readonly IGeoLocationService _geo;

    public StubReputationService(IGeoLocationService geo) => _geo = geo;

    public Task<ReputationInfo> GetReputationAsync(IPAddress ip, CancellationToken ct = default)
    {
        var geo = _geo.Lookup(ip);

        if (geo.IsPrivate || geo.IsLoopback)
            return Task.FromResult(ReputationInfo.Private);

        if (geo.InfrastructureCategory == IpInfrastructureCategory.MicrosoftTelemetry)
            return Task.FromResult(new ReputationInfo
            {
                Score       = 0,
                Category    = ReputationCategory.MicrosoftTelemetry,
                Source      = "Built-in list",
                LastChecked = DateTimeOffset.UtcNow
            });

        // Alle anderen IPs als "unbekannt" zurückgeben – kein API-Call
        return Task.FromResult(ReputationInfo.Unknown);
    }
}
