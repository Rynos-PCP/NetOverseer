// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using NetOverseer.Core.Interfaces;
using Microsoft.Windows.ApplicationModel.Resources;

namespace NetOverseer.App.Services;

/// <summary>
/// Lokalisierungsdienst für unpackaged WinUI 3 Apps.
/// Die Sprache wird über Microsoft.Windows.Globalization.ApplicationLanguages
/// (WinApp SDK) gesteuert – diese Variante funktioniert ab WinAppSDK 1.6 auch in
/// unpackaged Apps (im Gegensatz zur WinRT-API Windows.Globalization, die eine
/// Paketidentität voraussetzt). Bereits geladene Seiten aktualisieren ihre
/// x:Uid-Texte nicht automatisch; die vollständige Umstellung wird daher über den
/// Neustart-Dialog (MainWindow.OnLanguageChangeRequested) auf den nächsten
/// App-Start verschoben.
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

        // Microsoft.Windows.Globalization.ApplicationLanguages (WinApp SDK) steuert
        // die MRT-/x:Uid-Auflösung und funktioniert – anders als die WinRT-Variante
        // Windows.Globalization.ApplicationLanguages – auch in unpackaged Apps
        // (ab WinAppSDK 1.6). Bereits geladene Seiten werden NICHT automatisch
        // aktualisiert; deshalb verschiebt MainWindow.OnLanguageChangeRequested die
        // vollständige Umstellung per Neustart-Dialog auf den nächsten App-Start.
        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = languageCode;
        }
        catch
        {
            // Best-Effort – die Präferenz wird zusätzlich in den Einstellungen
            // gespeichert und beim nächsten Start in App.ApplySavedLanguage angewendet.
        }

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
            // Microsoft.Windows.ApplicationModel.Resources.ResourceLoader (WinApp SDK /
            // MRT Core) funktioniert in unpackaged Apps. Die WinRT-Variante
            // Windows.ApplicationModel.Resources.ResourceLoader benötigt eine
            // Paketidentität und löst sonst einen nicht abfangbaren Fail-Fast aus.
            var loader = new ResourceLoader();
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
        // Englisch ist Standard/Fallback – nur explizit deutsche Systeme nutzen de-DE.
        return lang.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? "de-DE" : "en-US";
    }
}
