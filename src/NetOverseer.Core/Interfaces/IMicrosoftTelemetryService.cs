// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
namespace NetOverseer.Core.Interfaces;

/// <summary>
/// Identifiziert bekannte Windows/Microsoft-Telemetrie-Endpunkte anhand von Hostnamen.
/// </summary>
public interface IMicrosoftTelemetryService
{
    /// <summary>Prüft ob der Hostname ein bekannter Microsoft-Telemetrie-Endpunkt ist.</summary>
    bool IsTelemetryHost(string hostname);

    /// <summary>Gibt den Dienstnamen für einen Telemetrie-Hostnamen zurück (z.B. "DiagTrack", "Windows Update").</summary>
    string GetServiceName(string hostname);
}
