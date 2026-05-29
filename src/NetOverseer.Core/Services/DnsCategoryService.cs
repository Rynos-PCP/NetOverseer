// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.Core.Services;

/// <summary>
/// Klassifiziert Domains mithilfe von eingebetteten Suffix-Listen.
/// Prüfung per endsWith-Matching → deckt alle Subdomains ab.
/// </summary>
public sealed class DnsCategoryService : IDnsCategoryService
{
    // ──────────────────────────────────────────────────────────────────────
    // Tracker-Domains (Werbung, Tracking)
    // ──────────────────────────────────────────────────────────────────────

    private static readonly HashSet<string> TrackerSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Google Werbung / Analytics
        "doubleclick.net",
        "googlesyndication.com",
        "google-analytics.com",
        "googletagmanager.com",
        "googletagservices.com",
        "googleadservices.com",
        "googlelabs.com",
        // Facebook / Meta
        "facebook.net",
        "fbcdn.net",
        "facebook-hardware.com",
        // Comscore / Nielsen
        "scorecardresearch.com",
        "imrworldwide.com",
        // Twitter / X
        "ads-twitter.com",
        "t.co",
        // Amazon Advertising
        "amazon-adsystem.com",
        "advertising.com",
        // Other ad networks
        "criteo.com",
        "criteo.net",
        "outbrain.com",
        "taboola.com",
        "rubiconproject.com",
        "pubmatic.com",
        "openx.net",
        "casalemedia.com",
        "moatads.com",
        "mopub.com",
        "appsflyer.com",
        "adjust.com",
        "branch.io",
        "kochava.com",
        "singular.net",
        // Hotjar / FullStory
        "hotjar.com",
        "fullstory.com",
        "logrocket.com",
    };

    // ──────────────────────────────────────────────────────────────────────
    // Telemetrie-Domains (Microsoft + andere)
    // ──────────────────────────────────────────────────────────────────────

    private static readonly HashSet<string> TelemetrySuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Microsoft Telemetrie
        "vortex.data.microsoft.com",
        "settings-win.data.microsoft.com",
        "watson.telemetry.microsoft.com",
        "telemetry.microsoft.com",
        "wer.microsoft.com",
        "dmd.metaservices.microsoft.com",
        "oca.telemetry.microsoft.com",
        "oca.microsoft.com",
        "ceuswatcab01.blob.core.windows.net",
        "ceuswatcab02.blob.core.windows.net",
        // Windows Update + Diagnose
        "statsfe2.update.microsoft.com.akadns.net",
        "diagnostics.support.microsoft.com",
        // Browser-Telemetrie
        "browser.events.data.microsoft.com",
        "edge.activity.windows.com",
        // Apple Telemetrie
        "captive.apple.com",
        "metrics.apple.com",
        "icloud-content.com",
        // Valve / Steam
        "valve.net",
        // Sonstige
        "crash.crashlytics.com",
        "e.crashlytics.com",
        "settings.crashlytics.com",
        "firebaseio.com",
        "app-measurement.com",
    };

    // ──────────────────────────────────────────────────────────────────────
    // CDN-Domains
    // ──────────────────────────────────────────────────────────────────────

    private static readonly HashSet<string> CdnSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "cloudflare.com",
        "cloudflare.net",
        "fastly.net",
        "fastlylb.net",
        "akamai.net",
        "akamaized.net",
        "akamaiedge.net",
        "akamaitechnologies.com",
        "cdnjs.cloudflare.com",
        "cdn.jsdelivr.net",
        "jsdelivr.net",
        "bootstrapcdn.com",
        "unpkg.com",
        "azureedge.net",
        "azurefd.net",
        "edgekey.net",
        "llnwd.net",
        "limelight.com",
        "hwcdn.net",
        "footprint.net",
        "cdnetworks.net",
        "incapdns.net",
        "imperva.com",
    };

    // ──────────────────────────────────────────────────────────────────────
    // Bekannte verdächtige Domains (Basis-Liste – wird durch BlocklistService ergänzt)
    // ──────────────────────────────────────────────────────────────────────

    private static readonly HashSet<string> SuspiciousSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Leere Basisliste – wird durch BlocklistService ergänzt
    };

    // ──────────────────────────────────────────────────────────────────────
    // IDnsCategoryService
    // ──────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public DnsCategory Classify(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return DnsCategory.Unknown;

        domain = domain.TrimEnd('.').ToLowerInvariant();

        if (MatchesSuffix(domain, SuspiciousSuffixes)) return DnsCategory.Suspicious;
        if (MatchesSuffix(domain, TrackerSuffixes))     return DnsCategory.Tracker;
        if (MatchesSuffix(domain, TelemetrySuffixes))   return DnsCategory.Telemetry;
        if (MatchesSuffix(domain, CdnSuffixes))         return DnsCategory.Cdn;

        return DnsCategory.Normal;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Hilfsmethode
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Prüft ob <paramref name="domain"/> einem der Suffixe in <paramref name="suffixes"/> entspricht
    /// oder eine Subdomain davon ist (d.h. endet auf ".{suffix}" oder ist exakt "{suffix}").
    /// </summary>
    private static bool MatchesSuffix(string domain, HashSet<string> suffixes)
    {
        // Exakter Treffer
        if (suffixes.Contains(domain)) return true;

        // Subdomain-Prüfung: Domain muss auf ".<suffix>" enden
        foreach (var suffix in suffixes)
        {
            if (domain.EndsWith('.' + suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
