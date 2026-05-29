// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace NetOverseer.App.ViewModels;

/// <summary>
/// Geteiltes Layout-Modell für die Spaltenbreiten der Live-Verbindungstabelle.
/// Header und Zeilen-Templates binden an dieselbe Instanz, damit Drag-Resize
/// in der Kopfzeile automatisch auf alle Zeilen wirkt.
///
/// Lebenszyklus: Singleton — die Breiten bleiben über Navigationswechsel hinweg
/// erhalten (kein Reset beim Wegnavigieren).
/// </summary>
public sealed class ConnectionColumnLayout : INotifyPropertyChanged
{
    /// <summary>Prozess-weite Singleton-Instanz. Wird über <see cref="ColumnLayoutSource"/> in XAML referenziert.</summary>
    public static ConnectionColumnLayout Instance { get; } = new();

    private ConnectionColumnLayout() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ── Default-Breiten (synchron mit ursprünglicher LiveConnectionsPage.xaml) ──

    private GridLength _appWidth        = new(155, GridUnitType.Pixel);
    private GridLength _protocolWidth   = new(48,  GridUnitType.Pixel);
    private GridLength _localWidth      = new(126, GridUnitType.Pixel);
    private GridLength _remoteWidth     = new(130, GridUnitType.Pixel);
    private GridLength _hostnameWidth   = new(148, GridUnitType.Pixel);
    private GridLength _countryWidth    = new(108, GridUnitType.Pixel);
    private GridLength _reputationWidth = new(85,  GridUnitType.Pixel);
    private GridLength _stateWidth      = new(82,  GridUnitType.Pixel);

    // ── Minimal-/Maximal-Pixel ──

    private const double MinWidth = 40;
    private const double MaxWidth = 600;

    public GridLength AppWidth        { get => _appWidth;        set => Set(ref _appWidth,        value); }
    public GridLength ProtocolWidth   { get => _protocolWidth;   set => Set(ref _protocolWidth,   value); }
    public GridLength LocalWidth      { get => _localWidth;      set => Set(ref _localWidth,      value); }
    public GridLength RemoteWidth     { get => _remoteWidth;     set => Set(ref _remoteWidth,     value); }
    public GridLength HostnameWidth   { get => _hostnameWidth;   set => Set(ref _hostnameWidth,   value); }
    public GridLength CountryWidth    { get => _countryWidth;    set => Set(ref _countryWidth,    value); }
    public GridLength ReputationWidth { get => _reputationWidth; set => Set(ref _reputationWidth, value); }
    public GridLength StateWidth      { get => _stateWidth;      set => Set(ref _stateWidth,      value); }

    /// <summary>
    /// Erhöht/verringert die Breite der angegebenen Spalte um <paramref name="delta"/> Pixel
    /// (mit Min/Max-Klemmung). Wird vom Thumb-DragDelta-Handler aufgerufen.
    /// </summary>
    public void Resize(string columnName, double delta)
    {
        switch (columnName)
        {
            case nameof(AppWidth):        AppWidth        = Clamp(AppWidth.Value        + delta); break;
            case nameof(ProtocolWidth):   ProtocolWidth   = Clamp(ProtocolWidth.Value   + delta); break;
            case nameof(LocalWidth):      LocalWidth      = Clamp(LocalWidth.Value      + delta); break;
            case nameof(RemoteWidth):     RemoteWidth     = Clamp(RemoteWidth.Value     + delta); break;
            case nameof(HostnameWidth):   HostnameWidth   = Clamp(HostnameWidth.Value   + delta); break;
            case nameof(CountryWidth):    CountryWidth    = Clamp(CountryWidth.Value    + delta); break;
            case nameof(ReputationWidth): ReputationWidth = Clamp(ReputationWidth.Value + delta); break;
            case nameof(StateWidth):      StateWidth      = Clamp(StateWidth.Value      + delta); break;
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

/// <summary>
/// Parameterloser XAML-Resource-Wrapper, der die Singleton-Instanz von
/// <see cref="ConnectionColumnLayout"/> bereitstellt. Wird in
/// <c>LiveConnectionsPage.xaml</c> als <c>StaticResource</c> registriert.
/// </summary>
public sealed class ColumnLayoutSource
{
    public ConnectionColumnLayout Layout => ConnectionColumnLayout.Instance;
}
