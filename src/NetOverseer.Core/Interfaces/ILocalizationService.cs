// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
namespace NetOverseer.Core.Interfaces;

/// <summary>
/// Dienst zur Laufzeit-Sprachumschaltung der Anwendung.
/// </summary>
public interface ILocalizationService
{
    /// <summary>Aktuell aktiver Sprach-Code (z.B. "de-DE" oder "en-US").</summary>
    string CurrentLanguageCode { get; }

    /// <summary>
    /// Ändert die Anzeigesprache der Anwendung.
    /// </summary>
    /// <param name="languageCode">BCP-47-Sprach-Tag (z.B. "de-DE" oder "en-US").</param>
    void SetLanguage(string languageCode);

    /// <summary>Wird ausgelöst nachdem die Sprache geändert wurde.</summary>
    event EventHandler<string> LanguageChanged;
}
