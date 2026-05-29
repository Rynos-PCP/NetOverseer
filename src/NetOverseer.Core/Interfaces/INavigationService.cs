// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
namespace NetOverseer.Core.Interfaces;

/// <summary>
/// Abstrahiert die Seiten-Navigation der Anwendung.
/// Implementiert in NetOverseer.App – kein WinUI-Typen im Interface.
/// </summary>
public interface INavigationService
{
    /// <summary>Gibt an ob Zurück-Navigation möglich ist.</summary>
    bool CanGoBack { get; }

    /// <summary>Der Schlüssel der aktuell angezeigten Seite.</summary>
    string? CurrentPageKey { get; }

    /// <summary>Navigiert zur Seite mit dem gegebenen Schlüssel.</summary>
    /// <param name="pageKey">Einer der <see cref="PageKeys"/>-Konstanten.</param>
    /// <param name="parameter">Optionaler Navigationsparameter.</param>
    /// <returns><c>true</c> wenn die Navigation erfolgreich war.</returns>
    bool NavigateTo(string pageKey, object? parameter = null);

    /// <summary>Navigiert zur vorherigen Seite im Back-Stack.</summary>
    bool GoBack();
}
