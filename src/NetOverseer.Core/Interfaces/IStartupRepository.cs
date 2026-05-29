// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using NetOverseer.Core.Models;

namespace NetOverseer.Core.Interfaces;

/// <summary>Datenzugriff für Startup-Aufzeichnungen.</summary>
public interface IStartupRepository
{
    /// <summary>Speichert eine Sitzung samt Verbindungen und gibt die neue Session-Id zurück.</summary>
    Task<long> SaveSessionAsync(
        StartupSession session,
        IReadOnlyList<StartupConnection> connections,
        CancellationToken ct = default);

    /// <summary>Gibt die jüngste gespeicherte Sitzung zurück, oder <c>null</c> wenn keine vorhanden.</summary>
    Task<StartupSession?> GetLastSessionAsync(CancellationToken ct = default);

    /// <summary>Gibt alle Verbindungen einer Sitzung zurück (nach OffsetSeconds sortiert).</summary>
    Task<IReadOnlyList<StartupConnection>> GetConnectionsAsync(
        long sessionId,
        CancellationToken ct = default);
}
