// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;
using Windows.UI;

namespace NetOverseer.App.ViewModels;

/// <summary>
/// ViewModel für eine einzelne Firewall-Regel.
/// Unterstützt sowohl die Listen-Darstellung als auch das Detail-Edit-Panel
/// (BeginEdit / SaveAsync / CancelEdit).
/// </summary>
public sealed partial class FirewallRuleItemViewModel : ObservableObject
{
    private readonly IFirewallService                       _firewall;
    private readonly ILogger<FirewallRuleItemViewModel>     _logger;
    private bool                                            _suppressChange;

    public FirewallRuleInfo Rule { get; private set; }

    // ──────────────────────────────────────────────────────────────────────
    // Listen-Properties (Read-Only / Toggle)
    // ──────────────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isBusy;

    public string DisplayName            { get; private set; } = string.Empty;
    public string DirectionGlyph         { get; private set; } = string.Empty;
    public string DirectionTooltip       { get; private set; } = string.Empty;
    public string ActionDisplay          { get; private set; } = string.Empty;
    public string ProtocolDisplay        { get; private set; } = string.Empty;
    public string RemoteAddressDisplay   { get; private set; } = string.Empty;
    public SolidColorBrush ActionChipBackground { get; private set; } = new(Colors.Gray);
    public SolidColorBrush ActionChipForeground { get; private set; } = new(Colors.White);
    public string PortsDisplay           { get; private set; } = string.Empty;
    public bool   IsOrphaned             { get; private set; }
    public bool   IsNetOverseerRule      { get; private set; }

    // ──────────────────────────────────────────────────────────────────────
    // Editier-Buffer (nur während IsEditing aktiv)
    // ──────────────────────────────────────────────────────────────────────

    [ObservableProperty] private bool    _isEditing;
    [ObservableProperty] private string  _editName             = string.Empty;
    [ObservableProperty] private string  _editDescription      = string.Empty;
    [ObservableProperty] private string  _editApplicationPath  = string.Empty;
    [ObservableProperty] private int     _editDirectionIndex;
    [ObservableProperty] private int     _editActionIndex;
    [ObservableProperty] private int     _editProtocolIndex;
    [ObservableProperty] private string  _editLocalPorts       = string.Empty;
    [ObservableProperty] private string  _editRemotePorts      = string.Empty;
    [ObservableProperty] private string  _editRemoteAddresses  = string.Empty;
    [ObservableProperty] private bool    _editIsEnabled;
    [ObservableProperty] private string  _editErrorMessage     = string.Empty;
    [ObservableProperty] private bool    _hasEditError;

    public FirewallRuleItemViewModel(
        FirewallRuleInfo rule,
        IFirewallService firewall,
        ILogger<FirewallRuleItemViewModel> logger)
    {
        Rule      = rule;
        _firewall = firewall;
        _logger   = logger;

        ApplyRule(rule);
    }

    private void ApplyRule(FirewallRuleInfo rule)
    {
        Rule        = rule;
        DisplayName = rule.Name;
        IsOrphaned  = rule.IsOrphaned;
        IsNetOverseerRule = rule.IsNetOverseerRule;

        _suppressChange = true;
        try { IsEnabled = rule.IsEnabled; }
        finally { _suppressChange = false; }

        DirectionGlyph   = rule.Direction == FirewallDirection.Inbound ? "\uE74A" : "\uE74B";
        DirectionTooltip = rule.Direction == FirewallDirection.Inbound ? "Eingehend" : "Ausgehend";

        ActionDisplay = rule.Action == FirewallAction.Block ? "Blockiert" : "Erlaubt";

        ActionChipBackground = rule.Action == FirewallAction.Block
            ? new SolidColorBrush(Color.FromArgb(0xFF, 0xC4, 0x1F, 0x1F))
            : new SolidColorBrush(Color.FromArgb(0xFF, 0x10, 0x7C, 0x10));

        ActionChipForeground = new SolidColorBrush(Colors.White);

        ProtocolDisplay = rule.Protocol switch
        {
            FirewallProtocol.Tcp  => "TCP",
            FirewallProtocol.Udp  => "UDP",
            FirewallProtocol.Icmp => "ICMP",
            _                     => "Alle"
        };

        var remAddr = rule.RemoteAddresses;
        RemoteAddressDisplay = string.IsNullOrEmpty(remAddr) || remAddr == "*"
            ? "Alle"
            : remAddr.Length > 20 ? remAddr[..20] + "…" : remAddr;

        var lp = rule.LocalPorts;
        var rp = rule.RemotePorts;
        PortsDisplay = (string.IsNullOrEmpty(lp) || lp == "*") &&
                       (string.IsNullOrEmpty(rp) || rp == "*")
            ? "Alle Ports"
            : $"{(string.IsNullOrEmpty(lp) ? "*" : lp)} → {(string.IsNullOrEmpty(rp) ? "*" : rp)}";

        // Anzeige-Properties neu binden lassen (kein generierter PropertyChanged).
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(DirectionGlyph));
        OnPropertyChanged(nameof(DirectionTooltip));
        OnPropertyChanged(nameof(ActionDisplay));
        OnPropertyChanged(nameof(ActionChipBackground));
        OnPropertyChanged(nameof(ActionChipForeground));
        OnPropertyChanged(nameof(ProtocolDisplay));
        OnPropertyChanged(nameof(RemoteAddressDisplay));
        OnPropertyChanged(nameof(PortsDisplay));
        OnPropertyChanged(nameof(IsOrphaned));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Toggle-Aktiv
    // ──────────────────────────────────────────────────────────────────────

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressChange) return;
        _ = ApplyEnabledChangeAsync(value);
    }

    private async Task ApplyEnabledChangeAsync(bool value)
    {
        IsBusy = true;
        try
        {
            await _firewall.SetRuleEnabledAsync(Rule.Name, value);
            Rule.IsEnabled = value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Ändern des Aktivierungsstatus der Regel {Rule}", Rule.Name);
            _suppressChange = true;
            try { IsEnabled = !value; }
            finally { _suppressChange = false; }
        }
        finally { IsBusy = false; }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Edit-Modus
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Wird ausgelöst wenn der Benutzer „Bearbeiten“ klickt.</summary>
    public event EventHandler? EditRequested;

    /// <summary>Wird ausgelöst, wenn die Regel erfolgreich gelöscht wurde.</summary>
    public event EventHandler? Removed;

    [RelayCommand]
    private void Edit() => EditRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Initialisiert die Edit-Felder aus dem aktuellen Regelzustand.</summary>
    public void BeginEdit()
    {
        EditName             = Rule.Name;
        EditDescription      = Rule.Description ?? string.Empty;
        EditApplicationPath  = Rule.ApplicationPath ?? string.Empty;
        EditDirectionIndex   = Rule.Direction == FirewallDirection.Inbound ? 0 : 1;
        EditActionIndex      = Rule.Action == FirewallAction.Allow ? 0 : 1;
        EditProtocolIndex    = Rule.Protocol switch
        {
            FirewallProtocol.Any  => 0,
            FirewallProtocol.Tcp  => 1,
            FirewallProtocol.Udp  => 2,
            FirewallProtocol.Icmp => 3,
            _                     => 0
        };
        EditLocalPorts      = Rule.LocalPorts      ?? string.Empty;
        EditRemotePorts     = Rule.RemotePorts     ?? string.Empty;
        EditRemoteAddresses = Rule.RemoteAddresses ?? string.Empty;
        EditIsEnabled       = Rule.IsEnabled;
        EditErrorMessage    = string.Empty;
        HasEditError        = false;
        IsEditing           = true;
    }

    public void CancelEdit()
    {
        IsEditing    = false;
        HasEditError = false;
        EditErrorMessage = string.Empty;
    }

    /// <summary>Validiert und speichert die Änderungen. Liefert <c>true</c> bei Erfolg.</summary>
    public async Task<bool> SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(EditName))
        {
            HasEditError     = true;
            EditErrorMessage = "Name darf nicht leer sein.";
            return false;
        }

        var direction = EditDirectionIndex == 0 ? FirewallDirection.Inbound : FirewallDirection.Outbound;
        var action    = EditActionIndex == 0 ? FirewallAction.Allow : FirewallAction.Block;
        var protocol  = EditProtocolIndex switch
        {
            1 => FirewallProtocol.Tcp,
            2 => FirewallProtocol.Udp,
            3 => FirewallProtocol.Icmp,
            _ => FirewallProtocol.Any
        };

        var update = new FirewallRuleUpdate
        {
            Name            = EditName,
            Description     = EditDescription,
            ApplicationPath = EditApplicationPath,
            Direction       = direction,
            Action          = action,
            Protocol        = protocol,
            LocalPorts      = string.IsNullOrWhiteSpace(EditLocalPorts)  ? "*" : EditLocalPorts,
            RemotePorts     = string.IsNullOrWhiteSpace(EditRemotePorts) ? "*" : EditRemotePorts,
            RemoteAddresses = string.IsNullOrWhiteSpace(EditRemoteAddresses) ? "*" : EditRemoteAddresses,
            IsEnabled       = EditIsEnabled
        };

        IsBusy = true;
        try
        {
            await _firewall.UpdateRuleAsync(Rule.Name, update);

            // Lokales Modell aktualisieren – kein Re-Read aus COM nötig.
            var updated = new FirewallRuleInfo
            {
                Name              = update.Name!,
                Description       = update.Description,
                ApplicationPath   = string.IsNullOrEmpty(update.ApplicationPath) ? null : update.ApplicationPath,
                ApplicationName   = string.IsNullOrEmpty(update.ApplicationPath)
                                       ? null
                                       : Path.GetFileNameWithoutExtension(update.ApplicationPath),
                Direction         = direction,
                Action            = action,
                Protocol          = protocol,
                LocalPorts        = update.LocalPorts,
                RemotePorts       = update.RemotePorts,
                RemoteAddresses   = update.RemoteAddresses,
                IsEnabled         = update.IsEnabled ?? Rule.IsEnabled,
                IsOrphaned        = Rule.IsOrphaned,
                IsNetOverseerRule = Rule.IsNetOverseerRule,
                GroupName         = Rule.GroupName
            };

            ApplyRule(updated);
            IsEditing    = false;
            HasEditError = false;
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            HasEditError     = true;
            EditErrorMessage = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Aktualisieren der Regel {Rule}", Rule.Name);
            HasEditError     = true;
            EditErrorMessage = ex.Message;
            return false;
        }
        finally { IsBusy = false; }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Löschen
    // ──────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RemoveAsync()
    {
        IsBusy = true;
        try
        {
            await _firewall.RemoveRuleAsync(Rule.Name);
            Removed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Entfernen der Regel {Rule}", Rule.Name);
        }
        finally { IsBusy = false; }
    }
}
