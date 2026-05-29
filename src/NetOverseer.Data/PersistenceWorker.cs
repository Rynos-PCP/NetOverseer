// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.Data;

/// <summary>
/// Hintergrund-Worker der Netzwerkverbindungen und DNS-Abfragen gepuffert
/// alle 5 Sekunden in SQLite schreibt (Batch-Inserts für Performance).
/// Abonniert <see cref="INetworkCapture.Connections"/> und <see cref="IDnsCapture.Queries"/>.
/// </summary>
public sealed class PersistenceWorker : IPersistenceWorker, IDisposable
{
    private const int FlushIntervalMs = 5_000;
    private const int MaxBatchSize    = 100;

    private readonly INetworkCapture          _capture;
    private readonly IDnsCapture              _dnsCapture;
    private readonly IConnectionRepository    _connections;
    private readonly IDnsRepository           _dns;
    private readonly IAppProfileRepository    _profiles;
    private readonly IProcessResolver         _processResolver;
    private readonly IGeoLocationService      _geoService;
    private readonly ILogger<PersistenceWorker> _logger;

    private readonly ConcurrentQueue<ConnectionEvent>  _connQueue = new();
    private readonly ConcurrentQueue<DnsQueryEvent>    _dnsQueue  = new();

    private IDisposable? _connSub;
    private IDisposable? _dnsSub;
    private CancellationTokenSource? _cts;
    private Task? _flushTask;
    private long  _currentSessionId;

    public bool IsRunning => _flushTask is { IsCompleted: false };

    public PersistenceWorker(
        INetworkCapture       capture,
        IDnsCapture           dnsCapture,
        IConnectionRepository connections,
        IDnsRepository        dns,
        IAppProfileRepository profiles,
        IProcessResolver      processResolver,
        IGeoLocationService   geoService,
        ILogger<PersistenceWorker> logger)
    {
        _capture         = capture;
        _dnsCapture      = dnsCapture;
        _connections     = connections;
        _dns             = dns;
        _profiles        = profiles;
        _processResolver = processResolver;
        _geoService      = geoService;
        _logger          = logger;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning) return;

        // Neue Sitzung anlegen (SessionId = aktuelle Startzeit als Unixtime)
        _currentSessionId = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Capture-Events abonnieren
        _connSub = _capture.Connections.Subscribe(e => _connQueue.Enqueue(e));
        _dnsSub  = _dnsCapture.Queries.Subscribe(e => _dnsQueue.Enqueue(e));

        _flushTask = FlushLoopAsync(_cts.Token);
        _logger.LogInformation("PersistenceWorker gestartet (Session {Id}).", _currentSessionId);
        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        if (_cts is null) return;

        _connSub?.Dispose();
        _dnsSub?.Dispose();

        await _cts.CancelAsync().ConfigureAwait(false);

        if (_flushTask is not null)
        {
            try { await _flushTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* erwartet */ }
        }

        // Letzten Flush durchführen
        await FlushConnectionsAsync(CancellationToken.None).ConfigureAwait(false);
        await FlushDnsAsync(CancellationToken.None).ConfigureAwait(false);

        _cts.Dispose();
        _cts = null;
        _logger.LogInformation("PersistenceWorker gestoppt.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Flush-Loop
    // ──────────────────────────────────────────────────────────────────────

    private async Task FlushLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(FlushIntervalMs, ct).ConfigureAwait(false);
                await FlushConnectionsAsync(ct).ConfigureAwait(false);
                await FlushDnsAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PersistenceWorker: Fehler beim Flush.");
            }
        }
    }

    private async Task FlushConnectionsAsync(CancellationToken ct)
    {
        if (_connQueue.IsEmpty) return;

        var batch = new List<ConnectionRecord>(MaxBatchSize);

        while (batch.Count < MaxBatchSize && _connQueue.TryDequeue(out var ev))
        {
            var geo  = _geoService.Lookup(ev.RemoteEndpoint.Address);
            var proc = _processResolver.GetProcessInfo(ev.ProcessId);

            batch.Add(new ConnectionRecord
            {
                SessionId      = _currentSessionId,
                Timestamp      = ev.Timestamp,
                ProcessId      = ev.ProcessId,
                ProcessName    = ev.ProcessName,
                ExecutablePath = proc?.ExecutablePath ?? string.Empty,
                LocalIp        = ev.LocalEndpoint.Address.ToString(),
                LocalPort      = ev.LocalEndpoint.Port,
                RemoteIp       = ev.RemoteEndpoint.Address.ToString(),
                RemotePort     = ev.RemoteEndpoint.Port,
                Protocol       = ev.Protocol.ToString(),
                BytesSent      = ev.BytesSent,
                BytesReceived  = ev.BytesReceived,
                GeoCountry     = geo.CountryCode,
                GeoCity        = geo.City,
            });

            // App-Profil aktualisieren (fire-and-forget, Fehler ignorieren)
            if (proc is not null && !string.IsNullOrEmpty(proc.ExecutablePath))
            {
                _ = UpdateProfileAsync(proc, ev, ct);
            }
        }

        if (batch.Count > 0)
        {
            await _connections.InsertBatchAsync(batch, ct).ConfigureAwait(false);
            _logger.LogDebug("PersistenceWorker: {N} Verbindungen geflusht.", batch.Count);
        }
    }

    private async Task FlushDnsAsync(CancellationToken ct)
    {
        if (_dnsQueue.IsEmpty) return;

        var batch = new List<DnsRecord>(MaxBatchSize);

        while (batch.Count < MaxBatchSize && _dnsQueue.TryDequeue(out var ev))
        {
            batch.Add(new DnsRecord
            {
                Timestamp   = ev.Timestamp,
                ProcessId   = ev.ProcessId,
                ProcessName = ev.ProcessName,
                Domain      = ev.QueryName,
                QueryType   = ev.QueryType.ToString(),
                ResolvedIps = string.Join(",", ev.ResolvedAddresses),
                Category    = ev.Category.ToString(),
            });
        }

        if (batch.Count > 0)
        {
            await _dns.InsertBatchAsync(batch, ct).ConfigureAwait(false);
            _logger.LogDebug("PersistenceWorker: {N} DNS-Abfragen geflusht.", batch.Count);
        }
    }

    private async Task UpdateProfileAsync(ProcessInfo proc, ConnectionEvent ev, CancellationToken ct)
    {
        try
        {
            var profile = new AppProfile
            {
                ExecutablePath     = proc.ExecutablePath,
                DisplayName        = proc.DisplayName,
                TotalBytesSent     = ev.BytesSent,
                TotalBytesReceived = ev.BytesReceived,
                LastSeen           = ev.Timestamp,
            };
            await _profiles.UpsertAsync(profile, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PersistenceWorker: Profil-Update für {Path} fehlgeschlagen.",
                proc.ExecutablePath);
        }
    }

    public void Dispose()
    {
        _connSub?.Dispose();
        _dnsSub?.Dispose();
        _cts?.Dispose();
    }
}
