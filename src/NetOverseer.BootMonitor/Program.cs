// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.Extensions.Logging;
using NetOverseer.Capture;
using NetOverseer.Data;
using Serilog;
using Serilog.Extensions.Logging;

namespace NetOverseer.BootMonitor;

/// <summary>
/// Standalone-Konsolen-EXE, die vom geplanten Task <c>NetOverseer-BootMonitor</c>
/// beim Systemstart (ONSTART, RunAs SYSTEM) ausgeführt wird. Hat absichtlich
/// KEINE WinUI-/WindowsAppRuntime-Abhängigkeiten, weil deren Bootstrap in
/// Session 0 (vor dem Login) scheitert (HRESULT 0x80070699).
/// Schreibt Verbindungen in <c>%ProgramData%\NetOverseer\startup.db</c> und
/// Logs in <c>%ProgramData%\NetOverseer\logs\boot-monitor-*.log</c>.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        WriteEarlyMarker("boot-monitor: invoked");

        string? logDir = null;
        try
        {
            logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "NetOverseer", "logs");
            Directory.CreateDirectory(logDir);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    path: Path.Combine(logDir, "boot-monitor-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    fileSizeLimitBytes: 5 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            WriteEarlyMarker("boot-monitor: serilog initialized");

            using var loggerFactory = new SerilogLoggerFactory(Log.Logger, dispose: false);
            var monitorLogger = loggerFactory.CreateLogger<StartupMonitorService>();
            var repoLogger    = loggerFactory.CreateLogger<StartupRepository>();

            var dbDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "NetOverseer");
            Directory.CreateDirectory(dbDir);
            var dbPath = Path.Combine(dbDir, "startup.db");

            using var repo = new StartupRepository(dbPath, repoLogger);
            var service = new StartupMonitorService(repo, loggerFactory, monitorLogger);

            monitorLogger.LogInformation(
                "Bootmonitor gestartet, UpTime={S:F0}s, User={U}.",
                TimeSpan.FromMilliseconds(Environment.TickCount64).TotalSeconds,
                Environment.UserName);

            // Force=true wenn Argument --self-test übergeben wurde
            // (für manuellen Test aus dem UI heraus).
            var force = args is not null && Array.Exists(args, a =>
                string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase));

            service.RecordIfStartupWindowAsync(force: force, CancellationToken.None)
                   .GetAwaiter().GetResult();

            monitorLogger.LogInformation("Bootmonitor beendet.");
            WriteEarlyMarker("boot-monitor: completed");
            return 0;
        }
        catch (Exception ex)
        {
            WriteEarlyMarker("boot-monitor: exception: " + ex);
            try { Log.Fatal(ex, "Bootmonitor abgebrochen."); } catch { }
            try
            {
                if (logDir is not null)
                {
                    File.WriteAllText(
                        Path.Combine(logDir, "boot-monitor-fatal.txt"),
                        ex.ToString());
                }
            }
            catch { }
            return 1;
        }
        finally
        {
            try { Log.CloseAndFlush(); } catch { }
        }
    }

    /// <summary>
    /// Schreibt eine Marker-Zeile in <c>%ProgramData%\NetOverseer\logs\boot-monitor-trace.log</c>.
    /// Robust gegen alle Fehler – das ist unsere letzte Sichtbarkeit, falls
    /// Serilog/DI scheitert.
    /// </summary>
    private static void WriteEarlyMarker(string message)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "NetOverseer", "logs");
            Directory.CreateDirectory(logDir);
            var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z] " +
                       $"UpTime={TimeSpan.FromMilliseconds(Environment.TickCount64).TotalSeconds:F0}s " +
                       $"User={Environment.UserName} PID={Environment.ProcessId} :: {message}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(logDir, "boot-monitor-trace.log"), line);
        }
        catch
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "netoverseer-boot-monitor.log"),
                    message + Environment.NewLine);
            }
            catch { }
        }
    }
}
