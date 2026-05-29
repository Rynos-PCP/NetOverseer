-- Migration 0001: Initiales Schema für die NetOverseer-Hauptdatenbank
-- Wird beim ersten Start automatisch angewendet.

PRAGMA foreign_keys = ON;

-- Versionstabelle für das Migration-System
CREATE TABLE IF NOT EXISTS SchemaVersions (
    Version   INTEGER PRIMARY KEY,
    AppliedAt TEXT    NOT NULL
);

-- Überwachungs-Sitzungen (eine pro App-Laufzeit)
CREATE TABLE IF NOT EXISTS Sessions (
    Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
    StartTime             TEXT    NOT NULL,
    EndTime               TEXT,
    TotalConnections      INTEGER NOT NULL DEFAULT 0,
    TotalBytesTransferred INTEGER NOT NULL DEFAULT 0
);

-- Persistierte Netzwerkverbindungen
CREATE TABLE IF NOT EXISTS Connections (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId       INTEGER NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
    Timestamp       TEXT    NOT NULL,
    ProcessId       INTEGER NOT NULL,
    ProcessName     TEXT    NOT NULL,
    ExecutablePath  TEXT    NOT NULL DEFAULT '',
    LocalIp         TEXT    NOT NULL,
    LocalPort       INTEGER NOT NULL,
    RemoteIp        TEXT    NOT NULL,
    RemotePort      INTEGER NOT NULL,
    Protocol        TEXT    NOT NULL,
    BytesSent       INTEGER NOT NULL DEFAULT 0,
    BytesReceived   INTEGER NOT NULL DEFAULT 0,
    Duration        REAL    NOT NULL DEFAULT 0.0,
    GeoCountry      TEXT    NOT NULL DEFAULT '',
    GeoCity         TEXT    NOT NULL DEFAULT '',
    ReputationScore INTEGER NOT NULL DEFAULT -1,
    IsBlocked       INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_conn_timestamp  ON Connections(Timestamp);
CREATE INDEX IF NOT EXISTS idx_conn_process    ON Connections(ProcessName);
CREATE INDEX IF NOT EXISTS idx_conn_remote_ip  ON Connections(RemoteIp);
CREATE INDEX IF NOT EXISTS idx_conn_session    ON Connections(SessionId);

-- Persistierte DNS-Abfragen
CREATE TABLE IF NOT EXISTS DnsQueries (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Timestamp   TEXT    NOT NULL,
    ProcessId   INTEGER NOT NULL,
    ProcessName TEXT    NOT NULL,
    Domain      TEXT    NOT NULL,
    QueryType   TEXT    NOT NULL,
    ResolvedIps TEXT    NOT NULL DEFAULT '',
    Category    TEXT    NOT NULL DEFAULT 'Unknown'
);

CREATE INDEX IF NOT EXISTS idx_dns_timestamp ON DnsQueries(Timestamp);
CREATE INDEX IF NOT EXISTS idx_dns_process   ON DnsQueries(ProcessName);
CREATE INDEX IF NOT EXISTS idx_dns_domain    ON DnsQueries(Domain);

-- Firewall-Ereignisse (Regeln die durch die App erstellt wurden)
CREATE TABLE IF NOT EXISTS FirewallEvents (
    Id                   INTEGER PRIMARY KEY AUTOINCREMENT,
    Timestamp            TEXT    NOT NULL,
    RuleName             TEXT    NOT NULL,
    Action               TEXT    NOT NULL,
    TriggeringConnection TEXT    NOT NULL DEFAULT ''
);

CREATE INDEX IF NOT EXISTS idx_fw_timestamp ON FirewallEvents(Timestamp);

-- App-Profile (kumulierte Statistiken pro ausführbarer Datei)
CREATE TABLE IF NOT EXISTS AppProfiles (
    Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    ExecutablePath     TEXT    NOT NULL UNIQUE,
    DisplayName        TEXT    NOT NULL DEFAULT '',
    TotalBytesSent     INTEGER NOT NULL DEFAULT 0,
    TotalBytesReceived INTEGER NOT NULL DEFAULT 0,
    LastSeen           TEXT    NOT NULL,
    IsTrusted          INTEGER NOT NULL DEFAULT 0,
    IsBlocked          INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_app_path      ON AppProfiles(ExecutablePath);
CREATE INDEX IF NOT EXISTS idx_app_bytes     ON AppProfiles(TotalBytesSent DESC);
