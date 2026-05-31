// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NetOverseer.Core.Models;
using Windows.ApplicationModel.DataTransfer;

namespace NetOverseer.App.ViewModels;

/// <summary>
/// Präsentationsmodell für eine einzelne Netzwerkverbindung in der Live-Ansicht.
/// Wird ausschließlich auf dem UI-Thread erstellt und aktualisiert (DispatcherQueue).
/// Alle Properties sind für x:Bind (Mode=OneWay) optimiert.
/// </summary>
public sealed partial class ConnectionItemViewModel : ObservableObject
{
    // ──────────────────────────────────────────────────────────────────────
    // Unveränderliche Identitätsdaten
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Eindeutiger Schlüssel: PID_Proto_Local_Remote.</summary>
    public string           ConnectionKey { get; }

    /// <summary>Transportprotokoll (TCP/UDP).</summary>
    public NetworkProtocol  Protocol      { get; }

    /// <summary>Zeitpunkt, zu dem diese Verbindung erstmals erfasst wurde.</summary>
    public DateTimeOffset   FirstSeen     { get; }

    /// <summary>Protokoll als Anzeigetext.</summary>
    public string           ProtocolText  => Protocol switch
    {
        NetworkProtocol.Tcp => "TCP",
        NetworkProtocol.Udp => "UDP",
        _                   => "?"
    };

    // ──────────────────────────────────────────────────────────────────────
    // Observable Properties (UI-Binding)
    // ──────────────────────────────────────────────────────────────────────

    [ObservableProperty] private string        _processDisplayName = string.Empty;
    [ObservableProperty] private BitmapImage?  _appIcon;
    [ObservableProperty] private string        _localAddress       = string.Empty;
    [ObservableProperty] private string        _remoteAddress      = string.Empty;
    [ObservableProperty] private string        _hostname           = string.Empty;
    [ObservableProperty] private string        _countryDisplay     = string.Empty;
    [ObservableProperty] private string        _reputationDisplay  = "–";
    [ObservableProperty] private string        _bytesUpDisplay     = "–";
    [ObservableProperty] private string        _bytesDownDisplay   = "–";
    [ObservableProperty] private string        _stateDisplay       = string.Empty;
    [ObservableProperty] private bool          _isAged;
    [ObservableProperty] private double        _itemOpacity        = 1.0;
    [ObservableProperty] private SolidColorBrush _rowBackground;

    // ──────────────────────────────────────────────────────────────────────
    // Rohdaten (für Detail-Dialog, nicht direkt gebunden)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// True wenn diese Verbindung als Microsoft-Telemetrie identifiziert wurde
    /// (über Hostname, Reputation oder IP-Range).
    /// </summary>
    public bool IsTelemetry =>
        _isMicrosoftTelemetry
        || ReputationCategory == ReputationCategory.MicrosoftTelemetry
        || InfrastructureCategory == IpInfrastructureCategory.MicrosoftTelemetry;

    /// <summary>Markiert diese Verbindung als Telemetrie-Endpunkt (Hostname-basiert).</summary>
    public void MarkAsTelemetry()
    {
        _isMicrosoftTelemetry = true;
        RefreshBackground();
    }

    public int                     ProcessId               { get; private set; }
    public string                  ProcessName             { get; private set; } = string.Empty;
    public string                  ExecutablePath          { get; private set; } = string.Empty;
    public string                  Publisher               { get; private set; } = string.Empty;
    public string                  CountryCode             { get; private set; } = string.Empty;
    public string                  CountryName             { get; private set; } = string.Empty;
    public string                  City                    { get; private set; } = string.Empty;
    public string                  Organization            { get; private set; } = string.Empty;
    public IpInfrastructureCategory InfrastructureCategory { get; private set; }
    public ReputationCategory      ReputationCategory      { get; private set; }
    public int                     ReputationScore         { get; private set; } = -1;
    public string?                 ReputationSource        { get; private set; }
    public ConnectionState         State                   { get; private set; }
    public DateTimeOffset          LastSeen                { get; private set; }
    private bool                   _isMicrosoftTelemetry;

    /// <summary>True, wenn die Remote-Adresse IPv6 ist (für IPv4/IPv6-Filter).</summary>
    public bool                    IsIpV6                  { get; private set; }

    // ──────────────────────────────────────────────────────────────────────
    // Hintergrundfarben (einmal erzeugt, dann wiederverwendet)
    // ──────────────────────────────────────────────────────────────────────

    private static readonly SolidColorBrush TransparentBrush =
        new(Colors.Transparent);
    private static readonly SolidColorBrush DangerBrush =
        new(Windows.UI.Color.FromArgb(40, 220, 50, 50));
    private static readonly SolidColorBrush SuspiciousBrush =
        new(Windows.UI.Color.FromArgb(40, 220, 140, 50));
    private static readonly SolidColorBrush TelemetryBrush =
        new(Windows.UI.Color.FromArgb(30, 50, 120, 220));

    // ──────────────────────────────────────────────────────────────────────
    // Konstruktor
    // ──────────────────────────────────────────────────────────────────────

    public ConnectionItemViewModel(ConnectionEvent ev)
    {
        ConnectionKey  = ev.ConnectionKey;
        Protocol       = ev.Protocol;
        FirstSeen      = ev.Timestamp;
        LastSeen       = ev.Timestamp;
        _rowBackground = TransparentBrush;

        _localAddress  = ev.LocalEndpoint.ToString();
        _remoteAddress = ev.RemoteEndpoint.ToString();
        _hostname      = ev.RemoteEndpoint.Address.ToString(); // DNS wird später aufgelöst
        _stateDisplay  = MapState(ev.State);
        State          = ev.State;
        IsIpV6         = ev.RemoteEndpoint.Address.AddressFamily
                              == System.Net.Sockets.AddressFamily.InterNetworkV6;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Update-Methoden (müssen auf dem UI-Thread aufgerufen werden)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Aktualisiert den Eintrag mit den neuesten Daten. Wird aus dem Batch-Timer heraus
    /// auf dem UI-Thread aufgerufen.
    /// </summary>
    public void Update(ConnectionEvent ev, GeoInfo geo, ProcessInfo proc, ReputationInfo rep)
    {
        LastSeen      = ev.Timestamp;
        LocalAddress  = ev.LocalEndpoint.ToString();
        RemoteAddress = ev.RemoteEndpoint.ToString();
        State         = ev.State;
        StateDisplay  = MapState(ev.State);
        IsIpV6        = ev.RemoteEndpoint.Address.AddressFamily
                            == System.Net.Sockets.AddressFamily.InterNetworkV6;

        if (ev.BytesSent > 0 || ev.BytesReceived > 0)
        {
            BytesUpDisplay   = FormatBytes(ev.BytesSent);
            BytesDownDisplay = FormatBytes(ev.BytesReceived);
        }

        ApplyGeo(geo);
        ApplyProcess(proc);
        ApplyReputation(rep);
        RefreshBackground();
    }

    /// <summary>Aktualisiert nur die Reputationsdaten (asynchroner Nachträger).</summary>
    public void UpdateReputation(ReputationInfo rep)
    {
        ApplyReputation(rep);
        RefreshBackground();
    }

    /// <summary>Schaltet den "veralteten" Zustand um (30 s ohne Aktivität → 60 % Deckkraft).</summary>
    public void SetAged(bool aged)
    {
        IsAged      = aged;
        ItemOpacity = aged ? 0.45 : 1.0;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Kontext-Menü-Befehle (direkt auf dem Item)
    // ──────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void CopyRemoteAddress()
    {
        var dp = new DataPackage();
        dp.SetText(RemoteAddress);
        Clipboard.SetContent(dp);
    }

    [RelayCommand]
    private void CopyHostname()
    {
        var dp = new DataPackage();
        dp.SetText(Hostname);
        Clipboard.SetContent(dp);
    }

    [RelayCommand]
    private async Task OpenWhois()
    {
        var ip = RemoteAddress.Contains(':')
            ? RemoteAddress[..RemoteAddress.LastIndexOf(':')]
            : RemoteAddress;
        await Windows.System.Launcher.LaunchUriAsync(
            new Uri($"https://www.whois.com/whois/{Uri.EscapeDataString(ip)}"));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Private Helpers
    // ──────────────────────────────────────────────────────────────────────

    private void ApplyGeo(GeoInfo geo)
    {
        CountryCode            = geo.CountryCode;
        CountryName            = geo.CountryName;
        City                   = geo.City;
        Organization           = geo.Organization;
        InfrastructureCategory = geo.InfrastructureCategory;

        CountryDisplay = (geo.IsPrivate || geo.IsLoopback) ? "🏠 Lokal"
            : geo.CountryCode is { Length: 2 } cc
                ? $"{ToFlagEmoji(cc)} {geo.CountryName}"
                : geo.CountryName;
    }

    private void ApplyProcess(ProcessInfo proc)
    {
        ProcessId          = proc.Pid;
        ProcessName        = proc.Name;
        ExecutablePath     = proc.ExecutablePath;
        Publisher          = proc.Publisher;
        ProcessDisplayName = proc.DisplayName;

        if (proc.IconPng?.Length > 0 && AppIcon is null)
            _ = LoadIconAsync(proc.IconPng);
    }

    private void ApplyReputation(ReputationInfo rep)
    {
        ReputationScore    = rep.Score;
        ReputationCategory = rep.Category;
        ReputationSource   = rep.Source;

        ReputationDisplay = rep.Category switch
        {
            ReputationCategory.Safe              => "✓ Sicher",
            ReputationCategory.Private           => "🏠 Lokal",
            ReputationCategory.MicrosoftTelemetry => "🪟 Telemetrie",
            ReputationCategory.Suspicious        => $"⚠ {rep.Score}%",
            ReputationCategory.Dangerous         => $"🚫 {rep.Score}%",
            _                                    => "–"
        };
    }

    private void RefreshBackground()
    {
        RowBackground = ReputationCategory switch
        {
            ReputationCategory.Dangerous          => DangerBrush,
            ReputationCategory.Suspicious         => SuspiciousBrush,
            ReputationCategory.MicrosoftTelemetry => TelemetryBrush,
            _ when InfrastructureCategory == IpInfrastructureCategory.MicrosoftTelemetry
                                                  => TelemetryBrush,
            _ when _isMicrosoftTelemetry          => TelemetryBrush,
            _                                     => TransparentBrush
        };
    }

    private async Task LoadIconAsync(byte[] iconPng)
    {
        try
        {
            using var ms  = new System.IO.MemoryStream(iconPng);
            var ras       = ms.AsRandomAccessStream();
            var bmp       = new BitmapImage();
            await bmp.SetSourceAsync(ras);
            AppIcon = bmp;
        }
        catch { /* Icon ist optional */ }
    }

    private static string MapState(ConnectionState state) => state switch
    {
        ConnectionState.Established => Services.LocalizationService.GetString("ConnState_Active"),
        ConnectionState.Listen      => Services.LocalizationService.GetString("ConnState_Listen"),
        ConnectionState.TimeWait    => Services.LocalizationService.GetString("ConnState_Closing"),
        ConnectionState.CloseWait   => Services.LocalizationService.GetString("ConnState_Closing"),
        ConnectionState.Closed      => Services.LocalizationService.GetString("ConnState_Closed"),
        _                           => "–"
    };

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024             => $"{bytes} B",
        < 1024 * 1024      => $"{bytes / 1024.0:F1} KB",
        _                  => $"{bytes / (1024.0 * 1024):F1} MB"
    };

    /// <summary>Wandelt einen zweistelligen ISO-Ländercode in das entsprechende Flaggen-Emoji um.</summary>
    private static string ToFlagEmoji(string code)
    {
        if (code.Length != 2) return "🌐";
        int c1 = char.ToUpperInvariant(code[0]) - 'A' + 0x1F1E6;
        int c2 = char.ToUpperInvariant(code[1]) - 'A' + 0x1F1E6;
        return char.ConvertFromUtf32(c1) + char.ConvertFromUtf32(c2);
    }
}
