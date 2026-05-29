# Contributing to NetOverseer

Thank you for considering a contribution!

## Requirements

- Windows 10 19041 or newer (WinUI 3 requirement)
- .NET 8 SDK
- Visual Studio 2022 17.10+ or VS Code with the C# Dev Kit
- Administrator rights at runtime (Windows Firewall + WFP / IP Helper)

## Project structure

```
src/
  NetOverseer.App/          WinUI 3 UI (XAML + ViewModels, MVVM toolkit)
  NetOverseer.BootMonitor/  Separate console EXE for boot-time capture
  NetOverseer.Core/         Platform-agnostic models, interfaces, services
  NetOverseer.Capture/      Native capture implementations (IP Helper, WFP, ETW DNS)
  NetOverseer.Data/         SQLite persistence (repositories, migrations)
tests/
  NetOverseer.Core.Tests/      xUnit unit tests
  NetOverseer.Capture.Tests/   xUnit P/Invoke smoke tests
tools/
  install.ps1 / uninstall.ps1  Installer scripts
  WfpDriver/                   Placeholder for a future kernel driver
```

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for details.

## Build & tests

```powershell
# Restore & build (Debug)
dotnet build NetOverseer.slnx -c Debug

# Full test suite
dotnet test NetOverseer.slnx

# Portable, self-contained publish
dotnet publish src/NetOverseer.App -c Release -r win-x64 --self-contained
```

Before opening a PR, **all tests must be green** and the build **must not
introduce new warnings**.

## Workflow

1. Fork the repository and create a branch: `feature/<name>` or `fix/<bug>`.
2. Keep changes small – one topic per PR.
3. Ship tests / docs alongside (public-API changes → README update).
4. Use meaningful commit messages (ideally
   [Conventional Commits](https://www.conventionalcommits.org/)).
5. Open a pull request against `main` and describe **what** changed,
   **why** (linked issue), and the **test plan**.

## Code style

- C# 12 / .NET 8, `nullable` enabled – no unjustified `!` suppressions.
- **MVVM** with `CommunityToolkit.Mvvm` (`[ObservableProperty]`,
  `[RelayCommand]`); ViewModels live in `NetOverseer.App/ViewModels`, Views
  in `…/Views`. No business logic in code-behind.
- **DI**: register all services through
  `Microsoft.Extensions.DependencyInjection`; no static singletons.
- **P/Invoke** belongs in `NetOverseer.Capture/Native` (or the future
  driver project), not in UI code.
- **Logging**: Serilog. No `Console.WriteLine`. Log-level convention:
  - `Trace`: per-connection events (very noisy)
  - `Debug`: diagnostics
  - `Information`: lifecycle events
  - `Warning`: tolerated failures
  - `Error`: failures that require action
- **Async**: `Async` suffix; `ConfigureAwait(false)` outside UI-thread code.

## Pull request checklist

- [ ] `dotnet build NetOverseer.slnx -c Release` – 0 errors, 0 new warnings
- [ ] `dotnet test NetOverseer.slnx` – all tests green
- [ ] No private paths, tokens, PII, or credentials in the diff
- [ ] Changed / new files carry the SPDX header
      (`// SPDX-License-Identifier: Apache-2.0`)
- [ ] New dependencies are Apache-2.0-compatible and listed in
      `THIRD-PARTY-NOTICES.md`
- [ ] README / `docs/` updated when behavior or usage changes
- [ ] Read and accepted the Code of Conduct
      ([CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md))

## Localization

Resource files live under `src/NetOverseer.App/Strings/<locale>/Resources.resw`.
New language? Submit a PR with a full translation of the resources plus the
matching entry in `LocalizationService`.

## Security vulnerabilities

Please do **not** open public issues for security problems. See
[SECURITY.md](SECURITY.md) and use GitHub *Private Vulnerability Reporting*.

## License of contributions

By submitting a pull request you agree that your contribution will be
licensed under the **Apache License 2.0** (see [LICENSE](LICENSE)).

## Note on AI-assisted contributions

This project was substantially developed with the help of AI tools and is
**not fully tested**. When contributing, please keep in mind:

- Review AI-generated contributions carefully yourself (correctness, license
  notices, no copyrighted snippets from other sources).
- Do not submit code whose license origin you cannot identify.
- State in the PR description if large portions are AI-generated so that
  review effort and accountability remain clear.

The disclaimer in
[README.md](README.md#%EF%B8%8F-important-notice--ai-assisted-development--disclaimer)
and Sections 7 and 8 of the Apache License 2.0 apply.
