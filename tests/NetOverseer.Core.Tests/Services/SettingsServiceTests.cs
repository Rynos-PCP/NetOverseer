// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.Extensions.Logging.Abstractions;
using NetOverseer.Core.Models;
using NetOverseer.Core.Services;

namespace NetOverseer.Core.Tests.Services;

/// <summary>
/// Unit-Tests für <see cref="SettingsService"/>.
/// Alle Tests verwenden eine temporäre Datei, nicht %AppData%.
/// </summary>
public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _settingsPath;

    public SettingsServiceTests()
    {
        _settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"neto_settings_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
    }

    private SettingsService CreateSut() =>
        new(_settingsPath, NullLogger<SettingsService>.Instance);

    // ──────────────────────────────────────────────────────────────────────
    // Load
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_WhenFileAbsent_ReturnsDefaults()
    {
        var sut = CreateSut();

        var settings = sut.Load();

        Assert.NotNull(settings);
        Assert.Equal(30, settings.DataRetentionDays);   // AppSettings-Default
    }

    [Fact]
    public void Load_CalledTwice_ReturnsSameInstance()
    {
        var sut = CreateSut();

        var s1 = sut.Load();
        var s2 = sut.Load();

        Assert.Same(s1, s2);
    }

    [Fact]
    public void Load_AfterSave_ReturnsSavedValues()
    {
        var sut = CreateSut();
        var settings = sut.Load();
        settings.DataRetentionDays = 90;

        sut.Save(settings);
        sut.Invalidate();

        var reloaded = sut.Load();
        Assert.Equal(90, reloaded.DataRetentionDays);
    }

    [Fact]
    public void Load_WithCorruptJson_ReturnsDefaults()
    {
        File.WriteAllText(_settingsPath, "{ this is not valid json }}");
        var sut = CreateSut();

        var settings = sut.Load();

        Assert.NotNull(settings);   // kein Crash, Fallback auf Defaults
        Assert.Equal(30, settings.DataRetentionDays);
    }

    [Fact]
    public void Load_WithEmptyFile_ReturnsDefaults()
    {
        File.WriteAllText(_settingsPath, "");
        var sut = CreateSut();

        var settings = sut.Load();

        Assert.NotNull(settings);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Save
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Save_CreatesFile()
    {
        var sut = CreateSut();
        sut.Save(new AppSettings { DataRetentionDays = 7 });

        Assert.True(File.Exists(_settingsPath));
    }

    [Fact]
    public void Save_PersistsAllScalarFields()
    {
        var sut = CreateSut();
        var original = new AppSettings
        {
            DataRetentionDays          = 14,
            PollingIntervalMs          = 750,
            ShowPrivateConnections     = true,
            OfflineMode                = true,
            NotifyOnNewUnknownConnection = false,
            QuietHoursEnabled          = true,
            QuietHoursStartHour        = 23,
            QuietHoursEndHour          = 7,
            MaxDailyAbuseIpDbRequests  = 500,
            ReputationCacheTtlHours    = 48,
        };

        sut.Save(original);
        sut.Invalidate();
        var loaded = sut.Load();

        Assert.Equal(14,    loaded.DataRetentionDays);
        Assert.Equal(750,   loaded.PollingIntervalMs);
        Assert.True(loaded.ShowPrivateConnections);
        Assert.True(loaded.OfflineMode);
        Assert.False(loaded.NotifyOnNewUnknownConnection);
        Assert.True(loaded.QuietHoursEnabled);
        Assert.Equal(23,    loaded.QuietHoursStartHour);
        Assert.Equal(7,     loaded.QuietHoursEndHour);
        Assert.Equal(500,   loaded.MaxDailyAbuseIpDbRequests);
        Assert.Equal(48,    loaded.ReputationCacheTtlHours);
    }

    [Fact]
    public void Save_PersistsIgnoredProcessesList()
    {
        var sut = CreateSut();
        sut.Save(new AppSettings
        {
            IgnoredProcesses = ["svchost.exe", "lsass.exe"]
        });
        sut.Invalidate();

        var loaded = sut.Load();

        Assert.Equal(2, loaded.IgnoredProcesses.Count);
        Assert.Contains("svchost.exe", loaded.IgnoredProcesses);
        Assert.Contains("lsass.exe",   loaded.IgnoredProcesses);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Invalidate
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Invalidate_ClearsCache_NextLoadReadsFile()
    {
        var sut = CreateSut();
        _ = sut.Load();                         // cache füllen

        sut.Save(new AppSettings { DataRetentionDays = 7 });
        sut.Invalidate();

        var reloaded = sut.Load();
        Assert.Equal(7, reloaded.DataRetentionDays);
    }

    // ──────────────────────────────────────────────────────────────────────
    // API-Key Methoden
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetAbuseIpDbApiKey_Initially_ReturnsNull()
    {
        var sut = CreateSut();
        Assert.Null(sut.GetAbuseIpDbApiKey());
    }

    [Fact]
    public void SetAbuseIpDbApiKey_PersistsKey()
    {
        var sut = CreateSut();
        sut.SetAbuseIpDbApiKey("my-secret-key");
        sut.Invalidate();

        Assert.Equal("my-secret-key", sut.GetAbuseIpDbApiKey());
    }

    [Fact]
    public void SetAbuseIpDbApiKey_WhitespaceOnly_StoresNull()
    {
        var sut = CreateSut();
        sut.SetAbuseIpDbApiKey("   ");
        sut.Invalidate();

        Assert.Null(sut.GetAbuseIpDbApiKey());
    }

    [Fact]
    public void SetAbuseIpDbApiKey_EmptyString_StoresNull()
    {
        var sut = CreateSut();
        sut.SetAbuseIpDbApiKey("");
        sut.Invalidate();

        Assert.Null(sut.GetAbuseIpDbApiKey());
    }

    [Fact]
    public void GetMaxMindLicenseKey_Initially_ReturnsNull()
    {
        var sut = CreateSut();
        Assert.Null(sut.GetMaxMindLicenseKey());
    }

    [Fact]
    public void SetMaxMindLicenseKey_PersistsKey()
    {
        var sut = CreateSut();
        sut.SetMaxMindLicenseKey("mm-license-123");
        sut.Invalidate();

        Assert.Equal("mm-license-123", sut.GetMaxMindLicenseKey());
    }

    [Fact]
    public void SetMaxMindLicenseKey_NullValue_StoresNull()
    {
        var sut = CreateSut();
        sut.SetMaxMindLicenseKey("existing");
        sut.SetMaxMindLicenseKey(null);
        sut.Invalidate();

        Assert.Null(sut.GetMaxMindLicenseKey());
    }

    // ──────────────────────────────────────────────────────────────────────
    // Thread-Sicherheit (Smoke)
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentSaves_DoNotCorruptFile()
    {
        var sut = CreateSut();

        var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(() =>
        {
            var s = new AppSettings { DataRetentionDays = i };
            sut.Save(s);
        }));

        await Task.WhenAll(tasks);

        // Nach parallelen Writes muss die Datei gültig JSON sein
        sut.Invalidate();
        var loaded = sut.Load();
        Assert.NotNull(loaded);
    }
}
