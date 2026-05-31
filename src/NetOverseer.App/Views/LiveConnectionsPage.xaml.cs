// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using NetOverseer.App.ViewModels;
using NetOverseer.Core.Interfaces;

namespace NetOverseer.App.Views;

/// <summary>
/// Live-Verbindungsansicht: ListView mit Echtzeit-Updates, Filter-Bar und Kontext-Menü.
/// </summary>
public sealed partial class LiveConnectionsPage : Page
{
    public LiveConnectionsViewModel ViewModel { get; }

    public LiveConnectionsPage()
    {
        ViewModel = App.Services.GetRequiredService<LiveConnectionsViewModel>();
        InitializeComponent();

        // Slide-Animationen an den IsOpen-Zustand des Side-Panels koppeln.
        ViewModel.AppFirewallRules.PropertyChanged += AppFirewallRules_PropertyChanged;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Side-Panel: Slide-In / Slide-Out
    // ──────────────────────────────────────────────────────────────────────

    private void AppFirewallRules_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppFirewallRulesViewModel.IsOpen)) return;

        var sb = ViewModel.AppFirewallRules.IsOpen
            ? Resources["AppRulesSlideIn"]  as Microsoft.UI.Xaml.Media.Animation.Storyboard
            : Resources["AppRulesSlideOut"] as Microsoft.UI.Xaml.Media.Animation.Storyboard;

        if (sb is null) return;

        // Backdrop & Panel im geöffneten Zustand interaktiv lassen,
        // im geschlossenen Zustand keine Clicks abfangen.
        var open = ViewModel.AppFirewallRules.IsOpen;
        AppRulesPanel.IsHitTestVisible    = open;
        AppRulesBackdrop.IsHitTestVisible = open;

        sb.Begin();
    }

    private void AppRulesBackdrop_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Klick auf den dunklen Hintergrund schließt das Panel (wie Azure-Blade).
        if (ViewModel.AppFirewallRules.CloseCommand.CanExecute(null))
            ViewModel.AppFirewallRules.CloseCommand.Execute(null);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Navigation
    // ──────────────────────────────────────────────────────────────────────

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // ViewModel beim ersten Navigieren mit DispatcherQueue verbinden
        ViewModel.Initialize(DispatcherQueue.GetForCurrentThread());
    }

    // ──────────────────────────────────────────────────────────────────────
    // Spalten-Resize via Border-Splitter im Header (ManipulationDelta)
    // ──────────────────────────────────────────────────────────────────────

    private static readonly InputCursor _resizeCursor =
        InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

    private static readonly InputCursor _arrowCursor =
        InputSystemCursor.Create(InputSystemCursorShape.Arrow);

    private void ColumnSplitter_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string column })
            ConnectionColumnLayout.Instance.Resize(column, e.Delta.Translation.X);
    }

    private void ColumnSplitter_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement el)
            this.ProtectedCursor = _resizeCursor;
    }

    private void ColumnSplitter_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        this.ProtectedCursor = _arrowCursor;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Doppelklick → Detail-Dialog
    // ──────────────────────────────────────────────────────────────────────

    private async void ConnectionList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ConnectionList.SelectedItem is not ConnectionItemViewModel item) return;
        await ShowDetailDialogAsync(item);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Rechtsklick → Kontext-Menü
    // ──────────────────────────────────────────────────────────────────────

    private void ConnectionList_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        // Item aus OriginalSource ermitteln
        var item = (args.OriginalSource as FrameworkElement)?.DataContext
                   as ConnectionItemViewModel;

        if (item is null && ConnectionList.SelectedItem is ConnectionItemViewModel sel)
            item = sel;

        if (item is null) return;

        // Selektion setzen damit Aktionen auf das richtige Item wirken
        ConnectionList.SelectedItem = item;

        // Menü dynamisch aufbauen
        var flyout = new MenuFlyout();

        flyout.Items.Add(new MenuFlyoutItem
        {
            Text    = Services.LocalizationService.GetString("ConnMenu_ShowDetails"),
            Icon    = new FontIcon { Glyph = "\uE946" },
            Command = new RelayCommandWrapper(async () => await ShowDetailDialogAsync(item))
        });

        flyout.Items.Add(new MenuFlyoutSeparator());

        flyout.Items.Add(new MenuFlyoutItem
        {
            Text    = Services.LocalizationService.GetString("ConnMenu_CopyHostname"),
            Icon    = new FontIcon { Glyph = "\uE8C8" },
            Command = item.CopyHostnameCommand
        });

        flyout.Items.Add(new MenuFlyoutItem
        {
            Text    = Services.LocalizationService.GetString("ConnMenu_CopyRemote"),
            Icon    = new FontIcon { Glyph = "\uE8C8" },
            Command = item.CopyRemoteAddressCommand
        });

        flyout.Items.Add(new MenuFlyoutItem
        {
            Text    = Services.LocalizationService.GetString("ConnMenu_OpenWhois"),
            Icon    = new FontIcon { Glyph = "\uE774" },
            Command = item.OpenWhoisCommand
        });

        flyout.Items.Add(new MenuFlyoutSeparator());

        var fwItem = new MenuFlyoutItem
        {
            Text    = Services.LocalizationService.GetString("ConnMenu_LoadFirewall"),
            Icon    = new FontIcon { Glyph = "\uE72E" }, // Shield
            Command = new RelayCommandWrapper(async () => await ShowAppFirewallRulesAsync(item))
        };
        // Wenn der EXE-Pfad fehlt (z. B. System-Prozess), Eintrag deaktivieren.
        fwItem.IsEnabled = !string.IsNullOrWhiteSpace(item.ExecutablePath);
        flyout.Items.Add(fwItem);

        flyout.Items.Add(new MenuFlyoutItem
        {
            Text    = Services.LocalizationService.GetString("ConnMenu_BlockConnection"),
            Icon    = new FontIcon { Glyph = "\uE72E" },
            Command = new RelayCommandWrapper(async () => await ShowBlockDialogAsync(item))
        });

        if (args.TryGetPosition(ConnectionList, out var pos))
            flyout.ShowAt(ConnectionList, new FlyoutShowOptions { Position = pos });
        else
            flyout.ShowAt(ConnectionList);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private async Task ShowDetailDialogAsync(ConnectionItemViewModel item)
    {
        var dialog = new ConnectionDetailDialog(item)
        {
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async Task ShowAppFirewallRulesAsync(ConnectionItemViewModel item)
    {
        if (string.IsNullOrWhiteSpace(item.ExecutablePath)) return;
        await ViewModel.AppFirewallRules.OpenForAppAsync(
            item.ExecutablePath, item.ProcessDisplayName);
    }

    private async Task ShowBlockDialogAsync(ConnectionItemViewModel item)
    {
        // Remote-IP aus der Adresse extrahieren (Format: "ip:port" oder reine IP)
        System.Net.IPAddress? remoteIp = null;
        var raw = item.RemoteAddress;
        if (!string.IsNullOrEmpty(raw))
        {
            var colonIdx = raw.LastIndexOf(':');
            var ipStr    = colonIdx > 0 ? raw[..colonIdx] : raw;
            System.Net.IPAddress.TryParse(ipStr, out remoteIp);
        }

        var firewall = App.Services.GetRequiredService<IFirewallService>();
        var logger   = App.Services.GetRequiredService<ILogger<BlockConfirmationDialog>>();

        var dialog = new BlockConfirmationDialog(
            firewall, logger,
            item.ExecutablePath ?? string.Empty,
            item.ProcessDisplayName,
            remoteIp)
        {
            XamlRoot = XamlRoot
        };

        await dialog.ShowAsync();
    }

    /// <summary>Minimaler Command-Wrapper für einmalige Lambda-Aktionen im Kontext-Menü.</summary>
    private sealed class RelayCommandWrapper : System.Windows.Input.ICommand
    {
        private readonly Func<Task> _execute;
        public RelayCommandWrapper(Func<Task> execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public async void Execute(object? parameter) => await _execute();
    }
}

