// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Collections.Concurrent;
using System.Net;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.Core.Services;

/// <summary>
/// Thread-sicherer In-Memory-Cache für DNS-Abfragen.
/// Speichert IP→Hostname-Zuordnungen und eine FIFO-Queue der letzten 1000 Ereignisse.
/// </summary>
public sealed class DnsCache : IDnsCache
{
    // ──────────────────────────────────────────────────────────────────────
    // Konfiguration
    // ──────────────────────────────────────────────────────────────────────

    private const int MaxRecentEntries = 1000;

    // ──────────────────────────────────────────────────────────────────────
    // Speicher
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>IP-Adresse (als String) → zuletzt aufgelöster Hostname.</summary>
    private readonly ConcurrentDictionary<string, string> _ipToHostname = new();

    /// <summary>Chronologische Liste der letzten Ereignisse (neueste am Ende).</summary>
    private readonly LinkedList<DnsQueryEvent> _recentQueries = new();

    private readonly object _queriesLock = new();

    // ──────────────────────────────────────────────────────────────────────
    // IDnsCache
    // ──────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string? GetHostname(IPAddress ip) =>
        _ipToHostname.TryGetValue(ip.ToString(), out var hostname) ? hostname : null;

    /// <inheritdoc/>
    public string? GetHostname(string ip) =>
        _ipToHostname.TryGetValue(ip, out var hostname) ? hostname : null;

    /// <inheritdoc/>
    public void Record(DnsQueryEvent evt)
    {
        // IP→Hostname-Index aktualisieren
        if (!string.IsNullOrEmpty(evt.QueryName))
        {
            foreach (var ip in evt.ResolvedAddresses)
            {
                if (!string.IsNullOrEmpty(ip))
                    _ipToHostname[ip] = evt.QueryName;
            }
        }

        // FIFO-Queue aktualisieren
        lock (_queriesLock)
        {
            _recentQueries.AddLast(evt);

            // Älteste Einträge entfernen wenn Limit überschritten
            while (_recentQueries.Count > MaxRecentEntries)
                _recentQueries.RemoveFirst();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<DnsQueryEvent> GetRecentQueries(int maxCount = 500)
    {
        lock (_queriesLock)
        {
            // Neueste zuerst zurückgeben
            var result = new List<DnsQueryEvent>(Math.Min(maxCount, _recentQueries.Count));
            var node   = _recentQueries.Last;

            while (node is not null && result.Count < maxCount)
            {
                result.Add(node.Value);
                node = node.Previous;
            }

            return result;
        }
    }
}
