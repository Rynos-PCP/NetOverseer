// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.App.ViewModels;

/// <summary>
/// ViewModel für die DNS-Anfragen-Seite.
/// Abonniert den DNS-Capture-Stream und zeigt alle Anfragen in Echtzeit.
/// Singleton: läuft persistent über die gesamte App-Lebenszeit.
/// </summary>
public sealed partial class DnsViewModel : ObservableObject, IDisposable
{
    // ──────────────────────────────────────────────────────────────────────
    // Dienste
    // ──────────────────────────────────────────────────────────────────────

    private readonly IDnsCapture                  _capture;
    private readonly IDnsCache                    _cache;
    private readonly IProcessResolver             _processResolver;
    private readonly ILogger<DnsViewModel>        _logger;

    // ──────────────────────────────────────────────────────────────────────
    // Threading
    // ──────────────────────────────────────────────────────────────────────

    private DispatcherQueue?      _dispatcherQueue;
    private IDisposable?          _subscription;
    private DispatcherQueueTimer? _diagnosticsTimer;
    private bool                  _disposed;
    private bool                  _initialized;

    /// <summary>
    /// Events, die eingingen bevor die UI bereit war. Werden beim ersten
    /// Setzen des Dispatchers in die ObservableCollection übernommen.
    /// </summary>
    private readonly Queue<DnsQueryEvent> _pendingEvents = new();
    private readonly object               _pendingLock   = new();

    // ──────────────────────────────────────────────────────────────────────
    // Collections
    // ──────────────────────────────────────────────────────────────────────

    private readonly List<DnsQueryItemViewModel> _allItems = [];
    public  ObservableCollection<DnsQueryItemViewModel> Queries { get; } = [];

    // ──────────────────────────────────────────────────────────────────────
    // Observable Properties
    // ──────────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStatusBanner))]
    [NotifyPropertyChangedFor(nameof(CaptureStateText))]
    [NotifyPropertyChangedFor(nameof(CaptureStateColor))]
    private bool _isCapturing;

    [ObservableProperty] private string _statusText = "DNS-Überwachung wird initialisiert …";
    [ObservableProperty] private string _selfTestText = string.Empty;

    public bool ShowStatusBanner => !IsCapturing;
    public string CaptureStateText => IsCapturing ? "Aktiv" : "Inaktiv";
    public string CaptureStateColor => IsCapturing ? "#10B981" : "#9CA3AF";

    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _trackerCount;
    [ObservableProperty] private int _telemetryCount;
    [ObservableProperty] private int _cdnCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuspiciousCountVisibility))]
    private int _suspiciousCount;

    public Visibility SuspiciousCountVisibility =>
        SuspiciousCount > 0 ? Visibility.Visible : Visibility.Collapsed;

    // ── Diagnose ────────────────────────────────────────────────────────

    [ObservableProperty] private long   _eventsReceived;
    [ObservableProperty] private long   _eventsAccepted;
    [ObservableProperty] private double _ratePerSecond;
    [ObservableProperty] private string _runtimeText = "—";

    private long _lastReceived;
    private DateTime _lastRateTick = DateTime.UtcNow;

    // ── Filter ──────────────────────────────────────────────────────────

    private string _filterText = string.Empty;
    public string FilterText
    {
        get => _filterText;
        set { if (SetProperty(ref _filterText, value)) ApplyFilter(); }
    }

    private string _selectedCategory = "Alle";
    public string SelectedCategory
    {
        get => _selectedCategory;
        set { if (SetProperty(ref _selectedCategory, value)) ApplyFilter(); }
    }

    public IReadOnlyList<string> CategoryOptions { get; } =
        ["Alle", "Normal", "Tracker", "Telemetrie", "CDN", "Verdächtig"];

    // ──────────────────────────────────────────────────────────────────────
    // Konstruktor – Subscribe sofort, damit keine Events verloren gehen
    // ──────────────────────────────────────────────────────────────────────

    public DnsViewModel(
        IDnsCapture           capture,
        IDnsCache             cache,
        IProcessResolver      processResolver,
        ILogger<DnsViewModel> logger)
    {
        _capture         = capture;
        _cache           = cache;
        _processResolver = processResolver;
        _logger          = logger;

        // Stream sofort abonnieren – auch bevor die UI geladen ist.
        // Events werden in _cache + _pendingEvents zwischengespeichert.
        _subscription = _capture.Queries.Subscribe(
            onNext:  OnDnsEvent,
            onError: OnDnsError);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Initialisierung (vom UI-Thread aus DnsPage.OnNavigatedTo)
    // ──────────────────────────────────────────────────────────────────────

    public Task InitializeAsync(DispatcherQueue dispatcherQueue)
    {
        if (_initialized)
        {
            // Bei erneuter Navigation: Diagnose-Werte sofort aktualisieren
            UpdateDiagnostics();
            return Task.CompletedTask;
        }

        _initialized     = true;
        _dispatcherQueue = dispatcherQueue;

        // 1) Bereits gecachte Anfragen laden
        var recent = _cache.GetRecentQueries(500);
        foreach (var evt in recent.Reverse())
            AddEventInternal(evt, refilter: false);

        // 2) Events, die vor UI-Init aufgelaufen sind, übernehmen
        lock (_pendingLock)
        {
            while (_pendingEvents.Count > 0)
                AddEventInternal(_pendingEvents.Dequeue(), refilter: false);
        }
        ApplyFilter();

        // 3) Status aus Capture übernehmen
        IsCapturing = _capture.IsRunning;
        StatusText  = _capture.IsRunning
            ? "DNS-Überwachung aktiv."
            : "DNS-Überwachung nicht aktiv – Administratorrechte erforderlich.";

        // 4) Diagnose-Timer starten (1× pro Sekunde Rate berechnen)
        _diagnosticsTimer            = dispatcherQueue.CreateTimer();
        _diagnosticsTimer.Interval   = TimeSpan.FromSeconds(1);
        _diagnosticsTimer.IsRepeating = true;
        _diagnosticsTimer.Tick      += (_, _) =>
        {
            if (_disposed) return;
            try { UpdateDiagnostics(); }
            catch (System.Runtime.InteropServices.COMException) { /* App-Shutdown */ }
        };
        _diagnosticsTimer.Start();
        UpdateDiagnostics();

        return Task.CompletedTask;
    }

    private void UpdateDiagnostics()
    {
        var rec = _capture.EventsReceived;
        var acc = _capture.EventsAccepted;

        EventsReceived = rec;
        EventsAccepted = acc;

        var now   = DateTime.UtcNow;
        var delta = (now - _lastRateTick).TotalSeconds;
        if (delta > 0)
            RatePerSecond = Math.Round((rec - _lastReceived) / delta, 1);
        _lastReceived = rec;
        _lastRateTick = now;

        if (_capture.StartedAt is { } start)
        {
            var elapsed = DateTimeOffset.UtcNow - start;
            RuntimeText = elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours} h {elapsed.Minutes} m"
                : elapsed.TotalMinutes >= 1
                    ? $"{(int)elapsed.TotalMinutes} m {elapsed.Seconds} s"
                    : $"{elapsed.Seconds} s";
        }
        else
        {
            RuntimeText = "—";
        }

        if (IsCapturing != _capture.IsRunning)
            IsCapturing = _capture.IsRunning;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Commands
    // ──────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task StartCaptureAsync()
    {
        try
        {
            await _capture.StartAsync();
            IsCapturing = true;
            StatusText  = "DNS-Überwachung aktiv.";
        }
        catch (UnauthorizedAccessException)
        {
            StatusText  = "Administratorrechte erforderlich für DNS-ETW-Überwachung.";
            IsCapturing = false;
            _logger.LogWarning("DNS-Capture benötigt Administratorrechte.");
        }
        catch (Exception ex)
        {
            StatusText  = $"Fehler beim Starten: {ex.Message}";
            IsCapturing = false;
            _logger.LogError(ex, "DNS-Capture konnte nicht gestartet werden");
        }
    }

    [RelayCommand]
    private async Task StopCaptureAsync()
    {
        try
        {
            await _capture.StopAsync();
            IsCapturing = false;
            StatusText  = "DNS-Überwachung gestoppt.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DNS-Capture konnte nicht gestoppt werden");
        }
    }

    [RelayCommand]
    private async Task RestartCaptureAsync()
    {
        await StopCaptureAsync();
        await Task.Delay(250);
        await StartCaptureAsync();
    }

    [RelayCommand]
    private void Clear()
    {
        _allItems.Clear();
        Queries.Clear();
        TotalCount      = 0;
        TrackerCount    = 0;
        TelemetryCount  = 0;
        CdnCount        = 0;
        SuspiciousCount = 0;
    }

    /// <summary>
    /// Führt eine Test-DNS-Anfrage aus und wartet darauf, dass sie in der
    /// Capture-Pipeline auftaucht. Verifiziert End-to-End-Funktion.
    /// </summary>
    [RelayCommand]
    private async Task RunSelfTestAsync()
    {
        SelfTestText = "Test läuft …";
        var beforeAccepted = _capture.EventsAccepted;
        var beforeReceived = _capture.EventsReceived;
        var testHost       = $"netoverseer-test-{DateTime.UtcNow.Ticks}.example.com";

        try
        {
            // Asynchrone DNS-Auflösung erzwingen – egal ob sie erfolgreich ist,
            // der DNS-Client-ETW-Provider sollte ein Event feuern.
            try { await Dns.GetHostEntryAsync(testHost); }
            catch { /* NXDOMAIN ist OK – wir wollen nur das ETW-Event auslösen */ }

            // Auch eine echte Domain abfragen (manche Systeme blocken erfundene)
            try { await Dns.GetHostEntryAsync("www.microsoft.com"); }
            catch { /* egal */ }

            // Bis zu 5 Sekunden warten, bis ein akzeptiertes DNS-Event eingeht.
            // Wichtig: NICHT bei EventsReceived abbrechen – der DNS-Client-Provider
            // feuert oft erst „DnsServerForInterface"-Events (ID 1001), die kein
            // QueryName-Feld haben, bevor das eigentliche Query-Event ankommt.
            for (var i = 0; i < 50; i++)
            {
                await Task.Delay(100);
                if (_capture.EventsAccepted > beforeAccepted) break;
            }

            var newReceived = _capture.EventsReceived - beforeReceived;
            var newAccepted = _capture.EventsAccepted - beforeAccepted;

            if (newReceived == 0)
            {
                SelfTestText =
                    "✗ Keine ETW-Events empfangen. Vermutlich fehlen Administratorrechte " +
                    "oder der DNS-Client-Provider ist deaktiviert.";
                _logger.LogWarning("DNS-Self-Test: keine Events vom Provider erhalten.");
            }
            else if (newAccepted == 0)
            {
                // Schema-Dump anzeigen, damit man sieht, welche Felder die Events haben
                var schemas = _capture.EventSchemas;
                var sample  = schemas.Count == 0
                    ? string.Empty
                    : "  Schema: " + string.Join(" | ",
                        schemas.Take(3).Select(kvp => $"ID {kvp.Key}: {kvp.Value}"));
                if (sample.Length > 400) sample = sample[..400] + "…";

                SelfTestText =
                    $"⚠ {newReceived} Roh-Events empfangen, aber keine als DNS-Anfrage geparst." + sample;
                _logger.LogWarning(
                    "DNS-Self-Test: {Received} Roh-Events, 0 akzeptiert. Schemas: {Schemas}",
                    newReceived,
                    string.Join("; ", schemas.Select(kvp => $"{kvp.Key}={kvp.Value}")));
            }
            else
            {
                SelfTestText =
                    $"✓ {newAccepted} DNS-Anfragen erfolgreich erfasst (von {newReceived} Events).";
                _logger.LogInformation(
                    "DNS-Self-Test OK: {Accepted}/{Received}", newAccepted, newReceived);
            }
        }
        catch (Exception ex)
        {
            SelfTestText = $"✗ Fehler: {ex.Message}";
            _logger.LogError(ex, "Fehler beim DNS-Self-Test");
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Stream-Handling
    // ──────────────────────────────────────────────────────────────────────

    private void OnDnsEvent(DnsQueryEvent evt)
    {
        _cache.Record(evt);

        // Falls UI noch nicht initialisiert: zwischenpuffern
        if (_dispatcherQueue is null)
        {
            lock (_pendingLock)
            {
                _pendingEvents.Enqueue(evt);
                if (_pendingEvents.Count > 1000) _pendingEvents.Dequeue();
            }
            return;
        }

        _dispatcherQueue.TryEnqueue(() => AddEventInternal(evt, refilter: false));
    }

    private void OnDnsError(Exception ex)
    {
        _logger.LogError(ex, "Fehler im DNS-Stream");
        _dispatcherQueue?.TryEnqueue(() =>
        {
            StatusText  = $"Fehler: {ex.Message}";
            IsCapturing = false;
        });
    }

    private void AddEventInternal(DnsQueryEvent evt, bool refilter)
    {
        // Prozessname via IProcessResolver auflösen (cached, 30s TTL).
        // ETW liefert oft nur die PID, nicht den Namen.
        var processInfo = _processResolver.GetProcessInfo(evt.ProcessId);
        var vm = new DnsQueryItemViewModel(evt, processInfo);
        _allItems.Insert(0, vm);

        TotalCount++;
        switch (evt.Category)
        {
            case DnsCategory.Tracker:    TrackerCount++;    break;
            case DnsCategory.Telemetry:  TelemetryCount++;  break;
            case DnsCategory.Cdn:        CdnCount++;        break;
            case DnsCategory.Suspicious: SuspiciousCount++; break;
        }

        if (_allItems.Count > 2000)
            _allItems.RemoveAt(_allItems.Count - 1);

        if (MatchesFilter(vm))
            Queries.Insert(0, vm);

        if (Queries.Count > 1000)
            Queries.RemoveAt(Queries.Count - 1);

        if (refilter) ApplyFilter();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Filter
    // ──────────────────────────────────────────────────────────────────────

    private void ApplyFilter()
    {
        Queries.Clear();
        foreach (var vm in _allItems)
        {
            if (MatchesFilter(vm))
                Queries.Add(vm);
            if (Queries.Count >= 1000) break;
        }
    }

    private bool MatchesFilter(DnsQueryItemViewModel vm)
    {
        if (_selectedCategory != "Alle")
        {
            var catMatch = _selectedCategory switch
            {
                "Normal"      => vm.Event.Category == DnsCategory.Normal,
                "Tracker"     => vm.Event.Category == DnsCategory.Tracker,
                "Telemetrie"  => vm.Event.Category == DnsCategory.Telemetry,
                "CDN"         => vm.Event.Category == DnsCategory.Cdn,
                "Verdächtig"  => vm.Event.Category == DnsCategory.Suspicious,
                _             => true,
            };
            if (!catMatch) return false;
        }

        if (!string.IsNullOrEmpty(_filterText))
        {
            if (!vm.Event.QueryName.Contains(_filterText, StringComparison.OrdinalIgnoreCase)
             && !vm.Event.ProcessName.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    // ──────────────────────────────────────────────────────────────────────
    // IDisposable
    // ──────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _diagnosticsTimer?.Stop();
        _subscription?.Dispose();
    }
}
