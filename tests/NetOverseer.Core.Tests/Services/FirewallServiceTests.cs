// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Net;
using NetOverseer.Core.Models;
using Xunit;

namespace NetOverseer.Core.Tests.Services;

/// <summary>
/// Einheitstests für FirewallService-bezogene Logik.
/// Da FirewallService COM-Objekte nutzt (Windows-Firewall), werden hier
/// nur die Unit-testbaren Teile abgedeckt:
///  - FirewallRuleInfo-Model
///  - Enums
///  - IsAdministrator-Erkennung (immer false in Test-Runner ohne UAC)
/// </summary>
public class FirewallRuleInfoTests
{
    // ──────────────────────────────────────────────────────────────────────
    // FirewallRuleInfo – Modell-Tests
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void FirewallRuleInfo_Defaults_AreCorrect()
    {
        var rule = new FirewallRuleInfo
        {
            Name      = "Test-Regel",
            Direction = FirewallDirection.Outbound,
            Action    = FirewallAction.Block,
            Protocol  = FirewallProtocol.Any,
            IsEnabled = true,
        };

        Assert.Equal("Test-Regel",           rule.Name);
        Assert.Equal(FirewallDirection.Outbound, rule.Direction);
        Assert.Equal(FirewallAction.Block,    rule.Action);
        Assert.Equal(FirewallProtocol.Any,    rule.Protocol);
        Assert.True(rule.IsEnabled);
        Assert.False(rule.IsOrphaned);
        Assert.False(rule.IsNetOverseerRule);
        Assert.Null(rule.Description);
        Assert.Null(rule.ApplicationPath);
        Assert.Null(rule.ApplicationName);
        Assert.Null(rule.LocalPorts);
        Assert.Null(rule.RemotePorts);
        Assert.Null(rule.RemoteAddresses);
        Assert.Null(rule.GroupName);
    }

    [Fact]
    public void FirewallRuleInfo_IsEnabled_CanBeToggled()
    {
        var rule = new FirewallRuleInfo
        {
            Name      = "Toggle-Regel",
            Direction = FirewallDirection.Inbound,
            Action    = FirewallAction.Allow,
            Protocol  = FirewallProtocol.Tcp,
            IsEnabled = true,
        };

        rule.IsEnabled = false;
        Assert.False(rule.IsEnabled);

        rule.IsEnabled = true;
        Assert.True(rule.IsEnabled);
    }

    [Fact]
    public void FirewallRuleInfo_IsNetOverseerRule_WhenGroupNameMatches()
    {
        var rule = new FirewallRuleInfo
        {
            Name             = "NetOverseer-Regel",
            Direction        = FirewallDirection.Outbound,
            Action           = FirewallAction.Block,
            Protocol         = FirewallProtocol.Any,
            IsEnabled        = true,
            GroupName        = "NetOverseer",
            IsNetOverseerRule = true,
        };

        Assert.True(rule.IsNetOverseerRule);
        Assert.Equal("NetOverseer", rule.GroupName);
    }

    [Fact]
    public void FirewallRuleInfo_IsOrphaned_WhenApplicationGone()
    {
        var rule = new FirewallRuleInfo
        {
            Name            = "Verwaiste-Regel",
            Direction       = FirewallDirection.Outbound,
            Action          = FirewallAction.Block,
            Protocol        = FirewallProtocol.Any,
            IsEnabled       = true,
            ApplicationPath = @"C:\nonexistent\app.exe",
            IsOrphaned      = true,
        };

        Assert.True(rule.IsOrphaned);
    }

    // ──────────────────────────────────────────────────────────────────────
    // FirewallDirection-Enum
    // ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(FirewallDirection.Inbound)]
    [InlineData(FirewallDirection.Outbound)]
    public void FirewallDirection_AllValues_AreDefined(FirewallDirection dir)
    {
        Assert.True(Enum.IsDefined(typeof(FirewallDirection), dir));
    }

    // ──────────────────────────────────────────────────────────────────────
    // FirewallAction-Enum
    // ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(FirewallAction.Allow)]
    [InlineData(FirewallAction.Block)]
    public void FirewallAction_AllValues_AreDefined(FirewallAction action)
    {
        Assert.True(Enum.IsDefined(typeof(FirewallAction), action));
    }

    // ──────────────────────────────────────────────────────────────────────
    // FirewallProtocol-Enum
    // ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(FirewallProtocol.Any)]
    [InlineData(FirewallProtocol.Tcp)]
    [InlineData(FirewallProtocol.Udp)]
    [InlineData(FirewallProtocol.Icmp)]
    public void FirewallProtocol_AllValues_AreDefined(FirewallProtocol proto)
    {
        Assert.True(Enum.IsDefined(typeof(FirewallProtocol), proto));
    }

    // ──────────────────────────────────────────────────────────────────────
    // FirewallRuleInfo – vollständige Befüllung
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void FirewallRuleInfo_FullProperties_AreAccessible()
    {
        var rule = new FirewallRuleInfo
        {
            Name             = "Vollständig",
            Description      = "Beschreibung",
            Direction        = FirewallDirection.Outbound,
            Action           = FirewallAction.Block,
            Protocol         = FirewallProtocol.Tcp,
            IsEnabled        = true,
            ApplicationPath  = @"C:\Windows\System32\svchost.exe",
            ApplicationName  = "svchost.exe",
            LocalPorts       = "443",
            RemotePorts      = "443",
            RemoteAddresses  = "1.2.3.4/32",
            GroupName        = "NetOverseer",
            IsNetOverseerRule = true,
            IsOrphaned       = false,
        };

        Assert.Equal("Beschreibung",                          rule.Description);
        Assert.Equal(@"C:\Windows\System32\svchost.exe",      rule.ApplicationPath);
        Assert.Equal("svchost.exe",                           rule.ApplicationName);
        Assert.Equal("443",                                   rule.LocalPorts);
        Assert.Equal("443",                                   rule.RemotePorts);
        Assert.Equal("1.2.3.4/32",                            rule.RemoteAddresses);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Admin-Guard Tests
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Verifiziert, dass FirewallService-Schreibmethoden ohne Administratorrechte
/// sofort eine <see cref="UnauthorizedAccessException"/> werfen.
///
/// Tests mit <see cref="SkipWhenAdminFact"/> werden automatisch übersprungen,
/// wenn der Prozess als Administrator läuft (z. B. auf GitHub Actions-Runnern).
/// </summary>
public sealed class FirewallServiceAdminGuardTests
{
    private static NetOverseer.Core.Services.FirewallService CreateSut() =>
        new(Microsoft.Extensions.Logging.Abstractions.NullLogger<
                NetOverseer.Core.Services.FirewallService>.Instance);

    [SkipWhenAdminFact]
    public void IsAdministrator_InTestRunner_IsFalse()
    {
        var sut = CreateSut();
        Assert.False(sut.IsAdministrator);
    }

    [SkipWhenAdminFact]
    public async Task BlockApplicationAsync_WithoutAdmin_ThrowsUnauthorizedAccess()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.BlockApplicationAsync(@"C:\Windows\System32\notepad.exe", "Test-Regel"));
    }

    [SkipWhenAdminFact]
    public async Task BlockRemoteIpAsync_WithoutAdmin_ThrowsUnauthorizedAccess()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.BlockRemoteIpAsync(IPAddress.Parse("1.2.3.4"), "Test-Regel"));
    }

    [SkipWhenAdminFact]
    public async Task BlockApplicationToIpAsync_WithoutAdmin_ThrowsUnauthorizedAccess()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.BlockApplicationToIpAsync(@"C:\app.exe", IPAddress.Parse("5.6.7.8")));
    }

    [SkipWhenAdminFact]
    public async Task RemoveRuleAsync_WithoutAdmin_ThrowsUnauthorizedAccess()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.RemoveRuleAsync("Test-Regel"));
    }

    [SkipWhenAdminFact]
    public async Task SetRuleEnabledAsync_WithoutAdmin_ThrowsUnauthorizedAccess()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.SetRuleEnabledAsync("Test-Regel", enabled: false));
    }

    [Fact]
    public async Task GetAllRulesAsync_WithoutAdmin_DoesNotThrow()
    {
        // Lesezugriff erfordert keine Admin-Rechte
        var sut = CreateSut();
        // COM-Fehler werden erwartet (kein Windows-Firewall in CI) – aber kein Admin-Guard
        var ex = await Record.ExceptionAsync(() => sut.GetAllRulesAsync());
        Assert.IsNotType<UnauthorizedAccessException>(ex);
    }
}
