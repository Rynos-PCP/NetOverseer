// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
namespace NetOverseer.Core.Models;

/// <summary>
/// Kategorisierung eines Prozesses nach Herkunft/Typ.
/// </summary>
public enum ProcessCategory
{
    /// <summary>Windows-Kernelprozess (System, Idle, smss.exe).</summary>
    System,
    /// <summary>Windows-Dienst (svchost.exe mit identifiziertem Service).</summary>
    WindowsService,
    /// <summary>Benutzerinstallierte Anwendung.</summary>
    UserApp,
    /// <summary>Typ konnte nicht ermittelt werden.</summary>
    Unknown
}

/// <summary>
/// Enthält alle aufgelösten Informationen zu einem Prozess (PID → App-Details).
/// </summary>
public sealed class ProcessInfo
{
    /// <summary>Prozess-ID.</summary>
    public int Pid { get; init; }

    /// <summary>Roher Prozessname aus dem System (z.B. "svchost.exe").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Anzeigename für die UI (z.B. "Windows Update (svchost)").</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Vollständiger Pfad zur ausführbaren Datei.</summary>
    public string ExecutablePath { get; init; } = string.Empty;

    /// <summary>Prozess-Icon als PNG-Byte-Array (kann null sein).</summary>
    public byte[]? IconPng { get; init; }

    /// <summary>Gibt an, ob es sich um einen Windows-Systemprozess handelt.</summary>
    public bool IsSystemProcess { get; init; }

    /// <summary>Herausgeber/Publisher der Anwendung (aus Versionsinformationen).</summary>
    public string Publisher { get; init; } = string.Empty;

    /// <summary>Kategorisierung des Prozesses.</summary>
    public ProcessCategory Category { get; init; }

    /// <summary>Windows-Dienstname für svchost.exe-Prozesse.</summary>
    public string? ServiceName { get; init; }

    /// <summary>Zeitpunkt der Auflösung (für Cache-Invalidierung).</summary>
    public DateTimeOffset ResolvedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Singleton für unbekannte/nicht auflösbare Prozesse.</summary>
    public static ProcessInfo Unknown(int pid) => new()
    {
        Pid = pid,
        Name = "Unknown",
        DisplayName = $"Unknown (PID {pid})",
        Category = ProcessCategory.Unknown
    };
}
