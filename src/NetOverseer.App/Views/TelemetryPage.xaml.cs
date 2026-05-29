// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using NetOverseer.App.ViewModels;

namespace NetOverseer.App.Views;

/// <summary>
/// Zeigt erkannte Windows-Telemetrie-Verbindungen als flache, resizable Tabelle.
/// </summary>
public sealed partial class TelemetryPage : Page
{
    public TelemetryViewModel ViewModel { get; }

    public TelemetryPage()
    {
        ViewModel = (TelemetryViewModel)App.Services.GetService(typeof(TelemetryViewModel))!;
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Initialize(DispatcherQueue.GetForCurrentThread());
    }

    // ──────────────────────────────────────────────────────────────────────
    // Spalten-Resize via Border-Splitter (Singleton TelemetryColumnLayout)
    // ──────────────────────────────────────────────────────────────────────

    private static readonly InputCursor _resizeCursor =
        InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

    private static readonly InputCursor _arrowCursor =
        InputSystemCursor.Create(InputSystemCursorShape.Arrow);

    private void ColumnSplitter_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string column })
            TelemetryColumnLayout.Instance.Resize(column, e.Delta.Translation.X);
    }

    private void ColumnSplitter_PointerEntered(object sender, PointerRoutedEventArgs e)
        => ProtectedCursor = _resizeCursor;

    private void ColumnSplitter_PointerExited(object sender, PointerRoutedEventArgs e)
        => ProtectedCursor = _arrowCursor;

    // ──────────────────────────────────────────────────────────────────────
    // Service-Chip-Klick → Filter umschalten
    // ──────────────────────────────────────────────────────────────────────

    private void ServiceChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string svc })
            ViewModel.ToggleServiceFilterCommand.Execute(svc);
    }

    private void ClearFilter_Click(object sender, RoutedEventArgs e)
        => ViewModel.ClearFilterCommand.Execute(null);

    // ──────────────────────────────────────────────────────────────────────
    // Kontext-Menü pro Zeile (Whois / Hostname kopieren / IP kopieren)
    // ──────────────────────────────────────────────────────────────────────

    private void EntriesList_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        var item = (args.OriginalSource as FrameworkElement)?.DataContext
                   as TelemetryEntryViewModel;

        if (item is null && EntriesList.SelectedItem is TelemetryEntryViewModel sel)
            item = sel;

        if (item is null) return;

        EntriesList.SelectedItem = item;

        var flyout = new MenuFlyout();

        flyout.Items.Add(new MenuFlyoutItem
        {
            Text    = "Whois öffnen",
            Icon    = new FontIcon { Glyph = "\uE774" },
            Command = item.OpenWhoisCommand,
        });

        flyout.Items.Add(new MenuFlyoutSeparator());

        flyout.Items.Add(new MenuFlyoutItem
        {
            Text    = "Hostname kopieren",
            Icon    = new FontIcon { Glyph = "\uE8C8" },
            Command = item.CopyHostnameCommand,
        });

        flyout.Items.Add(new MenuFlyoutItem
        {
            Text    = "Remote-IP kopieren",
            Icon    = new FontIcon { Glyph = "\uE8C8" },
            Command = item.CopyRemoteIpCommand,
        });

        if (args.TryGetPosition(EntriesList, out var pos))
            flyout.ShowAt(EntriesList, new FlyoutShowOptions { Position = pos });
        else
            flyout.ShowAt(EntriesList);
    }
}
