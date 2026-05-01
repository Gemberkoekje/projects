using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class MarketPriceSampleRepository(SpaceTradersDbContext db) : IMarketPriceSampleRepository
{
    public async Task AppendSamplesAsync(string waypointSymbol, string tradeGoodsJson, CancellationToken cancellationToken = default)
    {
        List<TradeGoodJson>? goods;
        try
        {
            goods = JsonSerializer.Deserialize<List<TradeGoodJson>>(tradeGoodsJson);
        }
        catch (JsonException)
        {
            return;
        }

        if (goods is null || goods.Count == 0)
        {
            return;
        }

        var now = TimeProvider.System.GetUtcNow();
        foreach (var good in goods)
        {
            if (string.IsNullOrWhiteSpace(good.Symbol))
            {
                continue;
            }

            var sample = new MarketPriceSample
            {
                AgentToken = db.AgentToken,
                WaypointSymbol = waypointSymbol,
                GoodSymbol = good.Symbol,
                ObservedAt = now,
                PurchasePrice = good.PurchasePrice,
                SellPrice = good.SellPrice,
                Supply = good.Supply,
                Activity = good.Activity,
                TradeVolume = good.TradeVolume,
            };

            db.MarketPriceSamples.Add(sample);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MarketPriceSampleDto>> GetGoodPricesAsync(
        string goodSymbol,
        string? waypointSymbol,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var query = db.MarketPriceSamples
            .AsNoTracking()
            .Where(s => s.GoodSymbol == goodSymbol && s.ObservedAt >= from && s.ObservedAt <= to);

        if (!string.IsNullOrWhiteSpace(waypointSymbol))
        {
            query = query.Where(s => s.WaypointSymbol == waypointSymbol);
        }

        var rows = await query
            .OrderBy(s => s.ObservedAt)
            .ToListAsync(cancellationToken);

        return rows.Select(MapToDto).ToList();
    }

    public async Task<IReadOnlyList<MarketPriceSampleDto>> GetWaypointPricesAsync(
        string waypointSymbol,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.MarketPriceSamples
            .AsNoTracking()
            .Where(s => s.WaypointSymbol == waypointSymbol && s.ObservedAt >= from && s.ObservedAt <= to)
            .OrderBy(s => s.ObservedAt)
            .ToListAsync(cancellationToken);

        return rows.Select(MapToDto).ToList();
    }

    public async Task<int> PruneAsync(DateTimeOffset rawRetentionCutoff, DateTimeOffset aggregateRetentionCutoff, CancellationToken cancellationToken = default)
    {
        // Step 1: Downsample the 7–90 day window — keep one row per (waypoint, good, hour), delete duplicates.
        var downsampledDeleted = await db.Database.ExecuteSqlAsync(
            $"""
            DELETE FROM market_price_samples
            WHERE "AgentToken" = {db.AgentToken}
              AND "ObservedAt" < {rawRetentionCutoff}
              AND "ObservedAt" >= {aggregateRetentionCutoff}
              AND "Id" NOT IN (
                SELECT MIN("Id")
                FROM market_price_samples
                WHERE "AgentToken" = {db.AgentToken}
                  AND "ObservedAt" < {rawRetentionCutoff}
                  AND "ObservedAt" >= {aggregateRetentionCutoff}
                GROUP BY "WaypointSymbol", "GoodSymbol", date_trunc('hour', "ObservedAt")
              )
            """, cancellationToken);

        // Step 2: Delete all rows older than the 90-day aggregate retention cutoff (query filter applies automatically).
        var purgedDeleted = await db.MarketPriceSamples
            .Where(s => s.ObservedAt < aggregateRetentionCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        return downsampledDeleted + purgedDeleted;
    }

    private sealed class TradeGoodJson
    {
        public string? Symbol { get; init; }
        public int PurchasePrice { get; init; }
        public int SellPrice { get; init; }
        public string? Supply { get; init; }
        public string? Activity { get; init; }
        public int TradeVolume { get; init; }
    }

    private static MarketPriceSampleDto MapToDto(MarketPriceSample s) =>
        new(s.ObservedAt, s.WaypointSymbol, s.GoodSymbol,
            s.PurchasePrice, s.SellPrice, s.Supply, s.Activity, s.TradeVolume);
}
