// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.Extensions.Logging;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.Capture;

/// <summary>
/// Prüft ob wir uns im Startup-Aufzeichnungsfenster befinden
/// (weniger als <see cref="WindowSeconds"/> Sekunden seit dem letzten Systemstart).
/// Wenn ja, wird eine <see cref="IpHelperCapture"/>-Instanz für bis zu
/// <see cref="RecordSeconds"/> Sekunden gestartet und die Ergebnisse in der
/// Startup-Datenbank gespeichert.
///
/// Wird sowohl im UI-Prozess (NetOverseer.App, Self-Test mit <c>force=true</c>)
/// als auch im headless Bootmonitor (NetOverseer.BootMonitor, direkt aus dem
/// SYSTEM-Task ONSTART) verwendet – deshalb liegt der Service in
/// <c>NetOverseer.Capture</c> und nicht im App-Projekt.
/// </summary>
public sealed class StartupMonitorService
{
    /// <summary>Maximale Zeit seit Systemstart (Sekunden) in der aufgezeichnet wird.</summary>
    public const double WindowSeconds = 120.0;

    /// <summary>Maximale Aufzeichnungsdauer in Sekunden.</summary>
    public const double RecordSeconds = 60.0;

    private readonly IStartupRepository _repo;
    private readonly ILoggerFactory     _loggerFactory;
    private readonly ILogger<StartupMonitorService> _logger;

    public StartupMonitorService(
        IStartupRepository repo,
        ILoggerFactory loggerFactory,
        ILogger<StartupMonitorService> logger)
    {
        _repo          = repo;
        _loggerFactory = loggerFactory;
        _logger        = logger;
    }

    /// <summary>
    /// Startet die Aufzeichnung im Hintergrund falls das Startup-Fenster aktiv ist.
    /// Gibt sofort zurück – die Aufzeichnung läuft asynchron weiter.
    /// </summary>
    public void BeginRecordIfInStartupWindow(CancellationToken appCt = default)
    {
        _ = Task.Run(() => RecordIfStartupWindowAsync(force: false, appCt), appCt);
    }

    /// <summary>
    /// Führt die Aufzeichnung synchron aus. Mit <paramref name="force"/>=<c>true</c>
    /// werden Boot-Fenster- und Dedupe-Checks übersprungen (für manuellen Self-Test).
    /// </summary>
    public async Task RecordIfStartupWindowAsync(bool force = false, CancellationToken ct = default)
    {
        var bootAge = TimeSpan.FromMilliseconds(Environment.TickCount64);

        if (!force && bootAge.TotalSeconds > WindowSeconds)
        {
            _logger.LogDebug(
                "StartupMonitor: System läuft seit {S:F0}s – außerhalb des Fensters.",
                bootAge.TotalSeconds);
            return;
        }

        // Bereits für diesen Boot aufgezeichnet?
        var bootTime = DateTime.UtcNow - bootAge;
        if (!force)
        {
            var last = await _repo.GetLastSessionAsync(ct).ConfigureAwait(false);
            if (last is not null && Math.Abs((last.BootTime - bootTime).TotalSeconds) < 60)
            {
                _logger.LogDebug("StartupMonitor: Für diesen Boot bereits aufgezeichnet.");
                return;
            }
        }

        var remainingSeconds = force
            ? RecordSeconds
            : Math.Max(10.0, RecordSeconds - bootAge.TotalSeconds);
        _logger.LogInformation(
            "StartupMonitor: Aufzeichnung für {D:F0}s gestartet ({B:F0}s seit Boot, force={F}).",
            remainingSeconds, bootAge.TotalSeconds, force);

        var recordingStart = DateTime.UtcNow;
        var connections    = new List<StartupConnection>();
        var seenKeys       = new HashSet<string>(StringComparer.Ordinal);

        using var capture = new IpHelperCapture(
            _loggerFactory.CreateLogger<IpHelperCapture>());

        using var sub = capture.Connections.Subscribe(ev =>
        {
            if (ev.RemoteEndpoint.Address.ToString() is "0.0.0.0" or "::" or "127.0.0.1" or "::1")
                return;

            var key = $"{ev.ProcessId}|{ev.RemoteEndpoint}|{ev.Protocol}";
            lock (connections)
            {
                if (!seenKeys.Add(key)) return;
                connections.Add(new StartupConnection
                {
                    Timestamp     = ev.Timestamp.UtcDateTime,
                    OffsetSeconds = (ev.Timestamp.UtcDateTime - bootTime).TotalSeconds,
                    ProcessId     = ev.ProcessId,
                    ProcessName   = ev.ProcessName,
                    LocalIp       = ev.LocalEndpoint.Address.ToString(),
                    LocalPort     = ev.LocalEndpoint.Port,
                    RemoteIp      = ev.RemoteEndpoint.Address.ToString(),
                    RemotePort    = ev.RemoteEndpoint.Port,
                    Protocol      = ev.Protocol.ToString().ToUpperInvariant(),
                });
            }
        });

        await capture.StartAsync(ct).ConfigureAwait(false);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(remainingSeconds), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }

        await capture.StopAsync().ConfigureAwait(false);

        IReadOnlyList<StartupConnection> snapshot;
        lock (connections) { snapshot = [.. connections]; }

        var session = new StartupSession
        {
            BootTime        = bootTime,
            RecordingStart  = recordingStart,
            RecordingEnd    = DateTime.UtcNow,
            ConnectionCount = snapshot.Count,
        };

        await _repo.SaveSessionAsync(session, snapshot, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "StartupMonitor: {Count} Verbindungen von {Procs} Prozessen aufgezeichnet.",
            snapshot.Count,
            snapshot.Select(c => c.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
