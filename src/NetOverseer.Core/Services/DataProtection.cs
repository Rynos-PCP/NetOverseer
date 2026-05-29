// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace NetOverseer.Core.Services;

/// <summary>
/// Verschlüsselt/entschlüsselt kurze sensible Strings (API-Keys, Lizenzen) via
/// Windows DPAPI (<see cref="ProtectedData"/>, <see cref="DataProtectionScope.CurrentUser"/>).
///
/// Format auf Disk: <c>dpapi:&lt;Base64-Ciphertext&gt;</c>. So bleiben alte unverschlüsselte
/// Werte rückwärtskompatibel lesbar und werden beim nächsten <c>Save</c> aktualisiert.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DataProtection
{
    private const string Prefix = "dpapi:";
    // Optional-Entropy = zusätzlicher fester Schlüssel-Salt; nicht geheim, aber bindet
    // den Ciphertext an "NetOverseer", damit andere DPAPI-Daten desselben Users nicht
    // versehentlich entschlüsselt werden können.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NetOverseer.v1");

    /// <summary>Verschlüsselt einen Klartext-String für den aktuellen Windows-Benutzer.</summary>
    public static string? Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return plain;
        try
        {
            byte[] cipher = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plain),
                Entropy,
                DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(cipher);
        }
        catch (CryptographicException)
        {
            // DPAPI nicht verfügbar (z.B. unter Tests in Container) → unverschlüsselt
            // zurückgeben statt zu crashen. Wert ist in %AppData% bereits ACL-geschützt.
            return plain;
        }
    }

    /// <summary>Entschlüsselt einen DPAPI-geschützten String. Akzeptiert auch Klartext (Legacy).</summary>
    public static string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored;
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored; // Legacy-Klartext

        var b64 = stored[Prefix.Length..];
        try
        {
            byte[] cipher = Convert.FromBase64String(b64);
            byte[] plain  = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Entschlüsselung fehlgeschlagen (anderer User, Profil-Migration etc.) →
            // null, damit Aufrufer es wie "Kein Key gesetzt" behandelt.
            return null;
        }
    }
}
