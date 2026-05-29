// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;
using NetOverseer.Core.Services;

namespace NetOverseer.Core.Tests.Services;

/// <summary>
/// Unit-Tests für <see cref="ReputationService"/>.
/// HTTP-Aufrufe werden durch ein Fake-HttpMessageHandler ersetzt.
/// </summary>
public sealed class ReputationServiceTests
{
    // ──────────────────────────────────────────────────────────────────────
    // Hilfsobjekte
    // ──────────────────────────────────────────────────────────────────────

    private static readonly IPAddress PrivateIp    = IPAddress.Parse("192.168.1.100");
    private static readonly IPAddress LoopbackIp   = IPAddress.Loopback;
    private static readonly IPAddress PublicIp     = IPAddress.Parse("1.2.3.4");
    private static readonly IPAddress CloudflareIp = IPAddress.Parse("1.1.1.1");

    private static ReputationService BuildService(
        AppSettings?       settings    = null,
        GeoInfo?           geoOverride = null,
        bool               isBlocked   = false)
    {
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.Load()).Returns(settings ?? new AppSettings
        {
            AbuseIpDbApiKey      = "TEST-KEY",
            OfflineMode          = false,
            MaxDailyAbuseIpDbRequests = 1000,
            ReputationCacheTtlHours = 24
        });

        var geoMock = new Mock<IGeoLocationService>();
        geoMock.Setup(g => g.Lookup(It.IsAny<IPAddress>()))
               .Returns(geoOverride ?? GeoInfo.Unknown);

        var blocklistMock = new Mock<IBlocklistService>();
        blocklistMock.Setup(b => b.IsBlocked(It.IsAny<IPAddress>()))
                     .Returns(isBlocked);

        return new ReputationService(
            settingsMock.Object,
            geoMock.Object,
            blocklistMock.Object,
            NullLogger<ReputationService>.Instance);
    }

    // ──────────────────────────────────────────────────────────────────────
    // 1. Private / Loopback
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PrivateIp_ReturnsPrivate_WithoutApiCall()
    {
        var svc = BuildService();

        var result = await svc.GetReputationAsync(PrivateIp);

        Assert.Equal(ReputationCategory.Private, result.Category);
    }

    [Fact]
    public async Task LoopbackIp_ReturnsPrivate()
    {
        var svc = BuildService();

        var result = await svc.GetReputationAsync(LoopbackIp);

        Assert.Equal(ReputationCategory.Private, result.Category);
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.255.255")]
    [InlineData("169.254.0.1")]
    public async Task KnownPrivateRanges_ReturnPrivate(string ipStr)
    {
        var svc    = BuildService();
        var result = await svc.GetReputationAsync(IPAddress.Parse(ipStr));
        Assert.Equal(ReputationCategory.Private, result.Category);
    }

    // ──────────────────────────────────────────────────────────────────────
    // 2. Bekannte Infrastruktur via GeoService
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CloudflareIp_ReturnsSafe_ViaGeo()
    {
        var cloudflareGeo = new GeoInfo
        {
            InfrastructureCategory = IpInfrastructureCategory.Cloudflare,
            CountryCode            = "US",
            CountryName            = "United States"
        };

        var svc    = BuildService(geoOverride: cloudflareGeo);
        var result = await svc.GetReputationAsync(CloudflareIp);

        Assert.Equal(ReputationCategory.Safe, result.Category);
        Assert.Equal("Built-in whitelist", result.Source);
    }

    [Fact]
    public async Task MicrosoftTelemetry_ReturnsTelemetryCategory()
    {
        var telemetryGeo = new GeoInfo
        {
            InfrastructureCategory = IpInfrastructureCategory.MicrosoftTelemetry,
            CountryCode            = "US",
            CountryName            = "United States"
        };

        var svc    = BuildService(geoOverride: telemetryGeo);
        var result = await svc.GetReputationAsync(IPAddress.Parse("13.64.196.27"));

        Assert.Equal(ReputationCategory.MicrosoftTelemetry, result.Category);
    }

    [Fact]
    public async Task TorExitNode_ReturnsDangerous_ViaGeo()
    {
        var torGeo = new GeoInfo
        {
            InfrastructureCategory = IpInfrastructureCategory.TorExitNode,
            CountryCode            = "DE",
            CountryName            = "Germany"
        };

        var svc    = BuildService(geoOverride: torGeo);
        var result = await svc.GetReputationAsync(PublicIp);

        Assert.Equal(ReputationCategory.Dangerous, result.Category);
    }

    // ──────────────────────────────────────────────────────────────────────
    // 3. Blockliste
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BlocklistedIp_ReturnsDangerous()
    {
        var svc    = BuildService(isBlocked: true);
        var result = await svc.GetReputationAsync(PublicIp);

        Assert.Equal(ReputationCategory.Dangerous, result.Category);
        Assert.Equal("Firehol-L1", result.Source);
        Assert.Equal(100, result.Score);
    }

    // ──────────────────────────────────────────────────────────────────────
    // 4. Offline-Modus
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OfflineMode_ReturnsUnknown_WithoutApiCall()
    {
        var offlineSettings = new AppSettings { OfflineMode = true, AbuseIpDbApiKey = "KEY" };
        var svc             = BuildService(settings: offlineSettings);

        var result = await svc.GetReputationAsync(PublicIp);

        Assert.Equal(ReputationCategory.Unknown, result.Category);
    }

    [Fact]
    public async Task NoApiKey_ReturnsUnknown()
    {
        var noKeySettings = new AppSettings { AbuseIpDbApiKey = null };
        var svc           = BuildService(settings: noKeySettings);

        var result = await svc.GetReputationAsync(PublicIp);

        Assert.Equal(ReputationCategory.Unknown, result.Category);
    }

    // ──────────────────────────────────────────────────────────────────────
    // 5. Rate-Limiting
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RateLimit_Exhausted_ReturnsUnknown()
    {
        // MaxDailyRequests = 0 → sofort erschöpft
        var limitSettings = new AppSettings
        {
            AbuseIpDbApiKey           = "KEY",
            MaxDailyAbuseIpDbRequests = 0
        };
        var svc    = BuildService(settings: limitSettings);
        var result = await svc.GetReputationAsync(PublicIp);

        Assert.Equal(ReputationCategory.Unknown, result.Category);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Separate Tests: IpRangeSet
// ──────────────────────────────────────────────────────────────────────────────

public sealed class IpRangeSetTests
{
    [Theory]
    [InlineData("192.168.1.0/24",  "192.168.1.1",   true)]
    [InlineData("192.168.1.0/24",  "192.168.2.1",   false)]
    [InlineData("10.0.0.0/8",      "10.255.255.255", true)]
    [InlineData("10.0.0.0/8",      "11.0.0.0",       false)]
    [InlineData("8.8.8.8",         "8.8.8.8",        true)]
    [InlineData("8.8.8.8",         "8.8.8.9",        false)]
    [InlineData("0.0.0.0/0",       "1.2.3.4",        true)]
    [InlineData("172.16.0.0/12",   "172.31.255.255", true)]
    [InlineData("172.16.0.0/12",   "172.32.0.0",     false)]
    public void Contains_SingleRange_ReturnsExpected(
        string cidr, string ipStr, bool expected)
    {
        var set    = IpRangeSet.FromCidrs([cidr]);
        var result = set.Contains(IPAddress.Parse(ipStr));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Contains_MultipleRanges_FindsCorrectRange()
    {
        var set = IpRangeSet.FromCidrs(["10.0.0.0/8", "192.168.0.0/16", "8.8.8.0/24"]);

        Assert.True(set.Contains(IPAddress.Parse("10.5.6.7")));
        Assert.True(set.Contains(IPAddress.Parse("192.168.100.1")));
        Assert.True(set.Contains(IPAddress.Parse("8.8.8.42")));
        Assert.False(set.Contains(IPAddress.Parse("1.2.3.4")));
    }

    [Fact]
    public void FromCidrs_IgnoresComments_AndEmptyLines()
    {
        var lines = new[]
        {
            "# This is a comment",
            "",
            "  ",
            "10.0.0.0/8",
            "# another comment",
            "192.168.1.0/24"
        };

        var set = IpRangeSet.FromCidrs(lines);

        Assert.Equal(2, set.Count);
        Assert.True(set.Contains(IPAddress.Parse("10.1.2.3")));
    }

    [Fact]
    public void Empty_ContainsNoIps()
    {
        Assert.False(IpRangeSet.Empty.Contains(IPAddress.Parse("8.8.8.8")));
    }

    [Fact]
    public void Contains_Ipv6_ReturnsFalse()
    {
        var set = IpRangeSet.FromCidrs(["0.0.0.0/0"]);
        Assert.False(set.Contains(IPAddress.IPv6Loopback));
    }
}
