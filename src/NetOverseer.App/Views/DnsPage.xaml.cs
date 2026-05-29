// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using NetOverseer.App.ViewModels;

namespace NetOverseer.App.Views;

public sealed partial class DnsPage : Page
{
    public DnsViewModel ViewModel { get; }

    public DnsPage()
    {
        ViewModel = App.Services.GetRequiredService<DnsViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.InitializeAsync(DispatcherQueue.GetForCurrentThread());
    }

    // ──────────────────────────────────────────────────────────────────────
    // Spalten-Resize via Border-Splitter im Header
    // ──────────────────────────────────────────────────────────────────────

    private static readonly InputCursor _resizeCursor =
        InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

    private static readonly InputCursor _arrowCursor =
        InputSystemCursor.Create(InputSystemCursorShape.Arrow);

    private void ColumnSplitter_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string column })
            DnsColumnLayout.Instance.Resize(column, e.Delta.Translation.X);
    }

    private void ColumnSplitter_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        this.ProtectedCursor = _resizeCursor;
    }

    private void ColumnSplitter_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        this.ProtectedCursor = _arrowCursor;
    }
}
