// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using NetOverseer.Core.Interfaces;

namespace NetOverseer.App.Services;

/// <summary>
/// Wrapper um <see cref="AppNotificationManager"/> für Toast-Benachrichtigungen.
/// Respektiert die Settings-Schalter (Quiet-Hours, individuelle Toast-Flags).
///
/// Hinweis: Unpackaged Apps benötigen <c>AppNotificationManager.Default.Register()</c>;
/// das geschieht beim ersten Aufruf in <see cref="EnsureRegistered"/>.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private readonly ISettingsService _settings;
    private readonly ILogger<NotificationService> _logger;
    private bool _registered;

    public NotificationService(ISettingsService settings, ILogger<NotificationService> logger)
    {
        _settings = settings;
        _logger   = logger;
    }

    /// <inheritdoc/>
    public void ShowInfo(string title, string body) => Show(title, body, category: "info");

    /// <inheritdoc/>
    public void ShowWarning(string title, string body) => Show(title, body, category: "warning");

    /// <inheritdoc/>
    public void ShowReputationAlert(string ip, int score, string body)
    {
        var settings = _settings.Load();
        if (!settings.NotifyOnLowReputation) return;
        Show($"Schlechte IP-Reputation ({score})", $"{ip}: {body}", category: "reputation");
    }

    private void Show(string title, string body, string category)
    {
        if (IsInQuietHours(_settings.Load())) return;
        try
        {
            EnsureRegistered();
            var note = new AppNotificationBuilder()
                .AddText(title)
                .AddText(body)
                .BuildNotification();
            AppNotificationManager.Default.Show(note);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Toast konnte nicht angezeigt werden ({Category}).", category);
        }
    }

    private void EnsureRegistered()
    {
        if (_registered) return;
        AppNotificationManager.Default.Register();
        _registered = true;
    }

    private static bool IsInQuietHours(NetOverseer.Core.Models.AppSettings s)
    {
        if (!s.QuietHoursEnabled) return false;
        var hour = DateTime.Now.Hour;
        // Bereich kann über Mitternacht laufen (z.B. 22 → 8)
        return s.QuietHoursStartHour <= s.QuietHoursEndHour
            ? hour >= s.QuietHoursStartHour && hour < s.QuietHoursEndHour
            : hour >= s.QuietHoursStartHour || hour < s.QuietHoursEndHour;
    }

    public void Dispose()
    {
        if (_registered)
        {
            try { AppNotificationManager.Default.Unregister(); } catch { /* ignore */ }
        }
    }
}
