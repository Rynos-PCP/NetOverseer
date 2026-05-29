// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.Data;

/// <summary>
/// SQLite-Implementierung von <see cref="IAppProfileRepository"/>.
/// UPSERT akkumuliert Bytes-Statistiken bei bestehenden Einträgen.
/// </summary>
public sealed class AppProfileRepository : IAppProfileRepository
{
    private readonly DatabaseService _db;
    private readonly ILogger<AppProfileRepository> _logger;

    public AppProfileRepository(DatabaseService db, ILogger<AppProfileRepository> logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(AppProfile profile, CancellationToken ct = default)
    {
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();

        // INSERT ... ON CONFLICT: Bytes akkumulieren, LastSeen aktualisieren
        cmd.CommandText = """
            INSERT INTO AppProfiles
                (ExecutablePath, DisplayName, TotalBytesSent, TotalBytesReceived, LastSeen, IsTrusted, IsBlocked)
            VALUES
                (@path, @name, @sent, @recv, @seen, @trusted, @blocked)
            ON CONFLICT(ExecutablePath) DO UPDATE SET
                DisplayName        = excluded.DisplayName,
                TotalBytesSent     = TotalBytesSent     + excluded.TotalBytesSent,
                TotalBytesReceived = TotalBytesReceived + excluded.TotalBytesReceived,
                LastSeen           = excluded.LastSeen
            WHERE excluded.LastSeen > AppProfiles.LastSeen;
            """;

        cmd.Parameters.AddWithValue("@path",    profile.ExecutablePath);
        cmd.Parameters.AddWithValue("@name",    profile.DisplayName);
        cmd.Parameters.AddWithValue("@sent",    profile.TotalBytesSent);
        cmd.Parameters.AddWithValue("@recv",    profile.TotalBytesReceived);
        cmd.Parameters.AddWithValue("@seen",    profile.LastSeen.ToString("O"));
        cmd.Parameters.AddWithValue("@trusted", profile.IsTrusted ? 1 : 0);
        cmd.Parameters.AddWithValue("@blocked", profile.IsBlocked ? 1 : 0);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AppProfile>> GetAllAsync(CancellationToken ct = default)
    {
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, ExecutablePath, DisplayName, TotalBytesSent, TotalBytesReceived,
                   LastSeen, IsTrusted, IsBlocked
            FROM AppProfiles
            ORDER BY TotalBytesSent DESC;
            """;
        return await ReadProfilesAsync(cmd, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<AppProfile?> GetByPathAsync(
        string executablePath, CancellationToken ct = default)
    {
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, ExecutablePath, DisplayName, TotalBytesSent, TotalBytesReceived,
                   LastSeen, IsTrusted, IsBlocked
            FROM AppProfiles
            WHERE ExecutablePath = @path
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@path", executablePath);
        var list = await ReadProfilesAsync(cmd, ct).ConfigureAwait(false);
        return list.Count > 0 ? list[0] : null;
    }

    /// <inheritdoc/>
    public async Task SetBlockedAsync(
        string executablePath, bool isBlocked, CancellationToken ct = default)
    {
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = "UPDATE AppProfiles SET IsBlocked = @v WHERE ExecutablePath = @path;";
        cmd.Parameters.AddWithValue("@v",    isBlocked ? 1 : 0);
        cmd.Parameters.AddWithValue("@path", executablePath);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SetTrustedAsync(
        string executablePath, bool isTrusted, CancellationToken ct = default)
    {
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = "UPDATE AppProfiles SET IsTrusted = @v WHERE ExecutablePath = @path;";
        cmd.Parameters.AddWithValue("@v",    isTrusted ? 1 : 0);
        cmd.Parameters.AddWithValue("@path", executablePath);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AppProfile>> GetTopByBytesSentAsync(
        int count, CancellationToken ct = default)
    {
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, ExecutablePath, DisplayName, TotalBytesSent, TotalBytesReceived,
                   LastSeen, IsTrusted, IsBlocked
            FROM AppProfiles
            ORDER BY TotalBytesSent DESC
            LIMIT @count;
            """;
        cmd.Parameters.AddWithValue("@count", count);
        return await ReadProfilesAsync(cmd, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<AppProfile>> ReadProfilesAsync(
        SqliteCommand cmd, CancellationToken ct)
    {
        var list = new List<AppProfile>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new AppProfile
            {
                Id                 = reader.GetInt64(0),
                ExecutablePath     = reader.GetString(1),
                DisplayName        = reader.GetString(2),
                TotalBytesSent     = reader.GetInt64(3),
                TotalBytesReceived = reader.GetInt64(4),
                LastSeen           = DateTimeOffset.Parse(reader.GetString(5)),
                IsTrusted          = reader.GetInt32(6) != 0,
                IsBlocked          = reader.GetInt32(7) != 0,
            });
        }
        return list;
    }
}
