// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using NetOverseer.Core.Models;
using NetOverseer.Core.Services;
using Xunit;

namespace NetOverseer.Core.Tests.Services;

public sealed class DnsCategoryServiceTests
{
    private readonly DnsCategoryService _sut = new();

    // ──────────────────────────────────────────────────────────────────────
    // Tracker
    // ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("doubleclick.net")]
    [InlineData("googlesyndication.com")]
    [InlineData("google-analytics.com")]
    [InlineData("scorecardresearch.com")]
    [InlineData("criteo.com")]
    public void Classify_KnownTrackerDomain_ReturnsTracker(string domain)
    {
        Assert.Equal(DnsCategory.Tracker, _sut.Classify(domain));
    }

    [Theory]
    [InlineData("ad.doubleclick.net")]
    [InlineData("sub.google-analytics.com")]
    [InlineData("www.criteo.com")]
    public void Classify_TrackerSubdomain_ReturnsTracker(string domain)
    {
        Assert.Equal(DnsCategory.Tracker, _sut.Classify(domain));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Telemetrie
    // ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("vortex.data.microsoft.com")]
    [InlineData("watson.telemetry.microsoft.com")]
    [InlineData("telemetry.microsoft.com")]
    [InlineData("app-measurement.com")]
    public void Classify_KnownTelemetryDomain_ReturnsTelemetry(string domain)
    {
        Assert.Equal(DnsCategory.Telemetry, _sut.Classify(domain));
    }

    [Theory]
    [InlineData("sub.app-measurement.com")]
    public void Classify_TelemetrySubdomain_ReturnsTelemetry(string domain)
    {
        Assert.Equal(DnsCategory.Telemetry, _sut.Classify(domain));
    }

    // ──────────────────────────────────────────────────────────────────────
    // CDN
    // ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("cloudflare.com")]
    [InlineData("fastly.net")]
    [InlineData("akamaized.net")]
    [InlineData("jsdelivr.net")]
    public void Classify_KnownCdnDomain_ReturnsCdn(string domain)
    {
        Assert.Equal(DnsCategory.Cdn, _sut.Classify(domain));
    }

    [Theory]
    [InlineData("cdn.jsdelivr.net")]
    [InlineData("assets.akamaized.net")]
    public void Classify_CdnSubdomain_ReturnsCdn(string domain)
    {
        Assert.Equal(DnsCategory.Cdn, _sut.Classify(domain));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Normal
    // ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("example.com")]
    [InlineData("github.com")]
    [InlineData("microsoft.com")]
    [InlineData("stackoverflow.com")]
    [InlineData("localhost")]
    public void Classify_NormalDomain_ReturnsNormal(string domain)
    {
        Assert.Equal(DnsCategory.Normal, _sut.Classify(domain));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Randfälle
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Classify_EmptyString_ReturnsUnknown()
    {
        Assert.Equal(DnsCategory.Unknown, _sut.Classify(string.Empty));
    }

    [Fact]
    public void Classify_WhitespaceOnly_ReturnsUnknown()
    {
        Assert.Equal(DnsCategory.Unknown, _sut.Classify("   "));
    }

    [Fact]
    public void Classify_DomainWithTrailingDot_Normalized()
    {
        // FQDN mit abschließendem Punkt → wird normalisiert
        Assert.Equal(DnsCategory.Tracker, _sut.Classify("doubleclick.net."));
    }

    [Fact]
    public void Classify_DomainIsCaseInsensitive()
    {
        Assert.Equal(DnsCategory.Tracker,   _sut.Classify("DOUBLECLICK.NET"));
        Assert.Equal(DnsCategory.Telemetry, _sut.Classify("Telemetry.Microsoft.COM"));
    }

    [Fact]
    public void Classify_SuffixMustBeFullSegment()
    {
        // "notdoubleclick.net" darf NICHT als Tracker gelten
        Assert.NotEqual(DnsCategory.Tracker, _sut.Classify("notdoubleclick.net"));
    }
}
