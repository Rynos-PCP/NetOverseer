// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
namespace NetOverseer.Core.Models;

/// <summary>
/// Beschreibt eine Änderung an einer bestehenden Firewall-Regel.
/// Nur Properties die nicht <c>null</c> sind werden auf das COM-Regelobjekt angewendet.
/// </summary>
public sealed class FirewallRuleUpdate
{
    /// <summary>Neuer Anzeigename der Regel (oder <c>null</c> für unverändert).</summary>
    public string? Name { get; init; }

    /// <summary>Neue Beschreibung.</summary>
    public string? Description { get; init; }

    /// <summary>Neuer Anwendungspfad ("" um zu löschen).</summary>
    public string? ApplicationPath { get; init; }

    /// <summary>Neue Richtung.</summary>
    public FirewallDirection? Direction { get; init; }

    /// <summary>Neue Aktion.</summary>
    public FirewallAction? Action { get; init; }

    /// <summary>Neues Protokoll.</summary>
    public FirewallProtocol? Protocol { get; init; }

    /// <summary>Neue lokale Ports.</summary>
    public string? LocalPorts { get; init; }

    /// <summary>Neue Remote-Ports.</summary>
    public string? RemotePorts { get; init; }

    /// <summary>Neue Remote-Adressen.</summary>
    public string? RemoteAddresses { get; init; }

    /// <summary>Aktivierungszustand.</summary>
    public bool? IsEnabled { get; init; }
}
