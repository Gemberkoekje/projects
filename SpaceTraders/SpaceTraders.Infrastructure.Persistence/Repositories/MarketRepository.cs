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
        var existing = await db.Markets.FindAsync([db.AgentToken, market.WaypointSymbol], cancellationToken);
        var now = TimeProvider.System.GetUtcNow();

        var values = new CachedMarket
        {
            AgentToken = db.AgentToken,
            WaypointSymbol = market.WaypointSymbol,
            SystemSymbol = market.SystemSymbol,
            TradeGoodsJson = market.TradeGoodsJson,
            ImportsJson = market.ImportsJson,
            ExportsJson = market.ExportsJson,
            ExchangeJson = market.ExchangeJson,
            LastObservedAt = now
        };

        if (existing is null)
        {
            db.Markets.Add(values);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(values);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<MarketSnapshot?> FindSnapshotByWaypointAsync(string waypointSymbol, CancellationToken cancellationToken = default)
    {
        var market = await db.Markets.AsNoTracking()
            .FirstOrDefaultAsync(m => m.WaypointSymbol == waypointSymbol && m.TradeGoodsJson != null, cancellationToken);

        return market is null ? null : TryCreateSnapshot(market);
    }

    public async Task<IReadOnlyList<MarketSnapshot>> GetAllSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        var markets = await db.Markets.AsNoTracking()
            .Where(m => m.TradeGoodsJson != null)
            .ToListAsync(cancellationToken);

        var snapshots = new List<MarketSnapshot>();
        foreach (var market in markets)
        {
            var snapshot = TryCreateSnapshot(market);
            if (snapshot is not null)
            {
                snapshots.Add(snapshot);
            }
        }

        return snapshots;
    }

    public async Task<IReadOnlyList<MarketFreshnessRecord>> GetAllFreshnessAsync(CancellationToken cancellationToken = default)
    {
        var markets = await db.Markets.AsNoTracking()
            .ToListAsync(cancellationToken);

        return markets
            .Select(m => new MarketFreshnessRecord(m.WaypointSymbol, m.SystemSymbol, m.LastObservedAt))
            .ToList();
    }

    private static MarketSnapshot? TryCreateSnapshot(CachedMarket market)
    {
        if (market.TradeGoodsJson is null)
        {
            return null;
        }

        try
        {
            var goods = JsonSerializer.Deserialize<List<TradeGoodJson>>(market.TradeGoodsJson);
            if (goods is null || goods.Count == 0)
            {
                return null;
            }

            var goodSnapshots = goods
                .Select(g => new TradeGoodSnapshot(
                    g.Symbol ?? string.Empty,
                    g.Type ?? string.Empty,
                    g.PurchasePrice,
                    g.SellPrice,
                    g.TradeVolume,
                    g.Supply ?? string.Empty,
                    g.Activity ?? string.Empty))
                .ToList();

            return new MarketSnapshot(
                market.WaypointSymbol,
                market.SystemSymbol,
                goodSnapshots,
                DeserializeSymbols(market.ImportsJson),
                DeserializeSymbols(market.ExportsJson),
                DeserializeSymbols(market.ExchangeJson));
        }
        catch (JsonException)
        {
            return null;
        }
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
