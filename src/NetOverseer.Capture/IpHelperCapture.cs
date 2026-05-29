// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Collections.Concurrent;
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
/// Netzwerk-Capture via Windows IP Helper API (GetExtendedTcpTable / GetExtendedUdpTable).
/// Pollt alle <see cref="PollingIntervalMs"/> Millisekunden den Verbindungsstatus
/// und emittiert neue/geänderte Verbindungen als Ereignisse.
///
/// Vorteile: Kein Kernel-Treiber nötig, funktioniert ohne WFP-Engine.
/// Nachteile: Keine Echtzeit-Paketerkennung, nur Verbindungstabellen-Snapshot.
/// </summary>
public sealed class IpHelperCapture : INetworkCapture
{
    private readonly ILogger<IpHelperCapture> _logger;
    private readonly Subject<ConnectionEvent> _subject = new();
    // Thread-sicheres Dictionary, falls künftig externe Snapshot-Aufrufe parallel
    // zum Polling-Loop laufen (z. B. eine Live-Snapshot-API für die UI).
    private readonly ConcurrentDictionary<string, ConnectionEvent> _knownConnections = new();
    private CancellationTokenSource? _cts;
    private Task? _pollingTask;

    /// <summary>
    /// Poll-Intervall in Millisekunden. Default: 500ms.
    /// Minimum: 100ms, Maximum: 5000ms.
    /// </summary>
    public int PollingIntervalMs { get; set; } = 500;

    /// <inheritdoc/>
    public IObservable<ConnectionEvent> Connections => _subject.AsObservable();

    /// <inheritdoc/>
    public bool IsCapturing => _pollingTask is { IsCompleted: false };

    public IpHelperCapture(ILogger<IpHelperCapture> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">Wenn die Aufzeichnung bereits läuft.</exception>
    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsCapturing)
            throw new InvalidOperationException("IpHelperCapture läuft bereits.");

        _logger.LogInformation("IpHelperCapture startet (Polling-Intervall: {Interval}ms)", PollingIntervalMs);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pollingTask = RunPollingLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        if (_cts is null) return;
        _logger.LogInformation("IpHelperCapture wird gestoppt...");
        await _cts.CancelAsync();
        if (_pollingTask is not null)
            await _pollingTask.ConfigureAwait(false);
        _cts.Dispose();
        _cts = null;
        _logger.LogInformation("IpHelperCapture gestoppt.");
    }

    private async Task RunPollingLoopAsync(CancellationToken ct)
    {
        _logger.LogDebug("Polling-Loop startet.");
        while (!ct.IsCancellationRequested)
        {
            try
            {
                PollConnections();
                await Task.Delay(PollingIntervalMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler im IpHelper-Polling-Loop.");
                // Kurze Pause, dann weitermachen
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
        }
        _logger.LogDebug("Polling-Loop beendet.");
    }

    private void PollConnections()
    {
        var currentConnections = new HashSet<string>();

        // Pro Poll werden ALLE aktiven Verbindungen emittiert (nicht nur neue),
        // damit die UI ihren "LastSeen"-Zeitstempel auffrischen kann. Andernfalls
        // würden langlebige Verbindungen (z. B. ein dauerhafter TCP-Stream) nach
        // der Aging-Schwelle entfernt, obwohl sie noch existieren.
        // Das ViewModel batcht Events alle 200 ms und dedupliziert nach
        // ConnectionKey, sodass die UI-Last konstant bleibt.

        // TCP-Verbindungen (IPv4)
        foreach (var row in GetTcpConnections())
            EmitEvent(ToConnectionEvent(row), currentConnections);

        // UDP-Verbindungen (IPv4)
        foreach (var row in GetUdpConnections())
            EmitEvent(ToConnectionEvent(row), currentConnections);

        // TCP-Verbindungen (IPv6)
        foreach (var row in GetTcp6Connections())
            EmitEvent(ToConnectionEvent(row), currentConnections);

        // UDP-Verbindungen (IPv6)
        foreach (var row in GetUdp6Connections())
            EmitEvent(ToConnectionEvent(row), currentConnections);

        // Geschlossene Verbindungen: synthetisches Closed-Event emittieren,
        // damit die UI die Zeile gezielt entfernen kann.
        var removed = _knownConnections.Keys.Except(currentConnections).ToList();
        foreach (var key in removed)
        {
            if (!_knownConnections.TryRemove(key, out var prev)) continue;

            var closedEvt = new ConnectionEvent
            {
                ProcessId      = prev.ProcessId,
                ProcessName    = prev.ProcessName,
                Protocol       = prev.Protocol,
                LocalEndpoint  = prev.LocalEndpoint,
                RemoteEndpoint = prev.RemoteEndpoint,
                State          = ConnectionState.Closed,
                Timestamp      = DateTimeOffset.UtcNow
            };
            _logger.LogTrace("Verbindung geschlossen: PID={Pid} {Local} → {Remote}",
                prev.ProcessId, prev.LocalEndpoint, prev.RemoteEndpoint);
            _subject.OnNext(closedEvt);
        }
    }

    /// <summary>
    /// Trackt das Event und veröffentlicht es. Aktualisiert den internen Snapshot,
    /// damit nachfolgende Polls erkennen können, dass die Verbindung noch lebt.
    /// </summary>
    private void EmitEvent(ConnectionEvent evt, HashSet<string> currentKeys)
    {
        currentKeys.Add(evt.ConnectionKey);

        var isNew = !_knownConnections.ContainsKey(evt.ConnectionKey);
        _knownConnections[evt.ConnectionKey] = evt;

        if (isNew)
        {
            _logger.LogTrace("Neue {Proto}-Verbindung: PID={Pid} {Local} → {Remote}",
                evt.Protocol, evt.ProcessId, evt.LocalEndpoint, evt.RemoteEndpoint);
        }

        _subject.OnNext(evt);
    }

    private static ConnectionEvent ToConnectionEvent(IpHelperApi.MibTcpRowOwnerPid row) => new()
    {
        ProcessId = (int)row.OwningPid,
        Protocol = NetworkProtocol.Tcp,
        LocalEndpoint = new IPEndPoint(IpHelperApi.ToIpAddress(row.LocalAddr),
            IpHelperApi.NetworkToHostPort(row.LocalPort)),
        RemoteEndpoint = new IPEndPoint(IpHelperApi.ToIpAddress(row.RemoteAddr),
            IpHelperApi.NetworkToHostPort(row.RemotePort)),
        State = MapTcpState(row.State),
        Timestamp = DateTimeOffset.UtcNow
    };

    private static ConnectionEvent ToConnectionEvent(IpHelperApi.MibUdpRowOwnerPid row) => new()
    {
        ProcessId = (int)row.OwningPid,
        Protocol = NetworkProtocol.Udp,
        LocalEndpoint = new IPEndPoint(IpHelperApi.ToIpAddress(row.LocalAddr),
            IpHelperApi.NetworkToHostPort(row.LocalPort)),
        RemoteEndpoint = new IPEndPoint(IPAddress.Any, 0),
        State = ConnectionState.Unknown,
        Timestamp = DateTimeOffset.UtcNow
    };

    private static ConnectionState MapTcpState(IpHelperApi.MibTcpState state) => state switch
    {
        IpHelperApi.MibTcpState.Established => ConnectionState.Established,
        IpHelperApi.MibTcpState.Listen => ConnectionState.Listen,
        IpHelperApi.MibTcpState.TimeWait => ConnectionState.TimeWait,
        IpHelperApi.MibTcpState.CloseWait => ConnectionState.CloseWait,
        IpHelperApi.MibTcpState.Closed => ConnectionState.Closed,
        _ => ConnectionState.Unknown
    };

    private static unsafe IEnumerable<IpHelperApi.MibTcpRowOwnerPid> GetTcpConnections()
    {
        uint bufferSize = 0;
        IpHelperApi.GetExtendedTcpTable(nint.Zero, ref bufferSize, true,
            IpHelperApi.AfInet, IpHelperApi.TcpTableClass.TcpTableOwnerPidAll, 0);

        if (bufferSize == 0) return [];

        nint buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            uint result = IpHelperApi.GetExtendedTcpTable(buffer, ref bufferSize, true,
                IpHelperApi.AfInet, IpHelperApi.TcpTableClass.TcpTableOwnerPidAll, 0);

            if (result != IpHelperApi.NoError) return [];

            int rowCount = Marshal.ReadInt32(buffer);
            int rowSize = Marshal.SizeOf<IpHelperApi.MibTcpRowOwnerPid>();
            nint rowPtr = buffer + sizeof(int);  // dwNumEntries (4 bytes)

            var rows = new List<IpHelperApi.MibTcpRowOwnerPid>(rowCount);
            for (int i = 0; i < rowCount; i++)
            {
                rows.Add(Marshal.PtrToStructure<IpHelperApi.MibTcpRowOwnerPid>(rowPtr + i * rowSize));
            }
            return rows;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IEnumerable<IpHelperApi.MibUdpRowOwnerPid> GetUdpConnections()
    {
        uint bufferSize = 0;
        IpHelperApi.GetExtendedUdpTable(nint.Zero, ref bufferSize, true,
            IpHelperApi.AfInet, IpHelperApi.UdpTableClass.UdpTableOwnerPid, 0);

        if (bufferSize == 0) return [];

        nint buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            uint result = IpHelperApi.GetExtendedUdpTable(buffer, ref bufferSize, true,
                IpHelperApi.AfInet, IpHelperApi.UdpTableClass.UdpTableOwnerPid, 0);

            if (result != IpHelperApi.NoError) return [];

            int rowCount = Marshal.ReadInt32(buffer);
            int rowSize = Marshal.SizeOf<IpHelperApi.MibUdpRowOwnerPid>();
            nint rowPtr = buffer + sizeof(int);

            var rows = new List<IpHelperApi.MibUdpRowOwnerPid>(rowCount);
            for (int i = 0; i < rowCount; i++)
            {
                rows.Add(Marshal.PtrToStructure<IpHelperApi.MibUdpRowOwnerPid>(rowPtr + i * rowSize));
            }
            return rows;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _subject.OnCompleted();
        _subject.Dispose();
        _cts?.Dispose();
    }

    // ── IPv6-Konvertierung ──────────────────────────────────────────────────

    private static ConnectionEvent ToConnectionEvent(IpHelperApi.MibTcp6RowOwnerPid row)
    {
        var local = IpHelperApi.ToIpV6Address(ref row.LocalAddr0, row.LocalScopeId);
        var remote = IpHelperApi.ToIpV6Address(ref row.RemoteAddr0, row.RemoteScopeId);
        return new ConnectionEvent
        {
            ProcessId = (int)row.OwningPid,
            Protocol = NetworkProtocol.Tcp,
            LocalEndpoint = new IPEndPoint(local, IpHelperApi.NetworkToHostPort(row.LocalPort)),
            RemoteEndpoint = new IPEndPoint(remote, IpHelperApi.NetworkToHostPort(row.RemotePort)),
            State = MapTcpState(row.State),
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private static ConnectionEvent ToConnectionEvent(IpHelperApi.MibUdp6RowOwnerPid row)
    {
        var local = IpHelperApi.ToIpV6Address(ref row.LocalAddr0, row.LocalScopeId);
        return new ConnectionEvent
        {
            ProcessId = (int)row.OwningPid,
            Protocol = NetworkProtocol.Udp,
            LocalEndpoint = new IPEndPoint(local, IpHelperApi.NetworkToHostPort(row.LocalPort)),
            RemoteEndpoint = new IPEndPoint(IPAddress.IPv6Any, 0),
            State = ConnectionState.Unknown,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private static IEnumerable<IpHelperApi.MibTcp6RowOwnerPid> GetTcp6Connections()
    {
        uint bufferSize = 0;
        IpHelperApi.GetExtendedTcpTable(nint.Zero, ref bufferSize, true,
            IpHelperApi.AfInet6, IpHelperApi.TcpTableClass.TcpTableOwnerPidAll, 0);

        if (bufferSize == 0) return [];

        nint buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            uint result = IpHelperApi.GetExtendedTcpTable(buffer, ref bufferSize, true,
                IpHelperApi.AfInet6, IpHelperApi.TcpTableClass.TcpTableOwnerPidAll, 0);

            if (result != IpHelperApi.NoError) return [];

            int rowCount = Marshal.ReadInt32(buffer);
            int rowSize = Marshal.SizeOf<IpHelperApi.MibTcp6RowOwnerPid>();
            nint rowPtr = buffer + sizeof(int);

            var rows = new List<IpHelperApi.MibTcp6RowOwnerPid>(rowCount);
            for (int i = 0; i < rowCount; i++)
                rows.Add(Marshal.PtrToStructure<IpHelperApi.MibTcp6RowOwnerPid>(rowPtr + i * rowSize));
            return rows;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IEnumerable<IpHelperApi.MibUdp6RowOwnerPid> GetUdp6Connections()
    {
        uint bufferSize = 0;
        IpHelperApi.GetExtendedUdpTable(nint.Zero, ref bufferSize, true,
            IpHelperApi.AfInet6, IpHelperApi.UdpTableClass.UdpTableOwnerPid, 0);

        if (bufferSize == 0) return [];

        nint buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            uint result = IpHelperApi.GetExtendedUdpTable(buffer, ref bufferSize, true,
                IpHelperApi.AfInet6, IpHelperApi.UdpTableClass.UdpTableOwnerPid, 0);

            if (result != IpHelperApi.NoError) return [];

            int rowCount = Marshal.ReadInt32(buffer);
            int rowSize = Marshal.SizeOf<IpHelperApi.MibUdp6RowOwnerPid>();
            nint rowPtr = buffer + sizeof(int);

            var rows = new List<IpHelperApi.MibUdp6RowOwnerPid>(rowCount);
            for (int i = 0; i < rowCount; i++)
                rows.Add(Marshal.PtrToStructure<IpHelperApi.MibUdp6RowOwnerPid>(rowPtr + i * rowSize));
            return rows;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
