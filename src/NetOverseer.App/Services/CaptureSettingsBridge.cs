// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.Extensions.Logging;
using NetOverseer.Capture;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.App.Services;

/// <summary>
/// Lauscht auf <see cref="ISettingsService.SettingsSaved"/> und wendet Capture-relevante
/// Einstellungen zur Laufzeit an, ohne dass die Anwendung neu gestartet werden muss.
///
/// Was live umschaltbar ist:
/// - <c>PollingIntervalMs</c> (sowohl für <see cref="IpHelperCapture"/> als auch
///   <see cref="WfpNetworkCapture"/> via Property-Setter).
/// Was einen Neustart erfordert:
/// - <c>CaptureMethod</c> (anderer Singleton wird benötigt). Wir loggen einen Hinweis.
/// </summary>
public sealed class CaptureSettingsBridge : IDisposable
{
    private readonly ISettingsService _settings;
    private readonly INetworkCapture  _capture;
    private readonly ILogger<CaptureSettingsBridge> _logger;
    private readonly string _initialMethod;

    public CaptureSettingsBridge(
        ISettingsService settings,
        INetworkCapture  capture,
        ILogger<CaptureSettingsBridge> logger)
    {
        _settings = settings;
        _capture  = capture;
        _logger   = logger;
        _initialMethod = settings.Load().CaptureMethod ?? "IpHelper";
        _settings.SettingsSaved += OnSettingsSaved;
    }

    private void OnSettingsSaved(object? sender, AppSettings s)
    {
        // Polling-Intervall live durchreichen (Property-Setter via Reflection-frei dynamic)
        var interval = Math.Clamp(s.PollingIntervalMs, 100, 5000);
        try
        {
            switch (_capture)
            {
                case IpHelperCapture ip:  ip.PollingIntervalMs  = interval; break;
                case WfpNetworkCapture w: w.PollingIntervalMs   = interval; break;
            }
            _logger.LogInformation("Capture-Polling-Intervall live aktualisiert: {Ms} ms", interval);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Polling-Intervall konnte nicht live übernommen werden.");
        }

        // Capture-Methode wechselt nur nach Neustart
        var newMethod = s.CaptureMethod ?? "IpHelper";
        if (!string.Equals(newMethod, _initialMethod, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Capture-Methode geändert ({Old} → {New}). Wirksam nach App-Neustart.",
                _initialMethod, newMethod);
        }
    }

    public void Dispose()
    {
        _settings.SettingsSaved -= OnSettingsSaved;
    }
}
