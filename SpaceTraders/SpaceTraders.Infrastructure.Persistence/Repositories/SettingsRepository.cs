using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

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
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        if (existing is null)
        {
            db.Settings.Add(new AgentSetting
            {
                Key = key,
                Value = raw,
                Type = typeof(T).Name.ToLowerInvariant(),
                Description = string.Empty
            });
        }
        else
        {
            existing.Value = raw;
        }

        await db.SaveChangesAsync(cancellationToken);
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
}
