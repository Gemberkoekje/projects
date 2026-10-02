using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using static SpaceTraders.Application.Tests.Siphoning.SiphonFixture;

namespace SpaceTraders.Application.Tests.Automation;

/// <summary>
/// Slice 6.7: the mining plan for gases. Every free siphoner takes one trip at a time, for the market shortest of
/// a gas first (D28); gases it holds are sold first, as a trip keeps every gas it siphons (D33). Only a ship that
/// can neither mine nor survey siphons, and a drone is bought only when its first trip would serve a market short
/// of a gas, up to <c>Siphon.MaxDrones</c> (D32).
/// </summary>
public sealed class SiphonAutomationServiceTests
{
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IShipAssignmentRepository _assignments = Substitute.For<IShipAssignmentRepository>();
    private readonly IShipyardRepository _shipyards = Substitute.For<IShipyardRepository>();
    private readonly ITradeContextReader _contexts = Substitute.For<ITradeContextReader>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly IPlanRepository _plans = Substitute.For<IPlanRepository>();
    private readonly IShipPurchaseService _purchases = Substitute.For<IShipPurchaseService>();
    private readonly LogRecorder _log = new();
    private readonly Dictionary<string, ShipGoal> _activeGoals = new(StringComparer.OrdinalIgnoreCase);
    private MiningAutomationPlanState? _state;

    public SiphonAutomationServiceTests()
    {
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipAssignmentDto>());
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context());
        _goals.GetActiveGoalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _activeGoals.GetValueOrDefault(call.Arg<string>()));
        _goals.When(goals => goals.SetActiveGoalAsync(Arg.Any<string>(), Arg.Any<ShipGoal>(), Arg.Any<CancellationToken>()))
            .Do(call => _activeGoals[call.ArgAt<string>(0)] = call.ArgAt<ShipGoal>(1));
        _plans.GetAsync<MiningAutomationPlanState>(PlanTypes.SiphonAutomation, Arg.Any<CancellationToken>()).Returns(_ => _state);
        _plans.When(plans => plans.UpsertAsync(PlanTypes.SiphonAutomation, Arg.Any<MiningAutomationPlanState>(), Arg.Any<CancellationToken>()))
            .Do(call => _state = call.ArgAt<MiningAutomationPlanState>(1));
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ShipyardWaypointDto
            {
                WaypointSymbol = C39,
                SystemSymbol = SystemSymbol,
                ShipTypes = ["SHIP_PROBE", "SHIP_SIPHON_DRONE"],
                Ships = [new ShipyardShipDto { Type = "SHIP_SIPHON_DRONE", PurchasePrice = 42_000, FuelCapacity = 80, CargoCapacity = 15 }],
            },
        ]);
        _purchases.TryPurchaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ShipPurchaseResult { IsSuccess = true });
    }

    [Fact]
    public async Task AFreeSiphoner_ServesTheScarcestMarketFirst_AtTheGasGiantNearestIt()
    {
        Fleet(SiphonDrone());

        await RunAsync();

        var trip = _activeGoals["SHIP-5"].Should().BeOfType<SiphonAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SourceWaypointSymbol, trip.SellWaypointSymbol, trip.Selling).Should().Be(("LIQUID_HYDROGEN", C38, G50, false));

        var started = _log.Journal.Should().ContainSingle().Subject;
        started.EventKind.Should().Be("SiphonStarted");
        started.Properties["Reason"].Should().Be("low_supply");
    }

    [Fact]
    public async Task OnceEveryMarketShortOfAGasHasASiphoner_AFreeSiphonerServesTheLowestSupplyLeft()
    {
        // D28: "keep mining for whatever the lowest supply ore is", for gases too.
        HeldBy("SHIP-6", G50, "LIQUID_HYDROGEN");
        HeldBy("SHIP-7", E47, "LIQUID_NITROGEN");
        HeldBy("SHIP-8", G50, "HYDROCARBON");
        Fleet(SiphonDrone(), SiphonDrone("SHIP-6"), SiphonDrone("SHIP-7"), SiphonDrone("SHIP-8"));

        await RunAsync();

        var trip = _activeGoals["SHIP-5"].Should().BeOfType<SiphonAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SellWaypointSymbol).Should().Be(("HYDROCARBON", C39));
        _log.Journal.Should().ContainSingle(entry => entry.EventKind == "SiphonStarted")
            .Which.Properties["Reason"].Should().Be("lowest_supply");
    }

    [Fact]
    public async Task TwoSiphoners_NeverShareASellMarketAndGas()
    {
        Fleet(SiphonDrone("SHIP-5"), SiphonDrone("SHIP-6"));

        await RunAsync();

        _activeGoals.Values.Cast<SiphonAndSellGoal>()
            .Select(trip => (trip.SellWaypointSymbol, trip.TradeSymbol))
            .Should().BeEquivalentTo([(G50, "LIQUID_HYDROGEN"), (E47, "LIQUID_NITROGEN")]);
    }

    [Fact]
    public async Task ASiphonerHoldingGas_SellsIt_WhereItFetchesMost()
    {
        // D33: a trip keeps every gas, and sells only its own; the others are sold one a trip.
        Fleet(SiphonDrone(waypoint: C38, status: "IN_ORBIT", cargo: [new CargoItemModel("LIQUID_NITROGEN", 6)]));

        await RunAsync();

        var trip = _activeGoals["SHIP-5"].Should().BeOfType<SiphonAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SellWaypointSymbol, trip.Selling).Should().Be(("LIQUID_NITROGEN", E47, true));
        _log.Journal.Should().ContainSingle(entry => Equals(entry.Properties["Reason"], "held_cargo"));
    }

    [Fact]
    public async Task ASiphonerWithAFullHold_SellsWhatItHolds_EvenWhenTheSaleDoesntPayForItsFuel()
    {
        // Only C39 buys its nitrogen, for 1 a unit: 15 credits against 80 for the fuel there. A siphon trip would
        // turn to selling at once and end without its gas aboard, on every tick.
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(
            Market(C39, Good("LIQUID_NITROGEN", "EXCHANGE", 2, 1, 60, "MODERATE"), Good("FUEL", "EXCHANGE", 80, 70, 180, "MODERATE")),
            Market(C40, Good("FUEL", "EXCHANGE", 75, 66, 180, "MODERATE")),
            Market(G50, Good("LIQUID_HYDROGEN", "IMPORT", 110, 55, 60, "SCARCE"), Good("FUEL", "EXCHANGE", 82, 72, 180, "MODERATE"))));
        Fleet(SiphonDrone(waypoint: C38, status: "IN_ORBIT", cargo: [new CargoItemModel("LIQUID_NITROGEN", 15)]));

        await RunAsync();

        var trip = _activeGoals["SHIP-5"].Should().BeOfType<SiphonAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SellWaypointSymbol, trip.Selling).Should().Be(("LIQUID_NITROGEN", C39, true));
    }

    [Fact]
    public async Task ASiphonerWithAFullHold_ThatNoMarketBuys_GetsNoTrip()
    {
        Fleet(SiphonDrone(waypoint: C38, status: "IN_ORBIT", cargo: [new CargoItemModel("EXOTIC_MATTER", 15)]));

        await RunAsync();

        _activeGoals.Should().BeEmpty();
        _log.Journal.Should().BeEmpty();
    }

    [Fact]
    public async Task OnlyAShipThatCanNeitherMineNorSurvey_Siphons()
    {
        // The command ship has a gas siphon, but it mines or surveys (D20); a mining drone has none.
        Fleet(CommandShip(), SiphonDrone() with { MountSymbols = ["MOUNT_MINING_LASER_I"], ShipType = "SHIP_MINING_DRONE" });

        await RunAsync();

        _activeGoals.Should().BeEmpty();
    }

    [Fact]
    public async Task ASiphonerWithAnotherGoal_OrAnAssignment_OrInTransit_IsNotFree()
    {
        _activeGoals["SHIP-5"] = new TradeBetweenMarketsGoal { TradeSymbol = "FOOD", BuyWaypointSymbol = C39, SellWaypointSymbol = G50 };
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(
            [new ShipAssignmentDto("SHIP-6", "Contract", C38, G50, "HYDROCARBON", "C-1", 0, DateTimeOffset.UtcNow, null)]);
        Fleet(SiphonDrone("SHIP-5"), SiphonDrone("SHIP-6"), SiphonDrone("SHIP-7") with { Status = "IN_TRANSIT", ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(1) });

        await RunAsync();

        _activeGoals.Should().ContainSingle().Which.Value.Should().BeOfType<TradeBetweenMarketsGoal>();
    }

    [Fact]
    public async Task WithNoSiphoner_ItBuysTheFirstDrone_WhereItSellsForTheLeast()
    {
        // D32: the miners' rule. The drone's first trip, from C39, would serve G50's scarce hydrogen.
        Fleet(CommandShip(waypoint: "X1-DC53-H51"));

        await RunAsync();

        await _purchases.Received(1).TryPurchaseAsync("SHIP_SIPHON_DRONE", C39, Arg.Any<CancellationToken>());
        _activeGoals.Should().BeEmpty();
    }

    [Fact]
    public async Task WithEveryMarketShortOfAGasServed_NoDroneIsBought_ThoughThereIsMoreToSiphon()
    {
        // D28, D32: a new drone would serve a MODERATE market, which would leave nothing to stop the next.
        HeldBy("SHIP-5", G50, "LIQUID_HYDROGEN");
        HeldBy("SHIP-6", E47, "LIQUID_NITROGEN");
        HeldBy("SHIP-7", G50, "HYDROCARBON");
        Fleet(SiphonDrone("SHIP-5"), SiphonDrone("SHIP-6"), SiphonDrone("SHIP-7"));

        await RunAsync();

        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    [Fact]
    public async Task WithAFreeSiphoner_NoDroneIsBought()
    {
        Fleet(SiphonDrone());

        await RunAsync();

        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    [Fact]
    public async Task AtTheCap_NoDroneIsBought()
    {
        // D32: Siphon.MaxDrones, 10 unless set.
        _settings.GetAsync<int>(SiphonAutomationService.MaxDronesSetting, Arg.Any<CancellationToken>()).Returns(1);
        HeldBy("SHIP-5", G50, "LIQUID_HYDROGEN");
        Fleet(SiphonDrone("SHIP-5"));

        await RunAsync();

        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    [Fact]
    public async Task TheState_ListsTheOpenings_WithTheSiphonersThatCouldTakeThem()
    {
        // The drone takes G50's hydrogen; E47's nitrogen and G50's hydrocarbon stay open, and so do F48's, beyond
        // its tank.
        Fleet(SiphonDrone(), SiphonDrone("SHIP-6") with { Status = "IN_TRANSIT", ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(1) });

        await RunAsync();

        _state!.Opportunities.Should().ContainSingle(o => o.Status == MarketAutomationOpportunityStatus.Assigned)
            .Which.Should().Match<MiningAutomationOpportunityState>(o => o.AssignedShipSymbol == "SHIP-5" && o.SourceWaypointSymbol == C38);
        _state.Opportunities.Where(o => o.Status == MarketAutomationOpportunityStatus.Pending).Should().HaveCount(4)
            .And.OnlyContain(o => o.CandidateShipSymbols.Count == 0, "the only free siphoner took a trip");
    }

    [Fact]
    public async Task TheState_IsWrittenOnlyWhenItChanges()
    {
        HeldBy("SHIP-5", G50, "LIQUID_HYDROGEN");
        _purchases.TryPurchaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ShipPurchaseResult { IsSuccess = false, FailureReason = "budget" });
        Fleet(SiphonDrone());

        await RunAsync();
        await RunAsync();
        await RunAsync();

        await _plans.Received(1).UpsertAsync(PlanTypes.SiphonAutomation, Arg.Any<MiningAutomationPlanState>(), Arg.Any<CancellationToken>());
    }

    private void Fleet(params ShipModel[] fleet) => _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(fleet);

    private void HeldBy(string ship, string market, string gas)
        => _activeGoals[ship] = new SiphonAndSellGoal { TradeSymbol = gas, SourceWaypointSymbol = C38, SellWaypointSymbol = market };

    private Task RunAsync()
        => new SiphonAutomationService(
                _ships,
                _goals,
                _assignments,
                _shipyards,
                _contexts,
                _settings,
                _plans,
                _purchases,
                _log.For<SiphonAutomationService>())
            .EnsureBootstrappedAsync();
}
