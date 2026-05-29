// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace NetOverseer.App.ViewModels;

// ──────────────────────────────────────────────────────────────────────────────
// Interner Datensatz – thread-safe, mutiert über AddOrUpdate-Pattern.
// ──────────────────────────────────────────────────────────────────────────────
internal sealed record TelRecord(
    string         ServiceName,
    string         Hostname,
    string         RemoteIp,
    string         CountryCode,
    string         ProcessName,
    int            ProcessId,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    long           BytesSent,
    long           BytesReceived,
    int            HitCount,
    bool           IpDetectedOnly);

// ──────────────────────────────────────────────────────────────────────────────
// Einzelner Eintrag (UI-Binding).
// ──────────────────────────────────────────────────────────────────────────────
public sealed partial class TelemetryEntryViewModel : ObservableObject
{
    public string ServiceName    { get; private set; }
    public string Hostname       { get; private set; }
    public string RemoteIp       { get; private set; }
    public string CountryCode    { get; private set; }
    public string ProcessName    { get; private set; }
    public int    ProcessId      { get; private set; }
    public int    HitCount       { get; private set; }
    public string BytesDisplay   { get; private set; }
    public string LastSeenText   { get; private set; }
    public string FirstSeenText  { get; private set; }
    public SolidColorBrush ServiceBadgeBrush { get; private set; }
    public string         HostnameDisplay   { get; private set; }

    internal string Key { get; }

    internal TelemetryEntryViewModel(string key, TelRecord r)
    {
        Key               = key;
        ServiceName       = r.ServiceName;
        Hostname          = r.Hostname;
        RemoteIp          = r.RemoteIp;
        CountryCode       = string.IsNullOrEmpty(r.CountryCode) ? "—" : r.CountryCode;
        ProcessName       = r.ProcessName;
        ProcessId         = r.ProcessId;
        HitCount          = r.HitCount;
        BytesDisplay      = FormatBytes(r.BytesSent + r.BytesReceived);
        LastSeenText      = r.LastSeen.LocalDateTime.ToString("HH:mm:ss");
        FirstSeenText     = r.FirstSeen.LocalDateTime.ToString("HH:mm:ss");
        HostnameDisplay   = r.IpDetectedOnly ? "(nur per IP erkannt)" : r.Hostname;
        ServiceBadgeBrush = BrushForService(r.ServiceName);
    }

    internal void UpdateFrom(TelRecord r)
    {
        if (ServiceName != r.ServiceName)
        {
            ServiceName       = r.ServiceName;
            ServiceBadgeBrush = BrushForService(r.ServiceName);
            OnPropertyChanged(nameof(ServiceName));
            OnPropertyChanged(nameof(ServiceBadgeBrush));
        }

        if (Hostname != r.Hostname)
        {
            Hostname        = r.Hostname;
            HostnameDisplay = r.IpDetectedOnly ? "(nur per IP erkannt)" : r.Hostname;
            OnPropertyChanged(nameof(Hostname));
            OnPropertyChanged(nameof(HostnameDisplay));
        }

        var bytes = FormatBytes(r.BytesSent + r.BytesReceived);
        if (BytesDisplay != bytes)
        {
            BytesDisplay = bytes;
            OnPropertyChanged(nameof(BytesDisplay));
        }

        if (HitCount != r.HitCount)
        {
            HitCount = r.HitCount;
            OnPropertyChanged(nameof(HitCount));
        }

        var last = r.LastSeen.LocalDateTime.ToString("HH:mm:ss");
        if (LastSeenText != last)
        {
            LastSeenText = last;
            OnPropertyChanged(nameof(LastSeenText));
        }

        var cc = string.IsNullOrEmpty(r.CountryCode) ? "—" : r.CountryCode;
        if (CountryCode != cc)
        {
            CountryCode = cc;
            OnPropertyChanged(nameof(CountryCode));
        }
    }

    // ── Kontextmenü-Befehle ──────────────────────────────────────────────

    [RelayCommand]
    private void CopyHostname()
        => CopyToClipboard(string.IsNullOrEmpty(Hostname) ? RemoteIp : Hostname);

    [RelayCommand]
    private void CopyRemoteIp() => CopyToClipboard(RemoteIp);

    [RelayCommand]
    private async Task OpenWhoisAsync()
    {
        var target = string.IsNullOrEmpty(Hostname) ? RemoteIp : Hostname;
        await Windows.System.Launcher.LaunchUriAsync(
            new Uri($"https://www.whois.com/whois/{Uri.EscapeDataString(target)}"));
    }

    private static void CopyToClipboard(string value)
    {
        var pkg = new DataPackage();
        pkg.SetText(value);
        Clipboard.SetContent(pkg);
    }

    // ── Helfer ───────────────────────────────────────────────────────────

    private static string FormatBytes(long b) => b switch
    {
        < 1024              => $"{b} B",
        < 1024 * 1024       => $"{b / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{b / (1024.0 * 1024):F1} MB",
        _                   => $"{b / (1024.0 * 1024 * 1024):F2} GB"
    };

    internal static SolidColorBrush BrushForService(string service)
    {
        var (r, g, b) = service switch
        {
            "DiagTrack"                       => ((byte)0xFF, (byte)0x8A, (byte)0x00),
            "Windows Update"                  => ((byte)0x00, (byte)0x78, (byte)0xD4),
            "Windows Defender"                => ((byte)0x10, (byte)0x7C, (byte)0x10),
            "Windows Fehlerberichterstattung" => ((byte)0xD1, (byte)0x34, (byte)0x38),
            "Windows Konfiguration"           => ((byte)0x5C, (byte)0x2D, (byte)0x91),
            "OneDrive"                        => ((byte)0x03, (byte)0x64, (byte)0xB8),
            "Office Telemetrie"               => ((byte)0xD8, (byte)0x3B, (byte)0x01),
            "Microsoft Account"               => ((byte)0x44, (byte)0x47, (byte)0x91),
            "Microsoft Cloud"                 => ((byte)0x5C, (byte)0x5C, (byte)0x5C),
            _                                  => ((byte)0x88, (byte)0x88, (byte)0x88),
        };
        return new SolidColorBrush(Color.FromArgb(0xFF, r, g, b));
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Dienstfilter-Chip (Service-Zusammenfassung im Header).
// ──────────────────────────────────────────────────────────────────────────────
public sealed partial class TelemetryServiceChipViewModel : ObservableObject
{
    public string          ServiceName { get; }
    public SolidColorBrush Brush       { get; }

    [ObservableProperty] private int    _hitCount;
    [ObservableProperty] private string _bytesDisplay = "0 B";
    [ObservableProperty] private bool   _isActiveFilter;

    public TelemetryServiceChipViewModel(string name, SolidColorBrush brush)
    {
        ServiceName = name;
        Brush       = brush;
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// TelemetryViewModel – Haupt-VM der Windows-Telemetrie-Seite.
//
// Architektur:
//   • Singleton (DI). Subscribe direkt im Konstruktor → keine verpassten
//     Events, auch wenn der Tab nie geöffnet wird.
//   • Eingehende ConnectionEvents werden gefiltert (Hostname-Match ODER
//     bekannter Microsoft-IP-Range) und in einem ConcurrentDictionary aggregiert.
//   • DnsQueryEvents werden zusätzlich abonniert, damit später aufgelöste
//     Hostnames die Service-Klassifikation retroaktiv aktualisieren.
//   • Ein DispatcherQueueTimer (2s) gleicht die UI-ObservableCollection mit
//     dem Backing-Store ab – nur die nötigen Add/Remove/Update-Aufrufe.
// ──────────────────────────────────────────────────────────────────────────────
public sealed partial class TelemetryViewModel : ObservableObject, IDisposable
{
    // ── Services ──────────────────────────────────────────────────────────
    private readonly INetworkCapture             _capture;
    private readonly IDnsCapture                 _dnsCapture;
    private readonly IDnsCache                   _dnsCache;
    private readonly IMicrosoftTelemetryService  _telemetryService;
    private readonly IGeoLocationService         _geoService;
    private readonly ILogger<TelemetryViewModel> _logger;

    // ── Zustand ───────────────────────────────────────────────────────────
    private readonly ConcurrentDictionary<string, TelRecord> _records = new();

    private DispatcherQueue?      _queue;
    private DispatcherQueueTimer? _refreshTimer;
    private IDisposable?          _connectionSub;
    private IDisposable?          _dnsSub;
    private bool                  _disposed;

    // ── Observable-Properties ─────────────────────────────────────────────
    [ObservableProperty] private int    _totalConnectionCount;
    [ObservableProperty] private int    _uniqueHostCount;
    [ObservableProperty] private int    _activeServiceCount;
    [ObservableProperty] private string _totalBytesDisplay = "0 B";
    [ObservableProperty] private bool   _isEmpty = true;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string? _serviceFilter;
    [ObservableProperty] private string  _statusText = "Bereit – warte auf Telemetrie-Verbindungen …";

    public Visibility EmptyStateVisibility => IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ContentVisibility    => IsEmpty ? Visibility.Collapsed : Visibility.Visible;

    partial void OnIsEmptyChanged(bool value)
    {
        OnPropertyChanged(nameof(EmptyStateVisibility));
        OnPropertyChanged(nameof(ContentVisibility));
    }

    partial void OnSearchTextChanged(string value)        => _queue?.TryEnqueue(RefreshUi);
    partial void OnServiceFilterChanged(string? value)    => _queue?.TryEnqueue(RefreshUi);

    public ObservableCollection<TelemetryEntryViewModel>      Entries  { get; } = [];
    public ObservableCollection<TelemetryServiceChipViewModel> Services { get; } = [];

    // ── Konstruktor (subscribed sofort) ───────────────────────────────────
    public TelemetryViewModel(
        INetworkCapture              capture,
        IDnsCapture                  dnsCapture,
        IDnsCache                    dnsCache,
        IMicrosoftTelemetryService   telemetryService,
        IGeoLocationService          geoService,
        ILogger<TelemetryViewModel>  logger)
    {
        _capture          = capture;
        _dnsCapture       = dnsCapture;
        _dnsCache         = dnsCache;
        _telemetryService = telemetryService;
        _geoService       = geoService;
        _logger           = logger;

        _connectionSub = _capture.Connections.Subscribe(
            onNext:  OnConnectionEvent,
            onError: ex => _logger.LogError(ex, "Fehler im Verbindungsstream (Telemetrie)"));

        _dnsSub = _dnsCapture.Queries.Subscribe(
            onNext:  OnDnsEvent,
            onError: ex => _logger.LogError(ex, "Fehler im DNS-Stream (Telemetrie)"));

        _logger.LogInformation("TelemetryViewModel aktiv – Streams abonniert.");
    }

    /// <summary>Startet den UI-Refresh-Timer auf dem angegebenen Dispatcher.</summary>
    public void Initialize(DispatcherQueue queue)
    {
        if (_queue is not null) return;
        _queue = queue;

        _refreshTimer          = queue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(2);
        _refreshTimer.Tick    += (_, _) => RefreshUi();
        _refreshTimer.Start();

        RefreshUi();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Event-Verarbeitung (Hintergrund-Thread)
    // ──────────────────────────────────────────────────────────────────────

    private void OnConnectionEvent(ConnectionEvent ev)
    {
        try
        {
            var ip = ev.RemoteEndpoint.Address;
            if (!IsRoutable(ip)) return;

            var hostname = _dnsCache.GetHostname(ip);
            var (isTelemetry, service, ipOnly) = Classify(hostname, ip);
            if (!isTelemetry) return;

            var geo = _geoService.Lookup(ip);
            var key = $"{ev.ProcessId}|{ip}";

            _records.AddOrUpdate(
                key,
                _ => new TelRecord(
                    ServiceName:    service,
                    Hostname:       hostname ?? string.Empty,
                    RemoteIp:       ip.ToString(),
                    CountryCode:    geo.CountryCode,
                    ProcessName:    ev.ProcessName,
                    ProcessId:      ev.ProcessId,
                    FirstSeen:      ev.Timestamp,
                    LastSeen:       ev.Timestamp,
                    BytesSent:      ev.BytesSent,
                    BytesReceived:  ev.BytesReceived,
                    HitCount:       1,
                    IpDetectedOnly: ipOnly),
                (_, old) => old with
                {
                    Hostname       = string.IsNullOrEmpty(old.Hostname) && hostname is not null
                                        ? hostname : old.Hostname,
                    ServiceName    = service != "Microsoft Cloud" || old.ServiceName == "Microsoft Cloud"
                                        ? service : old.ServiceName,
                    LastSeen       = ev.Timestamp,
                    BytesSent      = old.BytesSent     + ev.BytesSent,
                    BytesReceived  = old.BytesReceived + ev.BytesReceived,
                    HitCount       = old.HitCount      + 1,
                    IpDetectedOnly = ipOnly && old.IpDetectedOnly,
                });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Fehler bei Telemetrie-ConnectionEvent");
        }
    }

    /// <summary>Wird ein passender DNS-Eintrag aufgelöst, klassifizieren wir
    /// bestehende „nur per IP" erkannte Records nachträglich.</summary>
    private void OnDnsEvent(DnsQueryEvent ev)
    {
        try
        {
            if (string.IsNullOrEmpty(ev.QueryName)) return;
            if (!_telemetryService.IsTelemetryHost(ev.QueryName)) return;

            var service = _telemetryService.GetServiceName(ev.QueryName);

            foreach (var ipStr in ev.ResolvedAddresses)
            {
                foreach (var kvp in _records)
                {
                    if (!kvp.Value.RemoteIp.Equals(ipStr, StringComparison.OrdinalIgnoreCase)) continue;

                    _records[kvp.Key] = kvp.Value with
                    {
                        Hostname       = ev.QueryName,
                        ServiceName    = service,
                        IpDetectedOnly = false,
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Fehler bei Telemetrie-DnsEvent");
        }
    }

    // ── Klassifikation ────────────────────────────────────────────────────

    /// <returns>(istTelemetrie, dienstName, nurPerIpErkannt)</returns>
    private (bool, string, bool) Classify(string? hostname, IPAddress ip)
    {
        // 1) Hostname-Match (präferiert)
        if (!string.IsNullOrEmpty(hostname) && _telemetryService.IsTelemetryHost(hostname))
            return (true, _telemetryService.GetServiceName(hostname), false);

        // 2) IP-Range-Match: GeoLocationService kennt MicrosoftCloud-Bereiche.
        var geo = _geoService.Lookup(ip);
        switch (geo.InfrastructureCategory)
        {
            case IpInfrastructureCategory.MicrosoftTelemetry:
                return (true, "DiagTrack", string.IsNullOrEmpty(hostname));
            case IpInfrastructureCategory.MicrosoftCloud:
                // Nur als generische Microsoft-Cloud zählen, wenn kein Hostname
                // bekannt ist (sonst würde z.B. github.com fälschlich gematcht).
                if (string.IsNullOrEmpty(hostname))
                    return (true, "Microsoft Cloud", true);
                return (false, string.Empty, false);
            default:
                return (false, string.Empty, false);
        }
    }

    private static bool IsRoutable(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))         return false;
        if (ip.Equals(IPAddress.Any))         return false;
        if (ip.Equals(IPAddress.IPv6Any))     return false;
        if (ip.IsIPv6LinkLocal)               return false;
        if (ip.IsIPv6Multicast)               return false;
        if (ip.IsIPv6SiteLocal)               return false;

        // IPv4 link-local (169.254.x.x)
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            if (bytes[0] == 169 && bytes[1] == 254) return false;
        }
        return true;
    }

    // ──────────────────────────────────────────────────────────────────────
    // UI-Refresh (Dispatcher-Thread, 2 s)
    // ──────────────────────────────────────────────────────────────────────

    private void RefreshUi()
    {
        var snapshot = _records.ToArray();

        TotalConnectionCount = snapshot.Length;
        UniqueHostCount      = snapshot
            .Select(kv => string.IsNullOrEmpty(kv.Value.Hostname) ? kv.Value.RemoteIp : kv.Value.Hostname)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        TotalBytesDisplay    = FormatBytes(snapshot.Sum(kv => kv.Value.BytesSent + kv.Value.BytesReceived));

        // ── Service-Chips aktualisieren ───────────────────────────────────
        var byService = snapshot
            .GroupBy(kv => kv.Value.ServiceName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(kv => kv.Value.HitCount))
            .ToList();

        ActiveServiceCount = byService.Count;

        var existingChips = Services.ToDictionary(s => s.ServiceName, StringComparer.OrdinalIgnoreCase);
        var seenServices  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var g in byService)
        {
            seenServices.Add(g.Key);
            if (!existingChips.TryGetValue(g.Key, out var chip))
            {
                chip = new TelemetryServiceChipViewModel(g.Key, TelemetryEntryViewModel.BrushForService(g.Key));
                Services.Add(chip);
            }
            chip.HitCount       = g.Sum(kv => kv.Value.HitCount);
            chip.BytesDisplay   = FormatBytes(g.Sum(kv => kv.Value.BytesSent + kv.Value.BytesReceived));
            chip.IsActiveFilter = string.Equals(g.Key, ServiceFilter, StringComparison.OrdinalIgnoreCase);
        }
        for (int i = Services.Count - 1; i >= 0; i--)
        {
            if (!seenServices.Contains(Services[i].ServiceName))
                Services.RemoveAt(i);
        }

        // ── Einträge filtern & sortieren ──────────────────────────────────
        IEnumerable<KeyValuePair<string, TelRecord>> filtered = snapshot;

        if (!string.IsNullOrEmpty(ServiceFilter))
            filtered = filtered.Where(kv =>
                string.Equals(kv.Value.ServiceName, ServiceFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim();
            filtered = filtered.Where(kv =>
                   kv.Value.Hostname.Contains(q,    StringComparison.OrdinalIgnoreCase)
                || kv.Value.RemoteIp.Contains(q,    StringComparison.OrdinalIgnoreCase)
                || kv.Value.ProcessName.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var sorted = filtered
            .OrderByDescending(kv => kv.Value.LastSeen)
            .ToList();

        IsEmpty   = TotalConnectionCount == 0;
        StatusText = TotalConnectionCount == 0
            ? "Bereit – warte auf Telemetrie-Verbindungen …"
            : $"{TotalConnectionCount} Verbindungen • {UniqueHostCount} Hosts • {ActiveServiceCount} Dienste";

        // ── ObservableCollection diff-mergen ──────────────────────────────
        var existingEntries = Entries.ToDictionary(e => e.Key, StringComparer.Ordinal);
        var sortedKeys      = sorted.Select(kv => kv.Key).ToList();
        var sortedSet       = new HashSet<string>(sortedKeys, StringComparer.Ordinal);

        // Entferne, was nicht mehr durchs Filter passt
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            if (!sortedSet.Contains(Entries[i].Key))
                Entries.RemoveAt(i);
        }

        // Update / Insert in sortierter Reihenfolge
        for (int target = 0; target < sortedKeys.Count; target++)
        {
            var key    = sortedKeys[target];
            var record = sorted[target].Value;

            if (existingEntries.TryGetValue(key, out var existing))
            {
                existing.UpdateFrom(record);
                var currentIndex = Entries.IndexOf(existing);
                if (currentIndex != target)
                {
                    Entries.Move(currentIndex, target);
                }
            }
            else
            {
                Entries.Insert(target, new TelemetryEntryViewModel(key, record));
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Befehle
    // ──────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleServiceFilter(string? service)
    {
        if (string.IsNullOrEmpty(service))
        {
            ServiceFilter = null;
            return;
        }
        ServiceFilter = string.Equals(ServiceFilter, service, StringComparison.OrdinalIgnoreCase)
            ? null
            : service;
    }

    [RelayCommand]
    private void ClearFilter()
    {
        ServiceFilter = null;
        SearchText    = string.Empty;
    }

    [RelayCommand]
    private void ClearData()
    {
        _records.Clear();
        Entries.Clear();
        Services.Clear();
        TotalConnectionCount = 0;
        UniqueHostCount      = 0;
        ActiveServiceCount   = 0;
        TotalBytesDisplay    = "0 B";
        IsEmpty              = true;
        StatusText           = "Daten gelöscht – warte auf neue Verbindungen …";
    }

    [RelayCommand]
    private async Task ExportReportAsync()
    {
        try
        {
            var snapshot = _records.Values
                .OrderBy(r => r.ServiceName)
                .ThenByDescending(r => r.HitCount)
                .ToList();

            var html         = BuildHtmlReport(snapshot);
            var downloadsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(downloadsDir);
            var filePath     = Path.Combine(
                downloadsDir, $"NetOverseer-Telemetrie-{DateTime.Now:yyyyMMdd-HHmmss}.html");

            await File.WriteAllTextAsync(filePath, html, Encoding.UTF8);
            await Windows.System.Launcher.LaunchUriAsync(
                new Uri($"file:///{filePath.Replace('\\', '/')}"));

            StatusText = $"Bericht erstellt: {filePath}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Exportieren des Telemetrie-Berichts");
            StatusText = "Export fehlgeschlagen – siehe Log.";
        }
    }

    // ── HTML-Bericht ──────────────────────────────────────────────────────

    private static string BuildHtmlReport(List<TelRecord> records)
    {
        var sb  = new StringBuilder();
        var now = DateTime.Now;
        var uniqueHosts = records
            .Select(r => string.IsNullOrEmpty(r.Hostname) ? r.RemoteIp : r.Hostname)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();

        sb.Append($$"""
            <!DOCTYPE html>
            <html lang="de">
            <head>
              <meta charset="utf-8"/>
              <title>NetOverseer – Telemetrie-Bericht</title>
              <style>
                body   { font-family:"Segoe UI",Arial,sans-serif; margin:40px; color:#222; }
                h1     { color:#0078d4; margin-bottom:4px; }
                .sub   { color:#666; margin-bottom:24px; }
                .cards { display:flex; gap:16px; margin-bottom:28px; flex-wrap:wrap; }
                .card  { background:#f0f6ff; border:1px solid #bcd4f0; border-radius:8px;
                         padding:16px 24px; min-width:140px; }
                .cv    { font-size:28px; font-weight:bold; color:#0078d4; }
                .cl    { font-size:13px; color:#555; margin-top:2px; }
                h2     { color:#0078d4; border-bottom:2px solid #0078d4;
                         padding-bottom:4px; margin-top:28px; }
                table  { width:100%; border-collapse:collapse; font-size:13px; }
                th     { background:#0078d4; color:#fff; padding:8px 12px; text-align:left; }
                td     { padding:6px 12px; border-bottom:1px solid #e0e0e0; }
                tr:hover td { background:#f0f6ff; }
                .foot  { margin-top:40px; color:#999; font-size:12px; }
              </style>
            </head>
            <body>
            <h1>NetOverseer – Telemetrie-Bericht</h1>
            <p class="sub">Erstellt am {{now:dd.MM.yyyy}} um {{now:HH:mm:ss}} Uhr</p>
            <div class="cards">
              <div class="card"><div class="cv">{{records.Count}}</div><div class="cl">Verbindungen</div></div>
              <div class="card"><div class="cv">{{uniqueHosts}}</div><div class="cl">Eindeutige Hosts</div></div>
              <div class="card"><div class="cv">{{FormatBytes(records.Sum(r => r.BytesSent + r.BytesReceived))}}</div><div class="cl">Übertragen</div></div>
            </div>
            """);

        foreach (var group in records
            .GroupBy(r => r.ServiceName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key))
        {
            sb.Append($"<h2>{WebUtility.HtmlEncode(group.Key)}</h2>\n");
            sb.Append("""
                <table>
                  <thead><tr>
                    <th>Hostname</th><th>Remote-IP</th><th>Land</th><th>Prozess</th>
                    <th>PID</th><th>Treffer</th>
                    <th>Zuerst</th><th>Zuletzt</th><th>Übertragen</th>
                  </tr></thead>
                  <tbody>
                """);
            foreach (var r in group.OrderByDescending(r => r.HitCount))
            {
                var host = string.IsNullOrEmpty(r.Hostname) ? "(nur per IP)" : r.Hostname;
                sb.Append($"""
                    <tr>
                      <td>{WebUtility.HtmlEncode(host)}</td>
                      <td>{WebUtility.HtmlEncode(r.RemoteIp)}</td>
                      <td>{WebUtility.HtmlEncode(r.CountryCode)}</td>
                      <td>{WebUtility.HtmlEncode(r.ProcessName)}</td>
                      <td>{r.ProcessId}</td>
                      <td>{r.HitCount}</td>
                      <td>{r.FirstSeen.LocalDateTime:HH:mm:ss}</td>
                      <td>{r.LastSeen.LocalDateTime:HH:mm:ss}</td>
                      <td>{FormatBytes(r.BytesSent + r.BytesReceived)}</td>
                    </tr>
                    """);
            }
            sb.Append("  </tbody>\n</table>\n");
        }

        sb.Append("""
            <p class="foot">Generiert von NetOverseer</p>
            </body>
            </html>
            """);

        return sb.ToString();
    }

    // ── Hilfsfunktionen ───────────────────────────────────────────────────

    private static string FormatBytes(long b) => b switch
    {
        < 1024              => $"{b} B",
        < 1024 * 1024       => $"{b / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{b / (1024.0 * 1024):F1} MB",
        _                   => $"{b / (1024.0 * 1024 * 1024):F2} GB"
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refreshTimer?.Stop();
        _connectionSub?.Dispose();
        _dnsSub?.Dispose();
    }
}
