// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NetOverseer.App.ViewModels;

namespace NetOverseer.App.Views;

/// <summary>
/// Firewall-Seite: zeigt NetOverseer-Regeln, verwaiste Regeln und alle Windows-Firewall-Regeln.
/// </summary>
public sealed partial class FirewallPage : Page
{
    public FirewallViewModel ViewModel { get; }

    public FirewallPage()
    {
        ViewModel = App.Services.GetRequiredService<FirewallViewModel>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Initialize(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        _ = ViewModel.LoadAsync();
    }

    /// <summary>
    /// Lädt "Alle Regeln" nur wenn der dritte Tab aktiv ist und noch keine Daten vorhanden.
    /// </summary>
    private void TabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is TabView tv &&
            tv.SelectedItem == AllRulesTab &&
            !ViewModel.AllRulesLoaded &&
            !ViewModel.IsLoadingAll)
        {
            _ = ViewModel.LoadAllRulesCommand.ExecuteAsync(null);
        }
    }
}

