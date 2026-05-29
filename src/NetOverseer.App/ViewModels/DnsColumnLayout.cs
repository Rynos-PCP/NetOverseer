// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace NetOverseer.App.ViewModels;

/// <summary>
/// Geteiltes Layout-Modell für die Spaltenbreiten der DNS-Anfragen-Tabelle.
/// Header und Zeilen-Templates binden an dieselbe Instanz, damit Drag-Resize
/// in der Kopfzeile automatisch auf alle Zeilen wirkt. Singleton (Lebenszeit = Prozess).
/// </summary>
public sealed class DnsColumnLayout : INotifyPropertyChanged
{
    public static DnsColumnLayout Instance { get; } = new();

    private DnsColumnLayout() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ── Default-Breiten ──
    private GridLength _timeWidth     = new(90,  GridUnitType.Pixel);
    private GridLength _processWidth  = new(200, GridUnitType.Pixel);
    private GridLength _domainWidth   = new(320, GridUnitType.Pixel);
    private GridLength _typeWidth     = new(70,  GridUnitType.Pixel);
    private GridLength _categoryWidth = new(110, GridUnitType.Pixel);
    // IPs-Spalte nimmt den restlichen Platz ein – nicht in der Bindung enthalten.

    private const double MinWidth = 40;
    private const double MaxWidth = 800;

    public GridLength TimeWidth     { get => _timeWidth;     set => Set(ref _timeWidth,     value); }
    public GridLength ProcessWidth  { get => _processWidth;  set => Set(ref _processWidth,  value); }
    public GridLength DomainWidth   { get => _domainWidth;   set => Set(ref _domainWidth,   value); }
    public GridLength TypeWidth     { get => _typeWidth;     set => Set(ref _typeWidth,     value); }
    public GridLength CategoryWidth { get => _categoryWidth; set => Set(ref _categoryWidth, value); }

    public void Resize(string columnName, double delta)
    {
        switch (columnName)
        {
            case nameof(TimeWidth):     TimeWidth     = Clamp(TimeWidth.Value     + delta); break;
            case nameof(ProcessWidth):  ProcessWidth  = Clamp(ProcessWidth.Value  + delta); break;
            case nameof(DomainWidth):   DomainWidth   = Clamp(DomainWidth.Value   + delta); break;
            case nameof(TypeWidth):     TypeWidth     = Clamp(TypeWidth.Value     + delta); break;
            case nameof(CategoryWidth): CategoryWidth = Clamp(CategoryWidth.Value + delta); break;
        }
    }

    private static GridLength Clamp(double pixels) =>
        new(Math.Clamp(pixels, MinWidth, MaxWidth), GridUnitType.Pixel);

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>Parameterloser XAML-Resource-Wrapper für die Singleton-Instanz.</summary>
public sealed class DnsColumnLayoutSource
{
    public DnsColumnLayout Layout => DnsColumnLayout.Instance;
}
