// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using NetOverseer.Core.Interfaces;

namespace NetOverseer.App.ViewModels;

/// <summary>ViewModel fÃ¼r die vollstÃ¤ndige Einstellungsseite (alle 6 Kategorien).</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IStartupInstallerService _installer;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger<SettingsViewModel> _logger;

    /// <summary>Wird ausgelöst wenn sich die Sprache geändert hat (neuer Sprachcode).</summary>
    public event EventHandler<string>? LanguageChangeRequested;

    private bool _isInitializing = true;

    private const string AutostartKey       = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AutostartValueName = "NetOverseer";

    /// <summary>
    /// Geplante Aufgabe für den App-Autostart. Da die App Administratorrechte
    /// benötigt (siehe app.manifest), kann ein HKCU-Run-Eintrag sie beim Anmelden
    /// nicht starten – Run-Einträge lösen keine UAC-Anhebung aus. Eine geplante
    /// Aufgabe mit höchster Berechtigungsstufe (RL HIGHEST) und ONLOGON-Trigger
    /// startet die App dagegen ohne UAC-Abfrage elevated.
    /// </summary>
    private const string AppAutostartTaskName = "NetOverseer-AppAutostart";

    private static readonly int[] RetentionValues = [7, 30, 90, 0];

    /// <summary>0=Deutsch, 1=Englisch</summary>
    [ObservableProperty] private int  _languageIndex = 0;
    /// <summary>0=System, 1=Hell, 2=Dunkel</summary>
    [ObservableProperty] private int  _themeIndex = 0;
    [ObservableProperty] private bool _startWithWindows;
    /// <summary>0=7d, 1=30d, 2=90d, 3=Unbegrenzt</summary>
    [ObservableProperty] private int  _dataRetentionIndex = 1;
    /// <summary>Warnung ausblenden, wenn die GeoLite2-Datenbank fehlt.</summary>
    [ObservableProperty] private bool _hideGeoDbMissingWarning;

    /// <summary>0=IpHelper, 1=Wfp</summary>
    [ObservableProperty] private int    _captureMethodIndex = 0;
    [ObservableProperty] private double _pollingIntervalMs = 500;
    [ObservableProperty] private bool   _showPrivateConnections;
    [ObservableProperty] private string _ignoredProcessesText = string.Empty;

    // Nur für Code-Behind-Initialisierung lesbar; Schreiben über Code-Behind-Events
    public string AbuseIpDbApiKey    { get; set; } = string.Empty;
    public string MaxMindLicenseKey  { get; set; } = string.Empty;
    [ObservableProperty] private bool   _offlineMode;
    [ObservableProperty] private double _blocklistUpdateIntervalDays = 7;
    [ObservableProperty] private double _maxDailyAbuseIpDbRequests = 1000;

    [ObservableProperty] private bool   _notifyOnNewUnknownConnection;
    [ObservableProperty] private bool   _notifyOnLowReputation = true;
    [ObservableProperty] private double _minReputationScoreForAlert = 20;
    [ObservableProperty] private bool   _notifyOnSuspiciousDns;
    [ObservableProperty] private bool   _quietHoursEnabled;
    [ObservableProperty] private double _quietHoursStartHour = 22;
    [ObservableProperty] private double _quietHoursEndHour = 8;

    [ObservableProperty] private bool _isStartupMonitoringEnabled;

    [ObservableProperty] private string _statusMessage = string.Empty;

    public bool IsCaptureMethodIpHelper => CaptureMethodIndex == 0;
    public bool IsOfflineModeDisabled   => !OfflineMode;
    public string PollingIntervalMsDisplay        => $"{PollingIntervalMs:F0} ms";
    public string MaxDailyRequestsDisplay         => $"{MaxDailyAbuseIpDbRequests:F0}";
    public string BlocklistIntervalDisplay        => $"{BlocklistUpdateIntervalDays:F0} Tage";
    public string MinRepAlertDisplay              => $"Alarm-Schwellwert: Score < {MinReputationScoreForAlert:F0}";
    public string QuietHoursStartDisplay          => $"{QuietHoursStartHour:F0}:00";
    public string QuietHoursEndDisplay            => $"{QuietHoursEndHour:F0}:00";

    partial void OnCaptureMethodIndexChanged(int value)    => OnPropertyChanged(nameof(IsCaptureMethodIpHelper));
    partial void OnOfflineModeChanged(bool value)          => OnPropertyChanged(nameof(IsOfflineModeDisabled));
    partial void OnPollingIntervalMsChanged(double value)  => OnPropertyChanged(nameof(PollingIntervalMsDisplay));
    partial void OnMaxDailyAbuseIpDbRequestsChanged(double value) => OnPropertyChanged(nameof(MaxDailyRequestsDisplay));
    partial void OnBlocklistUpdateIntervalDaysChanged(double value) => OnPropertyChanged(nameof(BlocklistIntervalDisplay));
    partial void OnMinReputationScoreForAlertChanged(double value) => OnPropertyChanged(nameof(MinRepAlertDisplay));
    partial void OnQuietHoursStartHourChanged(double value) => OnPropertyChanged(nameof(QuietHoursStartDisplay));
    partial void OnQuietHoursEndHourChanged(double value)   => OnPropertyChanged(nameof(QuietHoursEndDisplay));

    partial void OnLanguageIndexChanged(int value)
    {
        if (_isInitializing) return;

        var langCode = value switch { 1 => "en-US", _ => "de-DE" };

        // Nur reagieren wenn sich die Sprache tatsächlich geändert hat.
        // Verhindert endlose Rekursion: beim Frame-Reload bindet die neue
        // SettingsPage die ComboBox neu, was diesen Handler erneut auslöst.
        if (_localizationService.CurrentLanguageCode == langCode) return;

        _localizationService.SetLanguage(langCode);

        // Spracheinstellung sofort persistieren (ohne manuelles Speichern)
        try
        {
            var s = _settingsService.Load();
            s.Language = value == 1 ? "en" : "de";
            _settingsService.Save(s);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Spracheinstellung konnte nicht gespeichert werden.");
        }

        LanguageChangeRequested?.Invoke(this, langCode);
    }

    public SettingsViewModel(
        ISettingsService settingsService,
        IStartupInstallerService installer,
        ILocalizationService localizationService,
        ILogger<SettingsViewModel> logger)
    {
        _settingsService     = settingsService;
        _installer           = installer;
        _localizationService = localizationService;
        _logger              = logger;
        LoadFromService();
    }


    private void LoadFromService()
    {
        var s = _settingsService.Load();

        // Allgemein
        LanguageIndex = s.Language == "en" ? 1 : 0;
        ThemeIndex = s.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        StartWithWindows = IsRegisteredAutostart();
        DataRetentionIndex = s.DataRetentionDays switch
        {
            7  => 0,
            90 => 2,
            0  => 3,
            _  => 1
        };
        HideGeoDbMissingWarning = s.HideGeoDbMissingWarning;

        // Ãœberwachung
        CaptureMethodIndex = s.CaptureMethod == "Wfp" ? 1 : 0;
        PollingIntervalMs = s.PollingIntervalMs;
        ShowPrivateConnections = s.ShowPrivateConnections;
        IgnoredProcessesText = string.Join(Environment.NewLine, s.IgnoredProcesses);

        // Reputation & APIs (Keys entschlüsseln für Code-Behind-Initialisierung)
        AbuseIpDbApiKey   = _settingsService.GetAbuseIpDbApiKey() ?? string.Empty;
        MaxMindLicenseKey = _settingsService.GetMaxMindLicenseKey() ?? string.Empty;
        OfflineMode = s.OfflineMode;
        BlocklistUpdateIntervalDays = s.BlocklistUpdateIntervalDays;
        MaxDailyAbuseIpDbRequests = s.MaxDailyAbuseIpDbRequests;

        // Benachrichtigungen
        NotifyOnNewUnknownConnection = s.NotifyOnNewUnknownConnection;
        NotifyOnLowReputation = s.NotifyOnLowReputation;
        MinReputationScoreForAlert = s.MinReputationScoreForAlert;
        NotifyOnSuspiciousDns = s.NotifyOnSuspiciousDns;
        QuietHoursEnabled = s.QuietHoursEnabled;
        QuietHoursStartHour = s.QuietHoursStartHour;
        QuietHoursEndHour = s.QuietHoursEndHour;

        IsStartupMonitoringEnabled = _installer.IsInstalled;
        _isInitializing = false;
    }


    [RelayCommand]
    private void SaveSettings()
    {
        try
        {
            var s = _settingsService.Load();

            // Allgemein
            s.Language         = LanguageIndex == 1 ? "en" : "de";
            s.Theme            = ThemeIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
            s.DataRetentionDays = RetentionValues[(uint)DataRetentionIndex < (uint)RetentionValues.Length
                ? DataRetentionIndex : 1];
            s.HideGeoDbMissingWarning = HideGeoDbMissingWarning;

            // Ãœberwachung
            s.CaptureMethod         = CaptureMethodIndex == 1 ? "Wfp" : "IpHelper";
            s.PollingIntervalMs     = (int)Math.Round(PollingIntervalMs);
            s.ShowPrivateConnections = ShowPrivateConnections;
            s.IgnoredProcesses      = [.. IgnoredProcessesText
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)];

            // Reputation & APIs
            s.OfflineMode                = OfflineMode;
            s.BlocklistUpdateIntervalDays = (int)Math.Round(BlocklistUpdateIntervalDays);
            s.MaxDailyAbuseIpDbRequests   = (int)Math.Round(MaxDailyAbuseIpDbRequests);

            // Benachrichtigungen
            s.NotifyOnNewUnknownConnection = NotifyOnNewUnknownConnection;
            s.NotifyOnLowReputation        = NotifyOnLowReputation;
            s.MinReputationScoreForAlert   = (int)Math.Round(MinReputationScoreForAlert);
            s.NotifyOnSuspiciousDns        = NotifyOnSuspiciousDns;
            s.QuietHoursEnabled            = QuietHoursEnabled;
            s.QuietHoursStartHour          = (int)Math.Round(QuietHoursStartHour);
            s.QuietHoursEndHour            = (int)Math.Round(QuietHoursEndHour);

            _settingsService.Save(s);

            // API-Keys DPAPI-verschlÃ¼sselt speichern
            _settingsService.SetAbuseIpDbApiKey(
                string.IsNullOrWhiteSpace(AbuseIpDbApiKey) ? null : AbuseIpDbApiKey);
            _settingsService.SetMaxMindLicenseKey(
                string.IsNullOrWhiteSpace(MaxMindLicenseKey) ? null : MaxMindLicenseKey);

            StatusMessage = "œ“ Einstellungen gespeichert";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Speichern der Einstellungen");
            StatusMessage = "Fehler beim Speichern";
        }
    }

    // Autostart-Überwachung (Task Scheduler Service) 

    partial void OnIsStartupMonitoringEnabledChanged(bool value)
    {
        _ = ApplyStartupMonitoringAsync(value);
    }

    private async Task ApplyStartupMonitoringAsync(bool enable)
    {
        try
        {
            if (enable)
                await _installer.InstallAsync().ConfigureAwait(false);
            else
                await _installer.UninstallAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Ã„ndern des Autostart-Status.");
            IsStartupMonitoringEnabled = _installer.IsInstalled;
        }
    }

    // Sonstige Commands    

    [RelayCommand]
    private void OpenDataFolder()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NetOverseer");
        OpenFolderInExplorer(dir);
    }

    /// <summary>Öffnet den GeoIP-Ordner, in den die GeoLite2-City.mmdb gehört.</summary>
    [RelayCommand]
    private void OpenGeoIpFolder()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NetOverseer", "geoip");
        OpenFolderInExplorer(dir);
    }

    /// <summary>
    /// Stellt sicher, dass der Ordner existiert, und öffnet ihn im Explorer.
    /// Da die App elevated läuft, wird explorer.exe explizit mit dem Pfad als
    /// Argument gestartet – ShellExecute auf einen gerade erstellten Ordner aus
    /// einem elevated Prozess schlägt sonst mit „Pfad nicht verfügbar" fehl.
    /// </summary>
    private void OpenFolderInExplorer(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ordner konnte nicht geöffnet werden: {Dir}", dir);
        }
    }

    //  Windows-Autostart (geplante Aufgabe, ONLOGON / RL HIGHEST)  

    /// <summary>
    /// Wendet die Autostart-Einstellung sofort an (wie der Überwachungs-Schalter).
    /// Schlägt das Setzen fehl, wird der Schalter auf den tatsächlichen Zustand
    /// zurückgesetzt.
    /// </summary>
    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_isInitializing) return;

        if (!SetWindowsAutostart(value))
        {
            _isInitializing = true;
            StartWithWindows = IsRegisteredAutostart();
            _isInitializing = false;
        }
    }

    private static bool IsRegisteredAutostart()
    {
        // Bevorzugt: geplante Aufgabe (läuft elevated via ONLOGON / RL HIGHEST).
        if (QueryScheduledTaskExists(AppAutostartTaskName))
            return true;

        // Abwärtskompatibel: veralteter HKCU-Run-Eintrag aus früheren Versionen.
        using var key = Registry.CurrentUser.OpenSubKey(AutostartKey, writable: false);
        return key?.GetValue(AutostartValueName) is not null;
    }

    /// <summary>
    /// Aktiviert/deaktiviert den App-Autostart über eine geplante Aufgabe und
    /// entfernt dabei stets einen evtl. vorhandenen veralteten HKCU-Run-Eintrag.
    /// </summary>
    private bool SetWindowsAutostart(bool enable)
    {
        // Etwaigen veralteten HKCU-Run-Eintrag entfernen (funktionierte nie für
        // die elevated App).
        RemoveLegacyRunEntry();

        if (enable)
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
                return false;

            // ONLOGON ohne /RU läuft im interaktiven Token des sich anmeldenden
            // Benutzers; /RL HIGHEST hebt elevated an (ohne UAC-Abfrage), /F
            // überschreibt eine bestehende Aufgabe.
            var ok = RunSchtasks(
                "/Create",
                "/TN", AppAutostartTaskName,
                "/TR", $"\"{exePath}\"",
                "/SC", "ONLOGON",
                "/RL", "HIGHEST",
                "/F");

            if (!ok)
                _logger.LogWarning("Autostart-Aufgabe konnte nicht erstellt werden.");
            return ok;
        }

        // Deaktivieren: Aufgabe löschen (Erfolg auch, wenn sie nicht existierte).
        RunSchtasks("/Delete", "/TN", AppAutostartTaskName, "/F");
        return true;
    }

    private static void RemoveLegacyRunEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutostartKey, writable: true);
            key?.DeleteValue(AutostartValueName, throwOnMissingValue: false);
        }
        catch { /* nicht kritisch */ }
    }

    private static bool QueryScheduledTaskExists(string taskName)
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

    private static bool RunSchtasks(params string[] args)
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
            foreach (var a in args)
                psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi)!;
            proc.WaitForExit();
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }
}




