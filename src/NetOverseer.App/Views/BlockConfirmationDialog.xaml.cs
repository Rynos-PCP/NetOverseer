// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using NetOverseer.Core.Interfaces;

namespace NetOverseer.App.Views;

/// <summary>Block-Modus für den Confirmation-Dialog.</summary>
public enum BlockMode { IpOnly, AppOnly, AppAndIp }

/// <summary>
/// ContentDialog zum Bestätigen einer Firewall-Blockierung.
/// Zeigt Verbindungsdetails und lässt den Benutzer den Block-Modus wählen.
/// </summary>
public sealed partial class BlockConfirmationDialog : ContentDialog
{
    private static readonly HashSet<string> SystemDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64"),
        Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? ""
    };

    private readonly IFirewallService _firewall;
    private readonly ILogger<BlockConfirmationDialog> _logger;

    // Verbindungsdaten
    private readonly string     _executablePath;
    private readonly string     _processDisplayName;
    private readonly IPAddress? _remoteIp;

    /// <summary>Vom Benutzer gewählter Block-Modus (gesetzt wenn PrimaryButton geklickt).</summary>
    public BlockMode SelectedMode { get; private set; } = BlockMode.IpOnly;

    /// <summary>Regel-Name der angelegten Firewall-Regel (nach erfolgreichem Blockieren).</summary>
    public string? CreatedRuleName { get; private set; }

    // ──────────────────────────────────────────────────────────────────────
    // Konstruktor
    // ──────────────────────────────────────────────────────────────────────

    public BlockConfirmationDialog(
        IFirewallService firewall,
        ILogger<BlockConfirmationDialog> logger,
        string executablePath,
        string processDisplayName,
        IPAddress? remoteIp)
    {
        _firewall           = firewall;
        _logger             = logger;
        _executablePath     = executablePath;
        _processDisplayName = processDisplayName;
        _remoteIp           = remoteIp;

        InitializeComponent();
        ConfigureContent();
        this.PrimaryButtonClick += OnPrimaryButtonClick;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Setup
    // ──────────────────────────────────────────────────────────────────────

    private void ConfigureContent()
    {
        // Verbindungs-Info anzeigen
        ProcessInfoText.Text = $"🔒 {_processDisplayName}";

        RemoteInfoText.Text = _remoteIp is not null
            ? $"→ {_remoteIp}"
            : "Remote-Adresse unbekannt";

        // RadioButton für IP-Blockierung deaktivieren falls keine IP bekannt
        if (_remoteIp is null)
        {
            RadioIpOnly.IsEnabled   = false;
            RadioAppAndIp.IsEnabled = false;
            RadioAppOnly.IsChecked  = true;
        }

        // Systemapp-Warnung
        if (IsSystemApp(_executablePath))
            SystemAppWarning.IsOpen = true;

        // Admin-Warnung
        if (!_firewall.IsAdministrator)
        {
            AdminWarning.IsOpen   = true;
            IsPrimaryButtonEnabled = false;
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Blockieren-Button-Handler
    // ──────────────────────────────────────────────────────────────────────

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Schließen verhindern bis die async-Aktion abgeschlossen ist
        var deferral = args.GetDeferral();
        try
        {
            SelectedMode = DetermineMode();
            await ExecuteBlockAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Erstellen der Firewall-Regel");
            // Dialog offen lassen damit Benutzer den Fehler sieht
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private BlockMode DetermineMode()
    {
        if (RadioAppOnly.IsChecked == true)  return BlockMode.AppOnly;
        if (RadioAppAndIp.IsChecked == true) return BlockMode.AppAndIp;
        return BlockMode.IpOnly;
    }

    private async Task ExecuteBlockAsync()
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        switch (SelectedMode)
        {
            case BlockMode.IpOnly when _remoteIp is not null:
            {
                var rule = $"NetOverseer – IP {_remoteIp} ({timestamp})";
                await _firewall.BlockRemoteIpAsync(_remoteIp, rule);
                CreatedRuleName = rule;
                break;
            }
            case BlockMode.AppOnly:
            {
                var rule = $"NetOverseer – {_processDisplayName} ({timestamp})";
                await _firewall.BlockApplicationAsync(_executablePath, rule);
                CreatedRuleName = rule;
                break;
            }
            case BlockMode.AppAndIp when _remoteIp is not null:
            {
                await _firewall.BlockApplicationToIpAsync(_executablePath, _remoteIp);
                CreatedRuleName = $"NetOverseer – {_processDisplayName} → {_remoteIp}";
                break;
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helper
    // ──────────────────────────────────────────────────────────────────────

    private static bool IsSystemApp(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return false;
        var dir = Path.GetDirectoryName(exePath) ?? "";
        return SystemDirs.Any(sd => dir.StartsWith(sd, StringComparison.OrdinalIgnoreCase));
    }
}
