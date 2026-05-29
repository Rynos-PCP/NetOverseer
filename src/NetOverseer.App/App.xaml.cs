// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using NetOverseer.App.Services;
using NetOverseer.App.ViewModels;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;
using NetOverseer.Core.Services;
using NetOverseer.Capture;
using NetOverseer.Data;
using Serilog;

namespace NetOverseer.App;

/// <summary>
/// WinUI 3 Application. Konfiguriert DI und startet das Hauptfenster.
/// </summary>
public partial class App : Application
{
    private IHost? _host;
    private MainWindow? _mainWindow;

    public static IServiceProvider Services => ((App)Current)._host!.Services;

    public App()
    {
        InitializeSerilog();
        BuildHost();
        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _host!.Start();

        // Gespeicherte Sprache vor dem Erstellen des Fensters anwenden
        ApplySavedLanguage();

        // Startup-Aufzeichnung im Hintergrund starten (prüft selbst ob im Fenster)
        _host.Services.GetRequiredService<StartupMonitorService>().BeginRecordIfInStartupWindow();

        // Haupt-Datenbank initialisieren (Migrationen anwenden)
        _ = _host.Services.GetRequiredService<IDatabaseService>()
                          .InitializeAsync();

        // Capture-Settings-Bridge aktivieren (lauscht auf Settings-Änderungen)
        _ = _host.Services.GetRequiredService<CaptureSettingsBridge>();

        _mainWindow = new MainWindow();
        _mainWindow.Activate();

        // Aufnahme automatisch beim App-Start aktivieren.
        // Fire-and-forget – Fehler werden vom ViewModel selbst geloggt.
        _ = _host.Services.GetRequiredService<MainViewModel>().StartCaptureAsync();

        // DNS-ETW-Capture ebenfalls automatisch starten. Wichtig: vor dem Resolve
        // des DnsViewModel, damit dieses sofort den laufenden Stream abonniert
        // und keine Events verpasst, die zwischen App-Start und Tab-Öffnung anfallen.
        _ = Task.Run(async () =>
        {
            try
            {
                await _host.Services.GetRequiredService<IDnsCapture>().StartAsync();
            }
            catch (UnauthorizedAccessException)
            {
                Log.Warning("DNS-ETW konnte nicht gestartet werden – Administratorrechte fehlen.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "DNS-ETW konnte beim Programmstart nicht gestartet werden.");
            }
        });

        // DnsViewModel sofort instanziieren (Singleton), damit das Abonnement
        // auf den DNS-Stream aktiv ist – auch ohne dass die Seite geöffnet wurde.
        _ = _host.Services.GetRequiredService<DnsViewModel>();

        // Gleiches Prinzip für TelemetryViewModel: ohne Eager-Resolve würde es
        // erst beim Öffnen des Telemetrie-Tabs entstehen und keine zuvor
        // erfassten Verbindungen sehen.
        _ = _host.Services.GetRequiredService<TelemetryViewModel>();
    }

    private void BuildHost()
    {
        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(services =>
            {
                // Core Services
                services.AddSingleton<IGeoLocationService, GeoLocationService>();
                services.AddSingleton<IProcessResolver, ProcessResolver>();

                // Capture – IpHelperCapture als Standard, WfpNetworkCapture als Option
                services.AddSingleton<INetworkCapture>(sp =>
                {
                    // Einstellungen einlesen (Capture-Methode + Polling-Intervall)
                    var settings = sp.GetRequiredService<ISettingsService>().Load();
                    var useWfp = string.Equals(settings.CaptureMethod, "Wfp",
                        StringComparison.OrdinalIgnoreCase);

                    // Polling-Intervall in den vom Capture unterstützten Bereich klemmen
                    var interval = Math.Clamp(settings.PollingIntervalMs, 100, 5000);

                    if (useWfp)
                    {
                        var wfp = new WfpNetworkCapture(
                            sp.GetRequiredService<ILogger<WfpNetworkCapture>>(),
                            sp.GetRequiredService<ILogger<IpHelperCapture>>());
                        wfp.PollingIntervalMs = interval;
                        return wfp;
                    }

                    var ipHelper = new IpHelperCapture(sp.GetRequiredService<ILogger<IpHelperCapture>>())
                    {
                        PollingIntervalMs = interval
                    };
                    return ipHelper;
                });

                // Navigation
                services.AddSingleton<NavigationService>();
                services.AddSingleton<INavigationService>(sp =>
                    sp.GetRequiredService<NavigationService>());

                // Einstellungen
                services.AddSingleton<ISettingsService, SettingsService>();

                // Lokalisierung
                services.AddSingleton<LocalizationService>();
                services.AddSingleton<ILocalizationService>(sp =>
                    sp.GetRequiredService<LocalizationService>());

                // Blockliste (Firehol Level 1)
                services.AddSingleton<BlocklistService>();
                services.AddSingleton<IBlocklistService>(sp =>
                    sp.GetRequiredService<BlocklistService>());

                // Reputation (vollständige Implementierung ab Phase 1/3)
                services.AddSingleton<ReputationService>();
                services.AddSingleton<IReputationService>(sp =>
                    sp.GetRequiredService<ReputationService>());

                // Firewall
                services.AddSingleton<IFirewallService, FirewallService>();

                // DNS-Überwachung (ETW)
                services.AddSingleton<IDnsCategoryService, DnsCategoryService>();
                services.AddSingleton<IDnsCache, DnsCache>();
                services.AddSingleton<IDnsCapture, DnsEtwCapture>();

                // Startup-Aufzeichnung – DB in ProgramData, damit SYSTEM-Task und
                // Benutzer-Session auf dieselbe Datei zugreifen können.
                var startupDbDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "NetOverseer");
                Directory.CreateDirectory(startupDbDir);
                var dbPath = Path.Combine(startupDbDir, "startup.db");
                services.AddSingleton<IStartupRepository>(sp =>
                    new StartupRepository(dbPath, sp.GetRequiredService<ILogger<StartupRepository>>()));
                services.AddSingleton<IStartupInstallerService, StartupInstallerService>();
                services.AddSingleton<StartupMonitorService>();

                // Haupt-Datenbank (Persistenz Phase 2)
                var mainDbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "NetOverseer", "netoverseer.db");
                services.AddSingleton<DatabaseService>(sp =>
                    new DatabaseService(mainDbPath, sp.GetRequiredService<ILogger<DatabaseService>>()));
                services.AddSingleton<IDatabaseService>(sp =>
                    sp.GetRequiredService<DatabaseService>());
                services.AddSingleton<IConnectionRepository, ConnectionRepository>();
                services.AddSingleton<IDnsRepository, DnsRepository>();
                services.AddSingleton<IAppProfileRepository, AppProfileRepository>();
                services.AddSingleton<IPersistenceWorker, PersistenceWorker>();
                services.AddHostedService<DatabaseMaintenanceService>();

                // Microsoft-Telemetrie-Erkennung
                services.AddSingleton<IMicrosoftTelemetryService, MicrosoftTelemetryService>();

                // GeoDb-Updater (lädt GeoLite2 wenn Lizenz-Key gesetzt)
                services.AddSingleton<GeoDbUpdater>();

                // Toast-Benachrichtigungen
                services.AddSingleton<INotificationService, NotificationService>();

                // Capture-Hot-Reload (Polling-Intervall live nach Settings-Save übernehmen)
                services.AddSingleton<CaptureSettingsBridge>();

                // ViewModels
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<LiveConnectionsViewModel>();
                services.AddSingleton<ApplicationsViewModel>();
                services.AddSingleton<AppFirewallRulesViewModel>();
                services.AddSingleton<FirewallViewModel>();
                services.AddSingleton<DnsViewModel>();
                services.AddSingleton<StartupViewModel>();
                services.AddSingleton<TelemetryViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<HistoryViewModel>();
            })
            .Build();
    }

    private static void InitializeSerilog()
    {
        string logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NetOverseer", "logs");
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                path: Path.Combine(logDir, "netoverseer-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 10 * 1024 * 1024,  // 10 MB
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    private void ApplySavedLanguage()
    {
        try
        {
            var settings = _host!.Services.GetRequiredService<ISettingsService>().Load();
            var locService = _host.Services.GetRequiredService<ILocalizationService>();

            // Sprache aus gespeicherten Einstellungen übernehmen
            var langCode = settings.Language switch
            {
                "en" => "en-US",
                "de" => "de-DE",
                _    => locService.CurrentLanguageCode   // System-Default beibehalten
            };

            // Prozess-UI-Sprache via Win32-API setzen – funktioniert in unpackaged Apps.
            // WinRT ResourceLoader und x:Uid respektieren diese Einstellung.
            // Muss VOR new MainWindow() aufgerufen werden, damit x:Uid richtig auflöst.
            // Multi-String-Format: "en-US\0" → CLR-Marshaler fügt weitere \0 an → "en-US\0\0"
            SetProcessPreferredUILanguages(MUI_LANGUAGE_NAME, langCode + "\0", out _);

            // .NET-Kultur ebenfalls setzen (für managed Resources / string-Formatierung)
            var culture = new System.Globalization.CultureInfo(langCode);
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;

            if (langCode != locService.CurrentLanguageCode)
                locService.SetLanguage(langCode);
        }
        catch (Exception ex)
        {
            // Spracheinstellung unkritisch – App startet trotzdem mit System-Default,
            // Fehler aber wenigstens loggen damit Diagnose möglich ist.
            Log.Warning(ex, "Konnte gespeicherte Sprache nicht anwenden – verwende System-Default.");
        }
    }

    // SetProcessPreferredUILanguages setzt die Prozess-bevorzugten UI-Sprachen im Windows
    // MUI-System. Erfordert keine Paketidentität und löst keinen FAIL_FAST aus.
    // dwFlags: MUI_LANGUAGE_NAME (0x8) = Sprachnamen wie "en-US" verwenden.
    // pwszLanguagesBuffer: Multi-String (null-separated, double-null-terminated).
    private const uint MUI_LANGUAGE_NAME = 0x8;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true,
        CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool SetProcessPreferredUILanguages(
        uint dwFlags,
        string? pwszLanguagesBuffer,
        out uint pulNumLanguages);
}
