// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using NetOverseer.Core.Models;

namespace NetOverseer.Core.Interfaces;

/// <summary>
/// Klassifiziert Domains nach ihrer Funktion (Tracker, Telemetrie, CDN, …).
/// </summary>
public interface IDnsCategoryService
{
    /// <summary>
    /// Gibt die Kategorie der übergebenen Domain zurück.
    /// Die Prüfung erfolgt über Suffix-Matching (inkl. Subdomains).
    /// </summary>
    DnsCategory Classify(string domain);
}
