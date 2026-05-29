// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;
using Windows.UI;

namespace NetOverseer.App.ViewModels;

/// <summary>
/// ViewModel für das Hauptfenster: Capture-Steuerung, Status-Anzeige, Durchsatz.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly INetworkCapture _capture;
    private readonly IPersistenceWorker _persistenceWorker;
    private readonly ILogger<MainViewModel> _logger;

    private IDisposable?            _subscription;
    private CancellationTokenSource? _cts;

    /// <summary>Aktuell offene Verbindungen, indexiert nach ConnectionKey.
    /// Wird über den Capture-Stream synchron gepflegt.</summary>
    private readonly ConcurrentDictionary<string, byte> _activeKeys = new();

    /// <summary>Monoton zählender Gesamtwert neuer (eindeutiger) Verbindungen
    /// seit Start der aktuellen Capture-Session.</summary>
    private long _newConnectionsRaw;

    private bool                    _disposed;

    // ──────────────────────────────────────────────────────────────
    // Observable Properties
    // ──────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    [NotifyPropertyChangedFor(nameof(ToggleIcon))]
    [NotifyPropertyChangedFor(nameof(ToggleButtonText))]
    [NotifyPropertyChangedFor(nameof(ToggleToolTip))]
    [NotifyPropertyChangedFor(nameof(IsNotCapturing))]
    private bool _isCapturing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveConnectionsText))]
    private int _activeConnections;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewConnectionsText))]
    private long _newConnectionsTotal;

    [ObservableProperty] private string _bytesPerSecondUp   = "–";
    [ObservableProperty] private string _bytesPerSecondDown = "–";

    // ──────────────────────────────────────────────────────────────
    // Computed Properties (UI-Binding)
    // ──────────────────────────────────────────────────────────────

    public string StatusText    => IsCapturing ? "● Überwachung aktiv" : "● Gestoppt";
    public string ToggleIcon    => IsCapturing ? "\uE71A" : "\uE768";   // Stop / Play
    public string ToggleButtonText => IsCapturing ? "Stoppen" : "Starten";
    public string ToggleToolTip => IsCapturing ? "Überwachung stoppen" : "Überwachung starten";

    /// <summary>Inverses von <see cref="IsCapturing"/> – für Button-IsEnabled-Bindings.</summary>
    public bool IsNotCapturing => !IsCapturing;

    /// <summary>Aktuell offene Verbindungen (Snapshot) – linker Statusleisten-Text.</summary>
    public string ActiveConnectionsText => $"{ActiveConnections} aktive Verbindungen";

    /// <summary>Monoton zählender Gesamtwert (rechter Statusleisten-Text).</summary>
    public string NewConnectionsText => $"{NewConnectionsTotal:N0} gesamt";

    public SolidColorBrush StatusBrush => IsCapturing
        ? new SolidColorBrush(Color.FromArgb(255, 16, 124, 16))   // Grün
        : new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)); // Grau

    // ──────────────────────────────────────────────────────────────
    // Konstruktor
    // ──────────────────────────────────────────────────────────────

    public MainViewModel(
        INetworkCapture capture,
        IPersistenceWorker persistenceWorker,
        ILogger<MainViewModel> logger)
    {
        _capture           = capture;
        _persistenceWorker = persistenceWorker;
        _logger            = logger;
    }

    // ──────────────────────────────────────────────────────────────
    // Commands
    // ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ToggleCaptureAsync()
    {
        if (IsCapturing)
            await StopCaptureInternalAsync();
        else
            await StartCaptureInternalAsync();
    }

    /// <summary>Capture starten (idempotent – tut nichts wenn bereits aktiv).</summary>
    [RelayCommand]
    public async Task StartCaptureAsync()
    {
        if (IsCapturing) return;
        await StartCaptureInternalAsync();
    }

    /// <summary>Capture stoppen (idempotent – tut nichts wenn nicht aktiv).</summary>
    [RelayCommand]
    public async Task StopCaptureAsync()
    {
        if (!IsCapturing) return;
        await StopCaptureInternalAsync();
    }

    /// <summary>Capture stoppen und neu starten (z. B. nach Settings-Änderung).</summary>
    [RelayCommand]
    public async Task RestartCaptureAsync()
    {
        if (IsCapturing)
            await StopCaptureInternalAsync();
        await StartCaptureInternalAsync();
    }

    // ──────────────────────────────────────────────────────────────
    // Private Helpers
    // ──────────────────────────────────────────────────────────────

    private async Task StartCaptureInternalAsync()
    {
        try
        {
            _cts = new CancellationTokenSource();

            // Zähler beim Neustart zurücksetzen
            _activeKeys.Clear();
            System.Threading.Interlocked.Exchange(ref _newConnectionsRaw, 0);
            ActiveConnections    = 0;
            NewConnectionsTotal  = 0;

            _subscription = _capture.Connections
                .Subscribe(OnConnectionEvent);

            await _capture.StartAsync(_cts.Token);

            // Persistenz-Worker schreibt Connection-/AppProfile-Daten in SQLite.
            // Ohne diesen Worker bleibt die Anwendungsseite leer.
            await _persistenceWorker.StartAsync(_cts.Token);

            IsCapturing = true;

            _ = PollConnectionCountAsync(_cts.Token);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Admin-Rechte für die Netzwerkerfassung fehlen");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Starten der Capture");
        }
    }

    private async Task StopCaptureInternalAsync()
    {
        try
        {
            await _cts!.CancelAsync();
            _subscription?.Dispose();
            _subscription = null;

            await _capture.StopAsync();

            if (_persistenceWorker.IsRunning)
                await _persistenceWorker.StopAsync();

            IsCapturing = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Stoppen der Capture");
        }
    }

    private async Task PollConnectionCountAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            ActiveConnections   = _activeKeys.Count;
            NewConnectionsTotal = System.Threading.Interlocked.Read(ref _newConnectionsRaw);
            try { await Task.Delay(500, ct); } catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Wird auf einem Hintergrund-Thread vom Capture-Stream aufgerufen.
    /// Aktive Keys werden eindeutig per ConcurrentDictionary nachgehalten;
    /// der "gesamt"-Zähler steigt nur bei eindeutig neuen Verbindungen.
    /// </summary>
    private void OnConnectionEvent(ConnectionEvent ev)
    {
        if (ev.State == ConnectionState.Closed)
        {
            _activeKeys.TryRemove(ev.ConnectionKey, out _);
            return;
        }

        if (_activeKeys.TryAdd(ev.ConnectionKey, 0))
            System.Threading.Interlocked.Increment(ref _newConnectionsRaw);
    }

    // ──────────────────────────────────────────────────────────────
    // IDisposable
    // ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _subscription?.Dispose();
        _cts?.Cancel();
        _cts?.Dispose();

        if (_persistenceWorker.IsRunning)
            _ = _persistenceWorker.StopAsync();

        if (IsCapturing)
            _ = _capture.StopAsync();
    }
}
