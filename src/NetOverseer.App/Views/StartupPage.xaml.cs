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

public sealed partial class StartupPage : Page
{
    public StartupViewModel ViewModel { get; }

    public StartupPage()
    {
        ViewModel = (StartupViewModel)App.Services.GetService(typeof(StartupViewModel))!;
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.InitializeAsync(DispatcherQueue.GetForCurrentThread());
    }

    // ──────────────────────────────────────────────────────────────────────
    // Spalten-Resize via Border-Splitter im Header (ManipulationDelta)
    // analog zu LiveConnectionsPage – Singleton-Layout in StartupColumnLayout.
    // ──────────────────────────────────────────────────────────────────────

    private static readonly InputCursor _resizeCursor =
        InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

    private static readonly InputCursor _arrowCursor =
        InputSystemCursor.Create(InputSystemCursorShape.Arrow);

    private void ColumnSplitter_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string column })
            StartupColumnLayout.Instance.Resize(column, e.Delta.Translation.X);
    }

    private void ColumnSplitter_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = _resizeCursor;
    }

    private void ColumnSplitter_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = _arrowCursor;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Rechtsklick → Kontext-Menü (Kopieren / Whois)
    // ──────────────────────────────────────────────────────────────────────

    private void ConnectionList_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        var item = (args.OriginalSource as FrameworkElement)?.DataContext
                   as StartupConnectionItemViewModel;

        if (item is null && ConnectionList.SelectedItem is StartupConnectionItemViewModel sel)
            item = sel;

        if (item is null) return;

        ConnectionList.SelectedItem = item;

        var flyout = new MenuFlyout();

        flyout.Items.Add(new MenuFlyoutItem
        {
            Text    = "Remote-Adresse kopieren",
            Icon    = new FontIcon { Glyph = "\uE8C8" },
            Command = item.CopyRemoteAddressCommand,
        });

        flyout.Items.Add(new MenuFlyoutItem
        {
            Text    = "Remote-IP kopieren",
            Icon    = new FontIcon { Glyph = "\uE8C8" },
            Command = item.CopyRemoteIpCommand,
        });

        flyout.Items.Add(new MenuFlyoutSeparator());

        flyout.Items.Add(new MenuFlyoutItem
        {
            Text    = "Whois öffnen",
            Icon    = new FontIcon { Glyph = "\uE774" },
            Command = item.OpenWhoisCommand,
        });

        if (args.TryGetPosition(ConnectionList, out var pos))
            flyout.ShowAt(ConnectionList, new FlyoutShowOptions { Position = pos });
        else
            flyout.ShowAt(ConnectionList);
    }
}

