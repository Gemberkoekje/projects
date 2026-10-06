using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.EventHandlers;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.ValueObjects;
using Wolverine;

namespace SpaceTraders.Application.Tests.EventHandlers;

public sealed class LedgerEntryHandlerTests
{
    private readonly ILedgerRepository _ledger = Substitute.For<ILedgerRepository>();
    private readonly IAutomationMetrics _metrics = Substitute.For<IAutomationMetrics>();
    private readonly LedgerEntryHandler _handler;

    public LedgerEntryHandlerTests()
    {
        _handler = new LedgerEntryHandler(_ledger, _metrics, Substitute.For<IDashboardNotifier>());
    }

    [Fact]
    public async Task ARefuel_IsALedgerRow_AndCreditsSpentOnFuel()
    {
        await _handler.Handle(new ShipRefueledEvent("AGENT-1", 720, 174_280, "X1-AB-2"), CancellationToken.None);

        await _ledger.Received(1).AppendAsync(
            "AGENT-1",
            LedgerCategory.FuelPurchase,
            -720,
            waypointSymbol: "X1-AB-2",
            cancellationToken: Arg.Any<CancellationToken>());
        _metrics.Received(1).CreditsSpent("FuelPurchase", 720);
        _metrics.DidNotReceive().CreditsEarned(Arg.Any<string>(), Arg.Any<long>());
    }

    [Fact]
    public async Task AJump_IsALedgerRow_AndCreditsSpentOnAntimatter()
    {
        // Exploring (asked on 2026-10-04): a jump buys one ANTIMATTER at the market of the gate it leaves.
        await _handler.Handle(new ShipJumpedEvent("AGENT-1", "X1-DC53-I55", "X1-KR90-AF5F", 4_520), CancellationToken.None);

        await _ledger.Received(1).AppendAsync(
            "AGENT-1",
            LedgerCategory.AntimatterPurchase,
            -4_520,
            goodSymbol: "ANTIMATTER",
            unitPrice: 4_520,
            units: 1,
            waypointSymbol: "X1-DC53-I55",
            cancellationToken: Arg.Any<CancellationToken>());
        _metrics.Received(1).CreditsSpent("AntimatterPurchase", 4_520);
    }

    [Fact]
    public async Task AChartsReward_IsALedgerRowOfItsOwn_AndCreditsEarned_AndAChartThatPaidNothingBooksNothing()
    {
        // Slice 6.30 (D99): charting a waypoint pays a one-off reward by the rarity of its traits.
        await _handler.Handle(new WaypointChartedEvent("SPECTER-50", "X1-QT24-A1", 3_105), CancellationToken.None);
        await _handler.Handle(new WaypointChartedEvent("SPECTER-50", "X1-QT24-B2", 0), CancellationToken.None);

        await _ledger.Received(1).AppendAsync(
            "SPECTER-50",
            LedgerCategory.ChartReward,
            3_105,
            waypointSymbol: "X1-QT24-A1",
            cancellationToken: Arg.Any<CancellationToken>());
        await _ledger.ReceivedWithAnyArgs(1).AppendAsync(default!, default, default);
        _metrics.Received(1).CreditsEarned("ChartReward", 3_105);
    }

    [Fact]
    public async Task ASale_IsALedgerRow_WithItsMarketAndUnitPrice_AndCreditsEarned()
    {
        // B57, found on 2026-10-03: all 1,336 sale rows since the first, on 2026-10-02 at 13:20Z, had no market and no unit
        // price, while every purchase row has both. The event carries the market; the handler passed it to the units-sold
        // metric but not to the row.
        await _handler.Handle(new ShipCargoSoldEvent("AGENT-1", new TradeSymbol("IRON_ORE"), 10, 450, 175_450, "X1-AB-SELL"), CancellationToken.None);

        await _ledger.Received(1).AppendAsync(
            "AGENT-1",
            LedgerCategory.TradeSell,
            450,
            goodSymbol: "IRON_ORE",
            unitPrice: 45,
            units: 10,
            waypointSymbol: "X1-AB-SELL",
            cancellationToken: Arg.Any<CancellationToken>());
        _metrics.Received(1).CreditsEarned("TradeSell", 450);
    }

    [Fact]
    public async Task ACargoPurchase_IsALedgerRow_AndCreditsSpent()
    {
        await _handler.Handle(new CargoPurchasedEvent("AGENT-1", new TradeSymbol("FOOD"), 40, 8_000, 180_000, "X1-AB-BUY"), CancellationToken.None);

        await _ledger.Received(1).AppendAsync("AGENT-1", LedgerCategory.TradeBuy, -8_000, goodSymbol: "FOOD", unitPrice: 200, units: 40, waypointSymbol: "X1-AB-BUY", cancellationToken: Arg.Any<CancellationToken>());
        _metrics.Received(1).CreditsSpent("TradeBuy", 8_000);
    }

    /// <summary>
    /// Every sale counts its units by market and good, whichever ship made it (a trader, a miner, a siphoner or the command
    /// ship in its spare time), so the markets dashboard can set what we sell into a market against what it makes of it.
    /// </summary>
    [Fact]
    public async Task ASale_CountsItsUnits_ByMarketAndGood()
    {
        await _handler.Handle(new ShipCargoSoldEvent("AGENT-8", new TradeSymbol("HYDROCARBON"), 18, 1_260, 176_710, "X1-DC53-H51"), CancellationToken.None);

        _metrics.Received(1).GoodsSold("X1-DC53-H51", "HYDROCARBON", 18);
        _metrics.DidNotReceiveWithAnyArgs().GoodsBought(default!, default!, default);
    }

    [Fact]
    public async Task MaterialsForTheJumpGate_AreBookedApartFromTrading()
    {
        // Slice 6.6: supplying pays nothing back, so the dashboard's spending shows the gate as a category of its own.
        await _handler.Handle(new CargoPurchasedEvent("AGENT-6", new TradeSymbol("FAB_MATS"), 80, 168_000, 132_000, "X1-DC53-F49") { ForConstruction = true }, CancellationToken.None);

        await _ledger.Received(1).AppendAsync("AGENT-6", LedgerCategory.ConstructionBuy, -168_000, goodSymbol: "FAB_MATS", unitPrice: 2_100, units: 80, waypointSymbol: "X1-DC53-F49", cancellationToken: Arg.Any<CancellationToken>());
        _metrics.Received(1).CreditsSpent("ConstructionBuy", 168_000);
        _metrics.Received(1).GoodsBought("X1-DC53-F49", "FAB_MATS", 80);
    }

    [Fact]
    public async Task ACargoPurchase_CountsItsUnits_ByMarketAndGood()
    {
        await _handler.Handle(new CargoPurchasedEvent("AGENT-6", new TradeSymbol("PLASTICS"), 20, 3_400, 173_310, "X1-DC53-K85"), CancellationToken.None);

        _metrics.Received(1).GoodsBought("X1-DC53-K85", "PLASTICS", 20);
        _metrics.DidNotReceiveWithAnyArgs().GoodsSold(default!, default!, default);
    }

    [Fact]
    public async Task AContractAcceptance_IsALedgerRow_AndCreditsEarned()
    {
        await _handler.Handle(new ContractAcceptedEvent("C-1", 1_136), CancellationToken.None);

        await _ledger.Received(1).AppendAsync("AGENT", LedgerCategory.ContractDeposit, 1_136, sourceEventId: "C-1", cancellationToken: Arg.Any<CancellationToken>());
        _metrics.Received(1).CreditsEarned("ContractDeposit", 1_136);
    }

    [Fact]
    public async Task AContractAcceptanceWithoutPayment_IsNoLedgerRow()
    {
        await _handler.Handle(new ContractAcceptedEvent("C-1"), CancellationToken.None);

        _ledger.ReceivedCalls().Should().BeEmpty();
        _metrics.DidNotReceiveWithAnyArgs().TripProfit(default!, default);
    }

    [Fact]
    public async Task AContractsDepositAndPayout_AreTheContractsProfit()
    {
        // D46: contract work is booked as activity contract: its payments as profit when they come, and the fuel of each
        // round trip as its loss, at the delivery.
        await _handler.Handle(new ContractAcceptedEvent("C-1", 1_136), CancellationToken.None);
        await _handler.Handle(new ContractFulfilledEvent("C-1", 6_620), CancellationToken.None);

        _metrics.Received(1).TripProfit("contract", 1_136);
        _metrics.Received(1).TripProfit("contract", 6_620);
    }

    [Fact]
    public async Task ASaleOrAPurchase_IsNoTripsProfitOnItsOwn()
    {
        // D46: a trip is booked when it ends, with all its sales and purchases (TripBook).
        await _handler.Handle(new ShipCargoSoldEvent("AGENT-1", new TradeSymbol("IRON_ORE"), 10, 450, 175_450, "X1-AB-SELL"), CancellationToken.None);
        await _handler.Handle(new CargoPurchasedEvent("AGENT-1", new TradeSymbol("FOOD"), 40, 8_000, 167_450, "X1-AB-BUY"), CancellationToken.None);
        await _handler.Handle(new ShipRefueledEvent("AGENT-1", 720, 166_730, "X1-AB-2"), CancellationToken.None);

        _metrics.DidNotReceiveWithAnyArgs().TripProfit(default!, default);
    }

    [Fact]
    public async Task AContractFulfilment_IsALedgerRow_AndCreditsEarned()
    {
        await _handler.Handle(new ContractFulfilledEvent("C-1", 6_620), CancellationToken.None);

        await _ledger.Received(1).AppendAsync("AGENT", LedgerCategory.ContractPayout, 6_620, sourceEventId: "C-1", cancellationToken: Arg.Any<CancellationToken>());
        _metrics.Received(1).CreditsEarned("ContractPayout", 6_620);
    }

    [Fact]
    public async Task AShipPurchase_IsALedgerRow_AndCreditsSpent()
    {
        await _handler.Handle(new NewShipPurchasedEvent("AGENT-2", ShipType.ShipMiningDrone, 12_000), CancellationToken.None);

        await _ledger.Received(1).AppendAsync("AGENT-2", LedgerCategory.ShipPurchase, -12_000, goodSymbol: "ShipMiningDrone", cancellationToken: Arg.Any<CancellationToken>());
        _metrics.Received(1).CreditsSpent("ShipPurchase", 12_000);
    }
}

public sealed class MessageMetricsMiddlewareTests
{
    [Fact]
    public void AHandledMessage_IsCountedByItsType()
    {
        var metrics = Substitute.For<IAutomationMetrics>();

        MessageMetricsMiddleware.After(new Envelope(new ShipRefueledEvent("AGENT-1", 720, 174_280, "X1-AB-2")), metrics);

        metrics.Received(1).MessageHandled("ShipRefueledEvent");
    }
}
