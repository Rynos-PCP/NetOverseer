// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NetOverseer.App.ViewModels;

namespace NetOverseer.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    /// <summary>Zugriff auf das MainViewModel für die Capture-Steuerungs-Card.</summary>
    public MainViewModel CaptureViewModel { get; }

    public SettingsPage()
    {
        ViewModel        = (SettingsViewModel)App.Services.GetService(typeof(SettingsViewModel))!;
        CaptureViewModel = (MainViewModel)App.Services.GetService(typeof(MainViewModel))!;
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // PasswordBox kann nicht via x:Bind TwoWay gebunden werden — wird manuell initialisiert
        AbuseIpDbPasswordBox.Password = ViewModel.AbuseIpDbApiKey;
        MaxMindPasswordBox.Password   = ViewModel.MaxMindLicenseKey;
    }

    private void AbuseIpDbPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.AbuseIpDbApiKey = AbuseIpDbPasswordBox.Password;
    }

    private void MaxMindPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.MaxMindLicenseKey = MaxMindPasswordBox.Password;
    }
}

