// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;
using NetOverseer.Core.Services;

namespace NetOverseer.Core.Tests.Services;

/// <summary>
/// Unit-Tests für <see cref="ReputationService"/> mit echten HTTP-Antwort-Simulationen
/// via <see cref="FakeHttpMessageHandler"/>.
/// Stellt sicher, dass API-Antworten korrekt geparst und gecacht werden.
/// </summary>
public sealed class ReputationServiceHttpTests
{
    // ──────────────────────────────────────────────────────────────────────
    // Hilfsobjekte
    // ──────────────────────────────────────────────────────────────────────

    private static readonly IPAddress PublicIp = IPAddress.Parse("5.6.7.8");

    private static ReputationService BuildService(
        FakeHttpMessageHandler handler,
        AppSettings? settings = null)
    {
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.Load()).Returns(settings ?? new AppSettings
        {
            AbuseIpDbApiKey           = "TEST-KEY",
            OfflineMode               = false,
            MaxDailyAbuseIpDbRequests = 1000,
            ReputationCacheTtlHours   = 24
        });
        settingsMock.Setup(s => s.GetAbuseIpDbApiKey()).Returns("TEST-KEY");

        var geoMock      = new Mock<IGeoLocationService>();
        geoMock.Setup(g => g.Lookup(It.IsAny<IPAddress>()))
               .Returns(GeoInfo.Unknown);

        var blocklistMock = new Mock<IBlocklistService>();
        blocklistMock.Setup(b => b.IsBlocked(It.IsAny<IPAddress>())).Returns(false);

        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

        return new ReputationService(
            settingsMock.Object,
            geoMock.Object,
            blocklistMock.Object,
            NullLogger<ReputationService>.Instance,
            http);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Korrekte API-Antworten
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApiResponse_Score0_ReturnsSafe()
    {
        const string json = """
            {
              "data": {
                "abuseConfidenceScore": 0,
                "totalReports": 0,
                "lastReportedAt": null
              }
            }
            """;

        var handler = FakeHttpMessageHandler.ReturnsJson(json);
        var svc     = BuildService(handler);

        var result = await svc.GetReputationAsync(PublicIp);

        Assert.Equal(ReputationCategory.Safe, result.Category);
        Assert.Equal("AbuseIPDB", result.Source);
        Assert.Equal(0, result.Score);
    }

    [Fact]
    public async Task ApiResponse_Score50_ReturnsDangerous()
    {
        const string json = """
            {
              "data": {
                "abuseConfidenceScore": 50,
                "totalReports": 5,
                "lastReportedAt": "2026-01-01T00:00:00Z"
              }
            }
            """;

        var handler = FakeHttpMessageHandler.ReturnsJson(json);
        var svc     = BuildService(handler);

        var result = await svc.GetReputationAsync(PublicIp);

        Assert.Equal(ReputationCategory.Dangerous, result.Category);
    }

    [Fact]
    public async Task ApiResponse_Score90_ReturnsDangerous()
    {
        const string json = """
            {
              "data": {
                "abuseConfidenceScore": 90,
                "totalReports": 200,
                "lastReportedAt": "2026-05-01T12:00:00Z"
              }
            }
            """;

        var handler = FakeHttpMessageHandler.ReturnsJson(json);
        var svc     = BuildService(handler);

        var result = await svc.GetReputationAsync(PublicIp);

        Assert.Equal(ReputationCategory.Dangerous, result.Category);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Fehler-Szenarien
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApiReturns401_ReturnsUnknown_NoException()
    {
        var handler = FakeHttpMessageHandler.ReturnsStatus(HttpStatusCode.Unauthorized);
        var svc     = BuildService(handler);

        var result = await svc.GetReputationAsync(PublicIp);

        Assert.Equal(ReputationCategory.Unknown, result.Category);
    }

    [Fact]
    public async Task ApiReturns429_TooManyRequests_ReturnsUnknown()
    {
        var handler = FakeHttpMessageHandler.ReturnsStatus(HttpStatusCode.TooManyRequests);
        var svc     = BuildService(handler);

        var result = await svc.GetReputationAsync(PublicIp);

        Assert.Equal(ReputationCategory.Unknown, result.Category);
    }

    [Fact]
    public async Task ApiReturnsInvalidJson_ReturnsUnknown()
    {
        var handler = FakeHttpMessageHandler.ReturnsJson("not-json{{");
        var svc     = BuildService(handler);

        var result = await svc.GetReputationAsync(PublicIp);

        Assert.Equal(ReputationCategory.Unknown, result.Category);
    }

    [Fact]
    public async Task ApiReturnsEmptyData_ReturnsUnknown()
    {
        var handler = FakeHttpMessageHandler.ReturnsJson("""{ "data": {} }""");
        var svc     = BuildService(handler);

        var result = await svc.GetReputationAsync(PublicIp);

        Assert.Equal(ReputationCategory.Unknown, result.Category);
    }

    [Fact]
    public async Task NetworkException_ReturnsUnknown_NoException()
    {
        var handler = FakeHttpMessageHandler.ThrowsException(
            new HttpRequestException("Netzwerk nicht verfügbar"));
        var svc = BuildService(handler);

        var result = await svc.GetReputationAsync(PublicIp);

        Assert.Equal(ReputationCategory.Unknown, result.Category);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Caching
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SecondCall_SameIp_DoesNotCallApiAgain()
    {
        const string json = """
            {
              "data": {
                "abuseConfidenceScore": 0,
                "totalReports": 0,
                "lastReportedAt": null
              }
            }
            """;
        var handler = FakeHttpMessageHandler.ReturnsJson(json);
        var svc     = BuildService(handler);

        await svc.GetReputationAsync(PublicIp);
        await svc.GetReputationAsync(PublicIp);

        Assert.Equal(1, handler.CallCount);   // nur ein HTTP-Request
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Hilfsmethode: Fake-HttpMessageHandler
// ──────────────────────────────────────────────────────────────────────────────

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;
    private readonly Exception? _exception;
    public int CallCount { get; private set; }

    private FakeHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> factory,
        Exception? exception = null)
    {
        _factory   = factory;
        _exception = exception;
    }

    public static FakeHttpMessageHandler ReturnsJson(string json) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

    public static FakeHttpMessageHandler ReturnsStatus(HttpStatusCode code) =>
        new(_ => new HttpResponseMessage(code));

    public static FakeHttpMessageHandler ThrowsException(Exception ex) =>
        new(_ => throw ex, ex);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        CallCount++;
        if (_exception is not null) throw _exception;
        return Task.FromResult(_factory(request));
    }
}
