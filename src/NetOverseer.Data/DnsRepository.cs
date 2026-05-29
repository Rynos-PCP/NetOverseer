// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.Data;

/// <summary>
/// SQLite-Implementierung von <see cref="IDnsRepository"/>.
/// </summary>
public sealed class DnsRepository : IDnsRepository
{
    private readonly DatabaseService _db;
    private readonly ILogger<DnsRepository> _logger;

    public DnsRepository(DatabaseService db, ILogger<DnsRepository> logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task InsertBatchAsync(
        IReadOnlyList<DnsRecord> records,
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
                INSERT INTO DnsQueries
                    (Timestamp, ProcessId, ProcessName, Domain, QueryType, ResolvedIps, Category)
                VALUES
                    (@ts, @pid, @pname, @domain, @qtype, @ips, @cat);
                """;

            var pTs    = cmd.Parameters.Add("@ts",     SqliteType.Text);
            var pPid   = cmd.Parameters.Add("@pid",    SqliteType.Integer);
            var pPname = cmd.Parameters.Add("@pname",  SqliteType.Text);
            var pDom   = cmd.Parameters.Add("@domain", SqliteType.Text);
            var pQtype = cmd.Parameters.Add("@qtype",  SqliteType.Text);
            var pIps   = cmd.Parameters.Add("@ips",    SqliteType.Text);
            var pCat   = cmd.Parameters.Add("@cat",    SqliteType.Text);

            foreach (var rec in records)
            {
                pTs.Value    = rec.Timestamp.ToString("O");
                pPid.Value   = rec.ProcessId;
                pPname.Value = rec.ProcessName;
                pDom.Value   = rec.Domain;
                pQtype.Value = rec.QueryType;
                pIps.Value   = rec.ResolvedIps;
                pCat.Value   = rec.Category;
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            txn.Commit();
            _logger.LogDebug("DnsRepository: {N} DNS-Abfragen gespeichert.", records.Count);
        }
        catch
        {
            txn.Rollback();
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DnsRecord>> GetByTimeRangeAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Timestamp, ProcessId, ProcessName, Domain, QueryType, ResolvedIps, Category
            FROM DnsQueries
            WHERE Timestamp >= @from AND Timestamp <= @to
            ORDER BY Timestamp DESC;
            """;
        cmd.Parameters.AddWithValue("@from", from.ToString("O"));
        cmd.Parameters.AddWithValue("@to",   to.ToString("O"));
        return await ReadRecordsAsync(cmd, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DnsRecord>> GetByProcessAsync(
        string processName, int limit = 500, CancellationToken ct = default)
    {
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Timestamp, ProcessId, ProcessName, Domain, QueryType, ResolvedIps, Category
            FROM DnsQueries
            WHERE ProcessName = @name
            ORDER BY Timestamp DESC
            LIMIT @limit;
            """;
        cmd.Parameters.AddWithValue("@name",  processName);
        cmd.Parameters.AddWithValue("@limit", limit);
        return await ReadRecordsAsync(cmd, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DnsRecord>> GetByDomainAsync(
        string domain, int limit = 200, CancellationToken ct = default)
    {
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Timestamp, ProcessId, ProcessName, Domain, QueryType, ResolvedIps, Category
            FROM DnsQueries
            WHERE Domain = @domain
            ORDER BY Timestamp DESC
            LIMIT @limit;
            """;
        cmd.Parameters.AddWithValue("@domain", domain);
        cmd.Parameters.AddWithValue("@limit",  limit);
        return await ReadRecordsAsync(cmd, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<int> DeleteOlderThanAsync(DateTimeOffset before, CancellationToken ct = default)
    {
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM DnsQueries WHERE Timestamp < @cutoff;";
        cmd.Parameters.AddWithValue("@cutoff", before.ToString("O"));
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<DnsRecord>> ReadRecordsAsync(
        SqliteCommand cmd, CancellationToken ct)
    {
        var list = new List<DnsRecord>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new DnsRecord
            {
                Id          = reader.GetInt64(0),
                Timestamp   = DateTimeOffset.Parse(reader.GetString(1)),
                ProcessId   = reader.GetInt32(2),
                ProcessName = reader.GetString(3),
                Domain      = reader.GetString(4),
                QueryType   = reader.GetString(5),
                ResolvedIps = reader.GetString(6),
                Category    = reader.GetString(7),
            });
        }
        return list;
    }
}
