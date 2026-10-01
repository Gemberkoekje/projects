using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence.Repositories;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Markets;

namespace SpaceTraders.Application.Tests.Services;

public sealed class MarketPriceSampleRepositoryTests
{
    [Fact]
    public async Task AppendSamplesAsync_StoresAPriceSample_PerGoodOfAMarketRefresh()
    {
        // B19: the trade goods are stored as the API model serializes them, in camelCase, and the
        // repository read them back case-sensitively: every good was skipped, so no price history.
        List<TradeGood> goods =
        [
            new() { Symbol = "FUEL", Type = "EXCHANGE", TradeVolume = 100, Supply = "MODERATE", Activity = "WEAK", PurchasePrice = 72, SellPrice = 68 },
            new() { Symbol = "IRON_ORE", Type = "IMPORT", TradeVolume = 60, Supply = "SCARCE", PurchasePrice = 120, SellPrice = 110 },
        ];
        var tradeGoodsJson = JsonSerializer.Serialize(goods);
        await using var db = TestDbContextFactory.Create();

        await new MarketPriceSampleRepository(db).AppendSamplesAsync("X1-AB-2", tradeGoodsJson);

        var samples = await db.MarketPriceSamples.AsNoTracking().OrderBy(s => s.GoodSymbol).ToListAsync();
        samples.Select(s => (s.WaypointSymbol, s.GoodSymbol, s.PurchasePrice, s.SellPrice, s.Supply, s.TradeVolume))
            .Should().Equal(
                ("X1-AB-2", "FUEL", 72, 68, "MODERATE", 100),
                ("X1-AB-2", "IRON_ORE", 120, 110, "SCARCE", 60));
    }
}
