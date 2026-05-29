// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.App.ViewModels;

public enum ApplicationsDetailsSection
{
    Activity,
    Firewall,
    Properties
}

public sealed class ApplicationListItemViewModel
{
    public string DisplayName { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public long TotalBytesSent { get; init; }
    public long TotalBytesReceived { get; init; }
    public DateTimeOffset LastSeen { get; init; }
    public bool IsTrusted { get; init; }
    public bool IsBlocked { get; init; }

    public string BytesSentDisplay => FormatBytes(TotalBytesSent);
    public string BytesRecvDisplay => FormatBytes(TotalBytesReceived);
    public string LastSeenDisplay  => LastSeen.LocalDateTime.ToString("g");
    public string TrustDisplay     => IsTrusted ? "Vertraut" : "Normal";
    public string BlockDisplay     => IsBlocked ? "Blockiert" : "Offen";

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024         => $"{bytes / 1_024.0:F1} KB",
        _                => $"{bytes} B"
    };
}

public sealed class AppRecentConnectionViewModel
{
    public string TimestampDisplay { get; init; } = string.Empty;
    public string RemoteDisplay { get; init; } = string.Empty;
    public string Protocol { get; init; } = string.Empty;
    public string BytesDisplay { get; init; } = string.Empty;
    public string ReputationDisplay { get; init; } = string.Empty;
}

public sealed partial class ApplicationsViewModel : ObservableObject
{
    private readonly IAppProfileRepository _profiles;
    private readonly IConnectionRepository _connections;
    private readonly IFirewallService _firewall;
    private readonly ILogger<ApplicationsViewModel> _logger;

    private readonly List<ApplicationListItemViewModel> _allApps = [];

    public AppFirewallRulesViewModel AppFirewallRules { get; }

    public ObservableCollection<ApplicationListItemViewModel> Applications { get; } = [];
    public ObservableCollection<AppRecentConnectionViewModel> RecentConnections { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingVisibility))]
    [NotifyPropertyChangedFor(nameof(ContentVisibility))]
    private bool _isLoading;

    [ObservableProperty] private string _statusText = string.Empty;

    [ObservableProperty] private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrafficColumnVisibility))]
    [NotifyPropertyChangedFor(nameof(SelectedTrafficDisplay))]
    private bool _isTrafficMetricsEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedAppName))]
    [NotifyPropertyChangedFor(nameof(SelectedAppPath))]
    [NotifyPropertyChangedFor(nameof(SelectedLastSeenDisplay))]
    [NotifyPropertyChangedFor(nameof(SelectedTrafficDisplay))]
    [NotifyPropertyChangedFor(nameof(SelectedTrustDisplay))]
    [NotifyPropertyChangedFor(nameof(SelectedBlockDisplay))]
    [NotifyPropertyChangedFor(nameof(FileExistsDisplay))]
    private ApplicationListItemViewModel? _selectedApp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsOpenVisibility))]
    [NotifyPropertyChangedFor(nameof(DetailsClosedVisibility))]
    private bool _isDetailsOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivityVisibility))]
    [NotifyPropertyChangedFor(nameof(FirewallVisibility))]
    [NotifyPropertyChangedFor(nameof(PropertiesVisibility))]
    [NotifyPropertyChangedFor(nameof(IsActivitySection))]
    [NotifyPropertyChangedFor(nameof(IsFirewallSection))]
    [NotifyPropertyChangedFor(nameof(IsPropertiesSection))]
    private ApplicationsDetailsSection _activeSection = ApplicationsDetailsSection.Activity;

    [ObservableProperty] private int _connectionCount24h;

    [ObservableProperty] private int _suspiciousCount24h;

    [ObservableProperty] private string _lastActivityStatus = "Noch keine Aktivität geladen.";

    public Visibility LoadingVisibility => IsLoading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ContentVisibility => IsLoading ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DetailsOpenVisibility => IsDetailsOpen ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DetailsClosedVisibility => IsDetailsOpen ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ActivityVisibility => ActiveSection == ApplicationsDetailsSection.Activity
        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FirewallVisibility => ActiveSection == ApplicationsDetailsSection.Firewall
        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PropertiesVisibility => ActiveSection == ApplicationsDetailsSection.Properties
        ? Visibility.Visible : Visibility.Collapsed;

    public bool IsActivitySection => ActiveSection == ApplicationsDetailsSection.Activity;
    public bool IsFirewallSection => ActiveSection == ApplicationsDetailsSection.Firewall;
    public bool IsPropertiesSection => ActiveSection == ApplicationsDetailsSection.Properties;
    public Visibility TrafficColumnVisibility => IsTrafficMetricsEnabled ? Visibility.Visible : Visibility.Collapsed;

    public bool HasSelection => SelectedApp is not null;
    public string SelectedAppName => SelectedApp?.DisplayName ?? "Keine Anwendung ausgewählt";
    public string SelectedAppPath => SelectedApp?.ExecutablePath ?? string.Empty;
    public string SelectedLastSeenDisplay => SelectedApp is null ? "-" : SelectedApp.LastSeenDisplay;
    public string SelectedTrafficDisplay => SelectedApp is null
        ? "-"
        : IsTrafficMetricsEnabled
            ? $"{SelectedApp.BytesSentDisplay} hoch / {SelectedApp.BytesRecvDisplay} runter"
            : "Deaktiviert";
    public string SelectedTrustDisplay => SelectedApp?.TrustDisplay ?? "-";
    public string SelectedBlockDisplay => SelectedApp?.BlockDisplay ?? "-";
    public string FileExistsDisplay
    {
        get
        {
            var path = SelectedApp?.ExecutablePath;
            if (string.IsNullOrWhiteSpace(path)) return "Unbekannt";
            return File.Exists(path) ? "Vorhanden" : "Nicht gefunden";
        }
    }

    public ApplicationsViewModel(
        IAppProfileRepository profiles,
        IConnectionRepository connections,
        IFirewallService firewall,
        AppFirewallRulesViewModel appFirewallRules,
        ILogger<ApplicationsViewModel> logger)
    {
        _profiles = profiles;
        _connections = connections;
        _firewall = firewall;
        AppFirewallRules = appFirewallRules;
        _logger = logger;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(LoadingVisibility));
        OnPropertyChanged(nameof(ContentVisibility));
    }

    partial void OnIsTrafficMetricsEnabledChanged(bool value)
    {
        ApplicationColumnLayout.Instance.TrafficColumnsEnabled = value;
        OnPropertyChanged(nameof(TrafficColumnVisibility));
        OnPropertyChanged(nameof(SelectedTrafficDisplay));
    }

    partial void OnIsDetailsOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(DetailsOpenVisibility));
        OnPropertyChanged(nameof(DetailsClosedVisibility));
    }

    partial void OnActiveSectionChanged(ApplicationsDetailsSection value)
    {
        OnPropertyChanged(nameof(ActivityVisibility));
        OnPropertyChanged(nameof(FirewallVisibility));
        OnPropertyChanged(nameof(PropertiesVisibility));
        OnPropertyChanged(nameof(IsActivitySection));
        OnPropertyChanged(nameof(IsFirewallSection));
        OnPropertyChanged(nameof(IsPropertiesSection));
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (IsLoading) return;

        IsLoading = true;
        StatusText = "Lade Anwendungen …";

        try
        {
            var profiles = await _profiles.GetAllAsync(ct);
            _allApps.Clear();

            if (profiles.Count > 0)
            {
                foreach (var p in profiles)
                {
                    var fallbackName = string.IsNullOrWhiteSpace(p.ExecutablePath)
                        ? "Unbekannte Anwendung"
                        : Path.GetFileNameWithoutExtension(p.ExecutablePath);

                    _allApps.Add(new ApplicationListItemViewModel
                    {
                        DisplayName = string.IsNullOrWhiteSpace(p.DisplayName) ? fallbackName : p.DisplayName,
                        ExecutablePath = p.ExecutablePath,
                        ProcessName = string.IsNullOrWhiteSpace(p.ExecutablePath)
                            ? p.DisplayName
                            : Path.GetFileNameWithoutExtension(p.ExecutablePath),
                        TotalBytesSent = p.TotalBytesSent,
                        TotalBytesReceived = p.TotalBytesReceived,
                        LastSeen = p.LastSeen,
                        IsTrusted = p.IsTrusted,
                        IsBlocked = p.IsBlocked
                    });
                }
            }
            else
            {
                // Fallback: wenn AppProfiles (noch) leer sind, aus persistierten
                // Verbindungen aggregieren. So liefert Reload trotzdem Ergebnisse.
                var from = DateTimeOffset.UtcNow.AddDays(-14);
                var to = DateTimeOffset.UtcNow;
                var summary = await _connections.GetAppActivityAsync(from, to, ct);

                foreach (var s in summary)
                {
                    var displayName = string.IsNullOrWhiteSpace(s.ExecutablePath)
                        ? s.ProcessName
                        : Path.GetFileNameWithoutExtension(s.ExecutablePath);

                    _allApps.Add(new ApplicationListItemViewModel
                    {
                        DisplayName = string.IsNullOrWhiteSpace(displayName)
                            ? "Unbekannte Anwendung"
                            : displayName,
                        ExecutablePath = s.ExecutablePath,
                        ProcessName = s.ProcessName,
                        TotalBytesSent = s.TotalBytesSent,
                        TotalBytesReceived = s.TotalBytesReceived,
                        LastSeen = s.LastSeen,
                        IsTrusted = false,
                        IsBlocked = false
                    });
                }
            }

            ApplyFilter();
            StatusText = Applications.Count == 0
                ? "Keine Anwendungen in der Datenbank gefunden."
                : $"{Applications.Count} Anwendungen geladen.";
        }
        catch (OperationCanceledException)
        {
            StatusText = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Laden der Anwendungsübersicht");
            StatusText = "Fehler beim Laden der Anwendungen.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();

        if (SelectedApp is not null)
            await LoadActivityForSelectionAsync(SelectedApp);
    }

    [RelayCommand]
    private void CloseDetails()
    {
        IsDetailsOpen = false;
        RecentConnections.Clear();
        LastActivityStatus = "Noch keine Aktivität geladen.";
        AppFirewallRules.CloseCommand.Execute(null);
    }

    [RelayCommand]
    private void OpenActivitySection() => ActiveSection = ApplicationsDetailsSection.Activity;

    [RelayCommand]
    private async Task OpenFirewallSectionAsync()
    {
        ActiveSection = ApplicationsDetailsSection.Firewall;

        if (SelectedApp is null)
            return;

        await LoadFirewallForSelectionAsync(SelectedApp);
    }

    [RelayCommand]
    private void OpenPropertiesSection() => ActiveSection = ApplicationsDetailsSection.Properties;

    public async Task OpenForAppAsync(
        ApplicationListItemViewModel item,
        ApplicationsDetailsSection section,
        CancellationToken ct = default)
    {
        SelectedApp = item;
        IsDetailsOpen = true;
        ActiveSection = section;

        await LoadActivityForSelectionAsync(item, ct);

        if (section == ApplicationsDetailsSection.Firewall)
            await LoadFirewallForSelectionAsync(item);
    }

    public async Task LoadFirewallForSelectionAsync(
        ApplicationListItemViewModel item,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(item.ExecutablePath)) return;

        await AppFirewallRules.OpenForAppAsync(item.ExecutablePath, item.DisplayName);

        if (ct.IsCancellationRequested)
            AppFirewallRules.CloseCommand.Execute(null);
    }

    public async Task LoadActivityForSelectionAsync(
        ApplicationListItemViewModel item,
        CancellationToken ct = default)
    {
        try
        {
            RecentConnections.Clear();

            var processKey = string.IsNullOrWhiteSpace(item.ProcessName)
                ? item.DisplayName
                : item.ProcessName;

            var rows = await _connections.GetByProcessAsync(processKey, 120, ct);

            ConnectionCount24h = rows.Count(r => r.Timestamp >= DateTimeOffset.UtcNow.AddHours(-24));
            SuspiciousCount24h = rows.Count(r => r.Timestamp >= DateTimeOffset.UtcNow.AddHours(-24)
                                           && r.ReputationScore is >= 0 and < 50);

            foreach (var row in rows.Take(40))
            {
                var bytes = row.BytesSent + row.BytesReceived;
                RecentConnections.Add(new AppRecentConnectionViewModel
                {
                    TimestampDisplay = row.Timestamp.LocalDateTime.ToString("g"),
                    RemoteDisplay = $"{row.RemoteIp}:{row.RemotePort}",
                    Protocol = row.Protocol,
                    BytesDisplay = FormatBytes(bytes),
                    ReputationDisplay = row.ReputationScore < 0
                        ? "Unbekannt"
                        : row.ReputationScore < 20 ? "Gefährlich"
                        : row.ReputationScore < 50 ? "Verdächtig"
                        : "Unauffällig"
                });
            }

            LastActivityStatus = rows.Count == 0
                ? "Keine Aktivität in der Datenbank gefunden."
                : $"{rows.Count} letzte Verbindungen geladen.";
        }
        catch (OperationCanceledException)
        {
            LastActivityStatus = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Laden der Aktivität für {App}", item.DisplayName);
            LastActivityStatus = "Aktivität konnte nicht geladen werden.";
        }
    }

    public async Task MarkTrustedAsync(ApplicationListItemViewModel item)
    {
        if (string.IsNullOrWhiteSpace(item.ExecutablePath)) return;
        await _profiles.SetTrustedAsync(item.ExecutablePath, true);
        await LoadAsync();
    }

    public async Task RemoveTrustedAsync(ApplicationListItemViewModel item)
    {
        if (string.IsNullOrWhiteSpace(item.ExecutablePath)) return;
        await _profiles.SetTrustedAsync(item.ExecutablePath, false);
        await LoadAsync();
    }

    public async Task BlockAppAsync(ApplicationListItemViewModel item)
    {
        if (string.IsNullOrWhiteSpace(item.ExecutablePath) || !_firewall.IsAdministrator) return;

        var ruleName = $"NetOverseer – Block {item.DisplayName}";
        await _firewall.BlockApplicationAsync(item.ExecutablePath, ruleName);
        await _profiles.SetBlockedAsync(item.ExecutablePath, true);
        await LoadAsync();
    }

    private void ApplyFilter()
    {
        var filter = SearchText.Trim();
        var filtered = string.IsNullOrEmpty(filter)
            ? _allApps
            : _allApps.Where(a =>
                a.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || a.ExecutablePath.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || a.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase))
              .ToList();

        Applications.Clear();
        foreach (var item in filtered.OrderByDescending(a => a.LastSeen))
            Applications.Add(item);
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024 => $"{bytes / 1_024.0:F1} KB",
        _ => $"{bytes} B"
    };
}
