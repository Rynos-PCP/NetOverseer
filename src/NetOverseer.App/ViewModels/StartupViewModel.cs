// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NetOverseer.App.Services;
using NetOverseer.Capture;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;
using Windows.UI;

namespace NetOverseer.App.ViewModels;

// ════════════════════════════════════════════════════════════════════════════
// StartupViewModel
// ════════════════════════════════════════════════════════════════════════════

public sealed partial class StartupViewModel : ObservableObject
{
    private readonly IStartupRepository       _repo;
    private readonly IStartupInstallerService _installer;
    private readonly StartupMonitorService    _monitor;
    private readonly ILogger<StartupViewModel> _logger;
    private DispatcherQueue? _dispatcher;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingVisibility))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDataVisibility), nameof(NoDataVisibility))]
    private bool _hasData;

    [ObservableProperty] private StartupSession? _lastSession;
    [ObservableProperty] private string _sessionBootText       = "";
    [ObservableProperty] private string _sessionDurationText   = "";
    [ObservableProperty] private string _sessionConnectionText = "";

    // ── Autostart-Status & Aktions-Feedback ──────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(StatusBadgeText),
        nameof(StatusBadgeBrush),
        nameof(CanInstall))]
    private bool _isAutostartInstalled;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _hasStatusMessage;

    public string StatusBadgeText  =>
        IsAutostartInstalled ? "Aktiv" : "Inaktiv";

    /// <summary>Farbe der Status-Badge (grün = aktiv, grau = inaktiv).</summary>
    public Brush StatusBadgeBrush => new SolidColorBrush(
        IsAutostartInstalled
            ? Color.FromArgb(0xFF, 0x10, 0x7C, 0x10)
            : Color.FromArgb(0xFF, 0x5A, 0x5A, 0x5A));

    /// <summary>True wenn der Aktivieren-Button verfügbar ist.</summary>
    public bool CanInstall => !IsAutostartInstalled;

    public ObservableCollection<StartupConnectionGroupViewModel> ProcessGroups { get; } = [];

    /// <summary>Flache Liste aller aufgezeichneten Verbindungen, sortiert nach Offset.
    /// Wird von der Tabellen-Ansicht (resizable Columns) auf der StartupPage gebunden.</summary>
    public ObservableCollection<StartupConnectionItemViewModel> Connections { get; } = [];

    public Visibility LoadingVisibility  => IsLoading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HasDataVisibility  => HasData   ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoDataVisibility   => HasData   ? Visibility.Collapsed : Visibility.Visible;

    public StartupViewModel(
        IStartupRepository repo,
        IStartupInstallerService installer,
        StartupMonitorService monitor,
        ILogger<StartupViewModel> logger)
    {
        _repo      = repo;
        _installer = installer;
        _monitor   = monitor;
        _logger    = logger;
    }

    public async Task InitializeAsync(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        dispatcher.TryEnqueue(() =>
        {
            IsLoading = true;
            IsAutostartInstalled = _installer.IsInstalled;
        });
        try
        {
            var session = await _repo.GetLastSessionAsync().ConfigureAwait(false);
            if (session is not null)
            {
                var connections = await _repo.GetConnectionsAsync(session.Id).ConfigureAwait(false);
                dispatcher.TryEnqueue(() =>
                {
                    LastSession           = session;
                    SessionBootText       = $"Systemstart: {session.BootTime.ToLocalTime():dd.MM.yyyy HH:mm:ss}";
                    SessionDurationText   = $"Aufgezeichnet: {(int)session.RecordingDuration.TotalSeconds}s";
                    SessionConnectionText = $"Verbindungen: {session.ConnectionCount}";
                    BuildProcessGroups(connections);
                    HasData = true;
                });
            }
            else
            {
                dispatcher.TryEnqueue(() => HasData = false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Laden der Startup-Daten.");
            dispatcher.TryEnqueue(() => HasData = false);
        }
        finally
        {
            dispatcher.TryEnqueue(() => IsLoading = false);
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_dispatcher is null) return;
        SetStatus("Aktualisiere…");
        await InitializeAsync(_dispatcher).ConfigureAwait(false);
        _dispatcher.TryEnqueue(() =>
        {
            IsAutostartInstalled = _installer.IsInstalled;
            SetStatus(HasData ? "Letzte Aufzeichnung geladen." : "Noch keine Aufzeichnung vorhanden.");
        });
    }

    [RelayCommand]
    private async Task InstallAutostartAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _installer.InstallAsync().ConfigureAwait(false);
            _dispatcher?.TryEnqueue(() =>
            {
                IsAutostartInstalled = _installer.IsInstalled;
                SetStatus(IsAutostartInstalled
                    ? "Bootmonitor installiert (läuft beim nächsten Systemstart als SYSTEM)."
                    : "Installation fehlgeschlagen – Task wurde nicht angelegt.");
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bootmonitor-Installation fehlgeschlagen.");
            _dispatcher?.TryEnqueue(() => SetStatus($"Fehler: {ex.Message}"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UninstallAutostartAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _installer.UninstallAsync().ConfigureAwait(false);
            _dispatcher?.TryEnqueue(() =>
            {
                IsAutostartInstalled = _installer.IsInstalled;
                SetStatus("Bootmonitor entfernt.");
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bootmonitor-Deinstallation fehlgeschlagen.");
            _dispatcher?.TryEnqueue(() => SetStatus($"Fehler: {ex.Message}"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RunNowAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            _dispatcher?.TryEnqueue(() =>
                SetStatus("Test-Aufzeichnung läuft – bitte ca. 60 Sekunden warten…"));

            await _monitor.RecordIfStartupWindowAsync(force: true).ConfigureAwait(false);

            if (_dispatcher is not null)
            {
                await InitializeAsync(_dispatcher).ConfigureAwait(false);
                _dispatcher.TryEnqueue(() => SetStatus(HasData
                    ? $"Test abgeschlossen – {LastSession?.ConnectionCount ?? 0} Verbindungen aufgezeichnet."
                    : "Test abgeschlossen – keine Verbindungen erfasst."));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manueller Bootmonitor-Lauf fehlgeschlagen.");
            _dispatcher?.TryEnqueue(() => SetStatus($"Fehler: {ex.Message}"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetStatus(string text)
    {
        StatusMessage     = text;
        HasStatusMessage  = !string.IsNullOrWhiteSpace(text);
    }

    private void BuildProcessGroups(IReadOnlyList<StartupConnection> connections)
    {
        ProcessGroups.Clear();
        Connections.Clear();

        var totalSeconds = Math.Max(1.0, connections.Count > 0
            ? connections.Max(c => c.OffsetSeconds) : 60.0);

        foreach (var g in connections
            .GroupBy(c => c.ProcessName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Min(c => c.OffsetSeconds)))
        {
            ProcessGroups.Add(new StartupConnectionGroupViewModel(g.Key, g, totalSeconds));
        }

        // Flache Liste für die Tabellenansicht (chronologisch sortiert).
        foreach (var c in connections.OrderBy(c => c.OffsetSeconds))
            Connections.Add(new StartupConnectionItemViewModel(c));
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Process-Group ViewModel
// ════════════════════════════════════════════════════════════════════════════

public sealed class StartupConnectionGroupViewModel
{
    public string ProcessName     { get; }
    public string FirstSeenText   { get; }
    public double FirstSeenSeconds { get; }

    /// <summary>Breite des Zeitbalken in Pixel (Max 260px entspricht ~Gesamtdauer).</summary>
    public double TimelineBarWidth { get; }

    public IReadOnlyList<StartupConnectionItemViewModel> Connections { get; }

    public StartupConnectionGroupViewModel(
        string processName,
        IEnumerable<StartupConnection> connections,
        double totalSeconds)
    {
        ProcessName = processName;
        var list = connections
            .OrderBy(c => c.OffsetSeconds)
            .Select(c => new StartupConnectionItemViewModel(c))
            .ToList();

        Connections       = list;
        FirstSeenSeconds  = list.Count > 0 ? list[0].OffsetSeconds : 0.0;
        FirstSeenText     = $"+{FirstSeenSeconds:F1}s";
        TimelineBarWidth  = Math.Max(4.0, (FirstSeenSeconds / Math.Max(1.0, totalSeconds)) * 260.0);
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Connection-Item ViewModel
// ════════════════════════════════════════════════════════════════════════════

public sealed partial class StartupConnectionItemViewModel : ObservableObject
{
    private readonly StartupConnection _c;

    public StartupConnectionItemViewModel(StartupConnection c) => _c = c;

    public string RemoteDisplay => $"{_c.RemoteIp}:{_c.RemotePort}";
    public string OffsetText    => $"+{_c.OffsetSeconds:F1}s";
    public string Protocol      => _c.Protocol;
    public string LocalDisplay  => $"{_c.LocalIp}:{_c.LocalPort}";
    public string ProcessName   => _c.ProcessName;
    public int    ProcessId     => _c.ProcessId;
    public double OffsetSeconds => _c.OffsetSeconds;

    /// <summary>Reine Remote-IP ohne Port – wird für Whois und „IP kopieren" benötigt.</summary>
    public string RemoteIp      => _c.RemoteIp;

    // ──────────────────────────────────────────────────────────────────────
    // Kontext-Menü-Befehle (analog zu ConnectionItemViewModel)
    // ──────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void CopyRemoteAddress()
    {
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(RemoteDisplay);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
    }

    [RelayCommand]
    private void CopyRemoteIp()
    {
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(RemoteIp);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
    }

    [RelayCommand]
    private async Task OpenWhois()
    {
        if (string.IsNullOrWhiteSpace(RemoteIp)) return;
        await Windows.System.Launcher.LaunchUriAsync(
            new Uri($"https://www.whois.com/whois/{Uri.EscapeDataString(RemoteIp)}"));
    }
}
