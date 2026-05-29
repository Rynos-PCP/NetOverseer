// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using NetOverseer.Core.Models;
using Windows.UI;

namespace NetOverseer.App.ViewModels;

/// <summary>
/// Präsentationsmodell für einen einzelnen DNS-Eintrag in der DNS-Ansicht.
/// Wird ausschließlich auf dem UI-Thread erzeugt.
/// </summary>
public sealed partial class DnsQueryItemViewModel : ObservableObject
{
    // ──────────────────────────────────────────────────────────────────────
    // Rohdaten
    // ──────────────────────────────────────────────────────────────────────

    public DnsQueryEvent Event { get; }

    /// <summary>Optionale aufgelöste Prozess-Info (Anzeigename, Kategorie).</summary>
    public ProcessInfo? Process { get; }

    // ──────────────────────────────────────────────────────────────────────
    // Konstruktor
    // ──────────────────────────────────────────────────────────────────────

    public DnsQueryItemViewModel(DnsQueryEvent evt, ProcessInfo? process = null)
    {
        Event   = evt;
        Process = process;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Anzeige-Properties (berechnete Werte für x:Bind)
    // ──────────────────────────────────────────────────────────────────────

    public string TimeDisplay =>
        Event.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");

    public string ProcessDisplay
    {
        get
        {
            // 1) Auflöser-Ergebnis bevorzugen (enthält svchost-Service-Namen)
            if (Process is { DisplayName.Length: > 0 } p &&
                !p.DisplayName.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))
                return p.DisplayName;

            // 2) ETW-eigener ProcessName (falls TraceEvent ihn kennt)
            if (!string.IsNullOrEmpty(Event.ProcessName))
                return Event.ProcessName;

            // 3) Fallback: PID
            return $"PID {Event.ProcessId}";
        }
    }

    public string QueryName =>
        Event.QueryName;

    public string QueryTypeDisplay =>
        Event.QueryType.ToString().ToUpperInvariant();

    public string CategoryDisplay => Event.Category switch
    {
        DnsCategory.Tracker    => "Tracker",
        DnsCategory.Telemetry  => "Telemetrie",
        DnsCategory.Cdn        => "CDN",
        DnsCategory.Suspicious => "Verdächtig",
        DnsCategory.Normal     => "Normal",
        _                      => "Unbekannt",
    };

    public string ResolvedIpsDisplay =>
        Event.ResolvedAddresses.Length == 0
            ? (Event.WasSuccessful ? "–" : "NXDOMAIN")
            : string.Join(", ", Event.ResolvedAddresses);

    // ──────────────────────────────────────────────────────────────────────
    // Farben für Kategorie-Chip
    // ──────────────────────────────────────────────────────────────────────

    private static readonly SolidColorBrush BrushSuspicious = new(Color.FromArgb(255, 200,  50,  50));
    private static readonly SolidColorBrush BrushTracker    = new(Color.FromArgb(255, 200, 100,  30));
    private static readonly SolidColorBrush BrushTelemetry  = new(Color.FromArgb(255,  50, 100, 200));
    private static readonly SolidColorBrush BrushCdn        = new(Color.FromArgb(255,  30, 150, 140));
    private static readonly SolidColorBrush BrushNormal     = new(Color.FromArgb(255, 100, 100, 100));
    private static readonly SolidColorBrush BrushUnknown    = new(Color.FromArgb(255, 130, 130, 130));
    private static readonly SolidColorBrush BrushWhite      = new(Colors.White);

    public SolidColorBrush CategoryBackground => Event.Category switch
    {
        DnsCategory.Suspicious => BrushSuspicious,
        DnsCategory.Tracker    => BrushTracker,
        DnsCategory.Telemetry  => BrushTelemetry,
        DnsCategory.Cdn        => BrushCdn,
        DnsCategory.Normal     => BrushNormal,
        _                      => BrushUnknown,
    };

    public SolidColorBrush CategoryForeground => BrushWhite;

    // ──────────────────────────────────────────────────────────────────────
    // Zeilenhintergrund für Malware-Hervorhebung
    // ──────────────────────────────────────────────────────────────────────

    private static readonly SolidColorBrush RowSuspicious =
        new(Color.FromArgb(30, 220, 50, 50));
    private static readonly SolidColorBrush RowTracker =
        new(Color.FromArgb(20, 220, 100, 30));
    private static readonly SolidColorBrush RowTransparent =
        new(Colors.Transparent);

    public SolidColorBrush RowBackground => Event.Category switch
    {
        DnsCategory.Suspicious => RowSuspicious,
        DnsCategory.Tracker    => RowTracker,
        _                      => RowTransparent,
    };
}
