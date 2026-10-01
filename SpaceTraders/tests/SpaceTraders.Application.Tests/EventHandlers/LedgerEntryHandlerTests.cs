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
    public async Task ASale_CountsAsCreditsEarned()
    {
        await _handler.Handle(new ShipCargoSoldEvent("AGENT-1", new TradeSymbol("IRON_ORE"), 10, 450, 175_450), CancellationToken.None);

        _metrics.Received(1).CreditsEarned("TradeSell", 450);
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
