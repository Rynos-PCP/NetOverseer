// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using NetOverseer.App.ViewModels;

namespace NetOverseer.App.Views;

/// <summary>
/// Zeigt die Verlaufsansicht: Zeitachse mit App-Aktivitäts-Sparklines
/// und einen Wochenbericht mit HTML-Export.
/// </summary>
public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; }

    public HistoryPage()
    {
        ViewModel = (HistoryViewModel)App.Services.GetService(typeof(HistoryViewModel))!;
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.InitializeAsync(DispatcherQueue.GetForCurrentThread());
    }

    // ── Zeitbereich-Auswahl ────────────────────────────────────────────────

    private void TimeRange_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag)
        {
            ViewModel.SelectedRange = tag switch
            {
                "LastHour"    => HistoryTimeRange.LastHour,
                "LastWeek"    => HistoryTimeRange.LastWeek,
                _             => HistoryTimeRange.Last24Hours,
            };
        }
    }

    // ── App-Zeile anklicken → Detail-Dialog ───────────────────────────────

    private async void AppRow_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe &&
            fe.DataContext is AppActivityViewModel app)
        {
            await ShowAppDetailDialogAsync(app);
        }
    }

    private async Task ShowAppDetailDialogAsync(AppActivityViewModel app)
    {
        var connections = await ViewModel.GetAppDetailAsync(app.ProcessName);

        var panel = new StackPanel { Spacing = 8, MaxWidth = 560 };
        panel.Children.Add(new TextBlock
        {
            Text       = $"Prozess: {app.ProcessName}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        panel.Children.Add(new TextBlock
        {
            Text       = app.ExecutablePath,
            FontSize   = 12,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.WrapWholeWords,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                             "SystemControlForegroundBaseMediumBrush"],
        });
        panel.Children.Add(new MenuFlyoutSeparator());

        var stats = new Grid { ColumnSpacing = 24 };
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        AddStatCard(stats, 0, "Verbindungen", app.ConnectionCount.ToString("N0"));
        AddStatCard(stats, 1, "Gesendet",     app.BytesSentDisplay);
        AddStatCard(stats, 2, "Empfangen",    app.BytesRecvDisplay);
        panel.Children.Add(stats);

        if (connections.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text     = $"Letzte {Math.Min(connections.Count, 10)} Verbindungen:",
                FontSize = 13,
                Margin   = new Thickness(0, 8, 0, 0),
            });

            var scroll = new ScrollViewer { MaxHeight = 250, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var list   = new StackPanel { Spacing = 4 };
            foreach (var c in connections.Take(10))
            {
                list.Children.Add(new TextBlock
                {
                    Text     = $"{c.Timestamp.LocalDateTime:g}  {c.RemoteIp}:{c.RemotePort}  [{c.Protocol}]",
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    FontSize   = 12,
                });
            }
            scroll.Content = list;
            panel.Children.Add(scroll);
        }

        var dialog = new ContentDialog
        {
            Title             = app.ProcessName,
            Content           = new ScrollViewer { Content = panel, MaxHeight = 480 },
            CloseButtonText   = "Schließen",
            XamlRoot          = XamlRoot,
            DefaultButton     = ContentDialogButton.Close,
        };

        await dialog.ShowAsync();
    }

    private static void AddStatCard(Grid grid, int column, string label, string value)
    {
        var card = new StackPanel { Spacing = 2 };
        card.Children.Add(new TextBlock
        {
            Text     = value,
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        card.Children.Add(new TextBlock
        {
            Text     = label,
            FontSize = 12,
        });
        Grid.SetColumn(card, column);
        grid.Children.Add(card);
    }
}
