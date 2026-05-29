// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using CommunityToolkit.Mvvm.ComponentModel;

namespace NetOverseer.App.ViewModels;

/// <summary>
/// Auswählbare Filter-Option für das Erweiterte-Filter-Flyout
/// (z. B. Verbindungs-Status, Reputation, Infrastruktur).
/// </summary>
public sealed partial class FilterOptionViewModel : ObservableObject
{
    /// <summary>Anzeigetext für die CheckBox.</summary>
    public string Label { get; }

    /// <summary>Identifikations-Wert (z. B. Enum-Name) – wird vom ViewModel zum Filtern genutzt.</summary>
    public string Value { get; }

    [ObservableProperty]
    private bool _isSelected;

    public FilterOptionViewModel(string label, string value, bool initiallySelected = false)
    {
        Label       = label;
        Value       = value;
        _isSelected = initiallySelected;
    }
}
