// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using NetOverseer.Core.Interfaces;

namespace NetOverseer.Core.Services;

/// <summary>
/// Identifiziert bekannte Windows/Microsoft-Telemetrie-Endpunkte anhand von Hostnamen.
/// Abgleich erfolgt exakt oder als DNS-Suffix (z.B. "sub.vortex.data.microsoft.com").
/// </summary>
public sealed class MicrosoftTelemetryService : IMicrosoftTelemetryService
{
    // Hostname (oder Suffix) → Dienst-/Kategorienname (auf Deutsch)
    private static readonly (string Suffix, string Service)[] Endpoints =
    [
        // ── DiagTrack / Connected User Experiences and Telemetry ────────────────
        ("vortex.data.microsoft.com",                "DiagTrack"),
        ("settings-win.data.microsoft.com",          "DiagTrack"),
        ("telemetry.microsoft.com",                  "DiagTrack"),
        ("oca.telemetry.microsoft.com",              "DiagTrack"),
        ("oca.microsoft.com",                        "DiagTrack"),
        ("dmd.metaservices.microsoft.com",           "DiagTrack"),
        ("browser.events.data.microsoft.com",        "DiagTrack"),
        ("events.data.microsoft.com",                "DiagTrack"),
        ("v10.events.data.microsoft.com",            "DiagTrack"),
        ("v20.events.data.microsoft.com",            "DiagTrack"),
        ("telecommand.telemetry.microsoft.com",      "DiagTrack"),

        // ── Windows Error Reporting (Watson) ─────────────────────────────────────
        ("watson.telemetry.microsoft.com",           "Windows Fehlerberichterstattung"),
        ("watson.microsoft.com",                     "Windows Fehlerberichterstattung"),
        ("wer.microsoft.com",                        "Windows Fehlerberichterstattung"),
        ("ceuswatcab01.blob.core.windows.net",       "Windows Fehlerberichterstattung"),
        ("ceuswatcab02.blob.core.windows.net",       "Windows Fehlerberichterstattung"),
        ("umwatson.events.data.microsoft.com",       "Windows Fehlerberichterstattung"),
        ("diagnostics.support.microsoft.com",        "Windows Fehlerberichterstattung"),

        // ── Windows Update ───────────────────────────────────────────────────────
        ("windowsupdate.microsoft.com",              "Windows Update"),
        ("update.microsoft.com",                     "Windows Update"),
        ("download.windowsupdate.com",               "Windows Update"),
        ("download.microsoft.com",                   "Windows Update"),
        ("fe2.update.microsoft.com",                 "Windows Update"),
        ("fe3.update.microsoft.com",                 "Windows Update"),
        ("sls.update.microsoft.com",                 "Windows Update"),
        ("statsfe2.update.microsoft.com.akadns.net", "Windows Update"),

        // ── Windows Konfiguration & Einstellungen ────────────────────────────────
        ("settings.data.microsoft.com",              "Windows Konfiguration"),
        ("time.windows.com",                         "Windows Konfiguration"),
        ("displaycatalog.mp.microsoft.com",          "Windows Konfiguration"),

        // ── Windows Defender / SmartScreen / MAPS ────────────────────────────────
        ("wdcp.microsoft.com",                       "Windows Defender"),
        ("wdcpalt.microsoft.com",                    "Windows Defender"),
        ("wd.microsoft.com",                         "Windows Defender"),
        ("smartscreen.microsoft.com",                "Windows Defender"),
        ("smartscreen-prod.microsoft.com",           "Windows Defender"),
        ("maps.windows.com",                         "Windows Defender"),

        // ── OneDrive ─────────────────────────────────────────────────────────────
        ("onedrive.live.com",                        "OneDrive"),
        ("skyapi.onedrive.live.com",                 "OneDrive"),
        ("storage.live.com",                         "OneDrive"),

        // ── Office / Microsoft 365 Telemetrie ────────────────────────────────────
        ("officeclient.microsoft.com",               "Office Telemetrie"),
        ("otelrules.azureedge.net",                  "Office Telemetrie"),
        ("nexusrules.officeapps.live.com",           "Office Telemetrie"),
        ("nexus.officeapps.live.com",                "Office Telemetrie"),
        ("templatesmetadata.office.net",             "Office Telemetrie"),

        // ── Microsoft Account / Authentifizierung ────────────────────────────────
        ("login.microsoftonline.com",                "Microsoft Account"),
        ("login.live.com",                           "Microsoft Account"),
        ("account.live.com",                         "Microsoft Account"),
    ];

    /// <inheritdoc/>
    public bool IsTelemetryHost(string hostname)
    {
        if (string.IsNullOrEmpty(hostname)) return false;

        foreach (var (suffix, _) in Endpoints)
        {
            if (hostname.Equals(suffix, StringComparison.OrdinalIgnoreCase)
                || hostname.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <inheritdoc/>
    public string GetServiceName(string hostname)
    {
        if (string.IsNullOrEmpty(hostname)) return "Microsoft";

        foreach (var (suffix, service) in Endpoints)
        {
            if (hostname.Equals(suffix, StringComparison.OrdinalIgnoreCase)
                || hostname.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
                return service;
        }
        return "Microsoft";
    }
}
