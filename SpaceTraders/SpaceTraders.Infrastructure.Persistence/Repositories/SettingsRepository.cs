using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Seed;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

/// <summary>
/// The agent's settings, and the values chosen for the next runs (D69). Every change it stores is a
/// <c>SettingChanged</c> journal line, whoever made it (the settings endpoints, the kill switch, the
/// size guard), and every change for the next runs a <c>NextRunSettingChanged</c> one; a value that
/// may hold a secret (a URL) is logged as <c>(hidden)</c>.
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
            Description = existing?.Description ?? string.Empty,
            FollowsDefault = false,
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
        LogIfChanged(logger, key, oldValue, raw);
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
        var chosenBefore = await db.NextRunSettings.AsNoTracking().OrderBy(s => s.Key).ToListAsync(cancellationToken);
        await DefaultSettingsSeed.ResetAsync(db, cancellationToken);
        var after = await ValuesAsync(cancellationToken);

        foreach (var (key, value) in after)
        {
            LogIfChanged(logger, key, before.GetValueOrDefault(key), value);
        }

        foreach (var setting in chosenBefore)
        {
            LogNextRunIfChanged(setting.Key, setting.Value, null);
        }
    }

    public async Task<IReadOnlyList<(string Key, string Value, string Type, string Description, bool IsDefault)>> GetNextRunSettingsAsync(CancellationToken cancellationToken = default)
    {
        var chosen = await db.NextRunSettings
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.Ordinal, cancellationToken);

        return DefaultSettingsSeed.RunSettings
            .Select(setting => chosen.TryGetValue(setting.Key, out var value)
                ? (setting.Key, value, setting.Type, setting.Description, IsDefault: false)
                : (setting.Key, setting.Value, setting.Type, setting.Description, IsDefault: true))
            .OrderBy(setting => setting.Key, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<bool> SetNextRunSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        if (!DefaultSettingsSeed.IsRunSetting(key))
        {
            return false;
        }

        var existing = await db.NextRunSettings.FindAsync([key], cancellationToken);
        var oldValue = existing?.Value;
        var values = new NextRunSetting { Key = key, Value = value };

        if (existing is null)
        {
            db.NextRunSettings.Add(values);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(values);
        }

        await db.SaveChangesAsync(cancellationToken);
        LogNextRunIfChanged(key, oldValue, value);
        return true;
    }

    public async Task<bool> RemoveNextRunSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!DefaultSettingsSeed.IsRunSetting(key))
        {
            return false;
        }

        var existing = await db.NextRunSettings.FindAsync([key], cancellationToken);
        if (existing is not null)
        {
            db.NextRunSettings.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
            LogNextRunIfChanged(key, existing.Value, null);
        }

        return true;
    }

    /// <summary>
    /// A setting's value as the journal and the metrics show it: <c>(hidden)</c> when the key may hold a secret (it
    /// ends in <c>Url</c>, or names a secret, password or API key) and the value isn't empty; otherwise the value.
    /// </summary>
    /// <param name="key">The setting's key.</param>
    /// <param name="value">Its value.</param>
    /// <returns>What may be shown.</returns>
    public static string Shown(string key, string value)
        => MayHoldASecret(key) && value.Length > 0 ? "(hidden)" : value;

    private static bool MayHoldASecret(string key)
        => key.EndsWith("Url", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Secret", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || key.Contains("ApiKey", StringComparison.OrdinalIgnoreCase);

    private async Task<Dictionary<string, string>> ValuesAsync(CancellationToken cancellationToken)
        => await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.Ordinal, cancellationToken);

    /// <summary>A <c>SettingChanged</c> journal line on <paramref name="logger"/>, when the value changed.</summary>
    /// <param name="logger">Where it is logged.</param>
    /// <param name="key">The setting's key.</param>
    /// <param name="oldValue">Its value before, or null when it had none.</param>
    /// <param name="newValue">Its value now.</param>
    internal static void LogIfChanged(ILogger logger, string key, string? oldValue, string newValue)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        logger.LogInformation(
            "{EventKind:l}: {Setting} changed from {OldValue} to {NewValue}.",
            JournalEvents.SettingChanged,
            key,
            oldValue is null ? "(unset)" : Shown(key, oldValue),
            Shown(key, newValue));
    }

    /// <summary>A <c>NextRunSettingChanged</c> journal line, when the value changed; null is the default.</summary>
    private void LogNextRunIfChanged(string key, string? oldValue, string? newValue)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        logger.LogInformation(
            "{EventKind:l}: {Setting} for the next runs changed from {OldValue} to {NewValue}.",
            JournalEvents.NextRunSettingChanged,
            key,
            oldValue is null ? "(default)" : Shown(key, oldValue),
            newValue is null ? "(default)" : Shown(key, newValue));
    }
}
