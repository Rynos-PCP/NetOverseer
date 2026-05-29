// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.UI.Xaml.Controls;
using NetOverseer.App.ViewModels;
using NetOverseer.Core.Models;

namespace NetOverseer.App.Views;

/// <summary>
/// Modaler Detail-Dialog für eine ausgewählte Verbindung.
/// </summary>
public sealed partial class ConnectionDetailDialog : ContentDialog
{
    public ConnectionDetailDialog(ConnectionItemViewModel item)
    {
        InitializeComponent();
        Populate(item);
    }

    private void Populate(ConnectionItemViewModel item)
    {
        // ── Prozess ──────────────────────────────────────────────────────
        ProcessDisplayNameText.Text = item.ProcessDisplayName;
        ProcessExecutableText.Text  = string.IsNullOrEmpty(item.ExecutablePath)
            ? "(Pfad unbekannt)"
            : item.ExecutablePath;
        ProcessPidText.Text = $"PID {item.ProcessId}";

        if (item.AppIcon is not null)
            ProcessIcon.Source = item.AppIcon;

        // ── Verbindung ────────────────────────────────────────────────────
        ProtoText.Text   = item.ProtocolText;
        LocalText.Text   = item.LocalAddress;
        RemoteText.Text  = item.RemoteAddress;
        HostnameText.Text = item.Hostname;
        StatusText.Text  = item.StateDisplay;

        // ── Geolocation ───────────────────────────────────────────────────
        CountryText.Text = BuildCountryText(item);
        OrgText.Text     = string.IsNullOrEmpty(item.Organization) ? "–" : item.Organization;
        InfraText.Text   = MapInfraCategory(item.InfrastructureCategory);

        // ── Reputation ────────────────────────────────────────────────────
        RepText.Text       = BuildReputationText(item);
        RepSourceText.Text = item.ReputationSource ?? "–";

        // ── Zeitstempel ───────────────────────────────────────────────────
        var fmt       = "dd.MM.yyyy HH:mm:ss";
        var local     = TimeZoneInfo.Local;
        FirstSeenText.Text = item.FirstSeen.ToLocalTime().ToString(fmt);
        LastSeenText.Text  = item.LastSeen.ToLocalTime().ToString(fmt);
    }

    private static string BuildCountryText(ConnectionItemViewModel item)
    {
        if (item.CountryDisplay.StartsWith("🏠")) return "Lokales Netzwerk";

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(item.CountryName)) parts.Add(item.CountryName);
        if (!string.IsNullOrEmpty(item.City))         parts.Add(item.City);
        return parts.Count > 0 ? string.Join(", ", parts) : "–";
    }

    private static string BuildReputationText(ConnectionItemViewModel item) =>
        item.ReputationCategory switch
        {
            ReputationCategory.Safe              => "✓ Vertrauenswürdig",
            ReputationCategory.Private           => "🏠 Privates Netzwerk",
            ReputationCategory.MicrosoftTelemetry => "🪟 Microsoft-Telemetrie",
            ReputationCategory.Suspicious        => $"⚠ Verdächtig ({item.ReputationScore}% Missbrauchs-Score)",
            ReputationCategory.Dangerous         => $"🚫 Gefährlich ({item.ReputationScore}% Missbrauchs-Score)",
            _                                    => "– Noch nicht abgefragt"
        };

    private static string MapInfraCategory(IpInfrastructureCategory cat) => cat switch
    {
        IpInfrastructureCategory.Private           => "Privates Netzwerk",
        IpInfrastructureCategory.Loopback          => "Loopback",
        IpInfrastructureCategory.MicrosoftCloud    => "Microsoft Cloud",
        IpInfrastructureCategory.MicrosoftTelemetry => "Microsoft Telemetrie",
        IpInfrastructureCategory.GoogleCloud       => "Google Cloud",
        IpInfrastructureCategory.AwsCloud          => "Amazon Web Services",
        IpInfrastructureCategory.Cloudflare        => "Cloudflare",
        IpInfrastructureCategory.AkamaiCdn         => "Akamai CDN",
        IpInfrastructureCategory.TorExitNode       => "Tor Exit Node",
        IpInfrastructureCategory.Public            => "Öffentlich",
        _                                          => "–"
    };
}
