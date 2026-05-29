// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
namespace NetOverseer.Core.Interfaces;

/// <summary>
/// Sendet Toast-Benachrichtigungen an den Benutzer.
/// </summary>
public interface INotificationService
{
    /// <summary>Allgemeine Info-Notification.</summary>
    void ShowInfo(string title, string body);

    /// <summary>Warnung mit höherer Aufmerksamkeit.</summary>
    void ShowWarning(string title, string body);

    /// <summary>Spezial-Variante für Reputations-Alerts inklusive Score.</summary>
    void ShowReputationAlert(string ip, int score, string body);
}
