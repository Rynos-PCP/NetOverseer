// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NetOverseer.Core.Interfaces;
using NetOverseer.Core.Models;

namespace NetOverseer.Core.Services;

/// <summary>
/// Implementiert <see cref="IFirewallService"/> via NetFwTypeLib COM API
/// (HNetCfg.FwPolicy2 / HNetCfg.FWRule).
///
/// Alle Schreibzugriffe erfordern Administrator-Rechte.
/// COM-Objekte werden per Late-Binding über dynamic instanziiert –
/// kein separates Interop-Assembly nötig.
/// </summary>
public sealed class FirewallService : IFirewallService
{
    // ──────────────────────────────────────────────────────────────────────
    // COM-Konstanten (aus mstypes.h / netfw.h)
    // ──────────────────────────────────────────────────────────────────────

    private const int NET_FW_ACTION_BLOCK = 0;
    private const int NET_FW_RULE_DIR_OUT = 2;
    private const int NET_FW_IP_PROTOCOL_TCP = 6;
    private const int NET_FW_IP_PROTOCOL_UDP = 17;
    private const int NET_FW_IP_PROTOCOL_ICMP = 1;
    private const int NET_FW_PROFILES_ALL = 7; // Domain(1) | Private(2) | Public(4)

    private const string NetOverseerGroup = "NetOverseer";

    private readonly ILogger<FirewallService> _logger;

    // Kurzzeit-Cache für das komplette Regelwerk. Beschleunigt sowohl die
    // "Alle Regeln"-Ansicht als auch die per-App-Detail-Leiste, wenn der
    // Benutzer mehrere Apps nacheinander öffnet.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(15);
    private readonly object _cacheLock = new();
    private DateTimeOffset _cacheTimestamp;
    private IReadOnlyList<FirewallRuleInfo>? _cachedRules;

    public FirewallService(ILogger<FirewallService> logger)
    {
        _logger = logger;
    }

    // ──────────────────────────────────────────────────────────────────────
    // IFirewallService – Lesend
    // ──────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FirewallRuleInfo>> GetNetOverseerRulesAsync(
        CancellationToken ct = default)
    {
        var all = await GetAllRulesAsync(ct);
        return all.Where(r => r.IsNetOverseerRule).ToList().AsReadOnly();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FirewallRuleInfo>> GetAllRulesAsync(
        CancellationToken ct = default)
    {
        // Cache-Treffer: vermeidet die teure COM-Enumeration komplett.
        if (TryGetCachedRules(out var cached))
            return cached;

        // ct wird an Task.Run übergeben, damit bereits abgebrochene Tokens die
        // COM-Enumeration gar nicht erst starten.
        return await Task.Run(() =>
        {
            var result = new List<FirewallRuleInfo>();

            try
            {
                dynamic policy = CreatePolicyObject();

                // WICHTIG: Kein expliziter Cast zu System.Collections.IEnumerable –
                // bei Late-Binding (dynamic / kein früh gebundenes Interop-Assembly)
                // schlägt dieser Cast in .NET 8 silent fehl. Stattdessen rulesCollection
                // als dynamic belassen; der DLR-Binder nutzt dann _NewEnum (DISPID -4)
                // des COM-Objekts für die Enumeration.
                dynamic rulesCollection = policy.Rules;

                foreach (dynamic rule in rulesCollection)
                {
                    try
                    {
                        result.Add(MapRule(rule));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Firewall-Regel konnte nicht gelesen werden");
                    }
                }

                _logger.LogInformation("{Count} Firewall-Regeln erfolgreich gelesen", result.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Lesen der Firewall-Regeln via COM");
                throw; // propagieren – Aufrufer zeigt Fehlerstatus
            }

            var readOnly = (IReadOnlyList<FirewallRuleInfo>)result.AsReadOnly();
            StoreCache(readOnly);
            return readOnly;
        }, ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FirewallRuleInfo>> GetRulesForApplicationAsync(
        string executablePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return Array.Empty<FirewallRuleInfo>();

        // Schnellweg: aus frischem Voll-Cache filtern (kein COM-Aufruf).
        if (TryGetCachedRules(out var cached))
        {
            return cached
                .Where(r => !string.IsNullOrEmpty(r.ApplicationPath) &&
                            string.Equals(r.ApplicationPath, executablePath,
                                          StringComparison.OrdinalIgnoreCase))
                .ToList()
                .AsReadOnly();
        }

        // Langsamer Pfad: COM-Enumeration mit Early-Filter (liest pro Regel
        // zunächst nur den ApplicationName; volles Mapping nur bei Treffer).
        return await Task.Run(() =>
        {
            var result = new List<FirewallRuleInfo>();

            try
            {
                dynamic policy = CreatePolicyObject();
                dynamic rulesCollection = policy.Rules;

                foreach (dynamic rule in rulesCollection)
                {
                    if (ct.IsCancellationRequested) break;

                    string? appName;
                    try { appName = rule.ApplicationName as string; }
                    catch { continue; } // Eigenschaft nicht lesbar – Regel überspringen

                    if (string.IsNullOrEmpty(appName)) continue;
                    if (!string.Equals(appName, executablePath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        result.Add(MapRule(rule));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Firewall-Regel konnte nicht gelesen werden");
                    }
                }

                _logger.LogInformation(
                    "{Count} Firewall-Regeln für {Path} gefunden (Early-Filter)",
                    result.Count, executablePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Lesen der App-Firewall-Regeln via COM");
                throw;
            }

            return (IReadOnlyList<FirewallRuleInfo>)result.AsReadOnly();
        }, ct);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<FirewallRuleInfo> StreamAllRulesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Frischen Cache als schnellen Pfad nutzen, damit wiederholtes Öffnen
        // des "Alle Regeln"-Tabs ohne erneute COM-Enumeration auskommt.
        if (TryGetCachedRules(out var cached))
        {
            foreach (var rule in cached)
            {
                ct.ThrowIfCancellationRequested();
                yield return rule;
            }
            yield break;
        }

        var channel = Channel.CreateUnbounded<FirewallRuleInfo>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        var collected = new List<FirewallRuleInfo>();

        _ = Task.Run(() =>
        {
            try
            {
                dynamic policy = CreatePolicyObject();
                dynamic rulesCollection = policy.Rules;

                foreach (dynamic rule in rulesCollection)
                {
                    if (ct.IsCancellationRequested) break;

                    FirewallRuleInfo? mapped = null;
                    try { mapped = MapRule(rule); }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Firewall-Regel konnte nicht gelesen werden");
                    }

                    if (mapped is not null)
                        channel.Writer.TryWrite(mapped);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Streamen der Firewall-Regeln via COM");
                channel.Writer.TryComplete(ex);
                return;
            }
            channel.Writer.TryComplete();
        }, ct);

        await foreach (var rule in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            collected.Add(rule);
            yield return rule;
        }

        // Erfolgreich komplett gelesen → Cache befüllen.
        if (!ct.IsCancellationRequested)
            StoreCache(collected.AsReadOnly());
    }

    /// <inheritdoc/>
    public async Task UpdateRuleAsync(
        string currentName, FirewallRuleUpdate update, CancellationToken ct = default)
    {
        RequireAdministrator();
        if (string.IsNullOrWhiteSpace(currentName))
            throw new ArgumentException("Regelname ist erforderlich.", nameof(currentName));
        if (update is null)
            throw new ArgumentNullException(nameof(update));

        await Task.Run(() =>
        {
            dynamic policy          = CreatePolicyObject();
            dynamic rulesCollection = policy.Rules;
            dynamic? target         = null;

            foreach (dynamic rule in rulesCollection)
            {
                if (string.Equals(TryGet<string>(() => rule.Name), currentName, StringComparison.Ordinal))
                {
                    target = rule;
                    break;
                }
            }

            if (target is null)
                throw new InvalidOperationException($"Regel '{currentName}' wurde nicht gefunden.");

            if (update.Description is not null) target.Description = update.Description;
            if (update.ApplicationPath is not null)
                target.ApplicationName = string.IsNullOrEmpty(update.ApplicationPath) ? null : update.ApplicationPath;
            if (update.Direction is not null)
                target.Direction = update.Direction == FirewallDirection.Inbound ? 1 : NET_FW_RULE_DIR_OUT;
            if (update.Action is not null)
                target.Action = update.Action == FirewallAction.Block ? NET_FW_ACTION_BLOCK : 1;
            if (update.Protocol is not null)
            {
                target.Protocol = update.Protocol switch
                {
                    FirewallProtocol.Tcp  => NET_FW_IP_PROTOCOL_TCP,
                    FirewallProtocol.Udp  => NET_FW_IP_PROTOCOL_UDP,
                    FirewallProtocol.Icmp => NET_FW_IP_PROTOCOL_ICMP,
                    _                     => 256 // NET_FW_IP_PROTOCOL_ANY
                };
            }
            if (update.LocalPorts is not null)      target.LocalPorts      = update.LocalPorts;
            if (update.RemotePorts is not null)     target.RemotePorts     = update.RemotePorts;
            if (update.RemoteAddresses is not null) target.RemoteAddresses = string.IsNullOrEmpty(update.RemoteAddresses) ? "*" : update.RemoteAddresses;
            if (update.IsEnabled is not null)       target.Enabled         = update.IsEnabled.Value;

            // Name als letztes – sonst kann der Lookup oben fehlschlagen.
            if (!string.IsNullOrWhiteSpace(update.Name) && update.Name != currentName)
                target.Name = update.Name;

            _logger.LogInformation("Regel aktualisiert: {Rule}", currentName);
        }, ct);

        InvalidateCache();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Cache-Hilfsmethoden
    // ──────────────────────────────────────────────────────────────────────

    private bool TryGetCachedRules(out IReadOnlyList<FirewallRuleInfo> rules)
    {
        lock (_cacheLock)
        {
            if (_cachedRules is not null &&
                DateTimeOffset.UtcNow - _cacheTimestamp < CacheTtl)
            {
                rules = _cachedRules;
                return true;
            }
        }
        rules = Array.Empty<FirewallRuleInfo>();
        return false;
    }

    private void StoreCache(IReadOnlyList<FirewallRuleInfo> rules)
    {
        lock (_cacheLock)
        {
            _cachedRules    = rules;
            _cacheTimestamp = DateTimeOffset.UtcNow;
        }
    }

    private void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cachedRules = null;
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // IFirewallService – Schreibend (erfordern Admin-Rechte)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Wirft <see cref="UnauthorizedAccessException"/> wenn der aktuelle Prozess
    /// keine Administrator-Rechte hat. Alle Schreibmethoden rufen dies zuerst auf.
    /// </summary>
    private void RequireAdministrator()
    {
        if (!IsAdministrator)
            throw new UnauthorizedAccessException(
                "Für Firewall-Änderungen werden Administrator-Rechte benötigt. " +
                "Starten Sie NetOverseer mit 'Als Administrator ausführen'.");
    }

    /// <inheritdoc/>
    public async Task BlockApplicationAsync(
        string executablePath, string ruleName, CancellationToken ct = default)
    {
        RequireAdministrator();
        await Task.Run(() =>
        {
            dynamic policy = CreatePolicyObject();
            dynamic rule   = CreateRuleObject();

            rule.Name            = ruleName;
            rule.Description     = "Erstellt von NetOverseer";
            rule.ApplicationName = executablePath;
            rule.Action          = NET_FW_ACTION_BLOCK;
            rule.Direction       = NET_FW_RULE_DIR_OUT;
            rule.Enabled         = true;
            rule.Grouping        = NetOverseerGroup;
            rule.Profiles        = NET_FW_PROFILES_ALL;

            policy.Rules.Add(rule);
            _logger.LogInformation("App blockiert: {Path} ({Rule})", executablePath, ruleName);
        }, ct);
        InvalidateCache();
    }

    /// <inheritdoc/>
    public async Task BlockRemoteIpAsync(
        IPAddress ip, string ruleName, CancellationToken ct = default)
    {
        RequireAdministrator();
        await Task.Run(() =>
        {
            dynamic policy = CreatePolicyObject();
            dynamic rule   = CreateRuleObject();

            rule.Name            = ruleName;
            rule.Description     = "Erstellt von NetOverseer";
            rule.RemoteAddresses = ip.ToString();
            rule.Action          = NET_FW_ACTION_BLOCK;
            rule.Direction       = NET_FW_RULE_DIR_OUT;
            rule.Enabled         = true;
            rule.Grouping        = NetOverseerGroup;
            rule.Profiles        = NET_FW_PROFILES_ALL;

            policy.Rules.Add(rule);
            _logger.LogInformation("IP blockiert: {IP} ({Rule})", ip, ruleName);
        }, ct);
        InvalidateCache();
    }

    /// <inheritdoc/>
    public async Task BlockApplicationToIpAsync(
        string executablePath, IPAddress ip, CancellationToken ct = default)
    {
        RequireAdministrator();
        var ruleName = $"NetOverseer – {Path.GetFileNameWithoutExtension(executablePath)} → {ip}";
        await Task.Run(() =>
        {
            dynamic policy = CreatePolicyObject();
            dynamic rule   = CreateRuleObject();

            rule.Name            = ruleName;
            rule.Description     = "Erstellt von NetOverseer";
            rule.ApplicationName = executablePath;
            rule.RemoteAddresses = ip.ToString();
            rule.Action          = NET_FW_ACTION_BLOCK;
            rule.Direction       = NET_FW_RULE_DIR_OUT;
            rule.Enabled         = true;
            rule.Grouping        = NetOverseerGroup;
            rule.Profiles        = NET_FW_PROFILES_ALL;

            policy.Rules.Add(rule);
            _logger.LogInformation(
                "App+IP blockiert: {Path} → {IP}", executablePath, ip);
        }, ct);
        InvalidateCache();
    }

    /// <inheritdoc/>
    public async Task RemoveRuleAsync(string ruleName, CancellationToken ct = default)
    {
        RequireAdministrator();
        await Task.Run(() =>
        {
            dynamic policy = CreatePolicyObject();
            policy.Rules.Remove(ruleName);
            _logger.LogInformation("Regel entfernt: {Rule}", ruleName);
        }, ct);
        InvalidateCache();
    }

    /// <inheritdoc/>
    public async Task SetRuleEnabledAsync(
        string ruleName, bool enabled, CancellationToken ct = default)
    {
        RequireAdministrator();
        await Task.Run(() =>
        {
            dynamic policy         = CreatePolicyObject();
            dynamic rulesCollection = policy.Rules;

            foreach (dynamic rule in rulesCollection)
            {
                if (string.Equals(TryGet<string>(() => rule.Name), ruleName, StringComparison.Ordinal))
                {
                    rule.Enabled = enabled;
                    _logger.LogInformation(
                        "Regel {State}: {Rule}", enabled ? "aktiviert" : "deaktiviert", ruleName);
                    break;
                }
            }
        }, ct);
        InvalidateCache();
    }

    // ──────────────────────────────────────────────────────────────────────
    // COM-Factory-Methoden
    // ──────────────────────────────────────────────────────────────────────

    private static dynamic CreatePolicyObject()
    {
        var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2", throwOnError: true)!;
        return Activator.CreateInstance(type)
               ?? throw new InvalidOperationException("HNetCfg.FwPolicy2 konnte nicht instanziiert werden.");
    }

    private static dynamic CreateRuleObject()
    {
        var type = Type.GetTypeFromProgID("HNetCfg.FWRule", throwOnError: true)!;
        return Activator.CreateInstance(type)
               ?? throw new InvalidOperationException("HNetCfg.FWRule konnte nicht instanziiert werden.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Mapping
    // ──────────────────────────────────────────────────────────────────────

    private static FirewallRuleInfo MapRule(dynamic rule)
    {
        var  name      = (string)rule.Name;
        var  appPath   = TryGet<string>(() => rule.ApplicationName);
        int  dirVal    = TryGet<int>(() => rule.Direction);
        int  actVal    = TryGet<int>(() => rule.Action);
        int  protoVal  = TryGet<int>(() => rule.Protocol);
        bool enabled   = TryGet<bool>(() => rule.Enabled);
        var  groupName = TryGet<string>(() => rule.Grouping);

        var direction = dirVal == 1 ? FirewallDirection.Inbound : FirewallDirection.Outbound;
        var action    = actVal == NET_FW_ACTION_BLOCK ? FirewallAction.Block : FirewallAction.Allow;

        var protocol = protoVal switch
        {
            NET_FW_IP_PROTOCOL_TCP  => FirewallProtocol.Tcp,
            NET_FW_IP_PROTOCOL_UDP  => FirewallProtocol.Udp,
            NET_FW_IP_PROTOCOL_ICMP => FirewallProtocol.Icmp,
            _                       => FirewallProtocol.Any
        };

        bool isOrphaned         = !string.IsNullOrEmpty(appPath) && !File.Exists(appPath);
        bool isNetOverseerRule  = groupName?.Contains(NetOverseerGroup,
                                     StringComparison.OrdinalIgnoreCase) == true;

        return new FirewallRuleInfo
        {
            Name             = name,
            Description      = TryGet<string>(() => rule.Description),
            ApplicationPath  = appPath,
            ApplicationName  = string.IsNullOrEmpty(appPath)
                                   ? null
                                   : Path.GetFileNameWithoutExtension(appPath),
            Direction        = direction,
            Action           = action,
            LocalPorts       = TryGet<string>(() => rule.LocalPorts),
            RemotePorts      = TryGet<string>(() => rule.RemotePorts),
            RemoteAddresses  = TryGet<string>(() => rule.RemoteAddresses),
            Protocol         = protocol,
            IsEnabled        = enabled,
            IsOrphaned       = isOrphaned,
            IsNetOverseerRule = isNetOverseerRule,
            GroupName        = groupName
        };
    }

    /// <summary>Liest einen COM-Eigenschaftswert ohne Exception bei fehlendem Wert.</summary>
    private static T? TryGet<T>(Func<T> getter)
    {
        try { return getter(); }
        catch { return default; }
    }
}
