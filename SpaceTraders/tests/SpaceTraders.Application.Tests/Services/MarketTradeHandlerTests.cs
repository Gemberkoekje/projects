using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.EventHandlers;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using Wolverine;

namespace SpaceTraders.Application.Tests.Services;

/// <summary>
/// Slice 6.47 (D133), asked on 2026-11-08: "The more other players are trading there, the lower the prediction accuracy should
/// be." A market read lists its recent transactions, every agent's; the other agents' are stored.
/// </summary>
public sealed class MarketTradeHandlerTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 08, 06, 00, 00, TimeSpan.Zero);

    private readonly IMarketTradeRepository _trades = Substitute.For<IMarketTradeRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();

    [Fact]
    public async Task OnlyTheOtherAgentsTransactions_AreStored()
    {
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("SPECTER", null, "X1-UK3-A1", 1_000_000, "COSMIC", 3));
        var ours = Trade("SPECTER-1A");
        var theirs = Trade("SPECTRE-4");

        await new MarketTradeHandler(_trades, _agents).Handle(new MarketTradesListedEvent("X1-UK3", [ours, theirs]), CancellationToken.None);

        await _trades.Received(1).RecordAsync(
            "X1-UK3",
            Arg.Is<IReadOnlyList<MarketTradeModel>>(stored => stored.Count == 1 && stored[0].ShipSymbol == "SPECTRE-4"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMarketReadThatListsTransactions_SaysSo_AndOneThatListsNone_DoesNot()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var markets = Substitute.For<IMarketRepository>();
        var bus = Substitute.For<IMessageBus>();
        var refresher = new MarketRefresher(port, markets, bus, Microsoft.Extensions.Logging.Abstractions.NullLogger<MarketRefresher>.Instance);
        var listed = new MarketDataModel("X1-UK3-D45", "X1-UK3", "[{}]", null, null, null) { Transactions = [Trade("SPECTRE-4")] };
        port.GetMarketAsync("X1-UK3", "X1-UK3-D45", Arg.Any<CancellationToken>()).Returns(listed);
        port.GetMarketAsync("X1-UK3", "X1-UK3-A4", Arg.Any<CancellationToken>()).Returns(new MarketDataModel("X1-UK3-A4", "X1-UK3", "[{}]", null, null, null));

        await refresher.RefreshAsync("X1-UK3", "X1-UK3-D45", CancellationToken.None);
        await refresher.RefreshAsync("X1-UK3", "X1-UK3-A4", CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Is<MarketTradesListedEvent>(listedEvent => listedEvent.SystemSymbol == "X1-UK3" && listedEvent.Trades.Count == 1), Arg.Any<DeliveryOptions?>());
    }

    private static MarketTradeModel Trade(string ship) => new("X1-UK3-D45", ship, "ADVANCED_CIRCUITRY", "PURCHASE", 20, 6_494, At);
}
