// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
namespace NetOverseer.Core.Models;

/// <summary>
/// Aggregiertes App-Profil: kumulierte Statistiken pro ausführbarer Datei.
/// Entspricht einer Zeile in der AppProfiles-Tabelle.
/// </summary>
public sealed class AppProfile
{
    public long Id { get; init; }
    public string ExecutablePath { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public long TotalBytesSent { get; init; }
    public long TotalBytesReceived { get; init; }
    public DateTimeOffset LastSeen { get; init; }
    public bool IsTrusted { get; init; }
    public bool IsBlocked { get; init; }
}
