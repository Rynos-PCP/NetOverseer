// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.Data;

/// <summary>
/// SQLite-Implementierung von <see cref="IConnectionRepository"/>.
/// Alle Schreib-Operationen verwenden explizite Transaktionen für Batch-Effizienz.
/// </summary>
public sealed class ConnectionRepository : IConnectionRepository
{
    private readonly DatabaseService _db;
    private readonly ILogger<ConnectionRepository> _logger;

    public ConnectionRepository(DatabaseService db, ILogger<ConnectionRepository> logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task InsertBatchAsync(
        IReadOnlyList<ConnectionRecord> records,
        CancellationToken ct = default)
    {
        if (records.Count == 0) return;

        using var conn = _db.OpenConnection();
        using var txn  = conn.BeginTransaction();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            cmd.CommandText = """
                INSERT INTO Connections (
                    SessionId, Timestamp, ProcessId, ProcessName, ExecutablePath,
                    LocalIp, LocalPort, RemoteIp, RemotePort, Protocol,
                    BytesSent, BytesReceived, Duration, GeoCountry, GeoCity,
                    ReputationScore, IsBlocked
                ) VALUES (
                    @sessionId, @ts, @pid, @pname, @exe,
                    @lip, @lport, @rip, @rport, @proto,
                    @sent, @recv, @dur, @country, @city,
                    @rep, @blocked
                );
                """;

            // Parameter einmal anlegen, dann pro Record befüllen
            var p = new Dictionary<string, SqliteParameter>();
            foreach (var name in new[]
            {
                "@sessionId","@ts","@pid","@pname","@exe",
                "@lip","@lport","@rip","@rport","@proto",
                "@sent","@recv","@dur","@country","@city",
                "@rep","@blocked"
            })
            {
                p[name] = cmd.Parameters.Add(name, SqliteType.Text);
            }

            foreach (var rec in records)
            {
                p["@sessionId"].Value = rec.SessionId;
                p["@ts"].Value        = rec.Timestamp.ToString("O");
                p["@pid"].Value       = rec.ProcessId;
                p["@pname"].Value     = rec.ProcessName;
                p["@exe"].Value       = rec.ExecutablePath;
                p["@lip"].Value       = rec.LocalIp;
                p["@lport"].Value     = rec.LocalPort;
                p["@rip"].Value       = rec.RemoteIp;
                p["@rport"].Value     = rec.RemotePort;
                p["@proto"].Value     = rec.Protocol;
                p["@sent"].Value      = rec.BytesSent;
                p["@recv"].Value      = rec.BytesReceived;
                p["@dur"].Value       = rec.Duration;
                p["@country"].Value   = rec.GeoCountry;
                p["@city"].Value      = rec.GeoCity;
                p["@rep"].Value       = rec.ReputationScore;
                p["@blocked"].Value   = rec.IsBlocked ? 1 : 0;
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            txn.Commit();
            _logger.LogDebug("ConnectionRepository: {N} Verbindungen gespeichert.", records.Count);
        }
        catch
        {
            txn.Rollback();
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConnectionRecord>> GetByTimeRangeAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, SessionId, Timestamp, ProcessId, ProcessName, ExecutablePath,
                   LocalIp, LocalPort, RemoteIp, RemotePort, Protocol,
                   BytesSent, BytesReceived, Duration, GeoCountry, GeoCity,
                   ReputationScore, IsBlocked
            FROM Connections
            WHERE Timestamp >= @from AND Timestamp <= @to
            ORDER BY Timestamp DESC;
            """;
        cmd.Parameters.AddWithValue("@from", from.ToString("O"));
        cmd.Parameters.AddWithValue("@to",   to.ToString("O"));
        return await ReadRecordsAsync(cmd, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConnectionRecord>> GetByProcessAsync(
        string processName, int limit = 500, CancellationToken ct = default)
    {
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, SessionId, Timestamp, ProcessId, ProcessName, ExecutablePath,
                   LocalIp, LocalPort, RemoteIp, RemotePort, Protocol,
                   BytesSent, BytesReceived, Duration, GeoCountry, GeoCity,
                   ReputationScore, IsBlocked
            FROM Connections
            WHERE ProcessName = @name
            ORDER BY Timestamp DESC
            LIMIT @limit;
            """;
        cmd.Parameters.AddWithValue("@name",  processName);
        cmd.Parameters.AddWithValue("@limit", limit);
        return await ReadRecordsAsync(cmd, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConnectionRecord>> GetByRemoteIpAsync(
        string remoteIp, int limit = 200, CancellationToken ct = default)
    {
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, SessionId, Timestamp, ProcessId, ProcessName, ExecutablePath,
                   LocalIp, LocalPort, RemoteIp, RemotePort, Protocol,
                   BytesSent, BytesReceived, Duration, GeoCountry, GeoCity,
                   ReputationScore, IsBlocked
            FROM Connections
            WHERE RemoteIp = @ip
            ORDER BY Timestamp DESC
            LIMIT @limit;
            """;
        cmd.Parameters.AddWithValue("@ip",    remoteIp);
        cmd.Parameters.AddWithValue("@limit", limit);
        return await ReadRecordsAsync(cmd, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<int> DeleteOlderThanAsync(DateTimeOffset before, CancellationToken ct = default)
    {
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Connections WHERE Timestamp < @cutoff;";
        cmd.Parameters.AddWithValue("@cutoff", before.ToString("O"));
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Connections;";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AppActivitySummary>> GetAppActivityAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT ProcessName, ExecutablePath,
                   COUNT(*)         AS ConnectionCount,
                   SUM(BytesSent)   AS TotalBytesSent,
                   SUM(BytesReceived) AS TotalBytesReceived,
                   MIN(CASE WHEN ReputationScore >= 0 THEN ReputationScore ELSE 101 END) AS MinRep,
                   MAX(Timestamp)   AS LastSeen
            FROM Connections
            WHERE Timestamp >= @from AND Timestamp <= @to
            GROUP BY ProcessName
            ORDER BY TotalBytesSent DESC;
            """;
        cmd.Parameters.AddWithValue("@from", from.ToString("O"));
        cmd.Parameters.AddWithValue("@to",   to.ToString("O"));

        var list = new List<AppActivitySummary>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            int minRep = reader.GetInt32(5);
            list.Add(new AppActivitySummary
            {
                ProcessName        = reader.GetString(0),
                ExecutablePath     = reader.GetString(1),
                ConnectionCount    = reader.GetInt64(2),
                TotalBytesSent     = reader.IsDBNull(3) ? 0 : reader.GetInt64(3),
                TotalBytesReceived = reader.IsDBNull(4) ? 0 : reader.GetInt64(4),
                MinReputationScore = minRep > 100 ? -1 : minRep,
                LastSeen           = DateTimeOffset.Parse(reader.GetString(6)),
            });
        }
        return list;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AppBucketCount>> GetBucketedActivityAsync(
        DateTimeOffset from, DateTimeOffset to, int bucketCount,
        CancellationToken ct = default)
    {
        long fromEpoch     = from.ToUnixTimeSeconds();
        long toEpoch       = to.ToUnixTimeSeconds();
        long totalSeconds  = Math.Max(1, toEpoch - fromEpoch);
        long bucketSeconds = Math.Max(1, totalSeconds / bucketCount);

        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                CAST((strftime('%s', Timestamp) - @fromEpoch) / @bucketSec AS INTEGER) AS BucketIdx,
                ProcessName,
                COUNT(*) AS Cnt
            FROM Connections
            WHERE Timestamp >= @from AND Timestamp <= @to
            GROUP BY BucketIdx, ProcessName
            ORDER BY ProcessName, BucketIdx;
            """;
        cmd.Parameters.AddWithValue("@fromEpoch",  fromEpoch);
        cmd.Parameters.AddWithValue("@bucketSec",  bucketSeconds);
        cmd.Parameters.AddWithValue("@from",        from.ToString("O"));
        cmd.Parameters.AddWithValue("@to",          to.ToString("O"));

        var list = new List<AppBucketCount>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            int idx = reader.GetInt32(0);
            if (idx < 0 || idx >= bucketCount) continue; // Randfall
            list.Add(new AppBucketCount
            {
                BucketIndex = idx,
                ProcessName = reader.GetString(1),
                Count       = reader.GetInt64(2),
            });
        }
        return list;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConnectionRecord>> GetSuspiciousConnectionsAsync(
        DateTimeOffset from, DateTimeOffset to, int limit = 20,
        CancellationToken ct = default)
    {
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, SessionId, Timestamp, ProcessId, ProcessName, ExecutablePath,
                   LocalIp, LocalPort, RemoteIp, RemotePort, Protocol,
                   BytesSent, BytesReceived, Duration, GeoCountry, GeoCity,
                   ReputationScore, IsBlocked
            FROM Connections
            WHERE Timestamp >= @from AND Timestamp <= @to
              AND ReputationScore >= 0 AND ReputationScore < 50
            ORDER BY ReputationScore ASC
            LIMIT @limit;
            """;
        cmd.Parameters.AddWithValue("@from",  from.ToString("O"));
        cmd.Parameters.AddWithValue("@to",    to.ToString("O"));
        cmd.Parameters.AddWithValue("@limit", limit);
        return await ReadRecordsAsync(cmd, ct).ConfigureAwait(false);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<ConnectionRecord>> ReadRecordsAsync(
        SqliteCommand cmd, CancellationToken ct)
    {
        var list = new List<ConnectionRecord>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new ConnectionRecord
            {
                Id              = reader.GetInt64(0),
                SessionId       = reader.GetInt64(1),
                Timestamp       = DateTimeOffset.Parse(reader.GetString(2)),
                ProcessId       = reader.GetInt32(3),
                ProcessName     = reader.GetString(4),
                ExecutablePath  = reader.GetString(5),
                LocalIp         = reader.GetString(6),
                LocalPort       = reader.GetInt32(7),
                RemoteIp        = reader.GetString(8),
                RemotePort      = reader.GetInt32(9),
                Protocol        = reader.GetString(10),
                BytesSent       = reader.GetInt64(11),
                BytesReceived   = reader.GetInt64(12),
                Duration        = reader.GetDouble(13),
                GeoCountry      = reader.GetString(14),
                GeoCity         = reader.GetString(15),
                ReputationScore = reader.GetInt32(16),
                IsBlocked       = reader.GetInt32(17) != 0,
            });
        }
        return list;
    }
}
