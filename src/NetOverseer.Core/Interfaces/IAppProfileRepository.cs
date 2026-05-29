// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using NetOverseer.Core.Models;

namespace NetOverseer.Core.Interfaces;

/// <summary>
/// Repository für App-Profile (kumulierte Statistiken pro Prozess).
/// </summary>
public interface IAppProfileRepository
{
    /// <summary>
    /// Aktualisiert oder erstellt ein App-Profil.
    /// Akkumuliert BytesSent/BytesReceived bei bestehenden Einträgen.
    /// </summary>
    Task UpsertAsync(AppProfile profile, CancellationToken ct = default);

    /// <summary>Gibt alle App-Profile zurück, absteigend nach TotalBytesSent sortiert.</summary>
    Task<IReadOnlyList<AppProfile>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Gibt das Profil für einen bestimmten Executable-Pfad zurück, oder null.</summary>
    Task<AppProfile?> GetByPathAsync(string executablePath, CancellationToken ct = default);

    /// <summary>Setzt IsBlocked für einen Executable-Pfad.</summary>
    Task SetBlockedAsync(string executablePath, bool isBlocked, CancellationToken ct = default);

    /// <summary>Setzt IsTrusted für einen Executable-Pfad.</summary>
    Task SetTrustedAsync(string executablePath, bool isTrusted, CancellationToken ct = default);

    /// <summary>Gibt die Top-N Apps nach gesendetem Datenvolumen zurück.</summary>
    Task<IReadOnlyList<AppProfile>> GetTopByBytesSentAsync(int count, CancellationToken ct = default);
}
