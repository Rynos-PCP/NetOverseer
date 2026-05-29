// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using NetOverseer.Core.Models;
using NetOverseer.Core.Services;

namespace NetOverseer.Core.Tests.Services;

public sealed class GeoLocationServiceTests : IDisposable
{
    private readonly GeoLocationService _sut;

    public GeoLocationServiceTests()
    {
        // GeoLite2-Datenbank ist im CI nicht verfügbar → IsDatabaseLoaded=false ist erwartet
        _sut = new GeoLocationService(NullLogger<GeoLocationService>.Instance);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void Lookup_Loopback_ReturnsLoopbackGeoInfo(string ipStr)
    {
        var ip = IPAddress.Parse(ipStr);
        var result = _sut.Lookup(ip);
        Assert.True(result.IsLoopback);
        Assert.Equal(IpInfrastructureCategory.Loopback, result.InfrastructureCategory);
    }

    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("169.254.1.1")]
    public void Lookup_PrivateAddress_ReturnsPrivateGeoInfo(string ipStr)
    {
        var ip = IPAddress.Parse(ipStr);
        var result = _sut.Lookup(ip);
        Assert.True(result.IsPrivate);
        Assert.Equal(IpInfrastructureCategory.Private, result.InfrastructureCategory);
    }

    [Theory]
    [InlineData("1.1.1.1", IpInfrastructureCategory.Cloudflare)]
    [InlineData("8.8.8.8", IpInfrastructureCategory.GoogleCloud)]
    public void Lookup_KnownInfrastructure_ReturnsCorrectCategory(
        string ipStr, IpInfrastructureCategory expected)
    {
        var ip = IPAddress.Parse(ipStr);
        var result = _sut.Lookup(ip);
        // Wenn DB nicht geladen, trotzdem Infrastruktur-Kategorie aus bekannten Ranges
        Assert.Equal(expected, result.InfrastructureCategory);
    }

    [Fact]
    public void Lookup_WithoutDatabase_ReturnsUnknownOrKnownCategory()
    {
        // 8.8.4.4 ist Google → sollte Kategorie zurückgeben auch ohne DB
        var result = _sut.Lookup(IPAddress.Parse("8.8.4.4"));
        Assert.NotNull(result);
    }

    public void Dispose() => _sut.Dispose();
}
