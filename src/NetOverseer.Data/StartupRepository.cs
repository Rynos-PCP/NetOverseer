// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.Data;

/// <summary>
/// SQLite-basierte Persistenz für Startup-Aufzeichnungen.
/// Thread-sicher durch einen internen SemaphoreSlim.
/// </summary>
public sealed class StartupRepository : IStartupRepository, IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<StartupRepository> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public StartupRepository(string dbPath, ILogger<StartupRepository> logger)
    {
        _logger = logger;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode       = SqliteOpenMode.ReadWriteCreate,
            Cache      = SqliteCacheMode.Private,
        }.ToString();
        EnsureSchema();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Schema
    // ──────────────────────────────────────────────────────────────────────

    private void EnsureSchema()
    {
        using var conn = OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA foreign_keys=ON;

            CREATE TABLE IF NOT EXISTS StartupSessions (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                BootTime        TEXT    NOT NULL,
                RecordingStart  TEXT    NOT NULL,
                RecordingEnd    TEXT    NOT NULL,
                ConnectionCount INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS StartupConnections (
                Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId     INTEGER NOT NULL REFERENCES StartupSessions(Id) ON DELETE CASCADE,
                Timestamp     TEXT    NOT NULL,
                OffsetSeconds REAL    NOT NULL,
                ProcessId     INTEGER NOT NULL,
                ProcessName   TEXT    NOT NULL,
                LocalIp       TEXT    NOT NULL,
                LocalPort     INTEGER NOT NULL,
                RemoteIp      TEXT    NOT NULL,
                RemotePort    INTEGER NOT NULL,
                Protocol      TEXT    NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_startup_conn_session
                ON StartupConnections(SessionId, OffsetSeconds);
            """;
        cmd.ExecuteNonQuery();
        _logger.LogDebug("StartupRepository: Schema sichergestellt.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<long> SaveSessionAsync(
        StartupSession session,
        IReadOnlyList<StartupConnection> connections,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = OpenConnection();
            using var tx   = conn.BeginTransaction();

            // Insert session
            using var insSession = conn.CreateCommand();
            insSession.Transaction  = tx;
            insSession.CommandText  = """
                INSERT INTO StartupSessions (BootTime, RecordingStart, RecordingEnd, ConnectionCount)
                VALUES ($boot, $start, $end, $count);
                SELECT last_insert_rowid();
                """;
            insSession.Parameters.AddWithValue("$boot",  Iso(session.BootTime));
            insSession.Parameters.AddWithValue("$start", Iso(session.RecordingStart));
            insSession.Parameters.AddWithValue("$end",   Iso(session.RecordingEnd));
            insSession.Parameters.AddWithValue("$count", connections.Count);

            var sessionId = (long)(await insSession.ExecuteScalarAsync(ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Session-INSERT lieferte keine ID."));

            // Bulk-insert connections
            using var insConn = conn.CreateCommand();
            insConn.Transaction = tx;
            insConn.CommandText = """
                INSERT INTO StartupConnections
                    (SessionId, Timestamp, OffsetSeconds, ProcessId, ProcessName,
                     LocalIp, LocalPort, RemoteIp, RemotePort, Protocol)
                VALUES
                    ($sid, $ts, $off, $pid, $pname, $lip, $lport, $rip, $rport, $proto);
                """;
            var pSid   = insConn.Parameters.Add("$sid",   SqliteType.Integer);
            var pTs    = insConn.Parameters.Add("$ts",    SqliteType.Text);
            var pOff   = insConn.Parameters.Add("$off",   SqliteType.Real);
            var pPid   = insConn.Parameters.Add("$pid",   SqliteType.Integer);
            var pPname = insConn.Parameters.Add("$pname", SqliteType.Text);
            var pLip   = insConn.Parameters.Add("$lip",   SqliteType.Text);
            var pLport = insConn.Parameters.Add("$lport", SqliteType.Integer);
            var pRip   = insConn.Parameters.Add("$rip",   SqliteType.Text);
            var pRport = insConn.Parameters.Add("$rport", SqliteType.Integer);
            var pProto = insConn.Parameters.Add("$proto", SqliteType.Text);

            pSid.Value = sessionId;
            foreach (var c in connections)
            {
                pTs.Value    = Iso(c.Timestamp);
                pOff.Value   = c.OffsetSeconds;
                pPid.Value   = c.ProcessId;
                pPname.Value = c.ProcessName;
                pLip.Value   = c.LocalIp;
                pLport.Value = c.LocalPort;
                pRip.Value   = c.RemoteIp;
                pRport.Value = c.RemotePort;
                pProto.Value = c.Protocol;
                await insConn.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            tx.Commit();
            _logger.LogInformation(
                "Startup-Session {Id} gespeichert ({Count} Verbindungen).", sessionId, connections.Count);
            return sessionId;
        }
        finally { _lock.Release(); }
    }

    /// <inheritdoc/>
    public async Task<StartupSession?> GetLastSessionAsync(CancellationToken ct = default)
    {
        using var conn = OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText =
            "SELECT Id, BootTime, RecordingStart, RecordingEnd, ConnectionCount " +
            "FROM StartupSessions ORDER BY Id DESC LIMIT 1;";

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return reader.Read() ? MapSession(reader) : null;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<StartupConnection>> GetConnectionsAsync(
        long sessionId, CancellationToken ct = default)
    {
        using var conn = OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText =
            "SELECT Id, SessionId, Timestamp, OffsetSeconds, ProcessId, ProcessName, " +
            "LocalIp, LocalPort, RemoteIp, RemotePort, Protocol " +
            "FROM StartupConnections WHERE SessionId = $sid ORDER BY OffsetSeconds;";
        cmd.Parameters.AddWithValue("$sid", sessionId);

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var list = new List<StartupConnection>();
        while (reader.Read())
            list.Add(MapConnection(reader));
        return list;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private static string Iso(DateTime dt) =>
        dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTime ParseIso(string s) =>
        DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static StartupSession MapSession(SqliteDataReader r) => new()
    {
        Id              = r.GetInt64(0),
        BootTime        = ParseIso(r.GetString(1)),
        RecordingStart  = ParseIso(r.GetString(2)),
        RecordingEnd    = ParseIso(r.GetString(3)),
        ConnectionCount = r.GetInt32(4),
    };

    private static StartupConnection MapConnection(SqliteDataReader r) => new()
    {
        Id            = r.GetInt64(0),
        SessionId     = r.GetInt64(1),
        Timestamp     = ParseIso(r.GetString(2)),
        OffsetSeconds = r.GetDouble(3),
        ProcessId     = r.GetInt32(4),
        ProcessName   = r.GetString(5),
        LocalIp       = r.GetString(6),
        LocalPort     = r.GetInt32(7),
        RemoteIp      = r.GetString(8),
        RemotePort    = r.GetInt32(9),
        Protocol      = r.GetString(10),
    };

    public void Dispose()
    {
        // Verbindungspool leeren damit temporäre DB-Dateien gelöscht werden können
        using (var conn = new SqliteConnection(_connectionString))
            SqliteConnection.ClearPool(conn);
        _lock.Dispose();
    }
}
