// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Collections.ObjectModel;
using NetOverseer.Core.Models;

namespace NetOverseer.App.ViewModels;

/// <summary>
/// Gruppiert alle Firewall-Regeln einer Anwendung (für die "Alle Regeln"-Ansicht).
/// </summary>
public sealed class FirewallAppGroupViewModel
{
    /// <summary>Vollständiger EXE-Pfad (oder leerer String für globale Regeln).</summary>
    public string AppPath { get; }

    /// <summary>Anzeigename der Anwendung.</summary>
    public string AppDisplayName { get; }

    /// <summary>True, wenn die EXE nicht mehr auf dem System vorhanden ist.</summary>
    public bool IsOrphaned { get; }

    /// <summary>Alle Regeln dieser Gruppe (eingehend + ausgehend).</summary>
    public ObservableCollection<FirewallRuleItemViewModel> Rules { get; } = new();

    // ──────────────────────────────────────────────────────────────────────
    // Aggregat-Properties (für Header-Anzeige)
    // ──────────────────────────────────────────────────────────────────────

    public int   TotalCount    => Rules.Count;
    public int   BlockedCount  => Rules.Count(r => r.Rule.Action == FirewallAction.Block);
    public int   AllowedCount  => Rules.Count(r => r.Rule.Action == FirewallAction.Allow);
    public int   InboundCount  => Rules.Count(r => r.Rule.Direction == FirewallDirection.Inbound);
    public int   OutboundCount => Rules.Count(r => r.Rule.Direction == FirewallDirection.Outbound);
    public bool  HasBlockedRules => BlockedCount > 0;

    public string RuleSummary => $"{TotalCount} Regel{(TotalCount != 1 ? "n" : "")} " +
                                 $"· {BlockedCount} blockiert · {AllowedCount} erlaubt";

    // ──────────────────────────────────────────────────────────────────────
    // Konstruktor
    // ──────────────────────────────────────────────────────────────────────

    public FirewallAppGroupViewModel(
        string appPath,
        IEnumerable<FirewallRuleItemViewModel> rules)
    {
        AppPath = appPath;

        if (string.IsNullOrEmpty(appPath))
        {
            AppDisplayName = "Globale Regeln";
            IsOrphaned     = false;
        }
        else
        {
            AppDisplayName = Path.GetFileName(appPath);
            IsOrphaned     = !File.Exists(appPath);
        }

        foreach (var r in rules)
        {
            r.Removed += (_, _) => Rules.Remove(r);
            Rules.Add(r);
        }
    }
}
