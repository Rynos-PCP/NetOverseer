# Architecture – NetOverseer

> This file describes the end-to-end architecture. For contribution conventions see
> [`CONTRIBUTING.md`](../CONTRIBUTING.md), for security aspects see
> [`SECURITY.md`](../SECURITY.md).

## Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│                    NetOverseer.App  (WinUI 3)                        │
│  ┌────────────┐  ┌────────────────┐  ┌─────────────────────────────┐ │
│  │   Views    │←─│   ViewModels   │←─│        Services / DI        │ │
│  │  (XAML)    │  │ (MVVM Toolkit) │  │  (Microsoft.Extensions.Host)│ │
│  └────────────┘  └────────────────┘  └─────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────┘
                                  │
                                  ▼
┌──────────────────────────────────────────────────────────────────────┐
│                    NetOverseer.Core                                  │
│   Domain Models • Interfaces • Reputation • DNS • Settings • Geo     │
└──────────────────────────────────────────────────────────────────────┘
        │                                                  │
        ▼                                                  ▼
┌──────────────────────┐                       ┌────────────────────────┐
│ NetOverseer.Capture  │                       │  NetOverseer.Data      │
│ • IpHelperCapture    │                       │ • SQLite               │
│ • WfpNetworkCapture  │                       │ • Repositories         │
│ • DnsEtwCapture      │                       │ • Migrations           │
│ • Native P/Invoke    │                       │ • PersistenceWorker    │
└──────────────────────┘                       └────────────────────────┘
        │
        ▼
   Windows API (iphlpapi.dll, fwpuclnt.dll, ETW Provider)
```

## Building Blocks

### NetOverseer.App

- **WinUI 3** via `Microsoft.WindowsAppSDK 1.8`
- **MVVM**: `CommunityToolkit.Mvvm 8.4` (`[ObservableProperty]`, `[RelayCommand]`)
- **Navigation**: `NavigationView` with `INavigationService` (page lookup via
  [`PageKeys`](../src/NetOverseer.App/PageKeys.cs))
- **DI/Hosting**: `Microsoft.Extensions.Hosting.CreateDefaultBuilder` + Serilog
- **Localization**: `Windows.ApplicationModel.Resources` with
  `SetProcessPreferredUILanguages` for unpackaged apps

### NetOverseer.Core

- Pure .NET library, no UI dependencies
- **Reactive**: `System.Reactive` (`IObservable<ConnectionEvent>`)
- **Reputation**: Local-first lookup (Private → Infra → Blocklist → Cache → AbuseIPDB)
- **Settings**: JSON at `%AppData%\NetOverseer\settings.json` with DPAPI encryption
  for API keys
- **Geo**: MaxMind GeoLite2 City + hard-coded CIDR ranges for Microsoft/Google/Cloudflare

### NetOverseer.Capture

- **`IpHelperCapture`**: Polling via `GetExtendedTcpTable` / `GetExtendedUdpTable`
  (IPv4 + IPv6). Default interval 500 ms. `ConcurrentDictionary` as tracking table.
- **`WfpNetworkCapture`**: Validates WFP engine access (admin check) and delegates
  connection tracking to `IpHelperCapture`. Designed for extension with a
  kernel callout driver (`tools/WfpDriver/`).
- **`DnsEtwCapture`**: ETW subscription to `Microsoft-Windows-DNS-Client`.

### NetOverseer.Data

- **SQLite** via `Microsoft.Data.Sqlite` (no EF Core – faster, less overhead)
- **Migrations**: Versioned schema in `Migrations/V0001_*.sql`
- **`PersistenceWorker`**: Batches `ConnectionEvent`s in 5-second chunks and writes
  them asynchronously to the database
- **`DatabaseMaintenanceService`**: Daily `VACUUM` + retention cleanup

## Data Flow: Live Connection

```
Windows Kernel                       ↓ syscall
   GetExtendedTcpTable
        │
        ▼
   IpHelperCapture                    ↓ Subject<ConnectionEvent>
        │
        ▼  IObservable
   LiveConnectionsViewModel           ↓ Throttle 200 ms
        │
        ▼ via DispatcherQueue
   ListView (UI)

   parallel:
        │
        ▼  PersistenceWorker
   ConnectionRepository → SQLite
```

## Threading

| Component                        | Thread                                          |
| -------------------------------- | ----------------------------------------------- |
| Polling loop (`IpHelperCapture`) | ThreadPool (`Task.Run` in loop)                 |
| `Subject<ConnectionEvent>`       | Published from polling thread                   |
| `LiveConnectionsViewModel`       | Batched via `DispatcherQueueTimer` on UI thread |
| `PersistenceWorker`              | Dedicated background worker                     |
| `BlocklistService.UpdateAsync`   | ThreadPool, single-flight via `SemaphoreSlim`   |

All `ObservableCollection`s visible to the UI are mutated exclusively from the
**DispatcherQueue**.

## Persistence Locations

| Path                                                    | Contents                               |
| ------------------------------------------------------- | -------------------------------------- |
| `%AppData%\NetOverseer\settings.json`                   | App settings (keys DPAPI-encrypted)    |
| `%AppData%\NetOverseer\netoverseer.db`                  | SQLite connection history              |
| `%AppData%\NetOverseer\startup.db`                      | Boot trace database                    |
| `%AppData%\NetOverseer\blocklists\combined.netset`      | Cache: Firehol + Spamhaus              |
| `%AppData%\NetOverseer\geoip\GeoLite2-City.mmdb`        | MaxMind GeoLite2 (user-installed)      |
| `%AppData%\NetOverseer\logs\netoverseer-YYYY-MM-DD.log` | Rolling Serilog logs                   |

## Extension Points

| Task                              | Hook                                                           |
| --------------------------------- | -------------------------------------------------------------- |
| New capture backend               | Implement `INetworkCapture`, register in `App.xaml.cs`         |
| Additional reputation source      | Implement `IReputationService` decoration or extend `ReputationService` |
| Additional blocklists             | Extend `BlocklistService.Update`                               |
| New language                      | Add `Strings/<locale>/Resources.resw`                          |
| Custom toast actions              | `NotificationService` (`Microsoft.Windows.AppNotifications`)   |

## Threat Model (Summary)

- **Attacker model**: Local malware process with user privileges.
- **Asset**: Network visibility (read), firewall rules (write).
- **Trusted components**: Windows Kernel, DPAPI, signed Microsoft DLLs.
- **Risk mitigations**:
  - Settings directory resides in `%AppData%` → per-user ACL protection
  - API keys via DPAPI → plaintext not readable by other profiles
  - HTTPS for all external calls; offline mode disables network access entirely

## Roadmap Status

See [`../Prompt.md`](../Prompt.md) for the original 24-week plan. Current
implementation status: all phases 0–2 implemented in their basic form; phase 3 (CI,
MSIX, signing) in progress.
