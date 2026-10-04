using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Infrastructure.Tests;

[Trait("Category", "Integration")]
public sealed class MarketRepositoryTests : IntegrationTestBase
{
    private const string Waypoint = "X1-KR90-E18A";
    private const string SystemSymbol = "X1-KR90";

    /// <summary>
    /// B61, on the cluster on 2026-10-04: SPECTER-1 arrived at a market the bot had never fetched, and the arrival and the
    /// market watch, each in a scope of its own, fetched it at the same moment. Both found no row, and both inserted one: the
    /// watch's insert failed on PK_cached_markets (23505), and the prices it fetched were lost. Here the arrival stores the
    /// market between the watch's read and its write.
    /// </summary>
    [SkippableFact]
    public async Task UpsertAsync_AMarketAnotherRefreshStoredMeanwhile_IsUpdated()
    {
        await using var arrival = CreateFreshContext();
        var arrivalStoresFirst = new BeforeInsertInterceptor(
            "cached_markets",
            async () => await new MarketRepository(arrival).UpsertAsync(Market(fuelPrice: 70)));
        await using var watch = CreateFreshContext(arrivalStoresFirst);

        await new MarketRepository(watch).UpsertAsync(Market(fuelPrice: 71));

        arrivalStoresFirst.HasRun.Should().BeTrue("the arrival stored the market first");
        await using var fresh = CreateFreshContext();
        var market = await new MarketRepository(fresh).FindSnapshotByWaypointAsync(Waypoint);
        market.Should().NotBeNull();
        market.TradeGoods.Should().ContainSingle().Which.PurchasePrice.Should().Be(71, "the watch's refresh went to the database last");
    }

    /// <summary>
    /// B62: a market cached without prices (a start's first fetch of a market, or one stored before B62) counts as never
    /// seen, so the market watch fetches it as soon as one of our ships is there, and a probe goes there first.
    /// </summary>
    [SkippableFact]
    public async Task AMarketCachedWithoutPrices_CountsAsNeverSeen_UntilItsPricesAreStored()
    {
        var markets = new MarketRepository(Db);
        await markets.UpsertAsync(new MarketDataModel(Waypoint, SystemSymbol, null, Symbols("ICE_WATER"), Symbols("FABRICS"), Symbols("FUEL")));

        (await markets.GetLastObservedAtAsync(Waypoint)).Should().BeNull();
        (await markets.GetAllFreshnessAsync()).Should().ContainSingle().Which.HasPrices.Should().BeFalse();

        await markets.UpsertAsync(new MarketDataModel(Waypoint, SystemSymbol, Goods(72), Symbols("ICE_WATER"), Symbols("FABRICS"), Symbols("FUEL")));

        (await markets.GetLastObservedAtAsync(Waypoint)).Should().NotBeNull();
        (await markets.GetAllFreshnessAsync()).Should().ContainSingle().Which.HasPrices.Should().BeTrue();
    }

    [SkippableFact]
    public async Task UpsertAsync_StoresTheMarketAsFetched_AndTheNextRefreshReplacesIt()
    {
        var markets = new MarketRepository(Db);
        await markets.UpsertAsync(new MarketDataModel(Waypoint, SystemSymbol, Goods(70), Symbols("ICE_WATER"), Symbols("FABRICS"), Symbols("FUEL")));
        var first = await StoredAsync();

        await markets.UpsertAsync(new MarketDataModel(Waypoint, SystemSymbol, Goods(71), Symbols("IRON_ORE"), Symbols("CLOTHING"), null));
        var second = await StoredAsync();

        first.Should().BeEquivalentTo(new
        {
            SystemSymbol,
            TradeGoodsJson = Goods(70),
            ImportsJson = Symbols("ICE_WATER"),
            ExportsJson = Symbols("FABRICS"),
            ExchangeJson = Symbols("FUEL"),
        });
        second.Should().BeEquivalentTo(new
        {
            SystemSymbol,
            TradeGoodsJson = Goods(71),
            ImportsJson = Symbols("IRON_ORE"),
            ExportsJson = Symbols("CLOTHING"),
            ExchangeJson = (string?)null,
        });
        second.LastObservedAt.Should().BeAfter(first.LastObservedAt);
    }

    private static MarketDataModel Market(int fuelPrice) => new(Waypoint, SystemSymbol, Goods(fuelPrice), "[]", "[]", Symbols("FUEL"));

    private static string Goods(int fuelPrice)
        => $$"""[{"symbol":"FUEL","type":"EXCHANGE","purchasePrice":{{fuelPrice}},"sellPrice":{{fuelPrice - 2}},"tradeVolume":180,"supply":"MODERATE","activity":"WEAK"}]""";

    private static string Symbols(string symbol) => $$"""[{"symbol":"{{symbol}}"}]""";

    private async Task<CachedMarket> StoredAsync()
    {
        await using var fresh = CreateFreshContext();
        return await fresh.Markets.AsNoTracking().SingleAsync(m => m.WaypointSymbol == Waypoint);
    }
}
