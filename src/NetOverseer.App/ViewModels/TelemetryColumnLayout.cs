// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace NetOverseer.App.ViewModels;

/// <summary>
/// Singleton mit den Spaltenbreiten der Telemetrie-Tabelle.
/// Wird sowohl vom Header-Grid als auch vom ListView-Item-Template über
/// dasselbe Static-Resource-Wrapper-Objekt gebunden – Drag-Resize des
/// Splitters wirkt damit auf alle Zeilen gleichzeitig.
/// </summary>
public sealed class TelemetryColumnLayout : INotifyPropertyChanged
{
    public static TelemetryColumnLayout Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    // ── Spaltenbreiten (Standardwerte) ────────────────────────────────────
    private GridLength _serviceWidth   = new(170);
    private GridLength _hostnameWidth  = new(240);
    private GridLength _processWidth   = new(140);
    private GridLength _pidWidth       = new(60);
    private GridLength _remoteIpWidth  = new(140);
    private GridLength _countryWidth   = new(60);
    private GridLength _hitsWidth      = new(60);
    private GridLength _bytesWidth     = new(80);
    private GridLength _lastSeenWidth  = new(80);

    public GridLength ServiceWidth   { get => _serviceWidth;   set => Set(ref _serviceWidth,   value); }
    public GridLength HostnameWidth  { get => _hostnameWidth;  set => Set(ref _hostnameWidth,  value); }
    public GridLength ProcessWidth   { get => _processWidth;   set => Set(ref _processWidth,   value); }
    public GridLength PidWidth       { get => _pidWidth;       set => Set(ref _pidWidth,       value); }
    public GridLength RemoteIpWidth  { get => _remoteIpWidth;  set => Set(ref _remoteIpWidth,  value); }
    public GridLength CountryWidth   { get => _countryWidth;   set => Set(ref _countryWidth,   value); }
    public GridLength HitsWidth      { get => _hitsWidth;      set => Set(ref _hitsWidth,      value); }
    public GridLength BytesWidth     { get => _bytesWidth;     set => Set(ref _bytesWidth,     value); }
    public GridLength LastSeenWidth  { get => _lastSeenWidth;  set => Set(ref _lastSeenWidth,  value); }

    /// <summary>
    /// Verschiebt die angegebene Spalte um <paramref name="delta"/> Pixel.
    /// Wird vom ManipulationDelta-Handler des Border-Splitters aufgerufen.
    /// </summary>
    public void Resize(string columnName, double delta)
    {
        switch (columnName)
        {
            case nameof(ServiceWidth):  ServiceWidth  = Clamp(ServiceWidth,  delta); break;
            case nameof(HostnameWidth): HostnameWidth = Clamp(HostnameWidth, delta); break;
            case nameof(ProcessWidth):  ProcessWidth  = Clamp(ProcessWidth,  delta); break;
            case nameof(PidWidth):      PidWidth      = Clamp(PidWidth,      delta); break;
            case nameof(RemoteIpWidth): RemoteIpWidth = Clamp(RemoteIpWidth, delta); break;
            case nameof(CountryWidth):  CountryWidth  = Clamp(CountryWidth,  delta); break;
            case nameof(HitsWidth):     HitsWidth     = Clamp(HitsWidth,     delta); break;
            case nameof(BytesWidth):    BytesWidth    = Clamp(BytesWidth,    delta); break;
            case nameof(LastSeenWidth): LastSeenWidth = Clamp(LastSeenWidth, delta); break;
        }
    }

    private static GridLength Clamp(GridLength current, double delta)
        => new(Math.Clamp(current.Value + delta, 40, 600));

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>
/// XAML-Wrapper damit die Singleton-Instanz als <c>StaticResource</c> verfügbar ist.
/// </summary>
public sealed class TelemetryColumnLayoutSource
{
    public TelemetryColumnLayout Layout => TelemetryColumnLayout.Instance;
}
