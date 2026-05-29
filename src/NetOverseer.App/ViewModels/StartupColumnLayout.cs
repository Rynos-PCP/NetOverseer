// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace NetOverseer.App.ViewModels;

/// <summary>
/// Geteiltes Layout-Modell für die Spaltenbreiten der Boot-Aufzeichnungs-Tabelle.
/// Header und Zeilen-Template binden an dieselbe Singleton-Instanz, damit Drag-Resize
/// in der Kopfzeile automatisch auf alle Zeilen wirkt – analog zur Live-Verbindungen-Seite.
/// </summary>
public sealed class StartupColumnLayout : INotifyPropertyChanged
{
    public static StartupColumnLayout Instance { get; } = new();

    private StartupColumnLayout() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ── Default-Breiten (Pixel) ────────────────────────────────────────────
    private GridLength _offsetWidth   = new(64,  GridUnitType.Pixel);
    private GridLength _processWidth  = new(180, GridUnitType.Pixel);
    private GridLength _pidWidth      = new(60,  GridUnitType.Pixel);
    private GridLength _protocolWidth = new(48,  GridUnitType.Pixel);
    private GridLength _localWidth    = new(140, GridUnitType.Pixel);
    private GridLength _remoteWidth   = new(180, GridUnitType.Pixel);

    private const double MinWidth = 40;
    private const double MaxWidth = 600;

    public GridLength OffsetWidth   { get => _offsetWidth;   set => Set(ref _offsetWidth,   value); }
    public GridLength ProcessWidth  { get => _processWidth;  set => Set(ref _processWidth,  value); }
    public GridLength PidWidth      { get => _pidWidth;      set => Set(ref _pidWidth,      value); }
    public GridLength ProtocolWidth { get => _protocolWidth; set => Set(ref _protocolWidth, value); }
    public GridLength LocalWidth    { get => _localWidth;    set => Set(ref _localWidth,    value); }
    public GridLength RemoteWidth   { get => _remoteWidth;   set => Set(ref _remoteWidth,   value); }

    /// <summary>Erhöht/verringert die Breite einer Spalte um <paramref name="delta"/> Pixel.</summary>
    public void Resize(string columnName, double delta)
    {
        switch (columnName)
        {
            case nameof(OffsetWidth):   OffsetWidth   = Clamp(OffsetWidth.Value   + delta); break;
            case nameof(ProcessWidth):  ProcessWidth  = Clamp(ProcessWidth.Value  + delta); break;
            case nameof(PidWidth):      PidWidth      = Clamp(PidWidth.Value      + delta); break;
            case nameof(ProtocolWidth): ProtocolWidth = Clamp(ProtocolWidth.Value + delta); break;
            case nameof(LocalWidth):    LocalWidth    = Clamp(LocalWidth.Value    + delta); break;
            case nameof(RemoteWidth):   RemoteWidth   = Clamp(RemoteWidth.Value   + delta); break;
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

/// <summary>Parameterloser XAML-Resource-Wrapper für das Singleton.</summary>
public sealed class StartupColumnLayoutSource
{
    public StartupColumnLayout Layout => StartupColumnLayout.Instance;
}
