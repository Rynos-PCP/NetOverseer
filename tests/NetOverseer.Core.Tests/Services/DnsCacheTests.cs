// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Net;
using NetOverseer.Core.Models;
using NetOverseer.Core.Services;
using Xunit;

namespace NetOverseer.Core.Tests.Services;

public sealed class DnsCacheTests
{
    private readonly DnsCache _sut = new();

    // ──────────────────────────────────────────────────────────────────────
    // GetHostname
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetHostname_BeforeAnyRecord_ReturnsNull()
    {
        Assert.Null(_sut.GetHostname("1.2.3.4"));
        Assert.Null(_sut.GetHostname(IPAddress.Parse("1.2.3.4")));
    }

    [Fact]
    public void GetHostname_AfterRecord_ReturnsHostname()
    {
        var evt = MakeEvent("example.com", "93.184.216.34");
        _sut.Record(evt);

        Assert.Equal("example.com", _sut.GetHostname("93.184.216.34"));
        Assert.Equal("example.com", _sut.GetHostname(IPAddress.Parse("93.184.216.34")));
    }

    [Fact]
    public void GetHostname_MultipleIps_AllMapped()
    {
        var evt = MakeEvent("multi.example.com", "1.1.1.1", "2.2.2.2");
        _sut.Record(evt);

        Assert.Equal("multi.example.com", _sut.GetHostname("1.1.1.1"));
        Assert.Equal("multi.example.com", _sut.GetHostname("2.2.2.2"));
    }

    [Fact]
    public void GetHostname_LaterRecordOverwrites()
    {
        _sut.Record(MakeEvent("old.example.com", "1.2.3.4"));
        _sut.Record(MakeEvent("new.example.com", "1.2.3.4"));

        Assert.Equal("new.example.com", _sut.GetHostname("1.2.3.4"));
    }

    // ──────────────────────────────────────────────────────────────────────
    // GetRecentQueries
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetRecentQueries_Empty_ReturnsEmpty()
    {
        Assert.Empty(_sut.GetRecentQueries());
    }

    [Fact]
    public void GetRecentQueries_ReturnsNewestFirst()
    {
        var e1 = MakeEvent("first.com");
        var e2 = MakeEvent("second.com");
        var e3 = MakeEvent("third.com");

        _sut.Record(e1);
        _sut.Record(e2);
        _sut.Record(e3);

        var recent = _sut.GetRecentQueries(10);
        Assert.Equal(3, recent.Count);
        Assert.Equal("third.com",  recent[0].QueryName); // neueste zuerst
        Assert.Equal("second.com", recent[1].QueryName);
        Assert.Equal("first.com",  recent[2].QueryName);
    }

    [Fact]
    public void GetRecentQueries_RespectsMaxCount()
    {
        for (int i = 0; i < 10; i++)
            _sut.Record(MakeEvent($"domain{i}.com"));

        var recent = _sut.GetRecentQueries(3);
        Assert.Equal(3, recent.Count);
    }

    [Fact]
    public void GetRecentQueries_FifoOver1000Entries()
    {
        // 1001 Einträge → ältester wird verdrängt
        for (int i = 0; i < 1001; i++)
            _sut.Record(MakeEvent($"domain{i}.com"));

        var recent = _sut.GetRecentQueries(2000);
        Assert.Equal(1000, recent.Count);
        // Neuester ist domain1000.com
        Assert.Equal("domain1000.com", recent[0].QueryName);
        // domain0.com wurde verdrängt
        Assert.DoesNotContain(recent, q => q.QueryName == "domain0.com");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Hilfsmethoden
    // ──────────────────────────────────────────────────────────────────────

    private static DnsQueryEvent MakeEvent(string queryName, params string[] ips) =>
        new()
        {
            QueryName         = queryName,
            ResolvedAddresses = ips,
            ProcessId         = 1,
            ProcessName       = "test.exe",
            QueryType         = DnsQueryType.A,
            Category          = DnsCategory.Normal,
        };
}
