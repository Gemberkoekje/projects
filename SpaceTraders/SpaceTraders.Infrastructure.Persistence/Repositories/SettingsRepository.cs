using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Seed;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

/// <summary>
/// The agent's settings. Every change it stores is a <c>SettingChanged</c> journal line, whoever made
/// it (the settings endpoints, the kill switch, the size guard); a value that may hold a secret (a
/// URL) is logged as <c>(hidden)</c>.
/// </summary>
public sealed class SettingsRepository(SpaceTradersDbContext db, ILogger<SettingsRepository> logger) : ISettingsRepository
{
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var raw = await GetRawAsync(key, cancellationToken);
        if (raw is null) return default;

        // String passthrough: no deserialization needed
        if (typeof(T) == typeof(string)) return (T)(object)raw;

        try
        {
            return JsonSerializer.Deserialize<T>(raw);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to deserialize setting '{Key}' as {Type}. Raw value: {Value}", key, typeof(T).Name, raw);
            return default;
        }
    }

    public async Task<string?> GetRawAsync(string key, CancellationToken cancellationToken = default)
    {
        var entity = await db.Settings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        return entity?.Value;
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        var raw = value is string s ? s : JsonSerializer.Serialize(value);

        var existing = await db.Settings
            .FindAsync([db.AgentId, key], cancellationToken);
        var oldValue = existing?.Value;

        var values = new AgentSetting
        {
            AgentId = db.AgentId,
            Key = key,
            Value = raw,
            Type = typeof(T).Name.ToLowerInvariant(),
            Description = existing?.Description ?? string.Empty
        };

        if (existing is null)
        {
            db.Settings.Add(values);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(values);
        }

        await db.SaveChangesAsync(cancellationToken);
        LogIfChanged(key, oldValue, raw);
    }

    public async Task<IReadOnlyList<(string Key, string Value, string Type, string Description)>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var settings = await db.Settings
            .AsNoTracking()
            .OrderBy(s => s.Key)
            .ToListAsync(cancellationToken);

        return settings
            .Select(s => (s.Key, s.Value, s.Type, s.Description))
            .ToList();
    }

    public async Task<IReadOnlyList<(string Key, string Value)>> GetByKeyPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var settings = await db.Settings
            .AsNoTracking()
            .Where(s => s.Key.StartsWith(prefix))
            .OrderBy(s => s.Key)
            .ToListAsync(cancellationToken);

        return settings
            .Select(s => (s.Key, s.Value))
            .ToList();
    }

    public async Task ResetToDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var before = await ValuesAsync(cancellationToken);
        await DefaultSettingsSeed.ResetAsync(db, cancellationToken);
        var after = await ValuesAsync(cancellationToken);

        foreach (var (key, value) in after)
        {
            LogIfChanged(key, before.GetValueOrDefault(key), value);
        }
    }

    private static bool MayHoldASecret(string key)
        => key.EndsWith("Url", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Secret", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || key.Contains("ApiKey", StringComparison.OrdinalIgnoreCase);

    private async Task<Dictionary<string, string>> ValuesAsync(CancellationToken cancellationToken)
        => await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.Ordinal, cancellationToken);

    private void LogIfChanged(string key, string? oldValue, string newValue)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        var secret = MayHoldASecret(key);
        logger.LogInformation(
            "{EventKind}: {Setting} changed from {OldValue} to {NewValue}.",
            JournalEvents.SettingChanged,
            key,
            Shown(oldValue, secret),
            Shown(newValue, secret));
    }

    private static string Shown(string? value, bool secret)
    {
        if (value is null)
        {
            return "(unset)";
        }

        return secret && value.Length > 0 ? "(hidden)" : value;
    }
}
