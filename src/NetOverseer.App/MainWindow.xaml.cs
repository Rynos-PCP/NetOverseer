// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NetOverseer.App.Services;
using NetOverseer.App.ViewModels;

namespace NetOverseer.App;

/// <summary>
/// Haupt-Shell der Anwendung: NavigationView, Mica-Backdrop, Status-Leiste.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>ViewModel für Header-Bindings (Status-Chip, Toggle-Button, Zähler).</summary>
    public MainViewModel ViewModel { get; }

    private readonly NavigationService _navigationService;
    private readonly SettingsViewModel _settingsViewModel;

    public MainWindow()
    {
        InitializeComponent();

        ViewModel           = App.Services.GetRequiredService<MainViewModel>();
        _navigationService  = App.Services.GetRequiredService<NavigationService>();
        _settingsViewModel  = App.Services.GetRequiredService<SettingsViewModel>();
        _navigationService.Initialize(ContentFrame);

        // Sprachumschaltung: Frame + NavigationView-Pane neu laden
        _settingsViewModel.LanguageChangeRequested += OnLanguageChangeRequested;

        // Beim Schließen die VM-Timer/Subscriptions sauber stoppen, damit
        // beim Tear-Down keine Tick-Handler mehr in bereits abgebaute XAML-Peers
        // schreiben (sonst: COMException 0x8000FFFF).
        Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        try
        {
            App.Services.GetService<LiveConnectionsViewModel>()?.Dispose();
            App.Services.GetService<DnsViewModel>()?.Dispose();
        }
        catch { /* Best-Effort beim Shutdown */ }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Sprachumschaltung
    // ──────────────────────────────────────────────────────────────────────────

    private async void OnLanguageChangeRequested(object? sender, string newLanguageCode)
    {
        // Frame-Reload führt in WinUI 3 zu stillen Abstürzen (unhandled exception
        // im DispatcherQueue-Callback). Stattdessen: Neustart-Dialog anzeigen.
        // Die Sprache wurde bereits gespeichert; beim nächsten Start lädt die App
        // alle XAML-Elemente über x:Uid in der neuen Sprache.
        var dialog = new ContentDialog
        {
            Title   = "Sprache geändert",
            Content = "Die Sprache wurde gespeichert. Starten Sie die App neu, damit die neue Sprache vollständig wirksam wird.",
            PrimaryButtonText = "Jetzt neu starten",
            CloseButtonText   = "Später",
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (exe is not null)
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
            Application.Current.Exit();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // NavigationView Events
    // ──────────────────────────────────────────────────────────────────────────

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        // Erste Seite (Verbindungen) vorab auswählen und navigieren
        NavView.SelectedItem = NavConnections;
        _navigationService.NavigateTo(PageKeys.LiveConnections);
    }

    private void NavView_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            _navigationService.NavigateTo(PageKeys.Settings);
            return;
        }

        if (args.SelectedItemContainer is NavigationViewItem { Tag: string tag })
            _navigationService.NavigateTo(tag);
    }
}

