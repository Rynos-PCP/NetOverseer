// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NetOverseer.Core.Interfaces;

namespace NetOverseer.Data;

/// <summary>
/// Verwaltet die SQLite-Hauptdatenbank: Initialisierung, Migrationen und Wartung.
/// Thread-sicher durch internen SemaphoreSlim.
/// </summary>
public sealed class DatabaseService : IDatabaseService, IDisposable
{
    private readonly string _dbPath;
    private readonly ILogger<DatabaseService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _initialized;

    public string ConnectionString { get; }

    public DatabaseService(string dbPath, ILogger<DatabaseService> logger)
    {
        _dbPath  = dbPath;
        _logger  = logger;

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode       = SqliteOpenMode.ReadWriteCreate,
            Cache      = SqliteCacheMode.Private,
        }.ToString();
    }

    // ──────────────────────────────────────────────────────────────────────
    // IDatabaseService
    // ──────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            await ApplyMigrationsAsync(ct).ConfigureAwait(false);
            _initialized = true;
            _logger.LogInformation("DatabaseService: Datenbank initialisiert ({Path}).", _dbPath);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task RunMaintenanceAsync(int retentionDays, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = OpenConnection();

            if (retentionDays > 0)
            {
                var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays)
                                           .ToString("O");

                // Verbindungen löschen
                var connDel = conn.CreateCommand();
                connDel.CommandText =
                    "DELETE FROM Connections WHERE Timestamp < @cutoff;";
                connDel.Parameters.AddWithValue("@cutoff", cutoff);
                int delConn = await connDel.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

                // DNS-Abfragen löschen
                var dnsDel = conn.CreateCommand();
                dnsDel.CommandText =
                    "DELETE FROM DnsQueries WHERE Timestamp < @cutoff;";
                dnsDel.Parameters.AddWithValue("@cutoff", cutoff);
                int delDns = await dnsDel.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

                // Firewall-Ereignisse löschen
                var fwDel = conn.CreateCommand();
                fwDel.CommandText =
                    "DELETE FROM FirewallEvents WHERE Timestamp < @cutoff;";
                fwDel.Parameters.AddWithValue("@cutoff", cutoff);
                int delFw = await fwDel.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

                // Abgeschlossene Sessions ohne verbleibende Verbindungen entfernen
                var sessionsDel = conn.CreateCommand();
                sessionsDel.CommandText = """
                    DELETE FROM Sessions
                    WHERE EndTime IS NOT NULL
                      AND Id NOT IN (SELECT DISTINCT SessionId FROM Connections);
                    """;
                int delSess = await sessionsDel.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

                _logger.LogInformation(
                    "Wartung: {C} Verbindungen, {D} DNS, {F} Firewall, {S} Sessions gelöscht.",
                    delConn, delDns, delFw, delSess);
            }

            // VACUUM für Dateigröße-Kontrolle
            var vacuum = conn.CreateCommand();
            vacuum.CommandText = "VACUUM;";
            await vacuum.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            _logger.LogDebug("DatabaseService: VACUUM abgeschlossen.");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public long GetDatabaseSizeBytes()
    {
        var fi = new FileInfo(_dbPath);
        return fi.Exists ? fi.Length : 0L;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Migrations
    // ──────────────────────────────────────────────────────────────────────

    private async Task ApplyMigrationsAsync(CancellationToken ct)
    {
        using var conn = OpenConnection();

        // SchemaVersions-Tabelle sicherstellen (Bootstrap)
        using (var bootstrap = conn.CreateCommand())
        {
            bootstrap.CommandText = """
                CREATE TABLE IF NOT EXISTS SchemaVersions (
                    Version   INTEGER PRIMARY KEY,
                    AppliedAt TEXT    NOT NULL
                );
                """;
            await bootstrap.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // Höchste angewendete Version lesen
        int currentVersion;
        using (var versionCmd = conn.CreateCommand())
        {
            versionCmd.CommandText =
                "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersions;";
            currentVersion = Convert.ToInt32(
                await versionCmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }

        _logger.LogDebug("DatabaseService: Schema-Version {V}.", currentVersion);

        // Alle Migrations-Skripte aus eingebetteten Ressourcen laden
        var assembly  = typeof(DatabaseService).Assembly;
        var prefix    = "NetOverseer.Data.Migrations.";
        var resources = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(n => n)
            .ToList();

        foreach (var resourceName in resources)
        {
            // Versionsnummer aus Dateiname "0001_initial.sql" extrahieren
            var fileName = resourceName[prefix.Length..];
            if (!int.TryParse(fileName.Split('_')[0], out int version))
                continue;

            if (version <= currentVersion) continue;

            _logger.LogInformation("Wende Migration {V} an: {F}.", version, fileName);

            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            string sql       = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

            // Transaktion für atomare Migration
            using var txn = conn.BeginTransaction();
            try
            {
                using (var migCmd = conn.CreateCommand())
                {
                    migCmd.Transaction  = txn;
                    migCmd.CommandText  = sql;
                    await migCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                // Version eintragen
                using (var markCmd = conn.CreateCommand())
                {
                    markCmd.Transaction = txn;
                    markCmd.CommandText =
                        "INSERT INTO SchemaVersions(Version, AppliedAt) VALUES(@v, @a);";
                    markCmd.Parameters.AddWithValue("@v", version);
                    markCmd.Parameters.AddWithValue("@a", DateTimeOffset.UtcNow.ToString("O"));
                    await markCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                txn.Commit();
                _logger.LogInformation("Migration {V} erfolgreich angewendet.", version);
            }
            catch (Exception ex)
            {
                txn.Rollback();
                _logger.LogError(ex, "Migration {V} fehlgeschlagen – Rollback.", version);
                throw;
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    internal SqliteConnection OpenConnection()
    {
        SqliteConnection conn;
        try
        {
            conn = new SqliteConnection(ConnectionString);
            conn.Open();
        }
        catch (SqliteException ex)
        {
            _logger.LogError(ex,
                "Datenbank konnte nicht geöffnet werden ({Path}). Möglicherweise korrupt.", _dbPath);
            throw new InvalidOperationException(
                $"Die Datenbank '{_dbPath}' konnte nicht geöffnet werden. " +
                "Wenn das Problem anhält, löschen Sie die Datei und starten Sie die Anwendung neu.",
                ex);
        }

        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    public void Dispose()
    {
        // Connection-Pool leeren, damit Tests die Datei löschen können
        SqliteConnection.ClearAllPools();
        _lock.Dispose();
    }
}
