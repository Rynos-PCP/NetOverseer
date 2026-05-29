// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Security.Principal;

namespace NetOverseer.Core.Tests;

/// <summary>
/// Ein xUnit-<see cref="FactAttribute"/>, der den Test automatisch überspringt,
/// wenn der aktuelle Prozess mit Administratorrechten ausgeführt wird.
/// Damit können Tests, die explizit einen Nicht-Admin-Kontext erfordern
/// (z. B. UAC-Guard-Tests), auf CI-Runnern (die als Admin laufen) ignoriert werden.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SkipWhenAdminFact : FactAttribute
{
    public SkipWhenAdminFact()
    {
        if (IsRunningAsAdmin())
            Skip = "Übersprungen: Prozess läuft als Administrator. Dieser Test erfordert einen Nicht-Admin-Kontext.";
    }

    private static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
