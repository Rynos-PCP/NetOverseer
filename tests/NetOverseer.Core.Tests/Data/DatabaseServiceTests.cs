// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.Extensions.Logging.Abstractions;
using NetOverseer.Core.Models;
using NetOverseer.Data;

namespace NetOverseer.Core.Tests.Data;

/// <summary>
/// Integrationstests für <see cref="DatabaseService"/>, <see cref="ConnectionRepository"/>,
/// <see cref="DnsRepository"/> und <see cref="AppProfileRepository"/> mit temporären SQLite-Dateien.
/// </summary>
public sealed class DatabaseServiceTests : IDisposable
{
    private readonly string          _dbPath;
    private readonly DatabaseService _db;

    public DatabaseServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"neto_main_{Guid.NewGuid():N}.db");
        _db     = new DatabaseService(_dbPath, NullLogger<DatabaseService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    // ──────────────────────────────────────────────────────────────────────
    // DatabaseService: Initialisierung
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_CreatesSchemaWithoutError()
    {
        await _db.InitializeAsync();
        // Zweifacher Aufruf muss idempotent sein
        await _db.InitializeAsync();
    }

    [Fact]
    public async Task GetDatabaseSizeBytes_ReturnsPositiveAfterInit()
    {
        await _db.InitializeAsync();
        Assert.True(_db.GetDatabaseSizeBytes() > 0);
    }

    [Fact]
    public async Task RunMaintenanceAsync_RunsWithoutError_OnEmptyDatabase()
    {
        await _db.InitializeAsync();
        await _db.RunMaintenanceAsync(retentionDays: 30);
    }

    // ──────────────────────────────────────────────────────────────────────
    // ConnectionRepository
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConnectionRepository_InsertAndRetrieve()
    {
        await _db.InitializeAsync();
        long sid  = await InsertTestSessionAsync();
        var repo  = new ConnectionRepository(_db, NullLogger<ConnectionRepository>.Instance);

        var records = new List<ConnectionRecord>
        {
            MakeConnection(sessionId: sid, processName: "chrome.exe",   remoteIp: "8.8.8.8"),
            MakeConnection(sessionId: sid, processName: "chrome.exe",   remoteIp: "1.1.1.1"),
            MakeConnection(sessionId: sid, processName: "explorer.exe", remoteIp: "8.8.8.8"),
        };

        await repo.InsertBatchAsync(records);

        var count = await repo.CountAsync();
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task ConnectionRepository_GetByProcess_ReturnsCorrectSubset()
    {
        await _db.InitializeAsync();
        long sid = await InsertTestSessionAsync();
        var repo = new ConnectionRepository(_db, NullLogger<ConnectionRepository>.Instance);

        await repo.InsertBatchAsync([
            MakeConnection(sessionId: sid, processName: "chrome.exe",   remoteIp: "8.8.8.8"),
            MakeConnection(sessionId: sid, processName: "explorer.exe", remoteIp: "1.1.1.1"),
        ]);

        var results = await repo.GetByProcessAsync("chrome.exe");
        Assert.Single(results);
        Assert.Equal("chrome.exe", results[0].ProcessName);
    }

    [Fact]
    public async Task ConnectionRepository_GetByRemoteIp_ReturnsMatches()
    {
        await _db.InitializeAsync();
        long sid = await InsertTestSessionAsync();
        var repo = new ConnectionRepository(_db, NullLogger<ConnectionRepository>.Instance);

        await repo.InsertBatchAsync([
            MakeConnection(sessionId: sid, processName: "chrome.exe",   remoteIp: "8.8.8.8"),
            MakeConnection(sessionId: sid, processName: "firefox.exe",  remoteIp: "8.8.8.8"),
            MakeConnection(sessionId: sid, processName: "explorer.exe", remoteIp: "1.1.1.1"),
        ]);

        var results = await repo.GetByRemoteIpAsync("8.8.8.8");
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("8.8.8.8", r.RemoteIp));
    }

    [Fact]
    public async Task ConnectionRepository_GetByTimeRange_FiltersCorrectly()
    {
        await _db.InitializeAsync();
        long sid = await InsertTestSessionAsync();
        var repo = new ConnectionRepository(_db, NullLogger<ConnectionRepository>.Instance);

        var t0 = DateTimeOffset.UtcNow.AddMinutes(-10);
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var t2 = DateTimeOffset.UtcNow;

        await repo.InsertBatchAsync([
            MakeConnection(sessionId: sid, timestamp: t0),
            MakeConnection(sessionId: sid, timestamp: t1),
            MakeConnection(sessionId: sid, timestamp: t2),
        ]);

        var results = await repo.GetByTimeRangeAsync(
            from: DateTimeOffset.UtcNow.AddMinutes(-7),
            to:   DateTimeOffset.UtcNow.AddMinutes(-3));

        Assert.Single(results);
    }

    [Fact]
    public async Task ConnectionRepository_DeleteOlderThan_RemovesOldEntries()
    {
        await _db.InitializeAsync();
        long sid = await InsertTestSessionAsync();
        var repo = new ConnectionRepository(_db, NullLogger<ConnectionRepository>.Instance);

        var old   = DateTimeOffset.UtcNow.AddDays(-35);
        var fresh = DateTimeOffset.UtcNow;

        await repo.InsertBatchAsync([
            MakeConnection(sessionId: sid, timestamp: old),
            MakeConnection(sessionId: sid, timestamp: fresh),
        ]);

        int deleted = await repo.DeleteOlderThanAsync(DateTimeOffset.UtcNow.AddDays(-30));
        Assert.Equal(1, deleted);
        Assert.Equal(1, await repo.CountAsync());
    }

    // ──────────────────────────────────────────────────────────────────────
    // DnsRepository
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DnsRepository_InsertAndRetrieveByDomain()
    {
        await _db.InitializeAsync();
        var repo = new DnsRepository(_db, NullLogger<DnsRepository>.Instance);

        await repo.InsertBatchAsync([
            MakeDns(processName: "chrome.exe", domain: "example.com"),
            MakeDns(processName: "chrome.exe", domain: "google.com"),
            MakeDns(processName: "firefox.exe", domain: "example.com"),
        ]);

        var results = await repo.GetByDomainAsync("example.com");
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("example.com", r.Domain));
    }

    [Fact]
    public async Task DnsRepository_GetByProcess_ReturnsSubset()
    {
        await _db.InitializeAsync();
        var repo = new DnsRepository(_db, NullLogger<DnsRepository>.Instance);

        await repo.InsertBatchAsync([
            MakeDns(processName: "chrome.exe",  domain: "a.com"),
            MakeDns(processName: "firefox.exe", domain: "b.com"),
        ]);

        var results = await repo.GetByProcessAsync("chrome.exe");
        Assert.Single(results);
        Assert.Equal("a.com", results[0].Domain);
    }

    [Fact]
    public async Task DnsRepository_DeleteOlderThan_Works()
    {
        await _db.InitializeAsync();
        var repo = new DnsRepository(_db, NullLogger<DnsRepository>.Instance);

        await repo.InsertBatchAsync([
            MakeDns(domain: "old.com",   timestamp: DateTimeOffset.UtcNow.AddDays(-40)),
            MakeDns(domain: "fresh.com", timestamp: DateTimeOffset.UtcNow),
        ]);

        int deleted = await repo.DeleteOlderThanAsync(DateTimeOffset.UtcNow.AddDays(-30));
        Assert.Equal(1, deleted);
    }

    // ──────────────────────────────────────────────────────────────────────
    // AppProfileRepository
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AppProfileRepository_UpsertCreatesNewEntry()
    {
        await _db.InitializeAsync();
        var repo = new AppProfileRepository(_db, NullLogger<AppProfileRepository>.Instance);

        await repo.UpsertAsync(new AppProfile
        {
            ExecutablePath     = @"C:\App\chrome.exe",
            DisplayName        = "Google Chrome",
            TotalBytesSent     = 1000,
            TotalBytesReceived = 2000,
            LastSeen           = DateTimeOffset.UtcNow,
        });

        var profile = await repo.GetByPathAsync(@"C:\App\chrome.exe");
        Assert.NotNull(profile);
        Assert.Equal("Google Chrome", profile.DisplayName);
        Assert.Equal(1000, profile.TotalBytesSent);
    }

    [Fact]
    public async Task AppProfileRepository_UpsertAccumulatesBytes()
    {
        await _db.InitializeAsync();
        var repo = new AppProfileRepository(_db, NullLogger<AppProfileRepository>.Instance);

        var profile = new AppProfile
        {
            ExecutablePath     = @"C:\App\chrome.exe",
            DisplayName        = "Chrome",
            TotalBytesSent     = 500,
            TotalBytesReceived = 1000,
            LastSeen           = DateTimeOffset.UtcNow.AddSeconds(-10),
        };
        await repo.UpsertAsync(profile);

        var update = new AppProfile
        {
            ExecutablePath     = @"C:\App\chrome.exe",
            DisplayName        = "Chrome",
            TotalBytesSent     = 300,
            TotalBytesReceived = 600,
            LastSeen           = DateTimeOffset.UtcNow,  // neuerer Zeitstempel → Bedingung erfüllt
        };
        await repo.UpsertAsync(update);

        var result = await repo.GetByPathAsync(@"C:\App\chrome.exe");
        Assert.NotNull(result);
        Assert.Equal(800,  result.TotalBytesSent);
        Assert.Equal(1600, result.TotalBytesReceived);
    }

    [Fact]
    public async Task AppProfileRepository_GetTopByBytesSent_ReturnsOrdered()
    {
        await _db.InitializeAsync();
        var repo = new AppProfileRepository(_db, NullLogger<AppProfileRepository>.Instance);

        await repo.UpsertAsync(MakeProfile(@"C:\a.exe", bytesSent: 100));
        await repo.UpsertAsync(MakeProfile(@"C:\b.exe", bytesSent: 5000));
        await repo.UpsertAsync(MakeProfile(@"C:\c.exe", bytesSent: 300));

        var top2 = await repo.GetTopByBytesSentAsync(2);
        Assert.Equal(2, top2.Count);
        Assert.Equal(@"C:\b.exe", top2[0].ExecutablePath);
        Assert.Equal(@"C:\c.exe", top2[1].ExecutablePath);
    }

    [Fact]
    public async Task AppProfileRepository_SetBlocked_UpdatesFlag()
    {
        await _db.InitializeAsync();
        var repo = new AppProfileRepository(_db, NullLogger<AppProfileRepository>.Instance);

        await repo.UpsertAsync(MakeProfile(@"C:\app.exe"));
        await repo.SetBlockedAsync(@"C:\app.exe", isBlocked: true);

        var result = await repo.GetByPathAsync(@"C:\app.exe");
        Assert.True(result!.IsBlocked);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static ConnectionRecord MakeConnection(
        long            sessionId   = 1,
        string          processName = "test.exe",
        string          remoteIp    = "8.8.8.8",
        DateTimeOffset? timestamp   = null) => new()
    {
        SessionId   = sessionId,
        Timestamp   = timestamp ?? DateTimeOffset.UtcNow,
        ProcessId   = 1234,
        ProcessName = processName,
        LocalIp     = "127.0.0.1",
        LocalPort   = 12345,
        RemoteIp    = remoteIp,
        RemotePort  = 443,
        Protocol    = "Tcp",
    };

    private static DnsRecord MakeDns(
        string          processName = "test.exe",
        string          domain      = "example.com",
        DateTimeOffset? timestamp   = null) => new()
    {
        Timestamp   = timestamp ?? DateTimeOffset.UtcNow,
        ProcessId   = 1234,
        ProcessName = processName,
        Domain      = domain,
        QueryType   = "A",
        ResolvedIps = "8.8.8.8",
        Category    = "Normal",
    };

    private static AppProfile MakeProfile(string path, long bytesSent = 0) => new()
    {
        ExecutablePath     = path,
        DisplayName        = Path.GetFileNameWithoutExtension(path),
        TotalBytesSent     = bytesSent,
        TotalBytesReceived = 0,
        LastSeen           = DateTimeOffset.UtcNow,
    };

    /// <summary>
    /// Legt eine Test-Sitzung in der Sessions-Tabelle an und gibt die neue Id zurück.
    /// Verbindungen benötigen eine gültige SessionId (FK-Constraint).
    /// </summary>
    private async Task<long> InsertTestSessionAsync()
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(_db.ConnectionString);
        await conn.OpenAsync();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Sessions (StartTime, EndTime, TotalConnections, TotalBytesTransferred)
            VALUES (@start, NULL, 0, 0);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@start", DateTimeOffset.UtcNow.ToString("O"));
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
