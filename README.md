# NetOverseer

> **Real-time network transparency for Windows** — See exactly which application connects to which server, get geolocation and reputation scores for remote IPs, and block connections via Windows Firewall.

[![Build Status](https://github.com/Rynos-PCP/NetOverseer/actions/workflows/build.yml/badge.svg)](https://github.com/Rynos-PCP/NetOverseer/actions)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-green.svg)](LICENSE)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%2021H2%2B-blue)](https://www.microsoft.com/windows)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/)
[![AI-assisted](https://img.shields.io/badge/AI--assisted-yes-orange)](#%EF%B8%8F-important-notice--ai-assisted-development--disclaimer)

---

## ⚠️ Important Notice – AI-Assisted Development & Disclaimer

This project was **substantially developed with the help of AI tools** (Large
Language Models). The following points apply and must be understood before
any use:

- **Not every feature has been fully tested.** There is no comprehensive test
  coverage; individual modules may contain bugs, race conditions, or
  unexpected behavior.
- **Deep system interaction:** The tool requires administrator privileges and
  interacts with the Windows Filtering Platform, IP Helper API, Windows
  Firewall COM, and ETW DNS tracing. Bugs in such components may destabilize
  the network stack, drop legitimate traffic, or interfere with other
  security software.
- **No claim of correctness, completeness, or security.** The software is
  provided "AS IS", without any express or implied warranty.
- **No liability of the author.** The author (`Rynos-PCP`) accepts **no
  liability** for any damages arising from the use, malfunction, misuse, or
  derivative use of this software – in particular but not limited to:
  - data loss or data corruption,
  - system crashes, network outages, or boot failures,
  - damage to hardware or any physical consequential damage,
  - security incidents, privacy breaches, or legal violations,
  - lost profit, business interruption, or any other consequential damages.
- **Use only on authorized systems.** Use this tool exclusively on systems
  for which you have explicit authorization to monitor. Lawful use is
  entirely your responsibility.

The legal basis of this disclaimer is formed by Sections **7 ("Disclaimer of
Warranty")** and **8 ("Limitation of Liability")** of the Apache License 2.0
(see [LICENSE](LICENSE)). By using, cloning, or forking this repository, you
accept these terms.

---

## Features

- **Live connection monitor** — real-time view of all active TCP/UDP connections with process, IP, port, hostname, and traffic rates
- **Geolocation** — offline country/city lookup via MaxMind GeoLite2 (no cloud dependency)
- **Reputation scoring** — AbuseIPDB integration + local blocklists (Firehol, Spamhaus DROP)
- **One-click firewall blocking** — block an app or IP directly via Windows Firewall
- **DNS query capture** — ETW-based DNS monitoring, tracker/telemetry categorization
- **Windows Telemetry dashboard** — visualize and measure Microsoft telemetry traffic
- **Startup connections** — record which processes phone home in the first 60 seconds after boot
- **History & reporting** — SQLite-backed history with weekly HTML reports

---

## Screenshots

> *Screenshots will be added after the UI milestone (Phase 1) is complete.*

---

## Quick Start

### Prerequisites

- Windows 10 21H2 or Windows 11
- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64)
- **Administrator rights** (required for WFP engine access and firewall management)

### Download & Install

1. Download the latest release from [Releases](https://github.com/Rynos-PCP/NetOverseer/releases)
2. Run `install.ps1` as Administrator **or** extract the ZIP and run `NetOverseer.App.exe`
3. Accept the UAC prompt — admin rights are needed for network capture

### Optional: MaxMind GeoLite2 Database

For full geolocation (city-level), download the free GeoLite2-City database:

1. Create a free account at [MaxMind](https://www.maxmind.com/en/geolite2/signup)
2. Download `GeoLite2-City.mmdb`
3. Place it in `%AppData%\NetOverseer\geoip\GeoLite2-City.mmdb`

Without the database, NetOverseer still categorizes well-known infrastructure (Microsoft, Google, Cloudflare, etc.).

---

## Build from Source

### Requirements

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) 1.6+
- Visual Studio 2022 17.8+ **or** VS Code with C# Dev Kit

### Build Steps

```powershell
git clone https://github.com/Rynos-PCP/NetOverseer.git
cd NetOverseer

# Restore NuGet packages
dotnet restore

# Build all projects
dotnet build

# Run tests
dotnet test

# Run the app (requires admin)
# Right-click → Run as Administrator, or:
Start-Process powershell -Verb RunAs -ArgumentList "dotnet run --project src/NetOverseer.App"
```

### Project Structure

```
NetOverseer/
├── src/
│   ├── NetOverseer.App/       ← WinUI 3 UI (ViewModels, Pages, XAML)
│   ├── NetOverseer.Core/      ← Business logic, Services, Models, Interfaces
│   ├── NetOverseer.Capture/   ← WFP + IP Helper network capture (P/Invoke)
│   └── NetOverseer.Data/      ← SQLite repositories, migrations
├── tests/
│   ├── NetOverseer.Core.Tests/
│   └── NetOverseer.Capture.Tests/
└── tools/
    └── WfpDriver/             ← Future: Native C++ WFP kernel callout driver
```

---

## Architecture

| Component | Technology | Purpose |
|-----------|-----------|---------|
| UI | WinUI 3 (Windows App SDK) | Modern Windows 11 UI with Mica |
| Capture | IP Helper API + WFP | Connection enumeration & firewall control |
| Geolocation | MaxMind GeoLite2 (local) | Offline country/city lookup |
| Reputation | AbuseIPDB API + local lists | IP reputation scoring |
| Firewall | NetFwTypeLib COM | Windows Firewall rule management |
| Storage | SQLite (Microsoft.Data.Sqlite) | Local connection history |
| Logging | Serilog | Structured file logging |

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the full architecture diagram.

---

## Privacy

NetOverseer is designed with privacy in mind:

- **No telemetry** — NetOverseer itself does not send any data anywhere
- **Local-first** — all data stays on your machine
- **Optional cloud** — AbuseIPDB and MaxMind APIs are opt-in (API key required)
- **"No external requests" mode** — run entirely offline using local blocklists

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup, code style, and PR guidelines.

---

## Notes / Ethical use

- **WFP / IP Helper / ETW** require administrator privileges. Without
  them, the live connection monitor, DNS capture, and firewall actions
  do not work.
- **Ethical use – important:** Use this tool exclusively on systems for
  which you have **explicit authorization** to monitor (e.g. your own
  devices, test / development machines assigned to you, documented
  penetration testing engagements). Capturing connections, DNS queries,
  or telemetry of foreign users without authorization may, depending on
  jurisdiction, violate criminal law (e.g. §§ 202a/c StGB in Germany;
  the Computer Fraud and Abuse Act in the United States), or breach
  employment or data protection law (e.g. GDPR). The author accepts
  **no liability for misuse** – see the disclaimer above and Sections
  7–8 of the Apache License 2.0.
- The app is intended for analysis and transparency purposes – use it at
  your own risk.

## License

The source code of this project is licensed under the **Apache License 2.0**
(see [LICENSE](LICENSE)).

Third-party components and their licenses are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). External data sources such
as the MaxMind GeoLite2 database and the AbuseIPDB API are **not** bundled
and are subject to their own license terms.

---

## Acknowledgements

- [MaxMind GeoLite2](https://www.maxmind.com) — free IP geolocation database
- [AbuseIPDB](https://www.abuseipdb.com) — IP reputation API
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM framework
- [Firehol](https://github.com/firehol/blocklist-ipsets) — IP blocklists
