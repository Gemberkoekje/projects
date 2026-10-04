using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Naming;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Tests.Services;

public sealed class ShipPurchaseServiceTests
{
    private const string Shipyard = "X1-AB-SY1";

    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipyardRepository _shipyards = Substitute.For<IShipyardRepository>();
    private readonly IBudgetPolicy _budget = Substitute.For<IBudgetPolicy>();
    private readonly ShipyardCalls _calls = new();
    private readonly PurchaseNeeds _purchases = new();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly IActiveReset _reset = Substitute.For<IActiveReset>();
    private readonly LogRecorder _log = new();
    private readonly ShipNameBook _names;

    public ShipPurchaseServiceTests()
    {
        _reset.ResetDate.Returns("2026-10-04");
        _names = new ShipNameBook(_reset);
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("AGENT", null, "X1-AB-HQ", 100_000, "COSMIC", 1));
        _shipyards.FindByWaypointAsync(Shipyard, Arg.Any<CancellationToken>()).Returns(ShipyardSelling(12_000));
        _budget.EvaluateAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(new BudgetDecision(true, 100_000, 20_000, 80_000));
        _port.PurchaseShipAsync("SHIP_MINING_DRONE", Shipyard, Arg.Any<CancellationToken>())
            .Returns(new PurchaseShipActionResult(
                new AgentModel("AGENT", null, "X1-AB-HQ", 88_000, "COSMIC", 2),
                "AGENT-2",
                new NavModel("DOCKED", "X1-AB", Shipyard, "CRUISE", null, null),
                new FuelModel(80, 80),
                new CargoModel(0, 15, []),
                12_000));
        ShipsAt(Shipyard);
    }

    [Fact]
    public async Task TryPurchaseAsync_PublishesThePurchase_ForTheLedgerAndTheCredits()
    {
        // B7: a ship purchase changed the cached credits, but nothing published it, so the ledger and
        // the credits-spent metric never saw the biggest expense there is.
        var result = await Service().TryPurchaseAsync("SHIP_MINING_DRONE", Shipyard);

        result.IsSuccess.Should().BeTrue();
        await _bus.Received(1).PublishAsync(
            Arg.Is<NewShipPurchasedEvent>(e => e.ShipSymbol == "AGENT-2" && e.Type == ShipType.ShipMiningDrone && e.CostPaid == 12_000),
            Arg.Any<DeliveryOptions>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<AgentCreditsChangedEvent>(e => e.OldCredits == 100_000 && e.NewCredits == 88_000),
            Arg.Any<DeliveryOptions>());
        _log.Journal.Should().ContainSingle().Which.Message.Should().StartWith("ShipPurchased: ship AGENT-2 (SHIP_MINING_DRONE) bought at X1-AB-SY1 for 12000 credits;");
    }

    [Fact]
    public async Task TryPurchaseAsync_NamesTheNewShip_InTheJournalAndTheNameBook()
    {
        // Slice 2.14 (D72): the bought ship takes the next number of its type, and its first lines carry its name.
        ShipModel probe = new("PROBE-1", "X1-AB", Shipyard, "DOCKED", "CRUISE", 0, 0, ShipType: "SATELLITE");
        ShipModel bought = new("AGENT-2", "X1-AB", Shipyard, "DOCKED", "CRUISE", 80, 80, ShipType: "SHIP_MINING_DRONE");
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([probe], [probe, bought]);
        var name = ShipNames.For([probe, bought], "2026-10-04")["AGENT-2"];

        await Service().TryPurchaseAsync("SHIP_MINING_DRONE", Shipyard);

        name.Should().EndWith("-1");
        _names.NameOf("AGENT-2").Should().Be(name);
        _log.Journal.Should().ContainSingle().Which.Message.Should().EndWith($"; the bot calls it {name}.");
    }

    [Fact]
    public async Task WithoutAShipOfOursAtTheShipyard_ItCallsForOne_InsteadOfAskingTheApi()
    {
        // D30: the API sells a ship only where one of ours is. Without one there the purchase could only
        // fail, so it makes no call to the API and calls for a ship, which the probe plan sends.
        ShipsAt("X1-AB-ELSEWHERE");

        var result = await Service().TryPurchaseAsync("SHIP_MINING_DRONE", Shipyard);

        result.IsSuccess.Should().BeFalse();
        result.Failure.Should().Be(ShipPurchaseFailure.NoShipAtShipyard);
        await _port.DidNotReceiveWithAnyArgs().PurchaseShipAsync(default!, default!, default);
        _calls.Open(TimeProvider.System.GetUtcNow()).Should().ContainSingle()
            .Which.Should().Match<ShipyardCall>(call => call.WaypointSymbol == Shipyard && call.ShipType == "SHIP_MINING_DRONE");
    }

    [Fact]
    public async Task AShipStillFlyingToTheShipyard_IsNotThereYet()
    {
        _ships.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([new ShipModel("PROBE-1", "X1-AB", "X1-AB-A1", "IN_TRANSIT", "CRUISE", 0, 0, ArrivesAt: DateTimeOffset.MaxValue, DestWaypointSymbol: Shipyard)]);

        var result = await Service().TryPurchaseAsync("SHIP_MINING_DRONE", Shipyard);

        result.Failure.Should().Be(ShipPurchaseFailure.NoShipAtShipyard);
    }

    [Fact]
    public async Task APurchaseItCantAfford_CallsForNoShip()
    {
        // A probe flies to a shipyard only for a purchase that can happen once it is there.
        ShipsAt("X1-AB-ELSEWHERE");
        _budget.EvaluateAsync(12_000, Arg.Any<CancellationToken>()).Returns(new BudgetDecision(false, 100_000, 100_000, 0, Reason: "over budget"));

        var result = await Service().TryPurchaseAsync("SHIP_MINING_DRONE", Shipyard);

        result.Failure.Should().Be(ShipPurchaseFailure.OverBudget);
        _calls.Open(TimeProvider.System.GetUtcNow()).Should().BeEmpty();
    }

    [Fact]
    public async Task APurchase_CountsInThePurchaseOrderAtOnce()
    {
        // Slice 6.10b (D43): drones and cargo ships take turns, counted from the purchases; the ledger's row comes after
        // the purchase, so a plan later in the same tick would otherwise buy another of the same kind.
        await Service().TryPurchaseAsync("SHIP_MINING_DRONE", Shipyard);

        _purchases.Purchases().Should().ContainSingle()
            .Which.Should().Match<PurchaseRecord>(purchase => purchase.ShipSymbol == "AGENT-2" && purchase.Type == ShipType.ShipMiningDrone);
    }

    [Fact]
    public async Task APurchase_AnswersTheCall()
    {
        _calls.Call(Shipyard, "SHIP_MINING_DRONE", TimeProvider.System.GetUtcNow());

        await Service().TryPurchaseAsync("SHIP_MINING_DRONE", Shipyard);

        _calls.Open(TimeProvider.System.GetUtcNow()).Should().BeEmpty();
    }

    [Fact]
    public async Task WithAShipThere_ThePriceIsFetchedAgain_AndTheReserveIsKeptWithTheNewPrice()
    {
        // D29: probes are bought while the credits stay at the reserve or above. The cached price can be
        // hours old, and every purchase raises it; the shipyard is fetched again while our ship is there.
        var fresh = new ShipyardDataModel(Shipyard, "X1-AB", "[]", "[{\"type\":\"SHIP_MINING_DRONE\",\"purchasePrice\":30000}]");
        _port.GetShipyardAsync("X1-AB", Shipyard, Arg.Any<CancellationToken>()).Returns(fresh);
        _shipyards.FindByWaypointAsync(Shipyard, Arg.Any<CancellationToken>()).Returns(ShipyardSelling(12_000), ShipyardSelling(30_000));
        _budget.EvaluateAsync(30_000, Arg.Any<CancellationToken>()).Returns(new BudgetDecision(false, 100_000, 80_000, 20_000, Reason: "over budget"));

        var result = await Service().TryPurchaseAsync("SHIP_MINING_DRONE", Shipyard);

        result.Failure.Should().Be(ShipPurchaseFailure.OverBudget);
        result.EstimatedCost.Should().Be(30_000);
        await _shipyards.Received(1).UpsertAsync(fresh, Arg.Any<CancellationToken>());
        await _port.DidNotReceiveWithAnyArgs().PurchaseShipAsync(default!, default!, default);
    }

    [Fact]
    public async Task AShipyardThatCantBeFetched_SellsAtItsCachedPrice()
    {
        _port.GetShipyardAsync("X1-AB", Shipyard, Arg.Any<CancellationToken>())
            .Returns<ShipyardDataModel>(_ => throw new HttpRequestException("timeout"));

        var result = await Service().TryPurchaseAsync("SHIP_MINING_DRONE", Shipyard);

        result.IsSuccess.Should().BeTrue();
        result.EstimatedCost.Should().Be(12_000);
    }

    [Theory]
    [InlineData("SHIP_MINING_DRONE", ShipType.ShipMiningDrone)]
    [InlineData("SHIP_PROBE", ShipType.ShipProbe)]
    [InlineData("SHIP_LIGHT_HAULER", ShipType.ShipLightHauler)]
    [InlineData("SHIP_SOMETHING_NEW", ShipType.None)]
    public void ToShipType_ReadsTheApisShipTypes(string apiType, ShipType expected)
    {
        ShipPurchaseService.ToShipType(apiType).Should().Be(expected);
    }

    private static ShipyardWaypointDto ShipyardSelling(long price) => new()
    {
        WaypointSymbol = Shipyard,
        SystemSymbol = "X1-AB",
        ShipTypes = ["SHIP_MINING_DRONE"],
        Ships = [new ShipyardShipDto { Type = "SHIP_MINING_DRONE", PurchasePrice = price }],
    };

    private void ShipsAt(string waypointSymbol)
        => _ships.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([new ShipModel("PROBE-1", "X1-AB", waypointSymbol, "DOCKED", "CRUISE", 0, 0, ShipType: "SATELLITE")]);

    private ShipPurchaseService Service() => new(
        _port,
        _agents,
        _ships,
        _shipyards,
        _budget,
        _calls,
        _purchases,
        _names,
        _bus,
        _log.For<ShipPurchaseService>());
}

public sealed class ShipyardCallsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 02, 16, 00, 00, TimeSpan.Zero);

    [Fact]
    public void ACall_StaysOpen_WhileAPlanKeepsMakingIt()
    {
        var calls = new ShipyardCalls();
        calls.Call("X1-AB-SY1", "SHIP_PROBE", Now);
        calls.Call("X1-AB-SY1", "SHIP_PROBE", Now.AddMinutes(1.5));

        calls.Open(Now.AddMinutes(3)).Should().ContainSingle().Which.Since.Should().Be(Now);
    }

    [Fact]
    public void ACallNobodyMakesAgain_Closes()
    {
        // A plan that no longer wants the ship (the credits went elsewhere) lets the probe go again.
        var calls = new ShipyardCalls();
        calls.Call("X1-AB-SY1", "SHIP_PROBE", Now);

        calls.Open(Now + ShipyardCalls.Lifetime + TimeSpan.FromSeconds(1)).Should().BeEmpty();
    }

    [Fact]
    public void TheOldestCall_ComesFirst()
    {
        var calls = new ShipyardCalls();
        calls.Call("X1-AB-SY2", "SHIP_MINING_DRONE", Now.AddSeconds(5));
        calls.Call("X1-AB-SY1", "SHIP_PROBE", Now);

        calls.Open(Now.AddSeconds(10)).Select(call => call.WaypointSymbol).Should().Equal("X1-AB-SY1", "X1-AB-SY2");
    }
}

public sealed class AgentCreditsUpdatesTests
{
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    [Fact]
    public async Task SetCreditsAsync_PublishesTheChange()
    {
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("AGENT", null, "X1-AB-HQ", 175_000, "COSMIC", 2));

        await _agents.SetCreditsAsync(_bus, 174_280, CancellationToken.None);

        await _agents.Received(1).UpsertAsync(Arg.Is<AgentModel>(a => a.Credits == 174_280), Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<AgentCreditsChangedEvent>(e => e.OldCredits == 175_000 && e.NewCredits == 174_280),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task SetCreditsAsync_PublishesNothing_WhenTheCreditsDidNotChange()
    {
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("AGENT", null, "X1-AB-HQ", 175_000, "COSMIC", 2));

        await _agents.SetCreditsAsync(_bus, 175_000, CancellationToken.None);

        await _bus.DidNotReceive().PublishAsync(Arg.Any<AgentCreditsChangedEvent>(), Arg.Any<DeliveryOptions>());
    }
}
