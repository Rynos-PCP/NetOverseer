// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Net;

namespace NetOverseer.Core.Models;

/// <summary>
/// Netzwerkprotokoll einer Verbindung.
/// </summary>
public enum NetworkProtocol
{
    Tcp,
    Udp,
    Unknown
}

/// <summary>
/// Repräsentiert ein aufgezeichnetes Netzwerkverbindungs-Ereignis.
/// </summary>
public sealed class ConnectionEvent
{
    /// <summary>Prozess-ID des sendenden/empfangenden Prozesses.</summary>
    public int ProcessId { get; init; }

    /// <summary>Name des Prozesses (z.B. "chrome.exe").</summary>
    public string ProcessName { get; init; } = string.Empty;

    /// <summary>Lokaler Endpunkt (IP:Port).</summary>
    public IPEndPoint LocalEndpoint { get; init; } = new(IPAddress.Any, 0);

    /// <summary>Entfernter Endpunkt (IP:Port).</summary>
    public IPEndPoint RemoteEndpoint { get; init; } = new(IPAddress.Any, 0);

    /// <summary>Transportprotokoll der Verbindung.</summary>
    public NetworkProtocol Protocol { get; init; }

    /// <summary>Zeitstempel des Ereignisses (UTC).</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gesendete Bytes (seit letztem Update).</summary>
    public long BytesSent { get; init; }

    /// <summary>Empfangene Bytes (seit letztem Update).</summary>
    public long BytesReceived { get; init; }

    /// <summary>Verbindungsstatus (aktiv, wartend, geschlossen etc.).</summary>
    public ConnectionState State { get; init; }

    /// <summary>Eindeutige ID dieser Verbindung (Local+Remote Endpunkt + PID).</summary>
    public string ConnectionKey =>
        $"{ProcessId}_{Protocol}_{LocalEndpoint}_{RemoteEndpoint}";
}

/// <summary>
/// Verbindungsstatus einer TCP/UDP-Verbindung.
/// </summary>
public enum ConnectionState
{
    Unknown,
    Established,
    Listen,
    TimeWait,
    CloseWait,
    Closed
}
