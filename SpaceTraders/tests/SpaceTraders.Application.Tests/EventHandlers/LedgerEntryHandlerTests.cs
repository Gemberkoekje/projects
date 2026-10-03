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
    public async Task ASale_IsALedgerRow_AndCreditsEarned()
    {
        await _handler.Handle(new ShipCargoSoldEvent("AGENT-1", new TradeSymbol("IRON_ORE"), 10, 450, 175_450, "X1-AB-SELL"), CancellationToken.None);

        await _ledger.Received(1).AppendAsync("AGENT-1", LedgerCategory.TradeSell, 450, goodSymbol: "IRON_ORE", units: 10, cancellationToken: Arg.Any<CancellationToken>());
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
