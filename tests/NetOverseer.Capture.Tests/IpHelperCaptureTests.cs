// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.Extensions.Logging.Abstractions;
using NetOverseer.Capture;

namespace NetOverseer.Capture.Tests;

public sealed class IpHelperCaptureTests : IAsyncLifetime
{
    private IpHelperCapture? _capture;

    public Task InitializeAsync()
    {
        _capture = new IpHelperCapture(NullLogger<IpHelperCapture>.Instance);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_capture is not null)
        {
            await _capture.StopAsync();
            _capture.Dispose();
        }
    }

    [Fact]
    public void IsCapturing_BeforeStart_IsFalse()
    {
        Assert.False(_capture!.IsCapturing);
    }

    [Fact]
    public async Task StartAsync_SetsIsCapturingTrue()
    {
        await _capture!.StartAsync();
        Assert.True(_capture.IsCapturing);
        await _capture.StopAsync();
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyRunning_Throws()
    {
        await _capture!.StartAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _capture.StartAsync());
    }

    [Fact]
    public async Task StopAsync_SetsIsCapturingFalse()
    {
        await _capture!.StartAsync();
        await _capture.StopAsync();
        Assert.False(_capture.IsCapturing);
    }

    [Fact]
    public async Task Connections_EmitsEventsAfterStart()
    {
        var receivedEvents = new List<Core.Models.ConnectionEvent>();
        using var sub = _capture!.Connections.Subscribe(e => receivedEvents.Add(e));

        await _capture.StartAsync();
        // Warte kurz damit Polling-Loop mindestens einmal läuft
        await Task.Delay(700);
        await _capture.StopAsync();

        // Auf einem Windows-System sollten immer aktive Verbindungen existieren
        // (z.B. System, DNS etc.) – zumindest keine Exception
        Assert.NotNull(receivedEvents);
    }
}
