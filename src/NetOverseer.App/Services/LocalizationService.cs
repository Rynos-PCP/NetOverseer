// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using NetOverseer.Core.Interfaces;
using Windows.ApplicationModel.Resources;

namespace NetOverseer.App.Services;

/// <summary>
/// Lokalisierungsdienst für unpackaged WinUI 3 Apps.
/// WinRT-APIs wie ApplicationLanguages.PrimaryLanguageOverride und
/// ResourceContext.SetGlobalQualifierValue sind in unpackaged Apps nicht
/// verfügbar (sie lösen native FAIL_FAST-Ausnahmen aus, die nicht abgefangen
/// werden können). Diese Klasse verwaltet die Sprachpräferenz daher rein
/// im Arbeitsspeicher. Sprachänderungen werden über den Neustart-Dialog
/// (MainWindow.OnLanguageChangeRequested) auf den nächsten App-Start verschoben.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    /// <inheritdoc/>
    public string CurrentLanguageCode { get; private set; }

    /// <inheritdoc/>
    public event EventHandler<string>? LanguageChanged;

    public LocalizationService()
    {
        CurrentLanguageCode = GetSystemLanguageCode();
    }

    /// <inheritdoc/>
    public void SetLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return;

        if (CurrentLanguageCode == languageCode)
            return;

        // HINWEIS: ResourceContext.SetGlobalQualifierValue und
        // ApplicationLanguages.PrimaryLanguageOverride funktionieren in
        // unpackaged Apps NICHT (native FAIL_FAST, nicht abfangbar).
        // Die Sprachpräferenz wird nur im Speicher gehalten und in den
        // Einstellungen gespeichert. Sie gilt ab dem nächsten App-Start.

        CurrentLanguageCode = languageCode;
        LanguageChanged?.Invoke(this, languageCode);
    }

    /// <summary>
    /// Lädt einen lokalisierten String aus der Resources.resw-Datei.
    /// Gibt den Schlüssel zurück wenn kein String gefunden wurde.
    /// </summary>
    public static string GetString(string resourceKey)
    {
        try
        {
            var loader = ResourceLoader.GetForViewIndependentUse();
            return loader.GetString(resourceKey) is { Length: > 0 } s ? s : resourceKey;
        }
        catch
        {
            return resourceKey;
        }
    }

    private static string GetSystemLanguageCode()
    {
        var lang = System.Globalization.CultureInfo.CurrentUICulture.Name;
        return lang.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en-US" : "de-DE";
    }
}
