// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Reactive.Subjects;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.Capture;

/// <summary>
/// Zeichnet DNS-Anfragen in Echtzeit über ETW auf.
/// Verwendet den Microsoft-Windows-DNS-Client-Provider (Event ID 3008).
/// Erfordert Administratorrechte.
/// </summary>
public sealed class DnsEtwCapture : IDnsCapture, IDisposable
{
    // ──────────────────────────────────────────────────────────────────────
    // Konstanten
    // ──────────────────────────────────────────────────────────────────────

    private const string SessionName = "NetOverseer-DNS-Capture";

    // Provider: Microsoft-Windows-DNS-Client
    private static readonly Guid DnsClientGuid =
        new("1C95126E-7EEA-49A9-A3FE-A378B03DDB4D");

    // ──────────────────────────────────────────────────────────────────────
    // Felder
    // ──────────────────────────────────────────────────────────────────────

    private readonly IDnsCategoryService               _categoryService;
    private readonly ILogger<DnsEtwCapture>            _logger;
    private readonly Subject<DnsQueryEvent>            _subject = new();

    private TraceEventSession? _session;
    private Task?              _processTask;
    private volatile bool      _running;
    private bool               _disposed;

    private long _eventsReceived;
    private long _eventsAccepted;
    private DateTimeOffset? _startedAt;
    private bool _firstEventLogged;

    // ──────────────────────────────────────────────────────────────────────
    // IDnsCapture
    // ──────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IObservable<DnsQueryEvent> Queries => _subject;

    /// <inheritdoc/>
    public bool IsRunning => _running;

    /// <inheritdoc/>
    public long EventsReceived => System.Threading.Interlocked.Read(ref _eventsReceived);

    /// <inheritdoc/>
    public long EventsAccepted => System.Threading.Interlocked.Read(ref _eventsAccepted);

    /// <inheritdoc/>
    public DateTimeOffset? StartedAt => _startedAt;

    /// <inheritdoc/>
    public IReadOnlyDictionary<int, string> EventSchemas => _eventSchemas;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> _eventSchemas = new();

    // ──────────────────────────────────────────────────────────────────────
    // Konstruktor
    // ──────────────────────────────────────────────────────────────────────

    public DnsEtwCapture(
        IDnsCategoryService    categoryService,
        ILogger<DnsEtwCapture> logger)
    {
        _categoryService = categoryService;
        _logger          = logger;
    }

    // ──────────────────────────────────────────────────────────────────────
    // StartAsync / StopAsync
    // ──────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken ct = default)
    {
        if (_running) return Task.CompletedTask;

        // Ggf. vorhandene Session sauber schließen (z.B. nach App-Absturz)
        try { TraceEventSession.GetActiveSession(SessionName)?.Stop(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Vorherige DNS-ETW-Session konnte nicht gestoppt werden"); }

        try
        {
            _session = new TraceEventSession(SessionName)
            {
                StopOnDispose = true,
            };

            // Verbose erfasst alle DNS-Event-Levels (Informational, Warning, Error, Verbose)
            _session.EnableProvider(
                DnsClientGuid,
                TraceEventLevel.Verbose,
                ulong.MaxValue);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex,
                "DNS-ETW-Session konnte nicht erstellt werden – Administratorrechte fehlen.");
            _session?.Dispose();
            _session = null;
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DNS-ETW-Session konnte nicht erstellt werden (Provider {Guid}).", DnsClientGuid);
            _session?.Dispose();
            _session = null;
            throw;
        }

        // Dynamic.All: manifest-basierte ETW-Events (Windows 10/11 DNS-Client)
        _session.Source.Dynamic.All += OnTraceEvent;
        // UnhandledEvents: Fallback für klassische MOF-basierte Events
        _session.Source.UnhandledEvents += OnTraceEvent;

        _running          = true;
        _startedAt        = DateTimeOffset.UtcNow;
        _firstEventLogged = false;
        System.Threading.Interlocked.Exchange(ref _eventsReceived, 0);
        System.Threading.Interlocked.Exchange(ref _eventsAccepted, 0);

        _logger.LogInformation("DNS-ETW-Session gestartet (Provider: {Guid})", DnsClientGuid);

        // ProcessTrace() blockiert – auf separatem Thread ausführen
        _processTask = Task.Run(() =>
        {
            try
            {
                _session!.Source.Process();
                _logger.LogInformation("DNS-ETW-Process-Loop regulär beendet.");
            }
            catch (Exception ex) when (!_disposed)
            {
                _logger.LogError(ex, "Fehler in der DNS-ETW-Verarbeitung");
                _subject.OnError(ex);
            }
        }, ct);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        if (!_running) return;
        _running = false;

        var session = _session;

        // Handler abmelden, bevor die Session verschwindet. Andernfalls bleiben sie
        // an der alten Source.Dynamic hängen und ein erneutes StartAsync() würde zu
        // doppelt verarbeiteten Events und akkumulierenden Sessions führen.
        if (session is not null)
        {
            session.Source.Dynamic.All -= OnTraceEvent;
            session.Source.UnhandledEvents -= OnTraceEvent;
            session.Stop();
        }

        if (_processTask is not null)
        {
            await _processTask.ConfigureAwait(false);
            _processTask = null;
        }

        // Managed Session-Objekt freigeben (kernel-seitige Session wurde via Stop()
        // bereits beendet) und Referenz löschen, damit ein nächstes StartAsync() sauber
        // eine frische Session erstellt.
        session?.Dispose();
        if (ReferenceEquals(_session, session))
            _session = null;

        _logger.LogInformation("DNS-ETW-Session gestoppt.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Ereignis-Verarbeitung
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Event-IDs, deren Payload-Schema bereits geloggt wurde.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, byte> _loggedEventIds = new();

    private void OnTraceEvent(TraceEvent evt)
    {
        if (evt.ProviderGuid != DnsClientGuid) return;

        System.Threading.Interlocked.Increment(ref _eventsReceived);

        if (!_firstEventLogged)
        {
            _firstEventLogged = true;
            _logger.LogInformation(
                "Erstes DNS-ETW-Event empfangen: ID={Id} Name={Name}",
                (int)evt.ID, evt.EventName);
        }

        // Diagnose: pro Event-ID einmalig das vollständige Payload-Schema loggen.
        // So sieht man im Log sofort, welche Felder dieser Windows-Build liefert.
        var eventId = (int)evt.ID;
        if (_loggedEventIds.TryAdd(eventId, 0))
        {
            try
            {
                var names = evt.PayloadNames ?? Array.Empty<string>();
                var schema = string.Join(", ", names.Select(n =>
                {
                    var v = SafePayloadString(evt, n);
                    if (v.Length > 80) v = v[..77] + "…";
                    return $"{n}=\"{v}\"";
                }));
                _eventSchemas[eventId] = $"{evt.EventName} {{ {schema} }}";
                _logger.LogInformation(
                    "DNS-ETW Event-Schema ID={Id} Name={EventName}: {{ {Schema} }}",
                    eventId, evt.EventName, schema);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Schema-Log fehlgeschlagen für Event-ID {Id}", eventId);
            }
        }

        try
        {
            // QueryName aus allen bekannten Feld-Aliassen extrahieren.
            // Verschiedene Windows-Builds nutzen unterschiedliche Namen.
            var queryName = ExtractQueryName(evt);
            if (string.IsNullOrEmpty(queryName)) return;

            // Loopback-/sehr kurze Anfragen filtern (Windows fragt häufig "."
            // und einzelne Buchstaben für interne Tests ab).
            if (queryName.Length <= 1) return;

            var queryTypeRaw = SafePayloadUInt16(evt, "QueryType");
            if (queryTypeRaw == 0)
                queryTypeRaw = SafePayloadUInt16(evt, "Type");

            var queryResults = SafePayloadString(evt, "QueryResults");
            if (string.IsNullOrEmpty(queryResults))
                queryResults = SafePayloadString(evt, "Results");
            if (string.IsNullOrEmpty(queryResults))
                queryResults = SafePayloadString(evt, "Address");

            var responseCode = SafePayloadUInt32(evt, "QueryStatus");
            if (responseCode == 0)
                responseCode = SafePayloadUInt32(evt, "ResponseCode");
            if (responseCode == 0)
                responseCode = SafePayloadUInt32(evt, "Status");

            var resolvedAddresses = ParseQueryResults(queryResults);
            var queryType         = MapQueryType(queryTypeRaw);
            var category          = _categoryService.Classify(queryName);

            // evt.TimeStamp ist typischerweise Kind=Local oder Unspecified.
            // Bei DateTimeOffset(localDt, TimeSpan.Zero) wirft .NET ArgumentException.
            // Daher explizit nach UTC normalisieren.
            var ts = evt.TimeStamp;
            DateTimeOffset timestamp = ts.Kind switch
            {
                DateTimeKind.Utc         => new DateTimeOffset(ts, TimeSpan.Zero),
                DateTimeKind.Local       => new DateTimeOffset(ts).ToUniversalTime(),
                _ /* Unspecified */      => new DateTimeOffset(
                                                DateTime.SpecifyKind(ts, DateTimeKind.Local))
                                            .ToUniversalTime(),
            };

            var dnsEvent = new DnsQueryEvent
            {
                Timestamp          = timestamp,
                ProcessId          = evt.ProcessID,
                ProcessName        = evt.ProcessName ?? string.Empty,
                QueryName          = queryName,
                QueryType          = queryType,
                ResolvedAddresses  = resolvedAddresses,
                Category           = category,
                WasSuccessful      = responseCode == 0,
            };

            System.Threading.Interlocked.Increment(ref _eventsAccepted);
            _subject.OnNext(dnsEvent);
        }
        catch (Exception ex)
        {
            // WARNUNG statt Debug, damit Drop-Gründe im Standard-Log sichtbar sind.
            _logger.LogWarning(ex,
                "DNS-Ereignis konnte nicht verarbeitet werden (ID {Id}, EventName={Name})",
                eventId, evt.EventName);
        }
    }

    /// <summary>
    /// Versucht den abgefragten Domain-Namen aus dem Event-Payload zu extrahieren.
    /// Probiert mehrere bekannte Feldnamen sowie eine generische Heuristik
    /// (jedes String-Feld mit „name" oder „query" im Bezeichner).
    /// </summary>
    private static string ExtractQueryName(TraceEvent evt)
    {
        // Bekannte Aliase – nach Häufigkeit sortiert.
        string[] knownAliases = ["QueryName", "Name", "DnsName", "Query", "QueryString"];
        foreach (var alias in knownAliases)
        {
            var v = SafePayloadString(evt, alias);
            if (!string.IsNullOrEmpty(v)) return v;
        }

        // Heuristik: jedes Feld mit "name" im Bezeichner durchsuchen.
        try
        {
            var names = evt.PayloadNames;
            if (names is null) return string.Empty;
            foreach (var n in names)
            {
                if (n.Contains("name", StringComparison.OrdinalIgnoreCase)
                 || n.Contains("query", StringComparison.OrdinalIgnoreCase))
                {
                    var v = SafePayloadString(evt, n);
                    if (!string.IsNullOrEmpty(v) && v.Length > 1 && v.Contains('.'))
                        return v;
                }
            }
        }
        catch { /* ignore */ }

        return string.Empty;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Hilfsmethoden
    // ──────────────────────────────────────────────────────────────────────

    private static string SafePayloadString(TraceEvent evt, string name)
    {
        try { return evt.PayloadStringByName(name) ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static ushort SafePayloadUInt16(TraceEvent evt, string name)
    {
        try { return Convert.ToUInt16(evt.PayloadByName(name)); }
        catch { return 0; }
    }

    private static uint SafePayloadUInt32(TraceEvent evt, string name)
    {
        try { return Convert.ToUInt32(evt.PayloadByName(name)); }
        catch { return 0; }
    }

    /// <summary>
    /// Parst das QueryResults-Format des DNS-Client-Providers.
    /// Format: "type:1 1.2.3.4;type:28 ::1;type:5 alias.example.com;"
    /// Gibt alle IP-Adressen (Typ 1 = A, Typ 28 = AAAA) zurück.
    /// </summary>
    private static string[] ParseQueryResults(string queryResults)
    {
        if (string.IsNullOrWhiteSpace(queryResults))
            return [];

        var addresses = new List<string>();

        foreach (var part in queryResults.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (!trimmed.StartsWith("type:", StringComparison.OrdinalIgnoreCase)) continue;

            // Format: "type:1 1.2.3.4" oder "type:28 2001:db8::1"
            var spaceIdx = trimmed.IndexOf(' ');
            if (spaceIdx < 0) continue;

            var typeStr = trimmed[5..spaceIdx]; // nach "type:"
            var value   = trimmed[(spaceIdx + 1)..].Trim();

            if (!ushort.TryParse(typeStr, out var type)) continue;

            // Typ 1 = A (IPv4), Typ 28 = AAAA (IPv6)
            if (type is 1 or 28 && !string.IsNullOrEmpty(value))
                addresses.Add(value);
        }

        return [.. addresses];
    }

    private static DnsQueryType MapQueryType(ushort type) => type switch
    {
        1  => DnsQueryType.A,
        2  => DnsQueryType.Ns,
        5  => DnsQueryType.CName,
        12 => DnsQueryType.Ptr,
        15 => DnsQueryType.Mx,
        16 => DnsQueryType.Txt,
        28 => DnsQueryType.Aaaa,
        33 => DnsQueryType.Srv,
        _  => DnsQueryType.Other,
    };

    // ──────────────────────────────────────────────────────────────────────
    // IDisposable
    // ──────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _running  = false;

        _session?.Dispose();
        _subject.OnCompleted();
        _subject.Dispose();
    }
}
