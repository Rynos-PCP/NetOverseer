// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.App.ViewModels;

/// <summary>
/// ViewModel für die Live-Verbindungsansicht.
/// Empfängt Events vom Capture-Service (Hintergrund-Thread), batcht sie in
/// 200ms-Intervallen und dispatcht Änderungen thread-safe auf den UI-Thread.
/// </summary>
public sealed partial class LiveConnectionsViewModel : ObservableObject, IDisposable
{
    // ──────────────────────────────────────────────────────────────────────
    // Dienste
    // ──────────────────────────────────────────────────────────────────────

    private readonly INetworkCapture       _capture;
    private readonly IProcessResolver      _processResolver;
    private readonly IGeoLocationService   _geoService;
    private readonly IReputationService    _reputationService;
    private readonly IDnsCache             _dnsCache;
    private readonly IMicrosoftTelemetryService _telemetryService;
    private readonly ILogger<LiveConnectionsViewModel> _logger;

    // ──────────────────────────────────────────────────────────────────────
    // Threading & Timer
    // ──────────────────────────────────────────────────────────────────────

    private DispatcherQueue?      _dispatcherQueue;
    private DispatcherQueueTimer? _flushTimer;   // 200ms – UI-Updates
    private DispatcherQueueTimer? _agingTimer;   // 5s   – Verbindungen ausgrauen/entfernen

    // ──────────────────────────────────────────────────────────────────────
    // Datenspeicher
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Alle bekannten Verbindungen, indiziert nach ConnectionKey.</summary>
    private readonly Dictionary<string, ConnectionItemViewModel> _allItems = new();

    /// <summary>Eingehende Events, thread-safe gepuffert.</summary>
    private readonly ConcurrentQueue<ConnectionEvent> _pendingQueue = new();

    private IDisposable? _captureSubscription;
    private bool         _disposed;

    // ──────────────────────────────────────────────────────────────────────
    // Öffentliche Collections & Properties
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Gefilterte Verbindungen – wird an die ListView gebunden.</summary>
    public ObservableCollection<ConnectionItemViewModel> FilteredConnections { get; } = [];

    /// <summary>
    /// Seitenleiste "Firewall-Regeln dieser App". Wird per Rechtsklick im
    /// Kontextmenü geöffnet und gleitet von rechts ins Bild (Azure-Style-Blade).
    /// </summary>
    public AppFirewallRulesViewModel AppFirewallRules { get; }
    /// <summary>
    /// True wenn die GeoLite2-Datenbank nicht geladen werden konnte.
    /// Steuert die InfoBar-Sichtbarkeit in LiveConnectionsPage.
    /// </summary>
    public bool ShowGeoDbMissingWarning => !_geoService.IsDatabaseLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionCountText))]
    private int _connectionCount;

    [ObservableProperty]
    private int _filteredCount;

    public string ConnectionCountText =>
        FilteredCount == ConnectionCount
            ? $"{ConnectionCount} aktive Verbindungen"
            : $"{FilteredCount} / {ConnectionCount} aktiv";

    // ── Filter ──────────────────────────────────────────────────────────

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                RebuildFilteredList();
        }
    }

    private string _filterProtocol = "All"; // "All" | "TCP" | "UDP"
    public string FilterProtocol
    {
        get => _filterProtocol;
        set
        {
            if (SetProperty(ref _filterProtocol, value))
                RebuildFilteredList();
        }
    }

    [ObservableProperty] private bool _filterTcpActive;
    [ObservableProperty] private bool _filterUdpActive;
    [ObservableProperty] private bool _filterTelemetryActive;

    // IPv4/IPv6-Sichtbarkeit. Semantik analog zu TCP/UDP:
    //  beide false => beide Familien werden angezeigt
    //  nur eine true => nur diese Familie sichtbar.
    [ObservableProperty] private bool _filterIpV4Active;
    [ObservableProperty] private bool _filterIpV6Active;

    // ── Erweiterte Filter (Flyout) ──────────────────────────────────────

    /// <summary>Verbindungs-Status (Established/Listen/TimeWait/CloseWait).</summary>
    public IReadOnlyList<FilterOptionViewModel> StateFilters { get; } =
    [
        new("Hergestellt",      nameof(ConnectionState.Established)),
        new("Lauscht",          nameof(ConnectionState.Listen)),
        new("TIME_WAIT",        nameof(ConnectionState.TimeWait)),
        new("CLOSE_WAIT",       nameof(ConnectionState.CloseWait)),
        new("Unbekannt",        nameof(ConnectionState.Unknown)),
    ];

    /// <summary>Reputation-Kategorien.</summary>
    public IReadOnlyList<FilterOptionViewModel> ReputationFilters { get; } =
    [
        new("Sicher",                nameof(ReputationCategory.Safe)),
        new("Verdächtig",            nameof(ReputationCategory.Suspicious)),
        new("Gefährlich",            nameof(ReputationCategory.Dangerous)),
        new("Microsoft-Telemetrie",  nameof(ReputationCategory.MicrosoftTelemetry)),
        new("Privat/Loopback",       nameof(ReputationCategory.Private)),
        new("Unbekannt",             nameof(ReputationCategory.Unknown)),
    ];

    /// <summary>Infrastruktur-Kategorien (Cloud-Anbieter etc.).</summary>
    public IReadOnlyList<FilterOptionViewModel> InfrastructureFilters { get; } =
    [
        new("Microsoft Cloud",       nameof(IpInfrastructureCategory.MicrosoftCloud)),
        new("Microsoft Telemetrie",  nameof(IpInfrastructureCategory.MicrosoftTelemetry)),
        new("Google Cloud",          nameof(IpInfrastructureCategory.GoogleCloud)),
        new("AWS",                   nameof(IpInfrastructureCategory.AwsCloud)),
        new("Cloudflare",            nameof(IpInfrastructureCategory.Cloudflare)),
        new("Akamai CDN",            nameof(IpInfrastructureCategory.AkamaiCdn)),
        new("Tor Exit-Node",         nameof(IpInfrastructureCategory.TorExitNode)),
        new("Öffentlich (sonstige)", nameof(IpInfrastructureCategory.Public)),
        new("Privat/Loopback",       nameof(IpInfrastructureCategory.Private)),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExtendedFilterActive))]
    private string _countryCodeFilter = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExtendedFilterActive))]
    private string _portFilter = string.Empty;

    /// <summary>Komma-separierte Substrings für Prozess-Namen, die ausgeblendet werden sollen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExtendedFilterActive))]
    private string _ignoreProcessFilter = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExtendedFilterActive))]
    private bool _hidePrivateAddresses;

    /// <summary>True, wenn mindestens eine erweiterte Filter-Option aktiv ist – steuert den Indikator-Punkt am Button.</summary>
    public bool IsExtendedFilterActive =>
        !string.IsNullOrWhiteSpace(CountryCodeFilter)
        || !string.IsNullOrWhiteSpace(PortFilter)
        || !string.IsNullOrWhiteSpace(IgnoreProcessFilter)
        || HidePrivateAddresses
        || StateFilters.Any(o => o.IsSelected)
        || ReputationFilters.Any(o => o.IsSelected)
        || InfrastructureFilters.Any(o => o.IsSelected);

    // ──────────────────────────────────────────────────────────────────────
    // Konstruktor
    // ──────────────────────────────────────────────────────────────────────

    public LiveConnectionsViewModel(
        INetworkCapture                    capture,
        IProcessResolver                   processResolver,
        IGeoLocationService                geoService,
        IReputationService                 reputationService,
        IDnsCache                          dnsCache,
        IMicrosoftTelemetryService         telemetryService,
        AppFirewallRulesViewModel          appFirewallRules,
        ILogger<LiveConnectionsViewModel>  logger)
    {
        _capture           = capture;
        _processResolver   = processResolver;
        _geoService        = geoService;
        _reputationService = reputationService;
        _dnsCache          = dnsCache;
        _telemetryService  = telemetryService;
        _logger            = logger;
        AppFirewallRules   = appFirewallRules;

        // Auswahl-Änderungen der erweiterten Filter-Optionen (CheckBoxen im Flyout)
        // an die Filter-Pipeline weiterreichen.
        foreach (var option in StateFilters)
            option.PropertyChanged += OnFilterOptionChanged;
        foreach (var option in ReputationFilters)
            option.PropertyChanged += OnFilterOptionChanged;
        foreach (var option in InfrastructureFilters)
            option.PropertyChanged += OnFilterOptionChanged;
    }

    private void OnFilterOptionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilterOptionViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(IsExtendedFilterActive));
            RebuildFilteredList();
        }
    }

    // Trigger für die Text-/Bool-basierten erweiterten Filter
    partial void OnCountryCodeFilterChanged(string value)   => RebuildFilteredList();
    partial void OnPortFilterChanged(string value)          => RebuildFilteredList();
    partial void OnIgnoreProcessFilterChanged(string value) => RebuildFilteredList();
    partial void OnHidePrivateAddressesChanged(bool value)  => RebuildFilteredList();

    // ──────────────────────────────────────────────────────────────────────
    // Initialisierung (muss auf UI-Thread aufgerufen werden)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Startet die UI-Timer und abonniert den Capture-Stream.
    /// Muss auf dem UI-Thread aufgerufen werden (z.B. aus OnNavigatedTo).
    /// </summary>
    public void Initialize(DispatcherQueue dispatcherQueue)
    {
        if (_dispatcherQueue is not null) return; // bereits initialisiert

        _dispatcherQueue = dispatcherQueue;

        // 200ms-Timer für UI-Updates
        _flushTimer = _dispatcherQueue.CreateTimer();
        _flushTimer.Interval = TimeSpan.FromMilliseconds(200);
        _flushTimer.Tick    += (_, _) =>
        {
            if (_disposed) return;
            try { FlushPendingUpdates(); }
            catch (System.Runtime.InteropServices.COMException) { /* App-Shutdown: XAML-Peers schon abgebaut */ }
        };
        _flushTimer.Start();

        // 5s-Timer für Verbindungs-Aging
        _agingTimer = _dispatcherQueue.CreateTimer();
        _agingTimer.Interval = TimeSpan.FromSeconds(5);
        _agingTimer.Tick    += (_, _) =>
        {
            if (_disposed) return;
            try { ProcessAging(); }
            catch (System.Runtime.InteropServices.COMException) { /* App-Shutdown */ }
        };
        _agingTimer.Start();

        // Capture-Stream abonnieren
        _captureSubscription = _capture.Connections.Subscribe(
            onNext:  ev  => _pendingQueue.Enqueue(ev),
            onError: ex  => _logger.LogError(ex, "Fehler im Verbindungs-Stream")
        );
    }

    // ──────────────────────────────────────────────────────────────────────
    // Commands
    // ──────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleTcpFilter()
    {
        FilterTcpActive = !FilterTcpActive;
        FilterProtocol  = (FilterTcpActive, FilterUdpActive) switch
        {
            (true,  false) => "TCP",
            (false, true)  => "UDP",
            _              => "All"
        };
    }

    [RelayCommand]
    private void ToggleUdpFilter()
    {
        FilterUdpActive = !FilterUdpActive;
        FilterProtocol  = (FilterTcpActive, FilterUdpActive) switch
        {
            (true,  false) => "TCP",
            (false, true)  => "UDP",
            _              => "All"
        };
    }

    [RelayCommand]
    private void ToggleTelemetryFilter()
    {
        FilterTelemetryActive = !FilterTelemetryActive;
        RebuildFilteredList();
    }

    [RelayCommand]
    private void ToggleIpV4Filter()
    {
        FilterIpV4Active = !FilterIpV4Active;
        RebuildFilteredList();
    }

    [RelayCommand]
    private void ToggleIpV6Filter()
    {
        FilterIpV6Active = !FilterIpV6Active;
        RebuildFilteredList();
    }

    [RelayCommand]
    private void ClearFilter()
    {
        _searchText           = string.Empty;
        FilterProtocol        = "All";
        FilterTcpActive       = false;
        FilterUdpActive       = false;
        FilterTelemetryActive = false;
        FilterIpV4Active      = false;
        FilterIpV6Active      = false;
        OnPropertyChanged(nameof(SearchText));
        ResetExtendedFilters();
        RebuildFilteredList();
    }

    /// <summary>Setzt nur die im Flyout konfigurierbaren erweiterten Filter zurück.</summary>
    [RelayCommand]
    private void ResetExtendedFilters()
    {
        foreach (var option in StateFilters)         option.IsSelected = false;
        foreach (var option in ReputationFilters)    option.IsSelected = false;
        foreach (var option in InfrastructureFilters) option.IsSelected = false;

        CountryCodeFilter    = string.Empty;
        PortFilter           = string.Empty;
        IgnoreProcessFilter  = string.Empty;
        HidePrivateAddresses = false;
        OnPropertyChanged(nameof(IsExtendedFilterActive));
    }

    [RelayCommand]
    private void ClearAll()
    {
        _allItems.Clear();
        FilteredConnections.Clear();
        ConnectionCount = 0;
        FilteredCount   = 0;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Batch-Verarbeitung (läuft auf UI-Thread via DispatcherQueueTimer)
    // ──────────────────────────────────────────────────────────────────────

    private void FlushPendingUpdates()
    {
        if (_pendingQueue.IsEmpty) return;

        // Alle ausstehenden Events drainieren; bei gleichem Key nur das letzte behalten.
        // Ausnahme: Closed-Events werden separat gesammelt, weil sie auch dann gelten,
        // wenn danach noch ein "lebendes" Event mit demselben Key käme (extrem unwahrscheinlich,
        // aber explizit korrekt behandeln).
        var batch       = new Dictionary<string, ConnectionEvent>();
        var closedKeys  = new HashSet<string>();

        while (_pendingQueue.TryDequeue(out var ev))
        {
            if (ev.State == ConnectionState.Closed)
            {
                closedKeys.Add(ev.ConnectionKey);
                batch.Remove(ev.ConnectionKey);
            }
            else
            {
                if (closedKeys.Contains(ev.ConnectionKey))
                    continue;        // Closed gewinnt
                batch[ev.ConnectionKey] = ev; // späteres Event überschreibt früheres
            }
        }

        var filterText = _searchText.ToLowerInvariant();

        // 1) Aktive/Neue Verbindungen einarbeiten
        foreach (var (key, ev) in batch)
        {
            try
            {
                // Geo + Prozess synchron auflösen (<1ms)
                var geo  = _geoService.Lookup(ev.RemoteEndpoint.Address);
                var proc = _processResolver.GetProcessInfo(ev.ProcessId);

                if (_allItems.TryGetValue(key, out var existing))
                {
                    // Vorhandenen Eintrag in-place aktualisieren (refreshed LastSeen)
                    existing.Update(ev, geo, proc, ReputationInfo.Unknown);

                    // "Aged"-Markierung zurücknehmen, wenn das Item wieder lebt
                    if (existing.IsAged)
                        existing.SetAged(false);

                    // Telemetrie-Check auch bei Updates (Hostname könnte jetzt bekannt sein)
                    var updatedHostname = _dnsCache.GetHostname(ev.RemoteEndpoint.Address);
                    if (!string.IsNullOrEmpty(updatedHostname)
                        && !existing.IsTelemetry
                        && _telemetryService.IsTelemetryHost(updatedHostname))
                    {
                        existing.MarkAsTelemetry();
                    }
                }
                else
                {
                    // Neuen Eintrag anlegen
                    var item = new ConnectionItemViewModel(ev);
                    item.Update(ev, geo, proc, ReputationInfo.Unknown);

                    // Hostname aus DNS-Cache nachschlagen
                    var cachedHostname = _dnsCache.GetHostname(ev.RemoteEndpoint.Address);
                    if (!string.IsNullOrEmpty(cachedHostname))
                    {
                        item.Hostname = cachedHostname;
                        if (_telemetryService.IsTelemetryHost(cachedHostname))
                            item.MarkAsTelemetry();
                    }
                    _allItems[key] = item;

                    if (PassesFilter(item, filterText))
                        FilteredConnections.Insert(0, item); // Neueste oben
                }

                // Reputation asynchron nachladen (feuert UpdateReputation wenn fertig)
                _ = LoadReputationAsync(key, ev.RemoteEndpoint.Address);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Fehler beim Verarbeiten von Event {Key}", key);
            }
        }

        // 2) Geschlossene Verbindungen aus der UI entfernen
        if (closedKeys.Count > 0)
        {
            foreach (var key in closedKeys)
            {
                if (_allItems.Remove(key, out var item))
                    FilteredConnections.Remove(item);
            }
        }

        ConnectionCount = _allItems.Count;
        FilteredCount   = FilteredConnections.Count;
        OnPropertyChanged(nameof(ConnectionCountText));
    }

    private async Task LoadReputationAsync(string connectionKey, System.Net.IPAddress ip)
    {
        try
        {
            var rep = await _reputationService.GetReputationAsync(ip);

            _dispatcherQueue?.TryEnqueue(() =>
            {
                if (_allItems.TryGetValue(connectionKey, out var item))
                    item.UpdateReputation(rep);
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Reputation für {Ip} konnte nicht geladen werden", ip);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Aging (läuft auf UI-Thread via DispatcherQueueTimer)
    //
    // WICHTIG: Aging entfernt KEINE Verbindungen mehr aus der Liste – das
    // erfolgt jetzt ausschließlich über Closed-Events aus dem Capture-Service.
    // Aging dimmt nur visuell ab, falls für längere Zeit keine Updates kommen
    // (z. B. wenn der Capture-Service kurzfristig stockt).
    // ──────────────────────────────────────────────────────────────────────

    private void ProcessAging()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var (_, item) in _allItems)
        {
            var age = now - item.LastSeen;

            if (age > TimeSpan.FromSeconds(15) && !item.IsAged)
                item.SetAged(true);
            else if (age <= TimeSpan.FromSeconds(15) && item.IsAged)
                item.SetAged(false);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Filterlogik
    // ──────────────────────────────────────────────────────────────────────

    private void RebuildFilteredList()
    {
        var filterText = _searchText.ToLowerInvariant();

        FilteredConnections.Clear();

        // Einträge nach LastSeen absteigend sortieren
        foreach (var item in _allItems.Values
            .OrderByDescending(x => x.LastSeen)
            .Where(x => PassesFilter(x, filterText)))
        {
            FilteredConnections.Add(item);
        }

        FilteredCount = FilteredConnections.Count;
        OnPropertyChanged(nameof(ConnectionCountText));
    }

    private bool PassesFilter(ConnectionItemViewModel item, string lowerSearch)
    {
        // Protokoll-Filter
        if (FilterProtocol != "All")
        {
            var proto = FilterProtocol == "TCP" ? NetworkProtocol.Tcp : NetworkProtocol.Udp;
            if (item.Protocol != proto) return false;
        }

        // Telemetrie-Filter (Schnellzugriff)
        if (FilterTelemetryActive && !item.IsTelemetry) return false;

        // IPv4/IPv6-Filter: wenn genau eine Familie aktiv ist, blende die andere aus.
        // (beide aktiv ≡ beide inaktiv ≡ keine Einschränkung.)
        if (FilterIpV4Active != FilterIpV6Active)
        {
            if (FilterIpV4Active && item.IsIpV6)  return false;
            if (FilterIpV6Active && !item.IsIpV6) return false;
        }

        // ── Erweiterte Filter (Flyout) ──

        // Verbindungs-Status: wenn mindestens eine Option ausgewählt, muss Status passen
        var stateSel = GetSelectedValues(StateFilters);
        if (stateSel.Length > 0 && !stateSel.Contains(item.State.ToString()))
            return false;

        // Reputation-Kategorie
        var repSel = GetSelectedValues(ReputationFilters);
        if (repSel.Length > 0 && !repSel.Contains(item.ReputationCategory.ToString()))
            return false;

        // Infrastruktur-Kategorie
        var infSel = GetSelectedValues(InfrastructureFilters);
        if (infSel.Length > 0 && !infSel.Contains(item.InfrastructureCategory.ToString()))
            return false;

        // Privat/Loopback ausblenden
        if (HidePrivateAddresses
            && (item.InfrastructureCategory == IpInfrastructureCategory.Private
             || item.InfrastructureCategory == IpInfrastructureCategory.Loopback
             || item.ReputationCategory     == ReputationCategory.Private))
            return false;

        // Länder-Code (komma-separierte Liste, case-insensitive, "include")
        if (!string.IsNullOrWhiteSpace(CountryCodeFilter))
        {
            var codes = CountryCodeFilter
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => c.ToUpperInvariant())
                .ToArray();
            if (codes.Length > 0
                && !codes.Contains(item.CountryCode.ToUpperInvariant()))
                return false;
        }

        // Port (komma-separierte Liste – matched gegen RemotePort und LocalPort)
        if (!string.IsNullOrWhiteSpace(PortFilter))
        {
            var ports = PortFilter
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(p => int.TryParse(p, out _))
                .Select(int.Parse)
                .ToArray();

            if (ports.Length > 0)
            {
                // RemoteAddress / LocalAddress haben Format "IP:Port"
                var remotePort = ParseTrailingPort(item.RemoteAddress);
                var localPort  = ParseTrailingPort(item.LocalAddress);
                if (!ports.Contains(remotePort) && !ports.Contains(localPort))
                    return false;
            }
        }

        // Ignore-Liste: Prozess-Namen-Substrings (komma-separiert)
        if (!string.IsNullOrWhiteSpace(IgnoreProcessFilter))
        {
            var patterns = IgnoreProcessFilter
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var pat in patterns)
            {
                if (item.ProcessDisplayName.Contains(pat, StringComparison.OrdinalIgnoreCase)
                    || item.ProcessName.Contains(pat, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        // Freitext-Suche
        if (!string.IsNullOrEmpty(lowerSearch))
        {
            return item.ProcessDisplayName.Contains(lowerSearch, StringComparison.OrdinalIgnoreCase)
                || item.RemoteAddress.Contains(lowerSearch, StringComparison.OrdinalIgnoreCase)
                || item.Hostname.Contains(lowerSearch, StringComparison.OrdinalIgnoreCase)
                || item.CountryDisplay.Contains(lowerSearch, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static string[] GetSelectedValues(IReadOnlyList<FilterOptionViewModel> options)
    {
        var sel = new List<string>(options.Count);
        for (int i = 0; i < options.Count; i++)
            if (options[i].IsSelected) sel.Add(options[i].Value);
        return sel.ToArray();
    }

    /// <summary>Liest den abschließenden Port aus einer "IP:Port"-Darstellung.
    /// Berücksichtigt auch IPv6-Notation "[::1]:443".</summary>
    private static int ParseTrailingPort(string address)
    {
        if (string.IsNullOrEmpty(address)) return -1;
        var idx = address.LastIndexOf(':');
        if (idx < 0 || idx == address.Length - 1) return -1;
        return int.TryParse(address.AsSpan(idx + 1), out var port) ? port : -1;
    }

    // ──────────────────────────────────────────────────────────────────────
    // IDisposable
    // ──────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _flushTimer?.Stop();
        _agingTimer?.Stop();
        _captureSubscription?.Dispose();
    }
}
