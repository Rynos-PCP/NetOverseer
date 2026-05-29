// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
namespace NetOverseer.Core.Interfaces;

/// <summary>Verwaltet den Autostart-Eintrag für die Startup-Aufzeichnung.</summary>
public interface IStartupInstallerService
{
    /// <summary>True wenn der NetOverseer-Bootmonitor-Task installiert ist
    /// (läuft als SYSTEM bereits beim Systemstart – vor der Benutzeranmeldung).</summary>
    bool IsInstalled { get; }

    /// <summary>Legt einen ONSTART/SYSTEM-Task an, der die Aufzeichnung vor dem Login startet.</summary>
    Task InstallAsync();

    /// <summary>Entfernt den Bootmonitor-Task aus dem Task Scheduler.</summary>
    Task UninstallAsync();

    /// <summary>Startet den Bootmonitor-Task sofort (für manuelle Tests ohne Reboot).
    /// Gibt true zurück, wenn schtasks /Run erfolgreich war.</summary>
    Task<bool> RunNowAsync();
}
