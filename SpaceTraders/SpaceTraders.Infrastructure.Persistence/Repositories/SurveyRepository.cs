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
        await db.Surveys
            .Where(s => s.AgentToken == db.AgentToken && s.Expiration <= now)
            .ExecuteDeleteAsync(cancellationToken);

        var signatures = surveys.Select(s => s.Signature).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var existing = await db.Surveys
            .Where(s => s.AgentToken == db.AgentToken && signatures.Contains(s.Signature))
            .ToDictionaryAsync(s => s.Signature, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var survey in surveys)
        {
            var values = new CachedSurvey
            {
                AgentToken = db.AgentToken,
                Signature = survey.Signature,
                ShipSymbol = shipSymbol,
                WaypointSymbol = survey.WaypointSymbol,
                DepositsJson = JsonSerializer.Serialize(survey.Deposits ?? []),
                Expiration = survey.Expiration,
                Size = survey.Size,
                RecordedAt = now,
            };

            if (existing.TryGetValue(survey.Signature, out var entity))
            {
                db.Entry(entity).CurrentValues.SetValues(values);
            }
            else
            {
                db.Surveys.Add(values);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SurveyModel>> GetActiveByWaypointAsync(string waypointSymbol, CancellationToken cancellationToken = default)
    {
        var now = TimeProvider.System.GetUtcNow();
        var entities = await db.Surveys
            .AsNoTracking()
            .Where(s => s.WaypointSymbol == waypointSymbol && s.Expiration > now)
            .OrderByDescending(s => s.Size == "LARGE"
                ? 3
                : s.Size == "MODERATE"
                    ? 2
                    : s.Size == "SMALL"
                        ? 1
                        : 0)
            .ThenByDescending(s => s.Expiration)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToModel).ToList();
    }

    public async Task<SurveyModel> GetBestActiveSurveyAsync(string waypointSymbol, string preferredDepositSymbol, CancellationToken cancellationToken = default)
    {
        var surveys = await GetActiveByWaypointAsync(waypointSymbol, cancellationToken);
        if (surveys.Count == 0)
        {
            return new SurveyModel(string.Empty, waypointSymbol, [], DateTimeOffset.MinValue, string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(preferredDepositSymbol))
        {
            var preferred = surveys.FirstOrDefault(s => s.Deposits.Any(d => d.Symbol.Equals(preferredDepositSymbol, StringComparison.OrdinalIgnoreCase)));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        return surveys[0];
    }

    private static SurveyModel MapToModel(CachedSurvey entity) =>
        new(entity.Signature, entity.WaypointSymbol, DeserializeDeposits(entity.DepositsJson), entity.Expiration, entity.Size);

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
