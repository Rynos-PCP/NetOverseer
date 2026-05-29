// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.App.ViewModels;

/// <summary>
/// ViewModel für die Firewall-Seite.
/// Lädt Regeln progressiv (inkrementell sichtbar) und stellt Detail-Bearbeitung bereit.
/// </summary>
public sealed partial class FirewallViewModel : ObservableObject
{
    private readonly IFirewallService           _firewall;
    private readonly ILogger<FirewallViewModel> _logger;

    private DispatcherQueue?         _dispatcher;
    private CancellationTokenSource? _allRulesCts;
    private CancellationTokenSource? _myRulesCts;

    private readonly Dictionary<string, FirewallAppGroupViewModel> _allGroupIndex
        = new(StringComparer.OrdinalIgnoreCase);

    // Batch-Konstanten für inkrementelle Anzeige. Kleine Batches + kurze Pausen
    // halten den UI-Thread reaktionsfähig, auch während tausende Regeln geladen werden.
    private const int BatchSize     = 25;
    private const int BatchPauseMs  = 15;

    // ──────────────────────────────────────────────────────────────────────
    // Observable Properties
    // ──────────────────────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private bool   _isLoadingAll;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool   _isAdministrator;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool   _allRulesLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoNetOverseerRules))]
    private int _netOverseerRuleCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoOrphanedRules))]
    private int _orphanedRuleCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoAllRules))]
    private int _allRuleCount;

    [ObservableProperty] private FirewallRuleItemViewModel? _editingRule;
    [ObservableProperty] private bool                       _isEditPaneOpen;

    /// <summary>True wenn die UAC-Warnung angezeigt werden soll (kein Admin).</summary>
    public bool ShowAdminWarning => !IsAdministrator;

    public bool HasNoNetOverseerRules => !IsLoading && NetOverseerRuleCount == 0;
    public bool HasNoOrphanedRules    => !IsLoading && OrphanedRuleCount    == 0;
    public bool HasNoAllRules         => !IsLoadingAll && AllRulesLoaded && AllRuleCount == 0;

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(HasNoNetOverseerRules));
        OnPropertyChanged(nameof(HasNoOrphanedRules));
    }

    partial void OnIsLoadingAllChanged(bool value)    => OnPropertyChanged(nameof(HasNoAllRules));
    partial void OnAllRulesLoadedChanged(bool value)  => OnPropertyChanged(nameof(HasNoAllRules));
    partial void OnIsAdministratorChanged(bool value) => OnPropertyChanged(nameof(ShowAdminWarning));

    // ──────────────────────────────────────────────────────────────────────
    // Collections
    // ──────────────────────────────────────────────────────────────────────

    public ObservableCollection<FirewallRuleItemViewModel> NetOverseerRules { get; } = new();
    public ObservableCollection<FirewallRuleItemViewModel> OrphanedRules    { get; } = new();
    public ObservableCollection<FirewallAppGroupViewModel> AllGroups        { get; } = new();

    // ──────────────────────────────────────────────────────────────────────
    // Konstruktor / Initialisierung
    // ──────────────────────────────────────────────────────────────────────

    public FirewallViewModel(IFirewallService firewall, ILogger<FirewallViewModel> logger)
    {
        _firewall       = firewall;
        _logger         = logger;
        IsAdministrator = firewall.IsAdministrator;
        StatusText      = "Bereit.";
    }

    public void Initialize(DispatcherQueue dispatcher) => _dispatcher ??= dispatcher;

    // ──────────────────────────────────────────────────────────────────────
    // Streaming-Load: NetOverseer- und verwaiste Regeln
    // ──────────────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task LoadAsync(CancellationToken externalCt = default)
    {
        if (IsLoading) return;

        _myRulesCts?.Cancel();
        _myRulesCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var ct = _myRulesCts.Token;

        IsLoading            = true;
        NetOverseerRules.Clear();
        OrphanedRules.Clear();
        NetOverseerRuleCount = 0;
        OrphanedRuleCount    = 0;
        StatusText           = "Lade NetOverseer-Regeln …";

        try
        {
            var batch = new List<FirewallRuleInfo>(BatchSize);

            await foreach (var rule in _firewall.StreamAllRulesAsync(ct).ConfigureAwait(false))
            {
                if (!rule.IsNetOverseerRule && !rule.IsOrphaned) continue;

                batch.Add(rule);
                if (batch.Count >= BatchSize)
                {
                    var snapshot = batch.ToArray();
                    batch.Clear();
                    PostLow(() => FlushMyRulesBatch(snapshot));
                    // Kurz dem UI-Thread Luft geben – Maus/Tastatur können dazwischen.
                    await Task.Delay(BatchPauseMs, ct).ConfigureAwait(false);
                }
            }

            if (batch.Count > 0)
            {
                var snapshot = batch.ToArray();
                batch.Clear();
                PostLow(() => FlushMyRulesBatch(snapshot));
            }

            Post(() =>
            {
                StatusText = NetOverseerRuleCount == 0
                    ? "Keine NetOverseer-Regeln vorhanden."
                    : $"{NetOverseerRuleCount} NetOverseer-Regel(n) · {OrphanedRuleCount} verwaist.";
            });
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Laden der NetOverseer-Regeln");
            Post(() => StatusText = "Fehler beim Laden der Regeln.");
        }
        finally
        {
            Post(() => IsLoading = false);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Streaming-Load: Alle Regeln (gruppiert nach App)
    // ──────────────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task LoadAllRulesAsync(CancellationToken externalCt = default)
    {
        if (IsLoadingAll) return;

        _allRulesCts?.Cancel();
        _allRulesCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var ct = _allRulesCts.Token;

        IsLoadingAll  = true;
        AllRulesLoaded = false;
        AllRuleCount   = 0;
        AllGroups.Clear();
        _allGroupIndex.Clear();

        var search = SearchText;

        try
        {
            var batch = new List<(FirewallRuleInfo Rule, string Key, string AppPath)>(BatchSize);

            await foreach (var rule in _firewall.StreamAllRulesAsync(ct).ConfigureAwait(false))
            {
                if (!Matches(rule, search)) continue;

                var key     = (rule.ApplicationPath ?? string.Empty).ToLowerInvariant();
                var appPath = rule.ApplicationPath ?? string.Empty;
                batch.Add((rule, key, appPath));

                if (batch.Count >= BatchSize)
                {
                    var snapshot = batch.ToArray();
                    batch.Clear();
                    PostLow(() => FlushAllRulesBatch(snapshot));
                    // UI-Thread atmen lassen – Klicks/Resize kommen währenddessen durch.
                    await Task.Delay(BatchPauseMs, ct).ConfigureAwait(false);
                }
            }

            if (batch.Count > 0)
            {
                var snapshot = batch.ToArray();
                batch.Clear();
                PostLow(() => FlushAllRulesBatch(snapshot));
            }

            Post(() =>
            {
                AllRulesLoaded = true;
                StatusText     = AllRuleCount == 0
                    ? "Keine Firewall-Regeln gefunden."
                    : $"{AllRuleCount} Regel(n) in {AllGroups.Count} Gruppe(n).";
            });
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Streamen der Firewall-Regeln");
            Post(() => StatusText = "Fehler beim Laden der Regeln.");
        }
        finally
        {
            Post(() => IsLoadingAll = false);
        }
    }

    private void AddRuleToGroup(string key, string appPath, FirewallRuleItemViewModel vm)
    {
        if (!_allGroupIndex.TryGetValue(key, out var group))
        {
            group = new FirewallAppGroupViewModel(appPath, Array.Empty<FirewallRuleItemViewModel>());
            _allGroupIndex[key] = group;

            var idx = 0;
            for (; idx < AllGroups.Count; idx++)
            {
                if (string.Compare(AllGroups[idx].AppDisplayName, group.AppDisplayName,
                                   StringComparison.OrdinalIgnoreCase) > 0)
                    break;
            }
            AllGroups.Insert(idx, group);
        }

        vm.Removed += (_, _) =>
        {
            group.Rules.Remove(vm);
            AllRuleCount = Math.Max(0, AllRuleCount - 1);
            if (group.Rules.Count == 0)
            {
                AllGroups.Remove(group);
                _allGroupIndex.Remove(key);
            }
        };

        group.Rules.Add(vm);
        AllRuleCount++;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Refresh / Filter / Edit
    // ──────────────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadAsync();
        if (AllRulesLoaded || IsLoadingAll)
            await LoadAllRulesAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (AllRulesLoaded || IsLoadingAll)
            _ = LoadAllRulesAsync();
    }

    [RelayCommand]
    public void BeginEdit(FirewallRuleItemViewModel rule)
    {
        rule.BeginEdit();
        EditingRule    = rule;
        IsEditPaneOpen = true;
    }

    [RelayCommand]
    public void CancelEdit()
    {
        EditingRule?.CancelEdit();
        EditingRule    = null;
        IsEditPaneOpen = false;
    }

    [RelayCommand]
    public async Task SaveEditAsync()
    {
        if (EditingRule is null) return;
        var ok = await EditingRule.SaveAsync();
        if (ok)
        {
            EditingRule    = null;
            IsEditPaneOpen = false;
            StatusText     = "Regel gespeichert.";
        }
        else
        {
            StatusText = "Speichern fehlgeschlagen.";
        }
    }

    [RelayCommand]
    public void RestartAsAdministrator()
    {
        try
        {
            var exe = Environment.ProcessPath ?? "NetOverseer.App.exe";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb            = "runas"
            });
            Microsoft.UI.Xaml.Application.Current.Exit();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UAC-Elevation abgelehnt oder fehlgeschlagen");
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private FirewallRuleItemViewModel CreateRuleVm(FirewallRuleInfo rule)
    {
        var vm = new FirewallRuleItemViewModel(
            rule,
            _firewall,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FirewallRuleItemViewModel>.Instance);

        vm.EditRequested += (_, _) => BeginEdit(vm);
        vm.Removed += (s, _) =>
        {
            if (s is FirewallRuleItemViewModel rvm)
            {
                if (NetOverseerRules.Remove(rvm))
                    NetOverseerRuleCount = NetOverseerRules.Count;
                if (OrphanedRules.Remove(rvm))
                    OrphanedRuleCount = OrphanedRules.Count;
            }
        };
        return vm;
    }

    private static bool Matches(FirewallRuleInfo r, string search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        return r.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (r.ApplicationPath?.Contains(search, StringComparison.OrdinalIgnoreCase) == true)
            || (r.RemoteAddresses?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
    }

    private void Post(Action action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess) action();
        else _dispatcher.TryEnqueue(() => action());
    }

    /// <summary>
    /// Dispatch mit niedriger Priorität – verarbeitet Batch-Updates erst,
    /// nachdem höher priorisierte Input-Events (Maus, Tastatur) abgearbeitet wurden.
    /// </summary>
    private void PostLow(Action action)
    {
        if (_dispatcher is null) action();
        else _dispatcher.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => action());
    }

    private void FlushMyRulesBatch(IReadOnlyList<FirewallRuleInfo> rules)
    {
        foreach (var rule in rules)
        {
            var vm = CreateRuleVm(rule);
            if (rule.IsOrphaned)
            {
                OrphanedRules.Add(vm);
                OrphanedRuleCount = OrphanedRules.Count;
            }
            else
            {
                NetOverseerRules.Add(vm);
                NetOverseerRuleCount = NetOverseerRules.Count;
            }
        }
    }

    private void FlushAllRulesBatch(
        IReadOnlyList<(FirewallRuleInfo Rule, string Key, string AppPath)> items)
    {
        foreach (var (rule, key, appPath) in items)
        {
            var vm = CreateRuleVm(rule);
            AddRuleToGroup(key, appPath, vm);
        }
    }
}
