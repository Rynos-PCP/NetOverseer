// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using NetOverseer.Core.Models;

namespace NetOverseer.Core.Interfaces;

/// <summary>
/// Echtzeit-DNS-Überwachung via ETW (Microsoft-Windows-DNS-Client-Provider).
/// Erfordert Administratorrechte zum Starten einer ETW-Trace-Session.
/// </summary>
public interface IDnsCapture
{
    /// <summary>Observable-Stream aller aufgezeichneten DNS-Ereignisse.</summary>
    IObservable<DnsQueryEvent> Queries { get; }

    /// <summary>Gibt an, ob die ETW-Session aktiv ist.</summary>
    bool IsRunning { get; }

    /// <summary>Anzahl der vom DNS-Provider erhaltenen Roh-Events
    /// (nützlich zur Diagnose: 0 ⇒ Provider liefert nichts).</summary>
    long EventsReceived { get; }

    /// <summary>Anzahl der erfolgreich geparsten DNS-Anfragen
    /// (Events mit QueryName, die als <see cref="DnsQueryEvent"/> ausgegeben wurden).</summary>
    long EventsAccepted { get; }

    /// <summary>Zeitpunkt des Start-Aufrufs (oder null, wenn nie gestartet).
    /// Erlaubt der UI „Läuft seit …" anzuzeigen.</summary>
    DateTimeOffset? StartedAt { get; }

    /// <summary>
    /// Pro Event-ID einmal aufgezeichnetes Payload-Schema des aktuellen Windows-Builds.
    /// Dient zur Diagnose, wenn Roh-Events ankommen aber keine als DNS-Anfrage geparst werden.
    /// </summary>
    IReadOnlyDictionary<int, string> EventSchemas { get; }

    /// <summary>Startet die ETW-Trace-Session.</summary>
    /// <exception cref="UnauthorizedAccessException">Wenn keine Administratorrechte vorhanden.</exception>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Stoppt die ETW-Trace-Session und gibt Ressourcen frei.</summary>
    Task StopAsync();
}
