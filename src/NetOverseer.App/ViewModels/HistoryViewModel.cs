// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace NetOverseer.App.ViewModels;

// ──────────────────────────────────────────────────────────────────────────────
// Hilfstypen
// ──────────────────────────────────────────────────────────────────────────────

// WinUI 3 x:Bind requires Visibility-typed properties (no bool-to-Visibility converter built-in)
// for advanced cases.

/// <summary>Ein Zeitbucket in der Sparkline-Leiste einer App-Zeile.</summary>
public sealed class TimeBucketViewModel
{
    public int    BucketIndex   { get; init; }
    public long   Count         { get; init; }
    public string TooltipText   { get; init; } = string.Empty;
    /// <summary>Pixel-Höhe der Balken (0..40 px).</summary>
    public double BarHeight     { get; init; }
    /// <summary>Skalierter Opacity-Wert für den Hintergrund (0.15..1.0).</summary>
    public double BarOpacity    { get; init; }
}

/// <summary>Eine App-Zeile in der Zeitachsen-Ansicht.</summary>
public sealed class AppActivityViewModel
{
    public string ProcessName         { get; init; } = string.Empty;
    public string ExecutablePath      { get; init; } = string.Empty;
    public long   ConnectionCount     { get; init; }
    public long   TotalBytesSent      { get; init; }
    public long   TotalBytesReceived  { get; init; }
    public int    MinReputationScore  { get; init; } = -1;
    public DateTimeOffset LastSeen    { get; init; }

    public string BytesSentDisplay    => FormatBytes(TotalBytesSent);
    public string BytesRecvDisplay    => FormatBytes(TotalBytesReceived);
    public string LastSeenDisplay     => LastSeen.LocalDateTime.ToString("g");

    /// <summary>Reputations-Hintergrund: rot für verdächtig, transparent sonst.</summary>
    public bool IsSuspicious          => MinReputationScore is >= 0 and < 50;
    public string IconGlyph             => IsSuspicious ? "\uE7BA" : "\uE80F"; // Warning vs App

    // Visibility helpers used by x:Bind in XAML
    public Visibility SuspiciousIconVisibility => IsSuspicious ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NormalIconVisibility     => IsSuspicious ? Visibility.Collapsed : Visibility.Visible;

    public IReadOnlyList<TimeBucketViewModel> Buckets { get; init; } = [];

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024         => $"{bytes / 1_024.0:F1} KB",
        _                => $"{bytes} B"
    };
}

/// <summary>Eine Zeile in der Wochenbericht-Top-5-App-Liste.</summary>
public sealed class WeeklyAppEntryViewModel
{
    public int    Rank            { get; init; }
    public string ProcessName     { get; init; } = string.Empty;
    public string ExecutablePath  { get; init; } = string.Empty;
    public long   TotalBytesSent  { get; init; }
    public long   TotalBytesReceived { get; init; }
    public string BytesSentDisplay   => FormatBytes(TotalBytesSent);
    public string BytesRecvDisplay   => FormatBytes(TotalBytesReceived);

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024         => $"{bytes / 1_024.0:F1} KB",
        _                => $"{bytes} B"
    };
}

/// <summary>Eine verdächtige Verbindung im Wochenbericht.</summary>
public sealed class SuspiciousConnectionViewModel
{
    public string ProcessName       { get; init; } = string.Empty;
    public string ExecutablePath    { get; init; } = string.Empty;
    public string RemoteIp          { get; init; } = string.Empty;
    public int    RemotePort        { get; init; }
    public int    ReputationScore   { get; init; }
    public string GeoCountry        { get; init; } = string.Empty;
    public string TimestampDisplay  { get; init; } = string.Empty;
    public string ReputationDisplay => $"Score: {ReputationScore} – {(ReputationScore < 20 ? "Gefährlich" : "Verdächtig")}";
}

// ──────────────────────────────────────────────────────────────────────────────
// Haupt-ViewModel
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Verfügbare Zeitbereiche für die Verlaufs-Zeitachse.
/// </summary>
public enum HistoryTimeRange
{
    LastHour,
    Last24Hours,
    LastWeek,
}

/// <summary>
/// ViewModel für die Verlaufsansicht (HistoryPage).
/// Verwaltet Timeline-Daten, App-Detail-Lookup und den Wochenbericht.
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private const int BucketCount = 24; // Anzahl Zeitbuckets in der Sparkline

    private readonly IConnectionRepository    _connRepo;
    private readonly IAppProfileRepository    _profileRepo;
    private readonly ILogger<HistoryViewModel> _logger;

    private DispatcherQueue? _queue;

    // ── Observable Properties ─────────────────────────────────────────────

    [ObservableProperty] private HistoryTimeRange _selectedRange = HistoryTimeRange.Last24Hours;
    [ObservableProperty] private string           _appFilter     = string.Empty;
    [ObservableProperty] private bool             _isLoading;
    [ObservableProperty] private bool             _isEmpty       = true;

    // Visibility helpers — used by x:Bind in XAML (WinUI 3 pattern)
    public Visibility LoadingVisibility    => IsLoading  ? Visibility.Visible   : Visibility.Collapsed;
    public Visibility ContentVisibility    => IsLoading  ? Visibility.Collapsed : Visibility.Visible;
    public Visibility EmptyVisibility      => IsEmpty    ? Visibility.Visible   : Visibility.Collapsed;
    public Visibility HasDataVisibility    => IsEmpty    ? Visibility.Collapsed : Visibility.Visible;
    public Visibility WeeklyLoadingVisible => WeeklyIsLoading ? Visibility.Visible : Visibility.Collapsed;

    // Wochenbericht-spezifisch
    [ObservableProperty] private bool             _weeklyIsLoading;
    [ObservableProperty] private long             _totalConnectionsWeek;
    [ObservableProperty] private string           _totalDataWeekDisplay = "0 B";
    [ObservableProperty] private long             _suspiciousCountWeek;
    [ObservableProperty] private int              _activeAppsRange;
    [ObservableProperty] private long             _totalConnectionsRange;
    [ObservableProperty] private string           _totalDataRangeDisplay = "0 B";
    [ObservableProperty] private int              _suspiciousAppsRange;
    [ObservableProperty] private string           _busiestAppName = "Keine Daten";
    [ObservableProperty] private string           _busiestAppTrafficDisplay = "-";
    [ObservableProperty] private string           _lastRefreshDisplay = "Noch nicht aktualisiert";
    [ObservableProperty] private string           _rangeSummaryText = string.Empty;

    // ── Collections ───────────────────────────────────────────────────────

    /// <summary>Alle App-Aktivitäten für den gewählten Zeitraum.</summary>
    private readonly List<AppActivityViewModel>           _allActivities = [];
    private readonly List<SuspiciousConnectionViewModel>  _allRangeSuspiciousConnections = [];
    public ObservableCollection<AppActivityViewModel>     AppActivities  { get; } = [];
    public ObservableCollection<AppActivityViewModel>     TopRangeActivities { get; } = [];
    public ObservableCollection<SuspiciousConnectionViewModel> RangeSuspiciousConnections { get; } = [];

    /// <summary>Top-5 Apps nach Datenmenge (Wochenbericht).</summary>
    public ObservableCollection<WeeklyAppEntryViewModel>  TopApps        { get; } = [];

    /// <summary>Top-5 verdächtigste Verbindungen (Wochenbericht).</summary>
    public ObservableCollection<SuspiciousConnectionViewModel> SuspiciousConnections { get; } = [];

    // ── Konstruktor ───────────────────────────────────────────────────────

    public HistoryViewModel(
        IConnectionRepository    connRepo,
        IAppProfileRepository    profileRepo,
        ILogger<HistoryViewModel> logger)
    {
        _connRepo    = connRepo;
        _profileRepo = profileRepo;
        _logger      = logger;
    }

    // ── Init ──────────────────────────────────────────────────────────────

    public async Task InitializeAsync(DispatcherQueue queue)
    {
        _queue = queue;
        await LoadTimelineAsync();
        await LoadWeeklyReportAsync();
    }

    // ── Partial property callbacks ────────────────────────────────────────

    partial void OnSelectedRangeChanged(HistoryTimeRange value)
    {
        OnPropertyChanged(nameof(SelectedRangeLabel));
        _ = LoadTimelineAsync();
    }

    partial void OnAppFilterChanged(string value)
        => ApplyFilter();

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(LoadingVisibility));
        OnPropertyChanged(nameof(ContentVisibility));
    }

    partial void OnIsEmptyChanged(bool value)
    {
        OnPropertyChanged(nameof(EmptyVisibility));
        OnPropertyChanged(nameof(HasDataVisibility));
    }

    partial void OnWeeklyIsLoadingChanged(bool value)
        => OnPropertyChanged(nameof(WeeklyLoadingVisible));

    public string SelectedRangeLabel => SelectedRange switch
    {
        HistoryTimeRange.LastHour => "Letzte Stunde",
        HistoryTimeRange.LastWeek => "Letzte 7 Tage",
        _ => "Letzte 24 Stunden",
    };

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadTimelineAsync();
        await LoadWeeklyReportAsync();
    }

    [RelayCommand]
    private async Task ExportWeeklyReportAsync()
    {
        try
        {
            string html  = BuildWeeklyReportHtml();
            string date  = DateTime.Now.ToString("yyyy-MM-dd");
            string fname = $"NetOverseer-Wochenbericht-{date}.html";
            string path  = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads", fname);

            await File.WriteAllTextAsync(path, html).ConfigureAwait(false);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Export des Wochenberichts.");
        }
    }

    // ── Timeline laden ────────────────────────────────────────────────────

    private async Task LoadTimelineAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            (DateTimeOffset from, DateTimeOffset to) = GetTimeRange(SelectedRange);

            var summariesTask = _connRepo.GetAppActivityAsync(from, to);
            var bucketsTask = _connRepo.GetBucketedActivityAsync(from, to, BucketCount);
            var suspiciousTask = _connRepo.GetSuspiciousConnectionsAsync(from, to, 6);

            await Task.WhenAll(summariesTask, bucketsTask, suspiciousTask).ConfigureAwait(false);

            var summaries = summariesTask.Result;
            var buckets = bucketsTask.Result;
            var suspicious = suspiciousTask.Result;

            // Buckets pro App gruppieren
            var bucketsByApp = buckets
                .GroupBy(b => b.ProcessName)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Max-Count für Normalisierung
            long maxCount = buckets.Count > 0 ? buckets.Max(b => b.Count) : 1;

            var activities = summaries.Select(s =>
            {
                var appBuckets = bucketsByApp.TryGetValue(s.ProcessName, out var bl) ? bl : [];
                var bucketVMs  = Enumerable.Range(0, BucketCount)
                    .Select(i =>
                    {
                        long cnt    = appBuckets.FirstOrDefault(b => b.BucketIndex == i)?.Count ?? 0;
                        double rel  = maxCount > 0 ? (double)cnt / maxCount : 0;
                        return new TimeBucketViewModel
                        {
                            BucketIndex = i,
                            Count       = cnt,
                            BarHeight   = Math.Max(cnt > 0 ? 2 : 0, rel * 40),
                            BarOpacity  = cnt > 0 ? Math.Max(0.15, rel) : 0,
                            TooltipText = cnt > 0 ? $"{cnt} Verb." : "–",
                        };
                    })
                    .ToList();

                return new AppActivityViewModel
                {
                    ProcessName        = s.ProcessName,
                    ExecutablePath     = s.ExecutablePath,
                    ConnectionCount    = s.ConnectionCount,
                    TotalBytesSent     = s.TotalBytesSent,
                    TotalBytesReceived = s.TotalBytesReceived,
                    MinReputationScore = s.MinReputationScore,
                    LastSeen           = s.LastSeen,
                    Buckets            = bucketVMs,
                };
            }).ToList();

            _allActivities.Clear();
            _allActivities.AddRange(activities);
            _allRangeSuspiciousConnections.Clear();
            _allRangeSuspiciousConnections.AddRange(suspicious.Select(c => new SuspiciousConnectionViewModel
            {
                ProcessName = c.ProcessName,
                ExecutablePath = c.ExecutablePath,
                RemoteIp = c.RemoteIp,
                RemotePort = c.RemotePort,
                ReputationScore = c.ReputationScore,
                GeoCountry = c.GeoCountry,
                TimestampDisplay = c.Timestamp.LocalDateTime.ToString("g"),
            }));

            _queue?.TryEnqueue(() =>
            {
                ApplyFilter();
                LastRefreshDisplay = $"Aktualisiert: {DateTime.Now:g}";
                IsEmpty = AppActivities.Count == 0;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Laden der Verlaufsdaten.");
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Filter ────────────────────────────────────────────────────────────

    private void ApplyFilter()
    {
        var filter   = AppFilter.Trim();
        var filtered = string.IsNullOrEmpty(filter)
            ? _allActivities
            : _allActivities.Where(a =>
                a.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                a.ExecutablePath.Contains(filter, StringComparison.OrdinalIgnoreCase))
              .ToList();

        var ordered = filtered
            .OrderByDescending(a => a.ConnectionCount)
            .ThenByDescending(a => a.TotalBytesSent + a.TotalBytesReceived)
            .ThenByDescending(a => a.LastSeen)
            .ToList();

        AppActivities.Clear();
        foreach (var item in ordered)
            AppActivities.Add(item);

        TopRangeActivities.Clear();
        foreach (var item in ordered.Take(5))
            TopRangeActivities.Add(item);

        RangeSuspiciousConnections.Clear();
        foreach (var item in _allRangeSuspiciousConnections
                     .Where(c => string.IsNullOrEmpty(filter)
                         || c.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                         || c.ExecutablePath.Contains(filter, StringComparison.OrdinalIgnoreCase))
                     .Take(6))
        {
            RangeSuspiciousConnections.Add(item);
        }

        ActiveAppsRange = ordered.Count;
        TotalConnectionsRange = ordered.Sum(a => a.ConnectionCount);
        TotalDataRangeDisplay = FormatBytes(ordered.Sum(a => a.TotalBytesSent + a.TotalBytesReceived));
        SuspiciousAppsRange = ordered.Count(a => a.IsSuspicious);

        var busiest = ordered.FirstOrDefault();
        BusiestAppName = busiest?.ProcessName ?? "Keine Daten";
        BusiestAppTrafficDisplay = busiest is null
            ? "-"
            : FormatBytes(busiest.TotalBytesSent + busiest.TotalBytesReceived);
        RangeSummaryText = ordered.Count == 0
            ? $"Keine Aktivitaet fuer {SelectedRangeLabel.ToLowerInvariant()}."
            : $"{ordered.Count} Apps, {TotalConnectionsRange:N0} Verbindungen, Fokus auf {BusiestAppName}.";

        IsEmpty = AppActivities.Count == 0;
    }

    // ── Wochenbericht laden ───────────────────────────────────────────────

    public async Task LoadWeeklyReportAsync()
    {
        if (WeeklyIsLoading) return;
        WeeklyIsLoading = true;
        try
        {
            var from = DateTimeOffset.UtcNow.AddDays(-7);
            var to   = DateTimeOffset.UtcNow;

            // Top-5 Apps nach gesendeten Bytes
            var profiles = await _profileRepo.GetTopByBytesSentAsync(5).ConfigureAwait(false);

            // Verdächtige Verbindungen
            var suspicious = await _connRepo.GetSuspiciousConnectionsAsync(from, to, 5)
                                            .ConfigureAwait(false);

            // Gesamt-Stats aus App-Aktivitäten (letzte 7 Tage)
            var weekSummary = await _connRepo.GetAppActivityAsync(from, to).ConfigureAwait(false);
            long totalConns = weekSummary.Sum(s => s.ConnectionCount);
            long totalBytes = weekSummary.Sum(s => s.TotalBytesSent + s.TotalBytesReceived);
            long suspCount  = weekSummary.Count(s => s.MinReputationScore is >= 0 and < 50);

            _queue?.TryEnqueue(() =>
            {
                TopApps.Clear();
                for (int i = 0; i < profiles.Count; i++)
                {
                    var p = profiles[i];
                    TopApps.Add(new WeeklyAppEntryViewModel
                    {
                        Rank              = i + 1,
                        ProcessName       = p.DisplayName.Length > 0 ? p.DisplayName
                                            : Path.GetFileNameWithoutExtension(p.ExecutablePath),
                        ExecutablePath    = p.ExecutablePath,
                        TotalBytesSent    = p.TotalBytesSent,
                        TotalBytesReceived = p.TotalBytesReceived,
                    });
                }

                SuspiciousConnections.Clear();
                foreach (var c in suspicious)
                {
                    SuspiciousConnections.Add(new SuspiciousConnectionViewModel
                    {
                        ProcessName      = c.ProcessName,
                        ExecutablePath   = c.ExecutablePath,
                        RemoteIp         = c.RemoteIp,
                        RemotePort       = c.RemotePort,
                        ReputationScore  = c.ReputationScore,
                        GeoCountry       = c.GeoCountry,
                        TimestampDisplay = c.Timestamp.LocalDateTime.ToString("g"),
                    });
                }

                TotalConnectionsWeek = totalConns;
                TotalDataWeekDisplay = FormatBytes(totalBytes);
                SuspiciousCountWeek  = suspCount;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Laden des Wochenberichts.");
        }
        finally
        {
            WeeklyIsLoading = false;
        }
    }

    // ── App-Detail laden ──────────────────────────────────────────────────

    /// <summary>
    /// Lädt alle Verbindungen einer App im aktuellen Zeitraum (für Detail-Dialog).
    /// </summary>
    public async Task<IReadOnlyList<ConnectionRecord>> GetAppDetailAsync(
        string processName, CancellationToken ct = default)
    {
        (DateTimeOffset from, DateTimeOffset to) = GetTimeRange(SelectedRange);
        return await _connRepo.GetByProcessAsync(processName, 200).ConfigureAwait(false);
    }

    // ── HTML-Export ───────────────────────────────────────────────────────

    private string BuildWeeklyReportHtml()
    {
        var sb   = new StringBuilder();
        string date = DateTime.Now.ToString("dd.MM.yyyy HH:mm");

        sb.Append($$"""
            <!DOCTYPE html>
            <html lang="de">
            <head>
            <meta charset="utf-8"/>
            <title>NetOverseer Wochenbericht – {{date}}</title>
            <style>
            body { font-family: 'Segoe UI', sans-serif; margin: 0; background: #0d1117; color: #c9d1d9; }
            .header { background: linear-gradient(135deg,#1565c0,#0d47a1); padding: 32px 40px; }
            .header h1 { margin: 0; font-size: 28px; color: #fff; }
            .header p  { margin: 4px 0 0; color: #90caf9; }
            .content { padding: 32px 40px; }
            .section { margin-bottom: 32px; }
            .section h2 { font-size: 18px; color: #58a6ff; border-bottom: 1px solid #30363d;
                           padding-bottom: 8px; margin-bottom: 16px; }
            .stat-row { display: flex; gap: 16px; margin-bottom: 24px; }
            .stat-card { background: #161b22; border: 1px solid #30363d; border-radius: 8px;
                         padding: 16px 24px; flex: 1; }
            .stat-card .value { font-size: 28px; font-weight: bold; color: #fff; }
            .stat-card .label { font-size: 13px; color: #8b949e; margin-top: 4px; }
            table { width: 100%; border-collapse: collapse; }
            th { text-align: left; color: #8b949e; font-size: 12px; padding: 8px 12px;
                  border-bottom: 1px solid #30363d; }
            td { padding: 10px 12px; border-bottom: 1px solid #21262d; font-size: 14px; }
            tr:hover td { background: #161b22; }
            .rank { color: #8b949e; width: 30px; }
            .danger { color: #f85149; font-weight: bold; }
            .warn   { color: #d29922; }
            .footer { text-align: center; color: #484f58; font-size: 12px;
                       padding: 24px; border-top: 1px solid #21262d; }
            </style>
            </head>
            <body>
            <div class="header">
              <h1>NetOverseer – Wochenbericht</h1>
              <p>Erstellt: {{date}}</p>
            </div>
            <div class="content">

            <div class="stat-row">
              <div class="stat-card">
                <div class="value">{{TotalConnectionsWeek:N0}}</div>
                <div class="label">Verbindungen (letzte 7 Tage)</div>
              </div>
              <div class="stat-card">
                <div class="value">{{TotalDataWeekDisplay}}</div>
                <div class="label">Übertragenes Datenvolumen</div>
              </div>
              <div class="stat-card">
                <div class="value">{{SuspiciousCountWeek}}</div>
                <div class="label">Apps mit verdächtigen Verbindungen</div>
              </div>
            </div>

            <div class="section">
              <h2>Top 5 – datenintensivste Anwendungen</h2>
              <table>
                <tr><th>#</th><th>Anwendung</th><th>Gesendet</th><th>Empfangen</th></tr>
            """);

        int rank = 1;
        foreach (var a in TopApps)
        {
            sb.Append($"""
                <tr>
                  <td class="rank">{rank++}</td>
                  <td>{HtmlEncode(a.ProcessName)}</td>
                  <td>{HtmlEncode(a.BytesSentDisplay)}</td>
                  <td>{HtmlEncode(a.BytesRecvDisplay)}</td>
                </tr>
                """);
        }
        if (TopApps.Count == 0)
            sb.Append("<tr><td colspan='4' style='color:#484f58'>Keine Daten erfasst.</td></tr>");

        sb.Append("""
              </table>
            </div>

            <div class="section">
              <h2>Top 5 – verdächtigste Verbindungen</h2>
              <table>
                <tr><th>Zeitpunkt</th><th>Prozess</th><th>Remote-IP</th><th>Land</th><th>Reputation</th></tr>
            """);

        foreach (var c in SuspiciousConnections)
        {
            string cls = c.ReputationScore < 20 ? "danger" : "warn";
            sb.Append($"""
                <tr>
                  <td>{HtmlEncode(c.TimestampDisplay)}</td>
                  <td>{HtmlEncode(c.ProcessName)}</td>
                  <td>{HtmlEncode(c.RemoteIp)}:{c.RemotePort}</td>
                  <td>{HtmlEncode(c.GeoCountry)}</td>
                  <td class="{cls}">{HtmlEncode(c.ReputationDisplay)}</td>
                </tr>
                """);
        }
        if (SuspiciousConnections.Count == 0)
            sb.Append("<tr><td colspan='5' style='color:#484f58'>Keine verdächtigen Verbindungen.</td></tr>");

        sb.Append($"""
              </table>
            </div>

            </div>
            <div class="footer">Erstellt mit NetOverseer – https://github.com/NetOverseer</div>
            </body></html>
            """);

        return sb.ToString();
    }

    // ── Hilfsmethoden ─────────────────────────────────────────────────────

    private static (DateTimeOffset from, DateTimeOffset to) GetTimeRange(HistoryTimeRange range)
        => range switch
        {
            HistoryTimeRange.LastHour    => (DateTimeOffset.UtcNow.AddHours(-1),  DateTimeOffset.UtcNow),
            HistoryTimeRange.Last24Hours => (DateTimeOffset.UtcNow.AddHours(-24), DateTimeOffset.UtcNow),
            HistoryTimeRange.LastWeek    => (DateTimeOffset.UtcNow.AddDays(-7),   DateTimeOffset.UtcNow),
            _                            => (DateTimeOffset.UtcNow.AddHours(-24), DateTimeOffset.UtcNow),
        };

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024         => $"{bytes / 1_024.0:F1} KB",
        _                => $"{bytes} B"
    };

    private static string HtmlEncode(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
         .Replace("\"", "&quot;").Replace("'", "&#39;");
}
