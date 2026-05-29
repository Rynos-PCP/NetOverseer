// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.UI.Xaml.Controls;
using NetOverseer.App.Views;
using NetOverseer.Core.Interfaces;

namespace NetOverseer.App.Services;

/// <summary>
/// Frame-basierter Navigationsdienst. Wird nach dem Erzeugen des Hauptfensters
/// über <see cref="Initialize"/> mit dem WinUI-Frame verknüpft.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private static readonly Dictionary<string, Type> Pages = new()
    {
        [PageKeys.LiveConnections] = typeof(LiveConnectionsPage),
        [PageKeys.Applications]    = typeof(ApplicationsPage),
        [PageKeys.Firewall]        = typeof(FirewallPage),
        [PageKeys.Dns]             = typeof(DnsPage),
        [PageKeys.Startup]         = typeof(StartupPage),
        [PageKeys.Telemetry]       = typeof(TelemetryPage),
        [PageKeys.Settings]        = typeof(SettingsPage),
        [PageKeys.History]          = typeof(HistoryPage),
    };

    private Frame? _frame;

    /// <inheritdoc/>
    public bool CanGoBack => _frame?.CanGoBack ?? false;

    /// <inheritdoc/>
    public string? CurrentPageKey { get; private set; }

    /// <summary>
    /// Verknüpft den Dienst mit dem <see cref="Frame"/> im Hauptfenster.
    /// Muss genau einmal nach <c>InitializeComponent()</c> aufgerufen werden.
    /// </summary>
    public void Initialize(Frame frame) => _frame = frame;

    /// <inheritdoc/>
    public bool NavigateTo(string pageKey, object? parameter = null)
    {
        if (_frame is null || !Pages.TryGetValue(pageKey, out var pageType))
            return false;

        // Nicht zur gleichen Seite doppelt navigieren
        if (_frame.Content?.GetType() == pageType)
            return false;

        var navigated = _frame.Navigate(pageType, parameter);
        if (navigated) CurrentPageKey = pageKey;
        return navigated;
    }

    /// <summary>
    /// Erzwingt Navigation zur angegebenen Seite – auch wenn sie bereits aktiv ist.
    /// Wird nach einer Sprachumschaltung verwendet, um den XAML-Inhalt neu zu laden.
    /// </summary>
    public bool ForceNavigateTo(string pageKey, object? parameter = null)
    {
        if (_frame is null || !Pages.TryGetValue(pageKey, out var pageType))
            return false;

        var navigated = _frame.Navigate(pageType, parameter);
        if (navigated) CurrentPageKey = pageKey;
        return navigated;
    }

    /// <inheritdoc/>
    public bool GoBack()
    {
        if (!CanGoBack) return false;
        _frame!.GoBack();
        CurrentPageKey = null;
        return true;
    }
}
