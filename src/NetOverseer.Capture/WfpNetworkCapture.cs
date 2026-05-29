// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Net;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NetOverseer.Capture.Native;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.Capture;

/// <summary>
/// Netzwerk-Capture primär über die Windows Filtering Platform (WFP) User-Mode API.
///
/// ARCHITEKTUR-HINWEIS:
/// Echtes Paket-Level-Capture via WFP erfordert einen Kernel-Mode-Callout-Treiber (C++).
/// Diese Klasse öffnet die WFP-Engine, validiert den Zugang und delegiert das eigentliche
/// Connection-Tracking an <see cref="IpHelperCapture"/>. Die WFP-Engine-Verbindung wird
/// für spätere Firewall-Blocking-Operationen gehalten.
///
/// Für den zukünftigen Kernel-Treiber (tools/WfpDriver) stehen alle P/Invoke-Signaturen
/// in <see cref="WfpApi"/> bereit.
/// </summary>
public sealed class WfpNetworkCapture : INetworkCapture
{
    private readonly ILogger<WfpNetworkCapture> _logger;
    private readonly IpHelperCapture _ipHelperCapture;
    private nint _engineHandle = nint.Zero;

    /// <inheritdoc/>
    public IObservable<ConnectionEvent> Connections => _ipHelperCapture.Connections;

    /// <inheritdoc/>
    public bool IsCapturing => _ipHelperCapture.IsCapturing;

    /// <summary>
    /// Poll-Intervall des internen IpHelper-Trackers in Millisekunden.
    /// </summary>
    public int PollingIntervalMs
    {
        get => _ipHelperCapture.PollingIntervalMs;
        set => _ipHelperCapture.PollingIntervalMs = value;
    }

    public WfpNetworkCapture(
        ILogger<WfpNetworkCapture> logger,
        ILogger<IpHelperCapture> ipHelperLogger)
    {
        _logger = logger;
        _ipHelperCapture = new IpHelperCapture(ipHelperLogger);
    }

    /// <inheritdoc/>
    /// <exception cref="UnauthorizedAccessException">
    /// Wenn keine Administrator-Rechte vorhanden sind (WFP erfordert Elevation).
    /// </exception>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsCapturing)
            throw new InvalidOperationException("WfpNetworkCapture läuft bereits.");

        OpenWfpEngine();  // Validiert Admin-Rechte und WFP-Verfügbarkeit
        await _ipHelperCapture.StartAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        await _ipHelperCapture.StopAsync().ConfigureAwait(false);
        CloseWfpEngine();
    }

    /// <summary>
    /// Öffnet die WFP-Engine. Schlägt fehl wenn:
    /// - Keine Admin-Rechte vorhanden (ERROR_ACCESS_DENIED = 0x80070005)
    /// - Windows Filtering Platform Base Filtering Engine Service nicht läuft
    /// </summary>
    private void OpenWfpEngine()
    {
        var session = new WfpApi.FwpmSession0
        {
            DisplayData = new WfpApi.FwpmDisplayData0
            {
                Name = "NetOverseer Monitor Session",
                Description = "NetOverseer network monitoring session"
            },
            Flags = WfpApi.FwpmSessionFlagDynamic,  // Session wird beim Handle-Close automatisch aufgeräumt
            TxnWaitTimeoutInMsec = 5000
        };

        uint result = WfpApi.FwpmEngineOpen0(
            serverName: null,                        // lokale WFP-Engine
            authnService: WfpApi.RpcCAuthnDefault,
            authIdentity: nint.Zero,
            session: in session,
            engineHandle: out _engineHandle);

        if (!WfpApi.IsSuccess(result))
        {
            _engineHandle = nint.Zero;
            string errorMsg = WfpApi.GetErrorMessage(result);

            if (result == 0x80070005)  // ERROR_ACCESS_DENIED
            {
                throw new UnauthorizedAccessException(
                    $"WFP-Engine konnte nicht geöffnet werden: {errorMsg}. " +
                    "NetOverseer muss mit Administrator-Rechten ausgeführt werden.");
            }

            throw new InvalidOperationException(
                $"WFP-Engine konnte nicht geöffnet werden: {errorMsg} (0x{result:X8}). " +
                "Stellen Sie sicher, dass der 'Base Filtering Engine' Dienst läuft.");
        }

        _logger.LogInformation("WFP-Engine erfolgreich geöffnet (Handle: 0x{Handle:X})", _engineHandle);
    }

    private void CloseWfpEngine()
    {
        if (_engineHandle == nint.Zero) return;

        uint result = WfpApi.FwpmEngineClose0(_engineHandle);
        if (!WfpApi.IsSuccess(result))
            _logger.LogWarning("WFP-Engine konnte nicht sauber geschlossen werden: {Error}",
                WfpApi.GetErrorMessage(result));
        else
            _logger.LogInformation("WFP-Engine geschlossen.");

        _engineHandle = nint.Zero;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _ipHelperCapture.Dispose();
        CloseWfpEngine();
    }
}
