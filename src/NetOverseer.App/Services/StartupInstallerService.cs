// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using NetOverseer.Core.Interfaces;
using System.Diagnostics;
using System.Text;

namespace NetOverseer.App.Services;

/// <summary>
/// Verwaltet die geplante Aufgabe "NetOverseer-BootMonitor", die bereits beim
/// Systemstart (vor der Benutzeranmeldung) als SYSTEM läuft und die initialen
/// Netzwerkverbindungen für die Startup-Analyse aufzeichnet.
/// Verwendet <c>schtasks /SC ONSTART /RU SYSTEM /RL HIGHEST</c>.
/// </summary>
public sealed class StartupInstallerService : IStartupInstallerService
{
    /// <summary>Aktueller Task-Name (SYSTEM, ONSTART, headless).</summary>
    public const string TaskName = "NetOverseer-BootMonitor";

    /// <summary>Altname aus früheren Versionen (User-ONLOGON-Variante) – wird automatisch entfernt.</summary>
    private const string LegacyTaskName = "NetOverseer";

    /// <inheritdoc/>
    public bool IsInstalled => QueryTaskExists(TaskName);

    /// <inheritdoc/>
    public async Task InstallAsync()
    {
        var exePath = ResolveBootMonitorExe();

        // Alten User-Task (falls vorhanden) entfernen – wir wechseln auf SYSTEM/ONSTART.
        await TryDeleteAsync(LegacyTaskName).ConfigureAwait(false);

        // /TR-Wert: exakt die Befehlszeile, die später ausgeführt wird.
        // Die separate Konsolen-EXE NetOverseer.BootMonitor.exe enthält absichtlich
        // KEIN WinUI – läuft sauber in Session 0 (SYSTEM, vor User-Login).
        var taskRunValue = $"\"{exePath}\"";

        var psi = new ProcessStartInfo
        {
            FileName               = "schtasks.exe",
            CreateNoWindow         = true,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        psi.ArgumentList.Add("/Create");
        psi.ArgumentList.Add("/TN"); psi.ArgumentList.Add(TaskName);
        psi.ArgumentList.Add("/TR"); psi.ArgumentList.Add(taskRunValue);
        psi.ArgumentList.Add("/SC"); psi.ArgumentList.Add("ONSTART");
        psi.ArgumentList.Add("/RU"); psi.ArgumentList.Add("SYSTEM");
        psi.ArgumentList.Add("/RL"); psi.ArgumentList.Add("HIGHEST");
        psi.ArgumentList.Add("/F");

        var (exit, stdout, stderr) = await RunAsync(psi).ConfigureAwait(false);
        if (exit != 0)
        {
            throw new InvalidOperationException(
                $"schtasks /Create schlug fehl (Exit {exit}). " +
                $"stdout='{stdout.Trim()}' stderr='{stderr.Trim()}'");
        }

        if (!QueryTaskExists(TaskName))
        {
            throw new InvalidOperationException(
                "schtasks /Create meldete Erfolg, der Task ist aber nicht abrufbar.");
        }

        await DumpTaskConfigAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task UninstallAsync()
    {
        await TryDeleteAsync(TaskName).ConfigureAwait(false);
        await TryDeleteAsync(LegacyTaskName).ConfigureAwait(false);
    }

    /// <summary>Startet den geplanten Task sofort (manueller Test ohne Reboot).</summary>
    public async Task<bool> RunNowAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName               = "schtasks.exe",
            CreateNoWindow         = true,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        psi.ArgumentList.Add("/Run");
        psi.ArgumentList.Add("/TN"); psi.ArgumentList.Add(TaskName);

        var (exit, _, _) = await RunAsync(psi).ConfigureAwait(false);
        return exit == 0;
    }

    /// <summary>
    /// Startet <c>NetOverseer.BootMonitor.exe --self-test</c> direkt (nicht
    /// via schtasks), für UI-Diagnosezwecke. Läuft im aktuellen User-Kontext.
    /// </summary>
    public async Task<int> RunBootMonitorSelfTestAsync()
    {
        var exePath = ResolveBootMonitorExe();
        var psi = new ProcessStartInfo
        {
            FileName        = exePath,
            UseShellExecute = false,
            CreateNoWindow  = true,
        };
        psi.ArgumentList.Add("--self-test");

        using var proc = Process.Start(psi)!;
        await proc.WaitForExitAsync().ConfigureAwait(false);
        return proc.ExitCode;
    }

    // ── Hilfsmethoden ─────────────────────────────────────────────────────────

    /// <summary>
    /// Ermittelt den vollständigen Pfad zu <c>NetOverseer.BootMonitor.exe</c>.
    /// Sucht zunächst neben der laufenden App-EXE; das ist der erwartete
    /// Standardfall (post-build copy aus dem App-csproj).
    /// </summary>
    private static string ResolveBootMonitorExe()
    {
        var appDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(appDir, "NetOverseer.BootMonitor.exe");
        if (File.Exists(candidate)) return candidate;

        // Fallback: relativ zur App-EXE
        var processDir = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);
        if (!string.IsNullOrEmpty(processDir))
        {
            candidate = Path.Combine(processDir, "NetOverseer.BootMonitor.exe");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException(
            $"NetOverseer.BootMonitor.exe nicht gefunden in '{appDir}'. " +
            "Bitte stelle sicher, dass beide EXEn im selben Installationsverzeichnis liegen.");
    }

    private static bool QueryTaskExists(string taskName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "schtasks.exe",
                CreateNoWindow         = true,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            psi.ArgumentList.Add("/Query");
            psi.ArgumentList.Add("/TN"); psi.ArgumentList.Add(taskName);

            using var proc = Process.Start(psi)!;
            proc.WaitForExit();
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }

    private static async Task TryDeleteAsync(string taskName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "schtasks.exe",
                CreateNoWindow         = true,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            psi.ArgumentList.Add("/Delete");
            psi.ArgumentList.Add("/TN"); psi.ArgumentList.Add(taskName);
            psi.ArgumentList.Add("/F");

            using var proc = Process.Start(psi)!;
            await proc.WaitForExitAsync().ConfigureAwait(false);
        }
        catch { /* still missing → ignore */ }
    }

    private static async Task<(int Exit, string Out, string Err)> RunAsync(ProcessStartInfo psi)
    {
        using var proc = Process.Start(psi)!;
        var errTask = proc.StandardError.ReadToEndAsync();
        var outTask = proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync().ConfigureAwait(false);
        return (proc.ExitCode,
                await outTask.ConfigureAwait(false),
                await errTask.ConfigureAwait(false));
    }

    /// <summary>
    /// Liest die XML-Konfiguration des installierten Tasks und schreibt sie zur
    /// Diagnose nach <c>%ProgramData%\NetOverseer\logs\task-config.xml</c>.
    /// </summary>
    private static async Task DumpTaskConfigAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "schtasks.exe",
                CreateNoWindow         = true,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                StandardOutputEncoding = Encoding.Unicode,
            };
            psi.ArgumentList.Add("/Query");
            psi.ArgumentList.Add("/TN"); psi.ArgumentList.Add(TaskName);
            psi.ArgumentList.Add("/XML");

            var (exit, stdout, _) = await RunAsync(psi).ConfigureAwait(false);
            if (exit != 0) return;

            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "NetOverseer", "logs");
            Directory.CreateDirectory(logDir);
            await File.WriteAllTextAsync(
                Path.Combine(logDir, "task-config.xml"),
                stdout,
                Encoding.UTF8).ConfigureAwait(false);
        }
        catch { /* Diagnose-Hilfe – Fehler ignorieren */ }
    }
}
