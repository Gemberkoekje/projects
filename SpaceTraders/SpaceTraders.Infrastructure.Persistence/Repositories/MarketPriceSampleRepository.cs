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

    private sealed class TradeGoodJson
    {
        public string? Symbol { get; init; }
        public int PurchasePrice { get; init; }
        public int SellPrice { get; init; }
        public string? Supply { get; init; }
        public string? Activity { get; init; }
        public int TradeVolume { get; init; }
    }
}
