// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
namespace NetOverseer.Core.Interfaces;

/// <summary>
/// Löst Prozess-IDs (PIDs) in vollständige Prozessinformationen auf.
/// </summary>
public interface IProcessResolver
{
    /// <summary>
    /// Gibt Informationen zum Prozess mit der angegebenen PID zurück.
    /// Cached-Ergebnisse werden für 30 Sekunden vorgehalten.
    /// </summary>
    /// <param name="pid">Die Prozess-ID.</param>
    /// <returns>
    /// Prozessinformationen oder <see cref="Models.ProcessInfo.Unknown(int)"/> wenn
    /// der Prozess nicht gefunden werden kann oder bereits beendet ist.
    /// </returns>
    Models.ProcessInfo GetProcessInfo(int pid);

    /// <summary>Löscht den internen Cache (z.B. nach System-Events).</summary>
    void ClearCache();
}
