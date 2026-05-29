# Third-Party Notices for NetOverseer

NetOverseer uses the following third-party components. Each component is
provided under its own license, listed below.

---

## Bundled NuGet packages

| Package | License | Project |
|---|---|---|
| CommunityToolkit.Mvvm | MIT | https://github.com/CommunityToolkit/dotnet |
| CommunityToolkit.WinUI.UI.Controls | MIT | https://github.com/CommunityToolkit/Windows |
| Microsoft.WindowsAppSDK | MIT | https://github.com/microsoft/WindowsAppSDK |
| Microsoft.Windows.SDK.BuildTools | MIT | https://github.com/microsoft/Windows-SDK |
| Microsoft.Extensions.DependencyInjection | MIT | https://github.com/dotnet/runtime |
| Microsoft.Extensions.DependencyInjection.Abstractions | MIT | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Hosting | MIT | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Hosting.Abstractions | MIT | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Logging | MIT | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Logging.Abstractions | MIT | https://github.com/dotnet/runtime |
| Microsoft.Data.Sqlite | MIT | https://github.com/dotnet/efcore |
| Microsoft.Diagnostics.Tracing.TraceEvent | MIT | https://github.com/microsoft/perfview |
| MaxMind.GeoIP2 | Apache-2.0 | https://github.com/maxmind/GeoIP2-dotnet |
| System.Reactive | MIT | https://github.com/dotnet/reactive |
| System.Drawing.Common | MIT | https://github.com/dotnet/runtime |
| System.ServiceProcess.ServiceController | MIT | https://github.com/dotnet/runtime |
| System.Security.Cryptography.ProtectedData | MIT | https://github.com/dotnet/runtime |
| Serilog | Apache-2.0 | https://github.com/serilog/serilog |
| Serilog.Sinks.File | Apache-2.0 | https://github.com/serilog/serilog-sinks-file |
| Serilog.Extensions.Hosting | Apache-2.0 | https://github.com/serilog/serilog-extensions-hosting |
| Serilog.Extensions.Logging | Apache-2.0 | https://github.com/serilog/serilog-extensions-logging |

Refer to each project's repository for the full license text.

---

## Optional external data (NOT bundled)

The following data sets are referenced at runtime if the user provides them
or enables the corresponding feature. They are **not** included in standard
builds and must be obtained separately under their own license terms:

| Component | License | Project |
|---|---|---|
| MaxMind GeoLite2 City database (`GeoLite2-City.mmdb`) | Creative Commons Attribution-ShareAlike 4.0 + MaxMind EULA | https://dev.maxmind.com/geoip/geolite2-free-geolocation-data |
| AbuseIPDB Reputation API | AbuseIPDB Terms of Service | https://www.abuseipdb.com/ |
| Firehol IP blocklists | MIT-style attribution | https://github.com/firehol/blocklist-ipsets |
| Spamhaus DROP list | Spamhaus Terms of Use | https://www.spamhaus.org/drop/ |

When using these data sources, review and comply with their respective
license terms (attribution requirements, commercial-use restrictions, rate
limits, etc.). NetOverseer does not redistribute any of these data sets.

---

## Test-only packages

These packages are used solely for the test projects in `tests/` and are not
shipped with the runtime:

| Package | License | Project |
|---|---|---|
| xunit | Apache-2.0 | https://github.com/xunit/xunit |
| xunit.runner.visualstudio | Apache-2.0 | https://github.com/xunit/visualstudio.xunit |
| Microsoft.NET.Test.Sdk | MIT | https://github.com/microsoft/vstest |
| Moq | BSD-3-Clause | https://github.com/devlooped/moq |
| coverlet.collector | MIT | https://github.com/coverlet-coverage/coverlet |
