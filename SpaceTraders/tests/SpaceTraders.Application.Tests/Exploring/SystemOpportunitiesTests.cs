using FluentAssertions;
using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Tests.Exploring;

/// <summary>
/// The systems dashboard (asked on 2026-10-04): "which systems have been explored and what kind of mining, trading and
/// shipyard opportunities it gives".
/// </summary>
public sealed class SystemOpportunitiesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 04, 08, 00, 00, TimeSpan.Zero);

    [Fact]
    public void ItsBestTrades_BuyWhereAGoodIsCheapest_AndSellWhereItPaysMost_OnePerGood()
    {
        var markets = new[]
        {
            Market("X1-B-1", Good("FOOD", "EXPORT", buy: 100, sell: 90), Good("IRON", "IMPORT", buy: 0, sell: 300)),
            Market("X1-B-2", Good("FOOD", "EXCHANGE", buy: 120, sell: 110), Good("IRON", "EXPORT", buy: 200, sell: 180)),
            Market("X1-B-3", Good("FOOD", "IMPORT", buy: 0, sell: 260), Good("FUEL", "EXCHANGE", buy: 70, sell: 68)),
        };

        var system = Summarise(markets).Single(sample => sample.System == "X1-B");

        system.Trades.Should().BeEquivalentTo(
            new[]
            {
                new TradeSample { Good = "FOOD", BuyAt = "X1-B-1", SellAt = "X1-B-3", Margin = 160, Volume = 10 },
                new TradeSample { Good = "IRON", BuyAt = "X1-B-2", SellAt = "X1-B-1", Margin = 100, Volume = 10 },
            },
            options => options.WithStrictOrdering(),
            "fuel is sold where it is bought, and nowhere pays more for it");
    }

    [Fact]
    public void ItsRawGoods_AreTheOresAndGasesItsMarketsBuy_WithTheBestPriceAndTheLowestSupply()
    {
        var markets = new[]
        {
            Market("X1-B-1", Good("IRON_ORE", "IMPORT", buy: 0, sell: 61, supply: "MODERATE"), Good("IRON", "IMPORT", buy: 0, sell: 300)),
            Market("X1-B-2", Good("IRON_ORE", "IMPORT", buy: 0, sell: 58, supply: "SCARCE"), Good("HYDROCARBON", "EXCHANGE", buy: 30, sell: 28, supply: "HIGH")),
            Market("X1-B-3", Good("COPPER_ORE", "EXPORT", buy: 40, sell: 35)),
        };

        var system = Summarise(markets).Single(sample => sample.System == "X1-B");

        system.RawGoods.Should().BeEquivalentTo(new[]
        {
            new RawGoodSample { Good = "HYDROCARBON", Price = 28, Market = "X1-B-2", Supply = "HIGH" },
            new RawGoodSample { Good = "IRON_ORE", Price = 61, Market = "X1-B-1", Supply = "SCARCE" },
        });
    }

    [Fact]
    public void ItsSites_AreTheAsteroidsWhoseDepositsYieldAGood_AndItsGasGiants()
    {
        WaypointCacheModel[] waypoints =
        [
            Waypoint("X1-B-R1", "ASTEROID", """[{"symbol":"COMMON_METAL_DEPOSITS"}]"""),
            Waypoint("X1-B-R2", "ASTEROID", """[{"symbol":"RARE_METAL_DEPOSITS"}]"""),
            Waypoint("X1-B-R3", "ASTEROID", """[{"symbol":"UNCHARTED"}]"""),
            Waypoint("X1-B-G1", "GAS_GIANT", "[]"),
            Waypoint("X1-B-A1", "PLANET", """[{"symbol":"MARKETPLACE"},{"symbol":"SHIPYARD"}]""", market: true, shipyard: true),
        ];

        var system = SystemOpportunities.Summarise(null, "X1-A", waypoints, [], Now).Single(sample => sample.System == "X1-B");

        system.GatheringSites.Should().Contain(new KeyValuePair<string, int>("IRON_ORE", 1))
            .And.Contain(new KeyValuePair<string, int>("URANITE_ORE", 1))
            .And.Contain(new KeyValuePair<string, int>("LIQUID_HYDROGEN", 1));
        system.WaypointTypes.Should().Contain(new KeyValuePair<string, int>("ASTEROID", 3));
        system.Markets.Should().Be(1);
        system.Shipyards.Should().Be(1);
        system.Uncharted.Should().Be(1);
    }

    [Fact]
    public void ItsState_SaysWhatTheExplorePlanKnows()
    {
        var plan = new ExplorePlanState
        {
            ShipSymbol = "SHIP-1",
            HomeSystemSymbol = "X1-A",
            Status = ExploreStatus.Exploring,
            UpdatedAt = Now,
            Systems =
            [
                new KnownSystem { SystemSymbol = "X1-A", GateWaypointSymbol = "X1-A-G", Gate = GateState.Active, Connections = ["X1-B-G", "X1-C-G", "X1-D-G", "X1-E-G"], ConnectionsCheckedAt = Now, ExploredAt = Now },
                new KnownSystem { SystemSymbol = "X1-B", GateWaypointSymbol = "X1-B-G", Gate = GateState.Active, ExploredAt = Now },
                new KnownSystem { SystemSymbol = "X1-C", GateWaypointSymbol = "X1-C-G", Gate = GateState.Active },
                new KnownSystem { SystemSymbol = "X1-D", GateWaypointSymbol = "X1-D-G", Gate = GateState.UnderConstruction },
                new KnownSystem { SystemSymbol = "X1-E", GateWaypointSymbol = "X1-E-G", Gate = GateState.Unknown, JumpRefusedAt = Now.AddMinutes(-10) },
            ],
        };

        var systems = SystemOpportunities.Summarise(plan, "X1-A", [], [], Now);

        systems.Select(system => (system.System, system.State, system.Jumps)).Should().Equal(
            ("X1-A", "home", (int?)0),
            ("X1-B", "explored", 1),
            ("X1-C", "to_explore", 1),
            ("X1-D", "gate_under_construction", 1),
            ("X1-E", "jump_refused", 1));
        systems[0].Connections.Should().Equal("X1-B", "X1-C", "X1-D", "X1-E");
    }

    private static IReadOnlyList<SystemSample> Summarise(IReadOnlyList<MarketSnapshot> markets)
        => SystemOpportunities.Summarise(null, "X1-A", [], markets, Now);

    private static MarketSnapshot Market(string waypoint, params TradeGoodSnapshot[] goods)
        => new(waypoint, "X1-B", goods, [], [], []);

    private static TradeGoodSnapshot Good(string symbol, string type, int buy, int sell, string supply = "MODERATE")
        => new(symbol, type, buy, sell, 10, supply);

    private static WaypointCacheModel Waypoint(string symbol, string type, string traits, bool market = false, bool shipyard = false)
        => new(symbol, "X1-B", type, 0, 0, market, shipyard, Now, traits);
}
