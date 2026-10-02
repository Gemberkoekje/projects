using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class SurveyRepository(SpaceTradersDbContext db) : ISurveyRepository
{
    public async Task UpsertAsync(string shipSymbol, IReadOnlyList<SurveyModel> surveys, CancellationToken cancellationToken = default)
    {
        if (surveys.Count == 0)
        {
            return;
        }

        var now = TimeProvider.System.GetUtcNow();
        var signatures = surveys.Select(s => s.Signature).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var existing = await db.Surveys
            .Where(s => s.AgentId == db.AgentId && signatures.Contains(s.Signature))
            .ToDictionaryAsync(s => s.Signature, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var survey in surveys)
        {
            if (existing.ContainsKey(survey.Signature))
            {
                // The API hands out a signature once; a survey stored already keeps its use count.
                continue;
            }

            db.Surveys.Add(new CachedSurvey
            {
                AgentId = db.AgentId,
                Signature = survey.Signature,
                ShipSymbol = shipSymbol,
                WaypointSymbol = survey.WaypointSymbol,
                DepositsJson = JsonSerializer.Serialize(survey.Deposits ?? []),
                Expiration = survey.Expiration,
                Size = survey.Size,
                RecordedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoredSurvey>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var now = TimeProvider.System.GetUtcNow();
        var entities = await db.Surveys
            .AsNoTracking()
            .Where(s => s.AgentId == db.AgentId && s.Expiration > now)
            .OrderByDescending(s => s.RecordedAt)
            .ThenBy(s => s.Signature)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToStored).ToList();
    }

    public async Task RecordExtractionAsync(string signature, CancellationToken cancellationToken = default)
    {
        var entity = await db.Surveys.FirstOrDefaultAsync(s => s.AgentId == db.AgentId && s.Signature == signature, cancellationToken);
        if (entity is null)
        {
            return;
        }

        db.Entry(entity).Property(s => s.Extractions).CurrentValue = entity.Extractions + 1;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoredSurvey>> RemoveAsync(string signature, CancellationToken cancellationToken = default)
    {
        var entities = await db.Surveys
            .Where(s => s.AgentId == db.AgentId && s.Signature == signature)
            .ToListAsync(cancellationToken);
        return await RemoveAllAsync(entities, cancellationToken);
    }

    public async Task<IReadOnlyList<StoredSurvey>> RemoveExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var entities = await db.Surveys
            .Where(s => s.AgentId == db.AgentId && s.Expiration <= now)
            .ToListAsync(cancellationToken);
        return await RemoveAllAsync(entities, cancellationToken);
    }

    private async Task<IReadOnlyList<StoredSurvey>> RemoveAllAsync(List<CachedSurvey> entities, CancellationToken cancellationToken)
    {
        if (entities.Count == 0)
        {
            return [];
        }

        db.Surveys.RemoveRange(entities);
        await db.SaveChangesAsync(cancellationToken);
        return entities.Select(MapToStored).ToList();
    }

    private static StoredSurvey MapToStored(CachedSurvey entity) =>
        new(
            new SurveyModel(entity.Signature, entity.WaypointSymbol, DeserializeDeposits(entity.DepositsJson), entity.Expiration, entity.Size),
            entity.ShipSymbol,
            entity.RecordedAt,
            entity.Extractions);

    private static IReadOnlyList<SurveyDepositModel> DeserializeDeposits(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<SurveyDepositModel>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
