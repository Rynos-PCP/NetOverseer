// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using NetOverseer.Core.Models;

namespace NetOverseer.Core.Interfaces;

/// <summary>
/// Liest und schreibt die Anwendungseinstellungen aus dem lokalen Dateisystem.
/// </summary>
public interface ISettingsService
{
    /// <summary>Gibt die aktuellen Einstellungen zurück (cached nach erstem Laden).</summary>
    AppSettings Load();

    /// <summary>Speichert die geänderten Einstellungen dauerhaft.</summary>
    void Save(AppSettings settings);

    /// <summary>Invalidiert den In-Memory-Cache, so dass beim nächsten <see cref="Load"/> neu gelesen wird.</summary>
    void Invalidate();

    /// <summary>Gibt den AbuseIPDB API-Key zurück (null wenn nicht gesetzt).</summary>
    string? GetAbuseIpDbApiKey();

    /// <summary>Speichert den AbuseIPDB API-Key.</summary>
    void SetAbuseIpDbApiKey(string? key);

    /// <summary>Gibt den MaxMind GeoLite2 Lizenz-Key zurück.</summary>
    string? GetMaxMindLicenseKey();

    /// <summary>Speichert den MaxMind Lizenz-Key.</summary>
    void SetMaxMindLicenseKey(string? key);

    /// <summary>
    /// Wird ausgelöst nachdem Einstellungen erfolgreich auf Datenträger geschrieben wurden.
    /// Konsumenten (z. B. Capture-Bootstrapper) können hier auf Änderungen reagieren.
    /// </summary>
    event EventHandler<AppSettings>? SettingsSaved;
}
