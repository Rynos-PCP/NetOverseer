// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Net;
using System.Net.Sockets;

namespace NetOverseer.Core.Services;

/// <summary>
/// Unveränderliche Menge von IPv4-Bereichen.
/// Unterstützt O(log n) Membership-Tests über binäre Suche auf einem sortierten Array.
/// </summary>
internal sealed class IpRangeSet
{
    private readonly uint[] _starts;
    private readonly uint[] _ends;

    /// <summary>Leere Menge (enthält keine IPs).</summary>
    public static readonly IpRangeSet Empty = new([]);

    /// <summary>Anzahl der enthaltenen Bereiche.</summary>
    public int Count => _starts.Length;

    private IpRangeSet(IEnumerable<(uint start, uint end)> ranges)
    {
        var sorted = ranges
            .OrderBy(r => r.start)
            .ToArray();

        _starts = sorted.Select(r => r.start).ToArray();
        _ends   = sorted.Select(r => r.end).ToArray();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Factory
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Erzeugt einen <see cref="IpRangeSet"/> aus einer Liste von CIDR-Notationen oder
    /// einzelnen IP-Adressen (z.B. "192.168.1.0/24" oder "10.0.0.1").
    /// Ungültige Einträge werden übersprungen.
    /// </summary>
    public static IpRangeSet FromCidrs(IEnumerable<string> cidrs)
    {
        var ranges = new List<(uint, uint)>();
        foreach (var cidr in cidrs)
        {
            var trimmed = cidr.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            if (TryParseCidr(trimmed, out var range))
                ranges.Add(range);
        }
        return new IpRangeSet(ranges);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Lookup
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gibt zurück, ob die angegebene IPv4-Adresse in einem der Bereiche liegt.
    /// IPv6-Adressen geben immer false zurück.
    /// </summary>
    public bool Contains(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork || _starts.Length == 0)
            return false;

        var addr = IpToUint(ip);

        // Binäre Suche nach dem letzten Start-Wert <= addr
        int lo = 0, hi = _starts.Length - 1, idx = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (_starts[mid] <= addr) { idx = mid; lo = mid + 1; }
            else                       hi = mid - 1;
        }

        return idx >= 0 && addr <= _ends[idx];
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static bool TryParseCidr(string cidr, out (uint start, uint end) range)
    {
        range = default;
        try
        {
            var slashIdx = cidr.IndexOf('/');

            if (slashIdx < 0)
            {
                if (!IPAddress.TryParse(cidr, out var single) ||
                    single.AddressFamily != AddressFamily.InterNetwork)
                    return false;
                var u = IpToUint(single);
                range = (u, u);
                return true;
            }

            if (!IPAddress.TryParse(cidr[..slashIdx], out var ip) ||
                ip.AddressFamily != AddressFamily.InterNetwork)
                return false;

            if (!int.TryParse(cidr[(slashIdx + 1)..], out int prefix) ||
                prefix < 0 || prefix > 32)
                return false;

            uint baseAddr = IpToUint(ip);

            if (prefix == 0)  { range = (0u, 0xFFFF_FFFFu); return true; }
            if (prefix == 32) { range = (baseAddr, baseAddr); return true; }

            uint mask  = ~((1u << (32 - prefix)) - 1u);
            uint start = baseAddr & mask;
            uint end   = start | ~mask;
            range = (start, end);
            return true;
        }
        catch { return false; }
    }

    internal static uint IpToUint(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }
}
