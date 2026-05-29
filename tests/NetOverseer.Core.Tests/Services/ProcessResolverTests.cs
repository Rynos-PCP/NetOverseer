// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.Extensions.Logging.Abstractions;
using NetOverseer.Core.Models;
using NetOverseer.Core.Services;

namespace NetOverseer.Core.Tests.Services;

public sealed class ProcessResolverTests
{
    private readonly ProcessResolver _sut;

    public ProcessResolverTests()
    {
        _sut = new ProcessResolver(NullLogger<ProcessResolver>.Instance);
    }

    [Fact]
    public void GetProcessInfo_Pid0_ReturnsSystemIdleProcess()
    {
        var result = _sut.GetProcessInfo(0);
        Assert.Equal("Idle", result.Name);
        Assert.True(result.IsSystemProcess);
        Assert.Equal(ProcessCategory.System, result.Category);
    }

    [Fact]
    public void GetProcessInfo_Pid4_ReturnsWindowsKernel()
    {
        var result = _sut.GetProcessInfo(4);
        Assert.Equal("System", result.Name);
        Assert.True(result.IsSystemProcess);
        Assert.Equal(ProcessCategory.System, result.Category);
    }

    [Fact]
    public void GetProcessInfo_CurrentProcess_ReturnsValidInfo()
    {
        int currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;
        var result = _sut.GetProcessInfo(currentPid);

        Assert.Equal(currentPid, result.Pid);
        Assert.NotEmpty(result.Name);
        Assert.NotEmpty(result.ExecutablePath);
    }

    [Fact]
    public void GetProcessInfo_NonExistentPid_ReturnsUnknown()
    {
        // PID 999999 existiert mit hoher Wahrscheinlichkeit nicht
        var result = _sut.GetProcessInfo(999999);
        Assert.Equal("Unknown", result.Name);
        Assert.Equal(ProcessCategory.Unknown, result.Category);
    }

    [Fact]
    public void GetProcessInfo_SecondCall_ReturnsCachedResult()
    {
        int pid = System.Diagnostics.Process.GetCurrentProcess().Id;

        var result1 = _sut.GetProcessInfo(pid);
        var result2 = _sut.GetProcessInfo(pid);

        // Selbe Instanz aus Cache
        Assert.Same(result1, result2);
    }

    [Fact]
    public void ClearCache_AfterClear_NextCallRefreshes()
    {
        int pid = System.Diagnostics.Process.GetCurrentProcess().Id;

        var result1 = _sut.GetProcessInfo(pid);
        _sut.ClearCache();
        var result2 = _sut.GetProcessInfo(pid);

        // Neue Instanz nach Cache-Invalidierung
        Assert.NotSame(result1, result2);
        Assert.Equal(result1.Name, result2.Name);  // Inhalt aber gleich
    }
}
