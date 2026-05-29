// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Net;
using NetOverseer.Core.Models;

namespace NetOverseer.Core.Interfaces;

/// <summary>
/// Abstrahiert den Zugriff auf die Windows-Firewall via NetFwTypeLib COM API.
/// Alle Schreiboperationen erfordern Administrator-Rechte.
/// </summary>
public interface IFirewallService
{
    /// <summary>True, wenn die App mit Administrator-Rechten läuft.</summary>
    bool IsAdministrator { get; }

    /// <summary>Blockiert alle ausgehenden Verbindungen der angegebenen Anwendung.</summary>
    Task BlockApplicationAsync(string executablePath, string ruleName, CancellationToken ct = default);

    /// <summary>Blockiert alle ausgehenden Verbindungen zur angegebenen IP.</summary>
    Task BlockRemoteIpAsync(IPAddress ip, string ruleName, CancellationToken ct = default);

    /// <summary>Blockiert ausgehende Verbindungen einer Anwendung zu einer bestimmten IP.</summary>
    Task BlockApplicationToIpAsync(string executablePath, IPAddress ip, CancellationToken ct = default);

    /// <summary>Entfernt eine Firewall-Regel anhand ihres Namens.</summary>
    Task RemoveRuleAsync(string ruleName, CancellationToken ct = default);

    /// <summary>Aktiviert oder deaktiviert eine Firewall-Regel.</summary>
    Task SetRuleEnabledAsync(string ruleName, bool enabled, CancellationToken ct = default);

    /// <summary>Gibt alle von NetOverseer erstellten Regeln zurück (schnell).</summary>
    Task<IReadOnlyList<FirewallRuleInfo>> GetNetOverseerRulesAsync(CancellationToken ct = default);

    /// <summary>Gibt alle Windows-Firewall-Regeln zurück (kann langsam sein).</summary>
    Task<IReadOnlyList<FirewallRuleInfo>> GetAllRulesAsync(CancellationToken ct = default);

    /// <summary>
    /// Streamt alle Windows-Firewall-Regeln einzeln, sobald sie aus der COM-Enumeration
    /// gelesen werden. Erlaubt dem UI das progressive Anzeigen großer Regelmengen.
    /// </summary>
    IAsyncEnumerable<FirewallRuleInfo> StreamAllRulesAsync(CancellationToken ct = default);

    /// <summary>
    /// Aktualisiert eine bestehende Firewall-Regel anhand des aktuellen Namens.
    /// Nur Felder die in <paramref name="update"/> gesetzt sind werden geändert.
    /// </summary>
    Task UpdateRuleAsync(string currentName, FirewallRuleUpdate update, CancellationToken ct = default);

    /// <summary>
    /// Gibt nur die Firewall-Regeln zurück, deren <c>ApplicationName</c> exakt auf den
    /// übergebenen EXE-Pfad zeigt. Deutlich schneller als <see cref="GetAllRulesAsync"/>,
    /// weil pro Regel nur eine COM-Eigenschaft (ApplicationName) gelesen wird –
    /// erst bei einem Treffer wird die vollständige Regel materialisiert.
    /// Interne Implementierungen dürfen zusätzlich einen Kurzzeit-Cache nutzen.
    /// </summary>
    Task<IReadOnlyList<FirewallRuleInfo>> GetRulesForApplicationAsync(
        string executablePath, CancellationToken ct = default);
}
