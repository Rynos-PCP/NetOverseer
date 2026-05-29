// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.App.ViewModels;

/// <summary>
/// ViewModel für die einfliegende Seitenleiste, die alle Firewall-Regeln
/// einer ausgewählten Anwendung anzeigt (Azure-Portal-artiges Blade).
/// </summary>
public sealed partial class AppFirewallRulesViewModel : ObservableObject
{
    private readonly IFirewallService _firewall;
    private readonly ILogger<AppFirewallRulesViewModel> _logger;
    private readonly ILogger<FirewallRuleItemViewModel> _itemLogger;

    private CancellationTokenSource? _loadCts;

    public AppFirewallRulesViewModel(
        IFirewallService firewall,
        ILogger<AppFirewallRulesViewModel>  logger,
        ILogger<FirewallRuleItemViewModel>  itemLogger)
    {
        _firewall   = firewall;
        _logger     = logger;
        _itemLogger = itemLogger;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Anzeige-Zustand
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Panel sichtbar? Steuert Slide-In/Out via VisualState.</summary>
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoRules))]
    [NotifyPropertyChangedFor(nameof(RuleCountText))]
    private int _ruleCount;

    [ObservableProperty]
    private string _appName = string.Empty;

    [ObservableProperty]
    private string _appPath = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public ObservableCollection<FirewallRuleItemViewModel> Rules { get; } = [];

    public bool HasNoRules => !IsLoading && RuleCount == 0;

    public string RuleCountText =>
        RuleCount == 1 ? "1 Regel" : $"{RuleCount} Regeln";

    // ──────────────────────────────────────────────────────────────────────
    // Commands
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Öffnet das Panel und lädt alle Regeln für den übergebenen EXE-Pfad.</summary>
    public async Task OpenForAppAsync(string executablePath, string displayName)
    {
        AppPath = executablePath ?? string.Empty;
        AppName = string.IsNullOrWhiteSpace(displayName) ? "Unbekannte Anwendung" : displayName;
        IsOpen  = true;

        await LoadAsync();
    }

    [RelayCommand]
    private void Close()
    {
        _loadCts?.Cancel();
        IsOpen = false;
        Rules.Clear();
        RuleCount  = 0;
        StatusText = string.Empty;
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    // ──────────────────────────────────────────────────────────────────────
    // Laden
    // ──────────────────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct   = _loadCts.Token;

        IsLoading = true;
        StatusText = "Lade Firewall-Regeln …";
        Rules.Clear();
        RuleCount = 0;

        try
        {
            // Schneller Pfad: per-App-Abfrage via Early-Filter (+ ggf. Service-Cache).
            // Liest pro Regel zunächst nur den ApplicationName und materialisiert
            // nur Treffer komplett. Dadurch deutlich schneller als GetAllRulesAsync.
            var path = AppPath;
            var matches = string.IsNullOrEmpty(path)
                ? Array.Empty<FirewallRuleInfo>()
                : await _firewall.GetRulesForApplicationAsync(path, ct);

            if (ct.IsCancellationRequested) return;

            var ordered = matches
                .OrderByDescending(r => r.IsNetOverseerRule)
                .ThenBy(r => r.Action == FirewallAction.Block ? 0 : 1)
                .ThenBy(r => r.Direction)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var rule in ordered)
                Rules.Add(new FirewallRuleItemViewModel(rule, _firewall, _itemLogger));

            RuleCount = Rules.Count;

            StatusText = RuleCount == 0
                ? "Für diese Anwendung sind keine Firewall-Regeln vorhanden."
                : string.Empty;
        }
        catch (OperationCanceledException) { /* erwartet beim Schließen */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Laden der App-Firewall-Regeln für {Path}", AppPath);
            StatusText = "Fehler beim Laden der Regeln.";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasNoRules));
        }
    }
}
