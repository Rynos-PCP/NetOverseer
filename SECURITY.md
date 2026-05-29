# Security Policy

## Supported versions

As the project is under active development, security fixes are only provided
for the `main` branch.

## Reporting a vulnerability

Please do **not** report security issues in public GitHub issues. Use GitHub
**Private Vulnerability Reporting** (PVR) instead:

- Repository tab **Security** → **Advisories** → **Report a vulnerability**
- Direct link: <https://github.com/Rynos-PCP/NetOverseer/security/advisories/new>

If PVR is not usable for any reason, the fallback channel is to reach out via
the GitHub account [@Rynos-PCP](https://github.com/Rynos-PCP) – please **do
not** post technical details publicly, only ask for a private contact channel.

When reporting, please include:

- A description of the vulnerability and the affected component
- Steps to reproduce / proof of concept
- Possible impact (severity, affected versions)
- Suggestions for a fix (optional)

We will try to respond promptly. Please give us a reasonable amount of time
to fix the issue before publishing details (coordinated disclosure; 90 days
is customary).

## Note on the nature of the tool

NetOverseer requires administrator privileges and interacts deeply with the
Windows network stack (Windows Filtering Platform, IP Helper API, Windows
Firewall COM, ETW DNS tracing). Behavior that is part of the intended
feature set – for example enumerating all sockets system-wide, reading every
DNS query, or creating Windows Firewall block rules while running as
administrator – is **not** a vulnerability; it requires administrator
privileges by design.

Genuine security issues include, for example:

- Privilege escalation without existing administrator rights
- Code execution by opening an imported report / blocklist file without
  further user action
- Path traversal or write access outside the user-chosen export folder
- Persistent storage of secrets (API keys) in plain text on disk
- Tampering with the DPAPI-protected settings file from an unprivileged
  context
- Denial of service against the host system or other processes through
  malformed input

## Handling of sensitive data

NetOverseer processes potentially sensitive network metadata. The following
mitigations are in place:

- **API keys** (AbuseIPDB, MaxMind) are stored Windows-DPAPI-encrypted under
  `%AppData%\NetOverseer\settings.json` (`DataProtectionScope.CurrentUser`).
- The **history database** (`netoverseer.db`) resides in `%AppData%` and is
  protected by the default Windows ACL of the current user profile.
- **Log files** rotate daily (max 10 MB, 7 files); no IP addresses are
  logged in plaintext at the `Information` level.
- **External requests** can be disabled entirely via the offline mode in
  the settings.
- **Administrator rights** are not requested at start – the user explicitly
  triggers elevation via UAC.

## OWASP Top-10 status

| Risk | Status |
| --- | --- |
| A01 Broken Access Control | Not applicable (single-user desktop) |
| A02 Cryptographic Failures | DPAPI for API keys; HTTPS for external calls |
| A03 Injection | SQLite via parameterized queries; no dynamic SQL |
| A04 Insecure Design | Threat model in [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) |
| A05 Misconfiguration | Safe defaults; no open ports |
| A06 Vulnerable Dependencies | Versions pinned in csproj; Dependabot recommended |
| A07 Authentication & Identity | Not applicable |
| A08 Software / Data Integrity | Code signing planned for release pipeline |
| A09 Logging Failures | Serilog with rotation; no secrets in logs |
| A10 SSRF | External URLs are hard-wired (Firehol, Spamhaus, AbuseIPDB) |
