// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
namespace NetOverseer.Core.Models;

/// <summary>Richtung einer Firewall-Regel.</summary>
public enum FirewallDirection
{
    Inbound,
    Outbound
}

/// <summary>Aktion einer Firewall-Regel.</summary>
public enum FirewallAction
{
    Allow,
    Block
}

/// <summary>Protokoll einer Firewall-Regel.</summary>
public enum FirewallProtocol
{
    Any,
    Tcp,
    Udp,
    Icmp
}

/// <summary>Beschreibt eine einzelne Windows-Firewall-Regel.</summary>
public sealed class FirewallRuleInfo
{
    /// <summary>Interner Name der Regel (eindeutig in der Firewall).</summary>
    public required string Name { get; init; }

    /// <summary>Optionale Beschreibung der Regel.</summary>
    public string? Description { get; init; }

    /// <summary>Vollständiger Pfad zur Anwendungs-EXE (oder null für globale Regeln).</summary>
    public string? ApplicationPath { get; init; }

    /// <summary>Anzeigename der Anwendung (Dateiname ohne Erweiterung).</summary>
    public string? ApplicationName { get; init; }

    /// <summary>Regelrichtung: eingehend oder ausgehend.</summary>
    public FirewallDirection Direction { get; init; }

    /// <summary>Aktion: erlauben oder blockieren.</summary>
    public FirewallAction Action { get; init; }

    /// <summary>Lokale Ports (z.B. "80,443" oder "*").</summary>
    public string? LocalPorts { get; init; }

    /// <summary>Remote-Ports.</summary>
    public string? RemotePorts { get; init; }

    /// <summary>Remote-IP-Adressen oder CIDR-Bereiche.</summary>
    public string? RemoteAddresses { get; init; }

    /// <summary>Protokoll der Regel.</summary>
    public FirewallProtocol Protocol { get; init; }

    /// <summary>Ob die Regel aktiv ist.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>True, wenn die Anwendungs-EXE nicht mehr gefunden wird.</summary>
    public bool IsOrphaned { get; init; }

    /// <summary>True, wenn die Regel von NetOverseer erstellt wurde.</summary>
    public bool IsNetOverseerRule { get; init; }

    /// <summary>Gruppen-Name aus der Windows Firewall (Grouping).</summary>
    public string? GroupName { get; init; }
}
