// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using NetOverseer.App.Services;
using NetOverseer.App.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace NetOverseer.App.Views;

public sealed partial class ApplicationsPage : Page
{
    private static readonly InputCursor ResizeCursor =
        InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

    private static readonly InputCursor ArrowCursor =
        InputSystemCursor.Create(InputSystemCursorShape.Arrow);

    public ApplicationsViewModel ViewModel { get; }

    public ApplicationsPage()
    {
        ViewModel = App.Services.GetRequiredService<ApplicationsViewModel>();
        InitializeComponent();

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ApplicationsViewModel.IsDetailsOpen)) return;

        var sb = ViewModel.IsDetailsOpen
            ? Resources["AppDetailsSlideIn"] as Microsoft.UI.Xaml.Media.Animation.Storyboard
            : Resources["AppDetailsSlideOut"] as Microsoft.UI.Xaml.Media.Animation.Storyboard;

        if (sb is null) return;

        var open = ViewModel.IsDetailsOpen;
        AppDetailsPanel.IsHitTestVisible = open;
        AppDetailsBackdrop.IsHitTestVisible = open;
        sb.Begin();
    }

    private async void ApplicationsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ApplicationsList.SelectedItem is not ApplicationListItemViewModel item) return;
        await ViewModel.OpenForAppAsync(item, ApplicationsDetailsSection.Activity);
    }

    private void AppColumnSplitter_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string column })
            ApplicationColumnLayout.Instance.Resize(column, e.Delta.Translation.X);
    }

    private void AppColumnSplitter_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = ResizeCursor;
    }

    private void AppColumnSplitter_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = ArrowCursor;
    }

    private void AppDetailsBackdrop_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ViewModel.CloseDetailsCommand.CanExecute(null))
            ViewModel.CloseDetailsCommand.Execute(null);
    }

    private void ApplicationsList_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        var item = (args.OriginalSource as FrameworkElement)?.DataContext as ApplicationListItemViewModel;
        if (item is null && ApplicationsList.SelectedItem is ApplicationListItemViewModel sel)
            item = sel;
        if (item is null) return;

        ApplicationsList.SelectedItem = item;

        var flyout = new MenuFlyout();

        flyout.Items.Add(new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("AppMenu_ShowActivity"),
            Icon = new FontIcon { Glyph = "\uE7C4" },
            Command = new RelayCommandWrapper(async () =>
                await ViewModel.OpenForAppAsync(item, ApplicationsDetailsSection.Activity))
        });

        flyout.Items.Add(new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("AppMenu_ShowFirewall"),
            Icon = new FontIcon { Glyph = "\uE72E" },
            Command = new RelayCommandWrapper(async () =>
                await ViewModel.OpenForAppAsync(item, ApplicationsDetailsSection.Firewall))
        });

        flyout.Items.Add(new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("AppMenu_ShowProperties"),
            Icon = new FontIcon { Glyph = "\uE946" },
            Command = new RelayCommandWrapper(async () =>
                await ViewModel.OpenForAppAsync(item, ApplicationsDetailsSection.Properties))
        });

        flyout.Items.Add(new MenuFlyoutSeparator());

        flyout.Items.Add(new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("AppMenu_CopyPath"),
            Icon = new FontIcon { Glyph = "\uE8C8" },
            Command = new RelayCommandWrapper(() =>
            {
                var data = new DataPackage();
                data.SetText(item.ExecutablePath ?? string.Empty);
                Clipboard.SetContent(data);
                return Task.CompletedTask;
            })
        });

        var openFolderItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("AppMenu_OpenExplorer"),
            Icon = new FontIcon { Glyph = "\uE838" },
            Command = new RelayCommandWrapper(() =>
            {
                if (string.IsNullOrWhiteSpace(item.ExecutablePath)) return Task.CompletedTask;

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{item.ExecutablePath}\"",
                    UseShellExecute = true
                });

                return Task.CompletedTask;
            })
        };
        openFolderItem.IsEnabled = !string.IsNullOrWhiteSpace(item.ExecutablePath);
        flyout.Items.Add(openFolderItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var trustText = item.IsTrusted
            ? LocalizationService.GetString("AppMenu_RemoveTrust")
            : LocalizationService.GetString("AppMenu_AddTrust");
        flyout.Items.Add(new MenuFlyoutItem
        {
            Text = trustText,
            Icon = new FontIcon { Glyph = "\uE73E" },
            Command = new RelayCommandWrapper(async () =>
            {
                if (item.IsTrusted)
                    await ViewModel.RemoveTrustedAsync(item);
                else
                    await ViewModel.MarkTrustedAsync(item);
            })
        });

        var blockItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("AppMenu_BlockFirewall"),
            Icon = new FontIcon { Glyph = "\uE72E" },
            Command = new RelayCommandWrapper(async () => await ViewModel.BlockAppAsync(item))
        };
        flyout.Items.Add(blockItem);

        if (args.TryGetPosition(ApplicationsList, out var pos))
            flyout.ShowAt(ApplicationsList, new FlyoutShowOptions { Position = pos });
        else
            flyout.ShowAt(ApplicationsList);
    }

    private sealed class RelayCommandWrapper : System.Windows.Input.ICommand
    {
        private readonly Func<Task> _execute;

        public RelayCommandWrapper(Func<Task> execute) => _execute = execute;

        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public async void Execute(object? parameter) => await _execute();
    }
}
