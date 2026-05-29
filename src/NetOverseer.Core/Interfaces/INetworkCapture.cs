// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
namespace NetOverseer.Core.Interfaces;

/// <summary>
/// Abstraktion für Netzwerk-Capture-Implementierungen.
/// Implementierungen: WfpNetworkCapture (WFP-basiert) und IpHelperCapture (Polling-Fallback).
/// </summary>
public interface INetworkCapture : IDisposable
{
    /// <summary>
    /// Reaktiver Stream aller aufgezeichneten Verbindungsereignisse.
    /// Subscriber werden auf einem Background-Thread benachrichtigt – UI-Thread-Dispatch ist Sache des Consumers.
    /// </summary>
    IObservable<Models.ConnectionEvent> Connections { get; }

    /// <summary>
    /// Startet die Netzwerkaufzeichnung.
    /// </summary>
    /// <param name="ct">Abbruch-Token. Wird ct abgebrochen, wird StopAsync automatisch aufgerufen.</param>
    /// <exception cref="UnauthorizedAccessException">
    /// Wenn die Anwendung nicht mit Administrator-Rechten läuft.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Wenn die Aufzeichnung bereits läuft.
    /// </exception>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Stoppt die Netzwerkaufzeichnung und gibt Ressourcen frei.</summary>
    Task StopAsync();

    /// <summary>Gibt an, ob die Aufzeichnung aktuell aktiv ist.</summary>
    bool IsCapturing { get; }
}
