// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace NetOverseer.App.ViewModels;

/// <summary>
/// Geteiltes Layout-Modell fuer die Spaltenbreiten der Anwendungstabelle.
/// Header und Zeilen binden an dieselbe Instanz, damit Drag-Resize
/// sofort auf alle Zeilen wirkt.
/// </summary>
public sealed class ApplicationColumnLayout : INotifyPropertyChanged
{
    public static ApplicationColumnLayout Instance { get; } = new();

    private ApplicationColumnLayout() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    private GridLength _nameWidth = new(220, GridUnitType.Pixel);
    private GridLength _pathWidth = new(320, GridUnitType.Pixel);
    private GridLength _uploadWidth = new(120, GridUnitType.Pixel);
    private GridLength _downloadWidth = new(120, GridUnitType.Pixel);
    private GridLength _lastSeenWidth = new(170, GridUnitType.Pixel);
    private GridLength _trustWidth = new(90, GridUnitType.Pixel);
    private GridLength _blockWidth = new(90, GridUnitType.Pixel);
    private bool _trafficColumnsEnabled;

    private const double MinWidth = 40;
    private const double MaxWidth = 900;

    public GridLength NameWidth { get => _nameWidth; set => Set(ref _nameWidth, value); }
    public GridLength PathWidth { get => _pathWidth; set => Set(ref _pathWidth, value); }
    public GridLength UploadWidth { get => _uploadWidth; set => Set(ref _uploadWidth, value); }
    public GridLength DownloadWidth { get => _downloadWidth; set => Set(ref _downloadWidth, value); }
    public GridLength LastSeenWidth { get => _lastSeenWidth; set => Set(ref _lastSeenWidth, value); }
    public GridLength TrustWidth { get => _trustWidth; set => Set(ref _trustWidth, value); }
    public GridLength BlockWidth { get => _blockWidth; set => Set(ref _blockWidth, value); }

    public bool TrafficColumnsEnabled
    {
        get => _trafficColumnsEnabled;
        set => Set(ref _trafficColumnsEnabled, value);
    }

    public GridLength EffectiveUploadWidth => TrafficColumnsEnabled ? UploadWidth : new GridLength(0);
    public GridLength EffectiveDownloadWidth => TrafficColumnsEnabled ? DownloadWidth : new GridLength(0);
    public GridLength UploadSplitterWidth => TrafficColumnsEnabled ? new GridLength(8) : new GridLength(0);
    public GridLength DownloadSplitterWidth => TrafficColumnsEnabled ? new GridLength(8) : new GridLength(0);

    public void Resize(string columnName, double delta)
    {
        switch (columnName)
        {
            case nameof(NameWidth):
                NameWidth = Clamp(NameWidth.Value + delta);
                break;
            case nameof(PathWidth):
                PathWidth = Clamp(PathWidth.Value + delta);
                break;
            case nameof(UploadWidth):
                UploadWidth = Clamp(UploadWidth.Value + delta);
                break;
            case nameof(DownloadWidth):
                DownloadWidth = Clamp(DownloadWidth.Value + delta);
                break;
            case nameof(LastSeenWidth):
                LastSeenWidth = Clamp(LastSeenWidth.Value + delta);
                break;
            case nameof(TrustWidth):
                TrustWidth = Clamp(TrustWidth.Value + delta);
                break;
            case nameof(BlockWidth):
                BlockWidth = Clamp(BlockWidth.Value + delta);
                break;
        }
    }

    private static GridLength Clamp(double pixels) =>
        new(Math.Clamp(pixels, MinWidth, MaxWidth), GridUnitType.Pixel);

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        if (propertyName is nameof(UploadWidth) or nameof(DownloadWidth) or nameof(TrafficColumnsEnabled))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveUploadWidth)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveDownloadWidth)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UploadSplitterWidth)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DownloadSplitterWidth)));
        }
    }
}

public sealed class ApplicationColumnLayoutSource
{
    public ApplicationColumnLayout Layout => ApplicationColumnLayout.Instance;
}