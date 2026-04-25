using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class MarketRepository(SpaceTradersDbContext db) : IMarketRepository
{
    public async Task<DateTimeOffset?> GetLastObservedAtAsync(string waypointSymbol, CancellationToken cancellationToken = default)
    {
        var entity = await db.Markets.AsNoTracking()
            .FirstOrDefaultAsync(m => m.WaypointSymbol == waypointSymbol, cancellationToken);
        return entity?.LastObservedAt;
    }

    public async Task UpsertAsync(MarketDataModel market, CancellationToken cancellationToken = default)
    {
        var existing = await db.Markets.FindAsync([market.WaypointSymbol], cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            db.Markets.Add(new CachedMarket
            {
                WaypointSymbol = market.WaypointSymbol,
                SystemSymbol = market.SystemSymbol,
                TradeGoodsJson = market.TradeGoodsJson,
                ImportsJson = market.ImportsJson,
                ExportsJson = market.ExportsJson,
                ExchangeJson = market.ExchangeJson,
                LastObservedAt = now
            });
        }
        else
        {
            existing.SystemSymbol = market.SystemSymbol;
            existing.TradeGoodsJson = market.TradeGoodsJson;
            existing.ImportsJson = market.ImportsJson;
            existing.ExportsJson = market.ExportsJson;
            existing.ExchangeJson = market.ExchangeJson;
            existing.LastObservedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MarketSnapshot>> GetAllSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        var markets = await db.Markets.AsNoTracking()
            .Where(m => m.TradeGoodsJson != null)
            .ToListAsync(cancellationToken);

        var snapshots = new List<MarketSnapshot>();
        foreach (var m in markets)
        {
            if (m.TradeGoodsJson is null) continue;
            try
            {
                var goods = JsonSerializer.Deserialize<List<TradeGoodJson>>(m.TradeGoodsJson);
                if (goods is null || goods.Count == 0) continue;
                var goodSnapshots = goods
                    .Select(g => new TradeGoodSnapshot(
                        g.Symbol ?? string.Empty,
                        g.Type ?? string.Empty,
                        g.PurchasePrice,
                        g.SellPrice,
                        g.TradeVolume,
                        g.Supply ?? string.Empty))
                    .ToList();

                snapshots.Add(new MarketSnapshot(
                    m.WaypointSymbol,
                    m.SystemSymbol,
                    goodSnapshots,
                    DeserializeSymbols(m.ImportsJson),
                    DeserializeSymbols(m.ExportsJson),
                    DeserializeSymbols(m.ExchangeJson)));
            }
            catch (JsonException)
            {
                // skip malformed entries
            }
        }
        return snapshots;
    }

    private static IReadOnlyList<string> DeserializeSymbols(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<TradeGoodSymbolJson>>(json)
                ?.Select(g => g.Symbol)
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .Cast<string>()
                .ToList()
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed class TradeGoodJson
    {
        public string? Symbol { get; init; }
        public string? Type { get; init; }
        public int TradeVolume { get; init; }
        public string? Supply { get; init; }
        public string? Activity { get; init; }
        public int PurchasePrice { get; init; }
        public int SellPrice { get; init; }
    }

    private sealed class TradeGoodSymbolJson
    {
        public string? Symbol { get; init; }
    }
}
