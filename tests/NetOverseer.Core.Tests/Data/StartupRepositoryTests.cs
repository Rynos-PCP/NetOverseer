// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.Extensions.Logging.Abstractions;
using NetOverseer.Core.Models;
using NetOverseer.Data;

namespace NetOverseer.Core.Tests.Data;

/// <summary>Integrationstests für <see cref="StartupRepository"/> mit einer temporären SQLite-Datei.</summary>
public sealed class StartupRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly StartupRepository _sut;

    public StartupRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"neto_test_{Guid.NewGuid():N}.db");
        _sut    = new StartupRepository(_dbPath, NullLogger<StartupRepository>.Instance);
    }

    public void Dispose()
    {
        _sut.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    // ──────────────────────────────────────────────────────────────────────
    // GetLastSessionAsync
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLastSession_ReturnsNull_WhenEmpty()
    {
        var result = await _sut.GetLastSessionAsync();
        Assert.Null(result);
    }

    // ──────────────────────────────────────────────────────────────────────
    // SaveSessionAsync
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveSession_ReturnsPositiveId()
    {
        var (session, _) = MakeSession();
        var id = await _sut.SaveSessionAsync(session, []);
        Assert.True(id > 0);
    }

    [Fact]
    public async Task SaveSession_WithConnections_CanBeRetrieved()
    {
        var (session, connections) = MakeSession(connectionCount: 3);
        var id = await _sut.SaveSessionAsync(session, connections);

        var loaded = await _sut.GetLastSessionAsync();

        Assert.NotNull(loaded);
        Assert.Equal(id, loaded!.Id);
        Assert.Equal(3, loaded.ConnectionCount);
        // Zeitstempel auf Sekunde genau (SQLite rundet auf ISO-String)
        Assert.InRange(
            (loaded.BootTime.ToUniversalTime() - session.BootTime.ToUniversalTime()).TotalSeconds,
            -1.0, 1.0);
    }

    [Fact]
    public async Task SaveMultipleSessions_GetLastReturnsNewest()
    {
        await _sut.SaveSessionAsync(MakeSession().session, []);
        await Task.Delay(10);
        var (newest, _) = MakeSession();
        var id2 = await _sut.SaveSessionAsync(newest, []);

        var last = await _sut.GetLastSessionAsync();
        Assert.Equal(id2, last!.Id);
    }

    // ──────────────────────────────────────────────────────────────────────
    // GetConnectionsAsync
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetConnections_ReturnsEmpty_WhenNoConnections()
    {
        var (session, _) = MakeSession();
        var id = await _sut.SaveSessionAsync(session, []);

        var conns = await _sut.GetConnectionsAsync(id);
        Assert.Empty(conns);
    }

    [Fact]
    public async Task GetConnections_ReturnsSortedByOffset()
    {
        var connections = new[]
        {
            MakeConnection(offsetSeconds: 30.0),
            MakeConnection(offsetSeconds:  5.0),
            MakeConnection(offsetSeconds: 15.0),
        };

        var (session, _) = MakeSession();
        var id = await _sut.SaveSessionAsync(session, connections);

        var result = await _sut.GetConnectionsAsync(id);

        Assert.Equal(3, result.Count);
        Assert.Equal(5.0,  result[0].OffsetSeconds, precision: 3);
        Assert.Equal(15.0, result[1].OffsetSeconds, precision: 3);
        Assert.Equal(30.0, result[2].OffsetSeconds, precision: 3);
    }

    [Fact]
    public async Task GetConnections_RoundTrips_AllFields()
    {
        var conn = new StartupConnection
        {
            Timestamp     = new DateTime(2026, 5, 25, 8, 0, 0, DateTimeKind.Utc),
            OffsetSeconds = 12.345,
            ProcessId     = 1234,
            ProcessName   = "chrome.exe",
            LocalIp       = "192.168.1.10",
            LocalPort     = 54321,
            RemoteIp      = "1.2.3.4",
            RemotePort    = 443,
            Protocol      = "TCP",
        };

        var (session, _) = MakeSession();
        var id = await _sut.SaveSessionAsync(session, [conn]);

        var result = await _sut.GetConnectionsAsync(id);

        Assert.Single(result);
        var r = result[0];
        Assert.Equal(conn.ProcessId,     r.ProcessId);
        Assert.Equal(conn.ProcessName,   r.ProcessName);
        Assert.Equal(conn.RemoteIp,      r.RemoteIp);
        Assert.Equal(conn.RemotePort,    r.RemotePort);
        Assert.Equal(conn.Protocol,      r.Protocol);
        Assert.Equal(conn.OffsetSeconds, r.OffsetSeconds, precision: 3);
    }

    [Fact]
    public async Task GetConnections_DoesNotReturnFromOtherSessions()
    {
        var (s1, _) = MakeSession();
        var id1 = await _sut.SaveSessionAsync(s1, [MakeConnection()]);

        var (s2, _) = MakeSession();
        await _sut.SaveSessionAsync(s2, [MakeConnection(), MakeConnection()]);

        var result = await _sut.GetConnectionsAsync(id1);
        Assert.Single(result);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static (StartupSession session, IReadOnlyList<StartupConnection> connections)
        MakeSession(int connectionCount = 0)
    {
        var now = DateTime.UtcNow;
        var session = new StartupSession
        {
            BootTime       = now.AddSeconds(-30),
            RecordingStart = now,
            RecordingEnd   = now.AddSeconds(60),
            ConnectionCount = connectionCount,
        };
        var connections = Enumerable.Range(0, connectionCount)
            .Select(i => MakeConnection(offsetSeconds: i * 5.0))
            .ToList();
        return (session, connections);
    }

    private static StartupConnection MakeConnection(double offsetSeconds = 0.0) => new()
    {
        Timestamp     = DateTime.UtcNow,
        OffsetSeconds = offsetSeconds,
        ProcessId     = 42,
        ProcessName   = "svchost.exe",
        LocalIp       = "127.0.0.1",
        LocalPort     = 1234,
        RemoteIp      = "8.8.8.8",
        RemotePort    = 443,
        Protocol      = "TCP",
    };
}
