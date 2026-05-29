// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ServiceProcess;
using Microsoft.Extensions.Logging;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.Core.Services;

/// <summary>
/// Implementiert <see cref="IProcessResolver"/> mit Caching und Spezialbehandlung
/// für Windows-Systemprozesse (svchost, System, Idle).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessResolver : IProcessResolver
{
    private readonly ILogger<ProcessResolver> _logger;
    private readonly ConcurrentDictionary<int, CachedEntry> _cache = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private static readonly HashSet<string> SystemProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "[System Process]", "smss.exe", "csrss.exe",
        "wininit.exe", "winlogon.exe", "lsass.exe", "services.exe"
    };

    public ProcessResolver(ILogger<ProcessResolver> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public ProcessInfo GetProcessInfo(int pid)
    {
        // PID 0 = System Idle, PID 4 = System
        if (pid is 0 or 4)
            return CreateKernelProcessInfo(pid);

        if (_cache.TryGetValue(pid, out var cached) &&
            DateTimeOffset.UtcNow - cached.CreatedAt < CacheDuration)
            return cached.Info;

        var info = ResolveProcess(pid);
        _cache[pid] = new CachedEntry(info, DateTimeOffset.UtcNow);
        return info;
    }

    /// <inheritdoc/>
    public void ClearCache() => _cache.Clear();

    private ProcessInfo ResolveProcess(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            string name = proc.ProcessName + ".exe";
            string path = string.Empty;

            try { path = proc.MainModule?.FileName ?? string.Empty; }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                // Kein Zugriff auf Prozess-Module (z.B. elevated Prozess wenn wir nicht elevated sind)
                _logger.LogDebug("Kein Zugriff auf Modulpfad für PID {Pid}: {Msg}", pid, ex.Message);
            }

            bool isSystem = IsSystemPath(path) || SystemProcessNames.Contains(name);
            ProcessCategory category;
            string displayName;
            string? serviceName = null;

            if (string.Equals(name, "svchost.exe", StringComparison.OrdinalIgnoreCase))
            {
                serviceName = GetServiceNameForPid(pid);
                displayName = serviceName is not null
                    ? $"{GetServiceDisplayName(serviceName)} (svchost)"
                    : "Windows Service Host (svchost)";
                category = ProcessCategory.WindowsService;
                isSystem = true;
            }
            else if (isSystem)
            {
                displayName = name;
                category = ProcessCategory.System;
            }
            else
            {
                displayName = GetProductName(path) is { Length: > 0 } productName
                    ? productName
                    : Path.GetFileNameWithoutExtension(name);
                category = ProcessCategory.UserApp;
            }

            byte[]? icon = ExtractIconPng(path);
            string publisher = GetPublisher(path);

            return new ProcessInfo
            {
                Pid = pid,
                Name = name,
                DisplayName = displayName,
                ExecutablePath = path,
                IconPng = icon,
                IsSystemProcess = isSystem,
                Publisher = publisher,
                Category = category,
                ServiceName = serviceName
            };
        }
        catch (ArgumentException)
        {
            // Prozess existiert nicht mehr
            _logger.LogTrace("Prozess PID {Pid} nicht mehr vorhanden.", pid);
            return ProcessInfo.Unknown(pid);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fehler beim Auflösen von PID {Pid}.", pid);
            return ProcessInfo.Unknown(pid);
        }
    }

    private static ProcessInfo CreateKernelProcessInfo(int pid) => new()
    {
        Pid = pid,
        Name = pid == 0 ? "Idle" : "System",
        DisplayName = pid == 0 ? "System Idle Process" : "Windows Kernel",
        ExecutablePath = string.Empty,
        IsSystemProcess = true,
        Category = ProcessCategory.System,
        Publisher = "Microsoft Corporation"
    };

    private static bool IsSystemPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return path.StartsWith(systemRoot, StringComparison.OrdinalIgnoreCase);
    }

    private string? GetServiceNameForPid(int pid)
    {
        const uint ScManagerConnect = 0x0001;

        // SCManager einmalig öffnen und für alle Dienste wiederverwenden – statt pro
        // Dienst neu zu öffnen. Das spart bei mehreren hundert Diensten entsprechend
        // viele OpenSCManager-Aufrufe.
        nint scm = OpenSCManager(null, null, ScManagerConnect);
        if (scm == nint.Zero) return null;
        try
        {
            foreach (var svc in ServiceController.GetServices())
            {
                using (svc)
                {
                    if (GetServicePid(scm, svc.ServiceName) == pid)
                        return svc.ServiceName;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Fehler beim Ermitteln des Service für PID {Pid}.", pid);
        }
        finally
        {
            CloseServiceHandle(scm);
        }
        return null;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryServiceStatusEx(
        nint hService, int InfoLevel, nint lpBuffer, int cbBufSize, out int pcbBytesNeeded);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint OpenService(nint hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(nint hSCObject);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint OpenSCManager(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType, CurrentState, ControlsAccepted;
        public uint Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint;
        public uint ProcessId, ServiceFlags;
    }

    private static int GetServicePid(nint scm, string serviceName)
    {
        const uint ServiceQueryStatus = 0x0004;
        const int ScStatusProcessInfo = 0;

        nint svcHandle = OpenService(scm, serviceName, ServiceQueryStatus);
        if (svcHandle == nint.Zero) return -1;
        try
        {
            int size = Marshal.SizeOf<ServiceStatusProcess>();
            nint buf = Marshal.AllocHGlobal(size);
            try
            {
                if (QueryServiceStatusEx(svcHandle, ScStatusProcessInfo, buf, size, out _))
                    return (int)Marshal.PtrToStructure<ServiceStatusProcess>(buf).ProcessId;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }
        finally
        {
            CloseServiceHandle(svcHandle);
        }
        return -1;
    }

    private string GetServiceDisplayName(string serviceName)
    {
        try
        {
            using var svc = new ServiceController(serviceName);
            return svc.DisplayName;
        }
        catch
        {
            return serviceName;
        }
    }

    private static string GetProductName(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(path);
            return vi.ProductName ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    private static string GetPublisher(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(path);
            return vi.CompanyName ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    private byte[]? ExtractIconPng(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            using var bmp = icon.ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Icon-Extraktion fehlgeschlagen für: {Path}", path);
            return null;
        }
    }

    private sealed record CachedEntry(ProcessInfo Info, DateTimeOffset CreatedAt);
}
