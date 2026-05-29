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

    private static readonly int[] RetentionValues = [7, 30, 90, 0];

    // â”€â”€ Allgemein â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    /// <summary>0=Deutsch, 1=Englisch</summary>
    [ObservableProperty] private int  _languageIndex = 0;
    /// <summary>0=System, 1=Hell, 2=Dunkel</summary>
    [ObservableProperty] private int  _themeIndex = 0;
    [ObservableProperty] private bool _startWithWindows;
    /// <summary>0=7d, 1=30d, 2=90d, 3=Unbegrenzt</summary>
    [ObservableProperty] private int  _dataRetentionIndex = 1;

    // â”€â”€ Ãœberwachung â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    /// <summary>0=IpHelper, 1=Wfp</summary>
    [ObservableProperty] private int    _captureMethodIndex = 0;
    [ObservableProperty] private double _pollingIntervalMs = 500;
    [ObservableProperty] private bool   _showPrivateConnections;
    [ObservableProperty] private string _ignoredProcessesText = string.Empty;

    // â”€â”€ Reputation & APIs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Nur fÃ¼r Code-Behind-Initialisierung lesbar; Schreiben Ã¼ber Code-Behind-Events
    public string AbuseIpDbApiKey    { get; set; } = string.Empty;
    public string MaxMindLicenseKey  { get; set; } = string.Empty;
    [ObservableProperty] private bool   _offlineMode;
    [ObservableProperty] private double _blocklistUpdateIntervalDays = 7;
    [ObservableProperty] private double _maxDailyAbuseIpDbRequests = 1000;

    // â”€â”€ Benachrichtigungen â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [ObservableProperty] private bool   _notifyOnNewUnknownConnection;
    [ObservableProperty] private bool   _notifyOnLowReputation = true;
    [ObservableProperty] private double _minReputationScoreForAlert = 20;
    [ObservableProperty] private bool   _notifyOnSuspiciousDns;
    [ObservableProperty] private bool   _quietHoursEnabled;
    [ObservableProperty] private double _quietHoursStartHour = 22;
    [ObservableProperty] private double _quietHoursEndHour = 8;

    // â”€â”€ Autostart-Ãœberwachung â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [ObservableProperty] private bool _isStartupMonitoringEnabled;

    // â”€â”€ Status â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [ObservableProperty] private string _statusMessage = string.Empty;

    // â”€â”€ Berechnete Properties â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

    // â”€â”€ Laden â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

        // Ãœberwachung
        CaptureMethodIndex = s.CaptureMethod == "Wfp" ? 1 : 0;
        PollingIntervalMs = s.PollingIntervalMs;
        ShowPrivateConnections = s.ShowPrivateConnections;
        IgnoredProcessesText = string.Join(Environment.NewLine, s.IgnoredProcesses);

        // Reputation & APIs (Keys entschlÃ¼sseln â€” fÃ¼r Code-Behind-Initialisierung)
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

    // â”€â”€ Speichern â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

            // Windows-Autostart (Registry)
            SetWindowsAutostart(StartWithWindows);

            StatusMessage = "âœ“ Einstellungen gespeichert";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Speichern der Einstellungen");
            StatusMessage = "Fehler beim Speichern";
        }
    }

    // â”€â”€ Autostart-Ãœberwachung (Task Scheduler Service) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    // â”€â”€ Sonstige Commands â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [RelayCommand]
    private void OpenDataFolder()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NetOverseer");
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    // â”€â”€ Windows-Autostart (Registry, HKCU) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static bool IsRegisteredAutostart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AutostartKey, writable: false);
        return key?.GetValue(AutostartValueName) is not null;
    }

    private static void SetWindowsAutostart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutostartKey, writable: true);
            if (key is null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath ?? string.Empty;
                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue(AutostartValueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(AutostartValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Registry-Schreibfehler ist nicht kritisch
        }
    }
}




