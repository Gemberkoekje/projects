using System.IO;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Prometheus;
using SpaceTraders.API.Services;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Scoping;

namespace SpaceTraders.API.Tests.Services;

/// <summary>B11: the state the dashboard shows comes from the cache, every 10 seconds.</summary>
public sealed class PrometheusMetricsServiceTests
{
    private const string AgentId = "AGENT@2026-09-27";

    private readonly IAutomationMetrics _metrics = Substitute.For<IAutomationMetrics>();

    [Fact]
    public async Task SampleAsync_ExportsCreditsShipsAndContracts_FromTheCache()
    {
        var blocked = new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-2", Status = GoalStatus.Blocked, StatusReason = "runaway" };
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Agents.Add(new CachedAgent { AgentId = AgentId, Symbol = "AGENT", StartingFaction = "COSMIC", Credits = 175_000 });
            db.Ships.Add(new CachedShip { AgentId = AgentId, Symbol = "AGENT-1", ShipType = "COMMAND", Status = "DOCKED" });
            db.Ships.Add(new CachedShip
            {
                AgentId = AgentId,
                Symbol = "AGENT-2",
                ShipType = "SATELLITE",
                Status = "IN_TRANSIT",
                ArrivesAt = TimeProvider.System.GetUtcNow().AddMinutes(5),
            });
            db.Ships.Add(new CachedShip
            {
                AgentId = AgentId,
                Symbol = "AGENT-3",
                ShipType = "EXCAVATOR",
                Status = "IN_TRANSIT",
                ArrivesAt = TimeProvider.System.GetUtcNow().AddMinutes(-1),
            });
            db.Ships.Add(new CachedShip
            {
                AgentId = AgentId,
                Symbol = "AGENT-4",
                ShipType = "COMMAND",
                Status = "IN_ORBIT",
                GoalId = blocked.GoalId,
                GoalKind = blocked.Kind.ToString(),
                GoalPayloadJson = JsonSerializer.Serialize<ShipGoal>(blocked),
                GoalStatus = (int)GoalStatus.Blocked,
            });
            db.ShipAssignments.Add(new ShipAssignmentRecord { AgentId = AgentId, ShipSymbol = "AGENT-3", Type = "Contract", ContractId = "C-1" });
            db.Contracts.Add(new CachedContract
            {
                AgentId = AgentId,
                Id = "C-1",
                FactionSymbol = "COSMIC",
                Type = "PROCUREMENT",
                IsAccepted = true,
                TermsDeadline = new DateTimeOffset(2026, 10, 08, 07, 09, 22, TimeSpan.Zero),
                DeliverablesJson = JsonSerializer.Serialize(new List<ContractDeliverableDto> { new("IRON_ORE", "X1-AB-2", 42, 7) }),
            });
            db.Contracts.Add(new CachedContract { AgentId = AgentId, Id = "C-2", FactionSymbol = "COSMIC", Type = "PROCUREMENT" });
            await db.SaveChangesAsync();
        }

        IReadOnlyCollection<ShipMetricsSample> ships = [];
        IReadOnlyCollection<ContractMetricsSample> contracts = [];
        _metrics.When(m => m.Fleet(Arg.Any<IReadOnlyCollection<ShipMetricsSample>>(), Arg.Any<DateTimeOffset>()))
            .Do(call => ships = call.Arg<IReadOnlyCollection<ShipMetricsSample>>());
        _metrics.When(m => m.Contracts(Arg.Any<IReadOnlyCollection<ContractMetricsSample>>()))
            .Do(call => contracts = call.Arg<IReadOnlyCollection<ContractMetricsSample>>());

        using var service = new PrometheusMetricsService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _metrics,
            new ShipStateJournal(NullLogger<ShipStateJournal>.Instance),
            NullLogger<PrometheusMetricsService>.Instance);
        await service.SampleAsync(CancellationToken.None);

        _metrics.Received(1).Credits(175_000);
        ships.Select(s => new ShipMetricsSample(s.Ship, s.Role, s.State, s.Goal, s.Reason)).Should().BeEquivalentTo(new[]
        {
            new ShipMetricsSample("AGENT-1", "COMMAND", "DOCKED", "None", string.Empty),
            new ShipMetricsSample("AGENT-2", "SATELLITE", "IN_TRANSIT", "None", string.Empty),
            new ShipMetricsSample("AGENT-3", "EXCAVATOR", "IN_ORBIT", "Contract", string.Empty),
            new ShipMetricsSample("AGENT-4", "COMMAND", "IN_ORBIT", "ScoutWaypoint", "runaway"),
        });
        contracts.Should().Equal(new ContractMetricsSample("C-1", "IRON_ORE", 42, 7, new DateTimeOffset(2026, 10, 08, 07, 09, 22, TimeSpan.Zero)));
    }

    /// <summary>
    /// The dashboard's fleet table shows where each ship is (and what is there), what the bot has it
    /// do and what it carries: on 2026-10-02 a drone in orbit was mining, and nothing showed it.
    /// </summary>
    [Fact]
    public async Task SampleAsync_SaysWhereEachShipIsWhatItDoesAndWhatItCarries()
    {
        var now = TimeProvider.System.GetUtcNow();
        var scout = new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-A2" };
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Waypoints.Add(Waypoint("X1-AB-XB5C", "ENGINEERED_ASTEROID"));
            db.Waypoints.Add(Waypoint("X1-AB-H51", "PLANET"));
            db.Waypoints.Add(Waypoint("X1-AB-A2", "MOON"));

            // The scout, on its way to the next market.
            db.Ships.Add(new CachedShip
            {
                AgentId = AgentId,
                Symbol = "AGENT-1",
                ShipType = "COMMAND",
                Status = "IN_TRANSIT",
                WaypointSymbol = "X1-AB-A2",
                DestWaypointSymbol = "X1-AB-A2",
                ArrivesAt = now.AddMinutes(2),
                CargoCapacity = 40,
                GoalId = scout.GoalId,
                GoalKind = scout.Kind.ToString(),
                GoalPayloadJson = JsonSerializer.Serialize<ShipGoal>(scout),
                GoalStatus = (int)scout.Status,
            });

            // The probe, parked without work (D9).
            db.Ships.Add(new CachedShip { AgentId = AgentId, Symbol = "AGENT-2", ShipType = "SATELLITE", Status = "DOCKED", WaypointSymbol = "X1-AB-H51" });

            // The contract's drone, in orbit at its asteroid, and a second one on its way to deliver.
            db.Ships.Add(new CachedShip
            {
                AgentId = AgentId,
                Symbol = "AGENT-3",
                ShipType = "SHIP_MINING_DRONE",
                Status = "IN_ORBIT",
                WaypointSymbol = "X1-AB-XB5C",
                CargoCurrent = 11,
                CargoCapacity = 15,
                CargoJson = """[{"Symbol":"COPPER_ORE","Units":9},{"Symbol":"SILICON_CRYSTALS","Units":2}]""",
            });
            db.Ships.Add(new CachedShip
            {
                AgentId = AgentId,
                Symbol = "AGENT-5",
                ShipType = "SHIP_MINING_DRONE",
                Status = "IN_TRANSIT",
                WaypointSymbol = "X1-AB-H51",
                DestWaypointSymbol = "X1-AB-H51",
                ArrivesAt = now.AddMinutes(3),
                CargoCurrent = 15,
                CargoCapacity = 15,
                CargoJson = """[{"Symbol":"COPPER_ORE","Units":15}]""",
            });
            db.ShipAssignments.Add(ContractAssignment("AGENT-3"));
            db.ShipAssignments.Add(ContractAssignment("AGENT-5"));
            await db.SaveChangesAsync();
        }

        IReadOnlyCollection<ShipMetricsSample> ships = [];
        _metrics.When(m => m.Fleet(Arg.Any<IReadOnlyCollection<ShipMetricsSample>>(), Arg.Any<DateTimeOffset>()))
            .Do(call => ships = call.Arg<IReadOnlyCollection<ShipMetricsSample>>());

        using var service = new PrometheusMetricsService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _metrics,
            new ShipStateJournal(NullLogger<ShipStateJournal>.Instance),
            NullLogger<PrometheusMetricsService>.Instance);
        await service.SampleAsync(CancellationToken.None);

        ships.Select(s => (s.Ship, s.Location, s.Activity)).Should().BeEquivalentTo(new[]
        {
            ("AGENT-1", "→ X1-AB-A2 (MOON)", "scouting"),
            ("AGENT-2", "X1-AB-H51 (PLANET)", "idle"),
            ("AGENT-3", "X1-AB-XB5C (ENGINEERED_ASTEROID)", "mining COPPER_ORE"),
            ("AGENT-5", "→ X1-AB-H51 (PLANET)", "on the way to deliver COPPER_ORE"),
        });
        var drone = ships.Single(s => s.Ship == "AGENT-3");
        drone.CargoCapacity.Should().Be(15);
        drone.Cargo.Should().BeEquivalentTo(new[] { new CargoItemModel("COPPER_ORE", 9), new CargoItemModel("SILICON_CRYSTALS", 2) });
        drone.ArrivesAt.Should().Be(default);
        ships.Single(s => s.Ship == "AGENT-1").ArrivesAt.Should().BeCloseTo(now.AddMinutes(2), TimeSpan.FromSeconds(1));
    }

    private static CachedWaypoint Waypoint(string symbol, string type)
        => new() { AgentId = AgentId, Symbol = symbol, SystemSymbol = "X1-AB", Type = type };

    private static ShipAssignmentRecord ContractAssignment(string ship) => new()
    {
        AgentId = AgentId,
        ShipSymbol = ship,
        Type = "Contract",
        OriginWaypoint = "X1-AB-XB5C",
        DestWaypoint = "X1-AB-H51",
        CargoSymbol = "COPPER_ORE",
        ContractId = "C-1",
    };

    private static ServiceProvider BuildProvider()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddSingleton<IAgentDataScope>(_ =>
        {
            var scope = new AgentDataScope();
            scope.Set(AgentId);
            return scope;
        });
        services.AddDbContext<SpaceTradersDbContext>(options => options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }
}

public sealed class PrometheusAutomationMetricsTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 01, 12, 00, 00, TimeSpan.Zero);

    private readonly CollectorRegistry _registry = Metrics.NewCustomRegistry();
    private readonly PrometheusAutomationMetrics _metrics;

    public PrometheusAutomationMetricsTests()
    {
        _metrics = new PrometheusAutomationMetrics(_registry);
    }

    [Fact]
    public async Task AScrape_ListsEveryMetric_BeforeAnyHasAValue()
    {
        var text = await ExportAsync();

        foreach (var name in MetricsEndpointTests.ExpectedMetrics)
        {
            text.Should().Contain($"# TYPE {name} ");
        }
    }

    /// <summary>
    /// Every counter, each with what its first increment looked like on the cluster: the contract's
    /// deposit and the drone, booked before Prometheus first scraped the pod; a single 429; a single
    /// circuit breaker trip.
    /// </summary>
    public static TheoryData<string, Action<IAutomationMetrics>, string> FirstIncrements => new()
    {
        { "credits earned", metrics => metrics.CreditsEarned("ContractDeposit", 4_267), "spacetraders_credits_earned_total{source=\"ContractDeposit\"} " },
        { "credits spent", metrics => metrics.CreditsSpent("ShipPurchase", 46_885), "spacetraders_credits_spent_total{category=\"ShipPurchase\"} " },
        { "429s", metrics => metrics.ApiThrottled("rate_limiter"), "spacetraders_api_throttled_total{source=\"rate_limiter\"} " },
        { "API responses", metrics => metrics.ApiRequest("POST", "my/ships/{shipSymbol}/extract", "429"), "spacetraders_api_requests_total{method=\"POST\",endpoint=\"my/ships/{shipSymbol}/extract\",status=\"429\"} " },
        { "breaker trips", metrics => metrics.GoalBreakerTripped("AGENT-3"), "spacetraders_goal_breaker_trips_total{ship=\"AGENT-3\"} " },
        { "goal steps", metrics => metrics.GoalStep("ScoutWaypoint"), "spacetraders_goal_steps_total{kind=\"ScoutWaypoint\"} " },
        { "messages", metrics => metrics.MessageHandled("ContractAcceptedEvent"), "spacetraders_messages_handled_total{type=\"ContractAcceptedEvent\"} " },
        { "rate-limit waits", metrics => metrics.RateLimitWait(TimeSpan.FromSeconds(3), "read"), "spacetraders_api_rate_limit_wait_seconds_total{kind=\"read\"} " },
        { "units extracted", metrics => metrics.Extracted("AGENT-3", "COPPER_ORE", 2), "spacetraders_extracted_units_total{ship=\"AGENT-3\",good=\"COPPER_ORE\"} " },
        { "units jettisoned", metrics => metrics.Jettisoned("AGENT-3", "SILICON_CRYSTALS", 2), "spacetraders_jettisoned_units_total{ship=\"AGENT-3\",good=\"SILICON_CRYSTALS\"} " },
    };

    /// <summary>
    /// B43: Prometheus's increase() and rate() count what a series gains between two scrapes, never
    /// the value Prometheus first sees. A series that held its first increment when it was first
    /// scraped never showed it on the dashboard: the contract's 4,267 and the drone's 46,885 were
    /// missing from the ledger panels, and a single 429 or breaker trip could never show at all.
    /// </summary>
    [Theory]
    [MemberData(nameof(FirstIncrements))]
    public async Task ACountersFirstIncrement_ReachesPrometheusAfterItHasScrapedTheSeriesAtZero(
        string counter, Action<IAutomationMetrics> increment, string series)
    {
        increment(_metrics);

        (await ExportAsync()).Should().Contain(series + "0", "the first scrape of {0} must see the series at 0", counter);
        (await ExportAsync()).Should().NotContain(series + "0\n", "the scrape after that must see {0}' first increment", counter);
    }

    [Fact]
    public async Task ASeriesThatFirstAppearsLater_IsScrapedAtZeroFirstToo()
    {
        await ExportAsync();
        await ExportAsync();

        _metrics.CreditsSpent("ShipPurchase", 46_885);

        (await ExportAsync()).Should().Contain("spacetraders_credits_spent_total{category=\"ShipPurchase\"} 0\n");
        (await ExportAsync()).Should().Contain("spacetraders_credits_spent_total{category=\"ShipPurchase\"} 46885\n");
    }

    [Fact]
    public async Task ASeriesPrometheusHasSeenAtZero_CountsAtOnce()
    {
        _metrics.CreditsEarned("TradeSell", 450);
        await ExportAsync();
        (await ExportAsync()).Should().Contain("spacetraders_credits_earned_total{source=\"TradeSell\"} 450\n");

        _metrics.CreditsEarned("TradeSell", 50);

        (await ExportAsync()).Should().Contain("spacetraders_credits_earned_total{source=\"TradeSell\"} 500\n");
    }

    [Fact]
    public async Task AShip_KeepsWhenItEnteredItsState_UntilItsStateChanges()
    {
        _metrics.Fleet([new ShipMetricsSample("AGENT-1", "COMMAND", "DOCKED", "None", string.Empty)], Start);
        _metrics.Fleet([new ShipMetricsSample("AGENT-1", "COMMAND", "DOCKED", "None", string.Empty)], Start.AddMinutes(1));

        (await ExportAsync()).Should().Contain(StatusLine("AGENT-1", "COMMAND", "DOCKED", "None", string.Empty, Start));

        _metrics.Fleet([new ShipMetricsSample("AGENT-1", "COMMAND", "IN_TRANSIT", "None", string.Empty)], Start.AddMinutes(2));

        var text = await ExportAsync();
        text.Should().Contain(StatusLine("AGENT-1", "COMMAND", "IN_TRANSIT", "None", string.Empty, Start.AddMinutes(2)));
        text.Should().NotContain("state=\"DOCKED\",goal");
        text.Should().Contain("spacetraders_ships{role=\"COMMAND\",state=\"DOCKED\"} 0");
        text.Should().Contain("spacetraders_ships{role=\"COMMAND\",state=\"IN_TRANSIT\"} 1");
    }

    [Fact]
    public async Task AShipThatIsGone_LosesItsSeries()
    {
        _metrics.Fleet([new ShipMetricsSample("AGENT-1", "COMMAND", "DOCKED", "None", string.Empty)], Start);

        _metrics.Fleet([], Start.AddMinutes(1));

        (await ExportAsync()).Should().NotContain("ship=\"AGENT-1\"");
    }

    /// <summary>
    /// The fleet table: where a ship is, what it does, and its hold. Series that no longer hold are
    /// removed, so the table shows one row per ship and the hold only what is in it.
    /// </summary>
    [Fact]
    public async Task AShip_ShowsWhereItIsWhatItDoesAndWhatItCarries()
    {
        _metrics.Fleet([Drone("X1-AB-XB5C (ENGINEERED_ASTEROID)", "mining COPPER_ORE", [new("COPPER_ORE", 9), new("SILICON_CRYSTALS", 2)])], Start);

        var text = await ExportAsync();
        text.Should().Contain("spacetraders_ship_info{ship=\"AGENT-3\",location=\"X1-AB-XB5C (ENGINEERED_ASTEROID)\",activity=\"mining COPPER_ORE\"} 1\n");
        text.Should().Contain("spacetraders_ship_cargo_units{ship=\"AGENT-3\",good=\"COPPER_ORE\"} 9\n");
        text.Should().Contain("spacetraders_ship_cargo_units{ship=\"AGENT-3\",good=\"SILICON_CRYSTALS\"} 2\n");
        text.Should().Contain("spacetraders_ship_cargo_capacity_units{ship=\"AGENT-3\"} 15\n");
        text.Should().NotContain("spacetraders_ship_arrival_timestamp_seconds{");

        // It jettisoned the crystals, filled up, and is on its way to deliver.
        _metrics.Fleet([Drone("→ X1-AB-H51 (PLANET)", "on the way to deliver COPPER_ORE", [new("COPPER_ORE", 15)], Start.AddMinutes(4))], Start.AddMinutes(1));

        text = await ExportAsync();
        text.Should().Contain("spacetraders_ship_info{ship=\"AGENT-3\",location=\"→ X1-AB-H51 (PLANET)\",activity=\"on the way to deliver COPPER_ORE\"} 1\n");
        text.Should().NotContain("activity=\"mining COPPER_ORE\"");
        text.Should().Contain("spacetraders_ship_cargo_units{ship=\"AGENT-3\",good=\"COPPER_ORE\"} 15\n");
        text.Should().NotContain("good=\"SILICON_CRYSTALS\"");
        text.Should().Contain($"spacetraders_ship_arrival_timestamp_seconds{{ship=\"AGENT-3\"}} {Start.AddMinutes(4).ToUnixTimeSeconds()}\n");

        // Arrived: no arrival time any more.
        _metrics.Fleet([Drone("X1-AB-H51 (PLANET)", "delivering COPPER_ORE", [new("COPPER_ORE", 15)])], Start.AddMinutes(4));

        (await ExportAsync()).Should().NotContain("spacetraders_ship_arrival_timestamp_seconds{");
    }

    /// <summary>The markets dashboard (slice 2.8): a market's goods with their prices, volume, supply and activity.</summary>
    [Fact]
    public async Task AMarket_ShowsItsGoodsWithPricesVolumeSupplyAndActivity()
    {
        _metrics.Markets([Market(
            new TradeGoodSnapshot("COPPER_ORE", "IMPORT", 60, 55, 60, "SCARCE", "WEAK"),
            new TradeGoodSnapshot("FUEL", "EXCHANGE", 72, 68, 180, "MODERATE", string.Empty))]);

        var text = await ExportAsync();
        text.Should().Contain($"spacetraders_market_observed_timestamp_seconds{{system=\"X1-AB\",waypoint=\"X1-AB-H51\",waypoint_type=\"PLANET\"}} {Start.ToUnixTimeSeconds()}\n");
        text.Should().Contain("spacetraders_market_purchase_price{system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"COPPER_ORE\",kind=\"IMPORT\"} 60\n");
        text.Should().Contain("spacetraders_market_sell_price{system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"COPPER_ORE\",kind=\"IMPORT\"} 55\n");
        text.Should().Contain("spacetraders_market_trade_volume{system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"COPPER_ORE\",kind=\"IMPORT\"} 60\n");
        text.Should().Contain("spacetraders_market_supply{system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"COPPER_ORE\",kind=\"IMPORT\"} 1\n");
        text.Should().Contain("spacetraders_market_activity{system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"COPPER_ORE\",kind=\"IMPORT\"} 1\n");
        text.Should().Contain("spacetraders_market_supply{system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"FUEL\",kind=\"EXCHANGE\"} 3\n");
        text.Should().NotContain("spacetraders_market_activity{system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"FUEL\"");

        // The next visit no longer lists copper; after a reset the new agent knows no markets yet.
        _metrics.Markets([Market(new TradeGoodSnapshot("FUEL", "EXCHANGE", 74, 70, 180, "LIMITED", "GROWING"))]);
        text = await ExportAsync();
        text.Should().NotContain("good=\"COPPER_ORE\"");
        text.Should().Contain("spacetraders_market_purchase_price{system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"FUEL\",kind=\"EXCHANGE\"} 74\n");
        text.Should().Contain("spacetraders_market_activity{system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"FUEL\",kind=\"EXCHANGE\"} 2\n");

        _metrics.Markets([]);
        (await ExportAsync()).Should().NotContain("waypoint=\"X1-AB-H51\"");
    }

    [Fact]
    public async Task AShipyard_ShowsItsShipTypesAndThePricesItKnows()
    {
        _metrics.Shipyards([new ShipyardMetricsSample(
            "X1-AB",
            "X1-AB-H52",
            "MOON",
            Start,
            ["SHIP_MINING_DRONE", "SHIP_PROBE"],
            [new ShipyardShipDto { Type = "SHIP_MINING_DRONE", PurchasePrice = 46_885, Supply = "MODERATE" }])]);

        var text = await ExportAsync();
        text.Should().Contain($"spacetraders_shipyard_observed_timestamp_seconds{{system=\"X1-AB\",waypoint=\"X1-AB-H52\",waypoint_type=\"MOON\"}} {Start.ToUnixTimeSeconds()}\n");
        text.Should().Contain("spacetraders_shipyard_ship_type{system=\"X1-AB\",waypoint=\"X1-AB-H52\",ship_type=\"SHIP_MINING_DRONE\"} 1\n");
        text.Should().Contain("spacetraders_shipyard_ship_type{system=\"X1-AB\",waypoint=\"X1-AB-H52\",ship_type=\"SHIP_PROBE\"} 1\n");
        text.Should().Contain("spacetraders_shipyard_ship_price{system=\"X1-AB\",waypoint=\"X1-AB-H52\",ship_type=\"SHIP_MINING_DRONE\"} 46885\n");
        text.Should().Contain("spacetraders_shipyard_ship_supply{system=\"X1-AB\",waypoint=\"X1-AB-H52\",ship_type=\"SHIP_MINING_DRONE\"} 3\n");
        text.Should().NotContain("spacetraders_shipyard_ship_price{system=\"X1-AB\",waypoint=\"X1-AB-H52\",ship_type=\"SHIP_PROBE\"}");

        _metrics.Shipyards([]);
        (await ExportAsync()).Should().NotContain("waypoint=\"X1-AB-H52\"");
    }

    [Fact]
    public async Task TheSupplyChain_ShowsWhatEachGoodIsMadeFromAndWhatIsMadeFromIt()
    {
        _metrics.SupplyChain(new Dictionary<string, IReadOnlyList<string>>
        {
            ["IRON"] = ["IRON_ORE"],
            ["MACHINERY"] = ["IRON"],
            ["FAB_MATS"] = ["QUARTZ_SAND", "IRON"],
        });

        var text = await ExportAsync();
        text.Should().Contain("spacetraders_good_supply_chain{good=\"IRON\",made_from=\"IRON_ORE\",used_for=\"FAB_MATS, MACHINERY\"} 1\n");
        text.Should().Contain("spacetraders_good_supply_chain{good=\"FAB_MATS\",made_from=\"IRON, QUARTZ_SAND\",used_for=\"\"} 1\n");
        text.Should().Contain("spacetraders_good_supply_chain{good=\"IRON_ORE\",made_from=\"\",used_for=\"IRON\"} 1\n");
        text.Should().Contain("spacetraders_good_supply_chain{good=\"QUARTZ_SAND\",made_from=\"\",used_for=\"FAB_MATS\"} 1\n");
    }

    [Fact]
    public async Task AContractThatIsGone_LosesItsSeries()
    {
        _metrics.Contracts([new ContractMetricsSample("C-1", "IRON_ORE", 42, 7, Start.AddDays(7))]);
        var text = await ExportAsync();
        text.Should().Contain("spacetraders_contract_units_required{contract=\"C-1\",trade_symbol=\"IRON_ORE\"} 42");
        text.Should().Contain("spacetraders_contract_units_fulfilled{contract=\"C-1\",trade_symbol=\"IRON_ORE\"} 7");
        text.Should().Contain($"spacetraders_contract_deadline_timestamp_seconds{{contract=\"C-1\"}} {Start.AddDays(7).ToUnixTimeSeconds()}");

        _metrics.Contracts([]);

        (await ExportAsync()).Should().NotContain("contract=\"C-1\"");
    }

    private static MarketMetricsSample Market(params TradeGoodSnapshot[] goods)
        => new("X1-AB", "X1-AB-H51", "PLANET", Start, goods);

    private static ShipMetricsSample Drone(string location, string activity, CargoItemModel[] cargo, DateTimeOffset arrivesAt = default)
        => new("AGENT-3", "SHIP_MINING_DRONE", arrivesAt == default ? "IN_ORBIT" : "IN_TRANSIT", "Contract", string.Empty)
        {
            Location = location,
            Activity = activity,
            ArrivesAt = arrivesAt,
            CargoCapacity = 15,
            Cargo = cargo,
        };

    private static string StatusLine(string ship, string role, string state, string goal, string reason, DateTimeOffset since)
        => $"spacetraders_ship_status_since_timestamp_seconds{{ship=\"{ship}\",role=\"{role}\",state=\"{state}\",goal=\"{goal}\",reason=\"{reason}\"}} {since.ToUnixTimeSeconds()}";

    private async Task<string> ExportAsync()
    {
        using var stream = new MemoryStream();
        await _registry.CollectAndExportAsTextAsync(stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
